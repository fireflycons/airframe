// Package app wires adapters to the core and runs the server.
package app

import (
	"context"
	"fmt"
	"log/slog"
	"time"

	"github.com/fireflycons/airframe/internal/adapters/airplaneslive"
	"github.com/fireflycons/airframe/internal/adapters/geoip"
	"github.com/fireflycons/airframe/internal/adapters/httpapi"
	"github.com/fireflycons/airframe/internal/core/domain"
	"github.com/fireflycons/airframe/internal/core/service"
	"github.com/fireflycons/geocoord"
)

// Config holds the runtime settings from the command line.
type Config struct {
	// Observer location; nil means auto-detect from public IP.
	Location *geocoord.Coordinate
	// Radius in nautical miles.
	Radius float64
	// Poll interval.
	Interval time.Duration
	// HTTP listen address.
	Listen string
}

// Run starts the service and HTTP server and blocks until ctx is cancelled.
func Run(ctx context.Context, cfg Config) error {
	var location geocoord.Coordinate
	if cfg.Location != nil {
		location = *cfg.Location
	} else {
		var err error
		if location, err = geoip.New().Locate(ctx); err != nil {
			return fmt.Errorf("auto-detecting location: %w", err)
		}
		slog.Info("auto-detected location from public IP", "location", location.String())
	}

	provider, err := airplaneslive.New()
	if err != nil {
		return fmt.Errorf("creating airplanes.live client: %w", err)
	}

	observer := domain.Observer{Location: location, Radius: cfg.Radius}
	svc := service.New(provider, observer, service.WithInterval(cfg.Interval))
	svc.Start(ctx)

	slog.Info("starting airframe", "location", location.String(), "radius", cfg.Radius, "interval", cfg.Interval)
	context.AfterFunc(ctx, func() { slog.Info("shutting down") })
	if err := httpapi.New(svc).ListenAndServe(ctx, cfg.Listen); err != nil {
		return err
	}
	slog.Info("airframe stopped")
	return nil
}
