package service

import (
	"context"
	"errors"
	"sync"
	"sync/atomic"
	"testing"
	"time"

	"github.com/fireflycons/airframe/internal/core/domain"
	"github.com/fireflycons/geocoord"
	"github.com/stretchr/testify/require"
)

type fakeProvider struct {
	calls  atomic.Int32
	fail   atomic.Bool
	delay  time.Duration
	onCall func(n int32) // called at the start of each fetch, if set

	mu     sync.Mutex
	coords []geocoord.Coordinate
}

func (f *fakeProvider) Aircraft(ctx context.Context, coord geocoord.Coordinate, _ float64) ([]domain.Aircraft, error) {
	n := f.calls.Add(1)
	f.mu.Lock()
	f.coords = append(f.coords, coord)
	f.mu.Unlock()
	if f.onCall != nil {
		f.onCall(n)
	}
	if f.delay > 0 {
		time.Sleep(f.delay)
	}
	if f.fail.Load() {
		return nil, errors.New("boom")
	}
	return []domain.Aircraft{{Icao: string(rune('a' + n - 1))}}, nil
}

var (
	origin    = geocoord.MustNewCoordinate(51.47, -0.4543)
	elsewhere = geocoord.MustNewCoordinate(40.64, -73.78)
)

func newStarted(t *testing.T, p *fakeProvider, opts ...Option) *Service {
	t.Helper()
	s := New(p, domain.Observer{Location: origin, Radius: 25}, opts...)
	s.Start(t.Context())
	return s
}

func TestStartsIdleAndWakesOnRequest(t *testing.T) {
	p := &fakeProvider{}
	s := newStarted(t, p, WithInterval(time.Hour))

	time.Sleep(20 * time.Millisecond)
	require.Zero(t, p.calls.Load(), "should not poll before first request")

	data, err := s.Aircraft(t.Context())
	require.NoError(t, err)
	require.Len(t, *data.Aircraft, 1)
	require.Equal(t, origin, data.Location)
	require.Equal(t, 25.0, data.Radius)
	require.EqualValues(t, 1, p.calls.Load())
}

func TestServesCacheWhileActive(t *testing.T) {
	p := &fakeProvider{}
	s := newStarted(t, p, WithInterval(time.Hour))

	first, err := s.Aircraft(t.Context())
	require.NoError(t, err)
	second, err := s.Aircraft(t.Context())
	require.NoError(t, err)
	require.Same(t, first, second)
	require.EqualValues(t, 1, p.calls.Load())
}

func TestPollsAtInterval(t *testing.T) {
	p := &fakeProvider{}
	s := newStarted(t, p, WithInterval(10*time.Millisecond), WithIdleTimeout(time.Hour))

	_, err := s.Aircraft(t.Context())
	require.NoError(t, err)
	require.Eventually(t, func() bool { return p.calls.Load() >= 4 }, time.Second, 5*time.Millisecond)
}

func TestGoesIdleThenWakes(t *testing.T) {
	p := &fakeProvider{}
	s := newStarted(t, p, WithInterval(10*time.Millisecond), WithIdleTimeout(30*time.Millisecond))

	_, err := s.Aircraft(t.Context())
	require.NoError(t, err)

	// Wait for the poller to go idle, then confirm polling has stopped.
	require.Eventually(t, func() bool {
		s.mu.Lock()
		defer s.mu.Unlock()
		return s.idle
	}, time.Second, 5*time.Millisecond)
	stopped := p.calls.Load()
	time.Sleep(50 * time.Millisecond)
	require.Equal(t, stopped, p.calls.Load())

	// A new request wakes it and receives freshly fetched data.
	data, err := s.Aircraft(t.Context())
	require.NoError(t, err)
	require.Equal(t, stopped+1, p.calls.Load())
	require.Equal(t, string(rune('a'+stopped)), (*data.Aircraft)[0].Icao)
}

func TestConcurrentWaitersShareOneRefresh(t *testing.T) {
	p := &fakeProvider{delay: 30 * time.Millisecond}
	s := newStarted(t, p, WithInterval(time.Hour))

	var wg sync.WaitGroup
	for range 10 {
		wg.Go(func() {
			data, err := s.Aircraft(t.Context())
			require.NoError(t, err)
			require.NotNil(t, data)
		})
	}
	wg.Wait()
	// Late arrivals may trigger at most one further refresh.
	require.LessOrEqual(t, p.calls.Load(), int32(2))
}

