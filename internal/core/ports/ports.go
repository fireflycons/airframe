// Package ports defines the interfaces between the core and its adapters.
package ports

import (
	"context"

	"github.com/fireflycons/airframe/internal/core/domain"
	"github.com/fireflycons/geocoord"
)

// AircraftProvider is a driven port: a back end that supplies aircraft data.
type AircraftProvider interface {
	// Aircraft returns all aircraft within radiusNM nautical miles of coord.
	Aircraft(ctx context.Context, coord geocoord.Coordinate, radiusNM float64) ([]domain.Aircraft, error)
}

// AircraftService is a driving port: what clients call to obtain aircraft.
type AircraftService interface {
	// Aircraft returns the most recently cached aircraft, together with the
	// observer location and radius they were fetched for.
	Aircraft(ctx context.Context) (*domain.AircraftData, error)

	// SetObserver changes the observer location and radius. It returns an
	// error only if the observer is invalid.
	SetObserver(observer domain.Observer) error
}

// Locator is a driven port that determines the observer's location.
type Locator interface {
	Locate(ctx context.Context) (geocoord.Coordinate, error)
}

// ObserverStore is a driven port that persists observer changes.
type ObserverStore interface {
	SaveObserver(observer domain.Observer) error
}

// SettingsStore is a driven port that loads and saves one component's own
// settings. Load reports whether any settings were found.
type SettingsStore interface {
	Load(v any) (found bool, err error)
	Save(v any) error
}
