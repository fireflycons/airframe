// Package service implements the aircraft data service: it polls a provider
// in the background and serves the cached result to clients.
package service

import (
	"context"
	"errors"
	"log/slog"
	"sync"
	"sync/atomic"
	"time"

	"github.com/fireflycons/airframe/internal/core/domain"
	"github.com/fireflycons/airframe/internal/core/ports"
)

const (
	DefaultInterval    = 5 * time.Second
	DefaultIdleTimeout = 30 * time.Second
)

// ErrNoData is returned when a client is woken but no data could be obtained.
var ErrNoData = errors.New("no aircraft data available")

// Service polls an AircraftProvider and caches the result.
// It implements ports.AircraftService.
type Service struct {
	provider    ports.AircraftProvider
	store       ports.ObserverStore // nil: observer changes are not saved
	interval    time.Duration
	idleTimeout time.Duration

	// cache holds the latest aircraft and the observer they were fetched for.
	// It is only written with mu held, so it always matches observer.
	cache       atomic.Pointer[domain.AircraftData]
	lastRequest atomic.Int64 // UnixNano

	mu       sync.Mutex
	observer domain.Observer
	idle     bool
	waiters  []chan struct{}
	wake     chan struct{}
}

var _ ports.AircraftService = (*Service)(nil)

// Option configures a Service.
type Option func(*Service)

// WithInterval sets the poll interval.
func WithInterval(d time.Duration) Option { return func(s *Service) { s.interval = d } }

// WithIdleTimeout sets how long without client requests before polling pauses.
func WithIdleTimeout(d time.Duration) Option { return func(s *Service) { s.idleTimeout = d } }

// WithObserverStore saves observer changes to store.
func WithObserverStore(store ports.ObserverStore) Option {
	return func(s *Service) { s.store = store }
}

// New creates a Service. Call Start to begin polling.
func New(provider ports.AircraftProvider, observer domain.Observer, opts ...Option) *Service {
	s := &Service{
		provider:    provider,
		observer:    observer,
		interval:    DefaultInterval,
		idleTimeout: DefaultIdleTimeout,
		idle:        true, // nothing is polled until the first client request
		wake:        make(chan struct{}, 1),
	}
	for _, o := range opts {
		o(s)
	}
	return s
}

// Start launches the background poll loop, which runs until ctx is cancelled.
func (s *Service) Start(ctx context.Context) {
	go s.run(ctx)
}

// SetObserver changes the observer location and radius. Cached data for the
// previous observer is discarded, so the next request waits for fresh data.
func (s *Service) SetObserver(observer domain.Observer) error {
	if err := observer.Validate(); err != nil {
		return err
	}

	s.mu.Lock()
	defer s.mu.Unlock()
	s.observer = observer
	s.cache.Store(nil)
	slog.Info("observer changed", "location", observer.Location.String(), "radius", observer.Radius)

	// Saved with mu held so that concurrent changes are saved in order. A
	// failed save doesn't fail the change, which is already in effect.
	if s.store != nil {
		if err := s.store.SaveObserver(observer); err != nil {
			slog.Warn("saving observer", "error", err)
		}
	}
	return nil
}

// Aircraft returns the cached aircraft data. If the poller is idle or the
// cache is empty, it is woken and this call blocks until a refresh has
// completed.
func (s *Service) Aircraft(ctx context.Context) (*domain.AircraftData, error) {
	s.lastRequest.Store(time.Now().UnixNano())

	s.mu.Lock()
	if !s.idle && s.cache.Load() != nil {
		s.mu.Unlock()
		return s.cache.Load(), nil
	}
	ch := make(chan struct{})
	s.waiters = append(s.waiters, ch)
	select {
	case s.wake <- struct{}{}:
	default:
	}
	s.mu.Unlock()

	select {
	case <-ch:
	case <-ctx.Done():
		return nil, ctx.Err()
	}

	if data := s.cache.Load(); data != nil {
		return data, nil
	}
	return nil, ErrNoData
}

func (s *Service) run(ctx context.Context) {
	ticker := time.NewTicker(s.interval)
	defer ticker.Stop()

	for {
		s.mu.Lock()
		idle := s.idle
		s.mu.Unlock()

		if idle {
			slog.Info("poller sleeping until next client request")
			select {
			case <-ctx.Done():
				return
			case <-s.wake:
			}
			slog.Info("poller woken by client request")
			s.refresh(ctx)
			s.release(false)
			ticker.Reset(s.interval)
			continue
		}

		select {
		case <-ctx.Done():
			return
		case <-s.wake:
			// A request raced with an active poller before the cache was
			// populated; refresh now so it is not kept waiting.
			s.refresh(ctx)
			s.release(false)
		case <-ticker.C:
			if time.Since(time.Unix(0, s.lastRequest.Load())) > s.idleTimeout {
				s.release(true)
				continue
			}
			s.refresh(ctx)
			s.release(false)
		}
	}
}

// release sets the idle state and, after a refresh, unblocks waiting clients.
func (s *Service) release(idle bool) {
	s.mu.Lock()
	defer s.mu.Unlock()
	if idle && len(s.waiters) > 0 {
		// A client arrived since the last check. Stay active; its pending
		// wake signal will trigger a refresh that releases it.
		s.idle = false
		return
	}
	s.idle = idle
	for _, ch := range s.waiters {
		close(ch)
	}
	s.waiters = nil
	// Any wake signal now pending belongs to a waiter just served.
	select {
	case <-s.wake:
	default:
	}
}

func (s *Service) refresh(ctx context.Context) {
	for {
		s.mu.Lock()
		observer := s.observer
		s.mu.Unlock()

		list, err := s.provider.Aircraft(ctx, observer.Location, observer.Radius)
		if err != nil {
			if ctx.Err() == nil {
				slog.Error("refreshing aircraft data", "error", err)
			}
			return
		}

		s.mu.Lock()
		current := s.observer == observer
		if current {
			s.cache.Store(&domain.AircraftData{
				Location: observer.Location,
				Radius:   observer.Radius,
				Aircraft: &list,
			})
		}
		s.mu.Unlock()

		if current {
			return
		}
		// The observer changed during the fetch; fetch again for the new one.
	}
}