func TestErrorWithNoCache(t *testing.T) {
	p := &fakeProvider{}
	p.fail.Store(true)
	s := newStarted(t, p, WithInterval(time.Hour))

	_, err := s.Aircraft(t.Context())
	require.ErrorIs(t, err, ErrNoData)
}

func TestErrorKeepsPreviousCache(t *testing.T) {
	p := &fakeProvider{}
	s := newStarted(t, p, WithInterval(10*time.Millisecond), WithIdleTimeout(time.Hour))

	first, err := s.Aircraft(t.Context())
	require.NoError(t, err)
	p.fail.Store(true)
	calls := p.calls.Load()
	require.Eventually(t, func() bool { return p.calls.Load() > calls+1 }, time.Second, 5*time.Millisecond)

	got, err := s.Aircraft(t.Context())
	require.NoError(t, err)
	require.Same(t, first, got)
}

func TestRequestContextCancelled(t *testing.T) {
	p := &fakeProvider{delay: 200 * time.Millisecond}
	s := newStarted(t, p, WithInterval(time.Hour))

	ctx, cancel := context.WithTimeout(t.Context(), 10*time.Millisecond)
	defer cancel()
	_, err := s.Aircraft(ctx)
	require.ErrorIs(t, err, context.DeadlineExceeded)
}

func TestSetObserverRejectsInvalid(t *testing.T) {
	s := newStarted(t, &fakeProvider{}, WithInterval(time.Hour))

	for _, radius := range []float64{0, -1, domain.MaxRadius + 1} {
		require.Error(t, s.SetObserver(domain.Observer{Location: elsewhere, Radius: radius}))
	}
	s.mu.Lock()
	defer s.mu.Unlock()
	require.Equal(t, domain.Observer{Location: origin, Radius: 25}, s.observer)
}

func TestSetObserverInvalidatesCache(t *testing.T) {
	p := &fakeProvider{}
	s := newStarted(t, p, WithInterval(time.Hour), WithIdleTimeout(time.Hour))

	first, err := s.Aircraft(t.Context())
	require.NoError(t, err)
	require.Equal(t, origin, first.Location)

	require.NoError(t, s.SetObserver(domain.Observer{Location: elsewhere, Radius: 50}))

	data, err := s.Aircraft(t.Context())
	require.NoError(t, err)
	require.Equal(t, elsewhere, data.Location)
	require.Equal(t, 50.0, data.Radius)
	require.EqualValues(t, 2, p.calls.Load())
	require.Equal(t, []geocoord.Coordinate{origin, elsewhere}, p.coords)
}

func TestObserverChangedDuringFetch(t *testing.T) {
	p := &fakeProvider{}
	var s *Service
	p.onCall = func(n int32) {
		if n == 1 {
			require.NoError(t, s.SetObserver(domain.Observer{Location: elsewhere, Radius: 50}))
		}
	}
	s = newStarted(t, p, WithInterval(time.Hour))

	// The first fetch is for the old observer and must be discarded.
	data, err := s.Aircraft(t.Context())
	require.NoError(t, err)
	require.Equal(t, elsewhere, data.Location)
	require.Equal(t, []geocoord.Coordinate{origin, elsewhere}, p.coords)
}

type fakeStore struct {
	saved []domain.Observer
	err   error
}

func (f *fakeStore) SaveObserver(o domain.Observer) error {
	f.saved = append(f.saved, o)
	return f.err
}

func TestSetObserverSaves(t *testing.T) {
	store := &fakeStore{}
	s := newStarted(t, &fakeProvider{}, WithInterval(time.Hour), WithObserverStore(store))

	require.Error(t, s.SetObserver(domain.Observer{Location: elsewhere, Radius: 0}))
	require.Empty(t, store.saved, "an invalid observer is not saved")

	want := domain.Observer{Location: elsewhere, Radius: 50}
	require.NoError(t, s.SetObserver(want))
	require.Equal(t, []domain.Observer{want}, store.saved)

	// A failed save doesn't fail the change.
	store.err = errors.New("disk full")
	require.NoError(t, s.SetObserver(domain.Observer{Location: origin, Radius: 10}))
	s.mu.Lock()
	defer s.mu.Unlock()
	require.Equal(t, domain.Observer{Location: origin, Radius: 10}, s.observer)
}
