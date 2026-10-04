// Package app wires adapters to the core and runs the server.
package app

import (
	"context"
	"fmt"
	"log/slog"
	"time"

	"github.com/fireflycons/airframe/internal/adapters/airplaneslive"
	"github.com/fireflycons/airframe/internal/adapters/configfile"
	"github.com/fireflycons/airframe/internal/adapters/geoip"
	"github.com/fireflycons/airframe/internal/adapters/httpapi"
	"github.com/fireflycons/airframe/internal/core/domain"
	"github.com/fireflycons/airframe/internal/core/ports"
	"github.com/fireflycons/airframe/internal/core/service"
	"github.com/fireflycons/geocoord"
)

// Config holds the runtime settings from the command line.
type Config struct {
	// Observer location; nil means auto-detect from public IP. Location and
	// Radius are only used when the config file holds no saved observer.
	Location *geocoord.Coordinate
	// Radius in nautical miles.
	Radius float64
	// Config file path; empty means nothing is saved.
	ConfigPath string
	// Poll interval.
	Interval time.Duration
	// HTTP listen address.
	Listen string
}

// Run starts the service and HTTP server and blocks until ctx is cancelled.
func Run(ctx context.Context, cfg Config) error {
	var store *configfile.Store
	if cfg.ConfigPath != "" {
		var err error
		if store, err = configfile.Open(cfg.ConfigPath); err != nil {
			return err
		}
	}

	observer, err := initialObserver(ctx, cfg, store)
	if err != nil {
		return err
	}

	var (
		settings ports.SettingsStore
		opts     = []service.Option{service.WithInterval(cfg.Interval)}
	)
	if store != nil {
		settings = store.Section("airplaneslive")
		opts = append(opts, service.WithObserverStore(store))
	}

	provider, err := airplaneslive.New(settings)
	if err != nil {
		return fmt.Errorf("creating airplanes.live client: %w", err)
	}

	svc := service.New(provider, observer, opts...)
	svc.Start(ctx)

	slog.Info("starting airframe", "location", observer.Location.String(), "radius", observer.Radius, "interval", cfg.Interval, "config", cfg.ConfigPath)
	context.AfterFunc(ctx, func() { slog.Info("shutting down") })
	if err := httpapi.New(svc, cfg.Interval).ListenAndServe(ctx, cfg.Listen); err != nil {
		return err
	}
	slog.Info("airframe stopped")
	return nil
}

// initialObserver returns the observer saved in the config file, if there is
// one. Otherwise it uses the command line, auto-detecting the location if it
// wasn't given.
func initialObserver(ctx context.Context, cfg Config, store *configfile.Store) (domain.Observer, error) {
	if store != nil {
		if observer, ok := store.Observer(); ok {
			slog.Info("using saved observer; --location and --radius apply only when none is saved", "config", store.Path())
			return observer, nil
		}
	}

	observer := domain.Observer{Radius: cfg.Radius}
	if cfg.Location != nil {
		observer.Location = *cfg.Location
		return observer, nil
	}
	location, err := geoip.New().Locate(ctx)
	if err != nil {
		return observer, fmt.Errorf("auto-detecting location: %w", err)
	}
	slog.Info("auto-detected location from public IP", "location", location.String())
	observer.Location = location
	return observer, nil
}
