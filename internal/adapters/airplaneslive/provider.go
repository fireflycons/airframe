// Package airplaneslive adapts the airplanes.live API to ports.AircraftProvider.
package airplaneslive

import (
	"context"
	"strings"

	"github.com/fireflycons/airframe/internal/core/domain"
	"github.com/fireflycons/airframe/internal/core/ports"
	api "github.com/fireflycons/airplaneslive"
	"github.com/fireflycons/geocoord"
)

// client is the subset of *airplaneslive.Api used by the provider.
type client interface {
	AircraftWithinRadius(ctx context.Context, coord geocoord.Coordinate, radius float64) (*api.V2Response, error)
	Airlines(ctx context.Context, name, callsign, countryCode, iataCode, icaoCode string, opts api.ListOpts) (*api.AirlineList, error)
}

// Provider fetches aircraft from airplanes.live.
type Provider struct {
	client   client
	airlines *airlineCache
}

var _ ports.AircraftProvider = (*Provider)(nil)

// New creates a Provider using the public airplanes.live API. Airline names
// are loaded from and saved to settings; nil means they are not persisted.
func New(settings ports.SettingsStore) (*Provider, error) {
	c, err := api.NewApi()
	if err != nil {
		return nil, err
	}
	return newProvider(c, settings), nil
}

func newProvider(c client, settings ports.SettingsStore) *Provider {
	return &Provider{client: c, airlines: newAirlineCache(c, settings)}
}

// Aircraft returns all aircraft with a known position within radiusNM of coord.
func (p *Provider) Aircraft(ctx context.Context, coord geocoord.Coordinate, radiusNM float64) ([]domain.Aircraft, error) {
	resp, err := p.client.AircraftWithinRadius(ctx, coord, radiusNM)
	if err != nil {
		return nil, err
	}

	list := make([]domain.Aircraft, 0, len(resp.Ac))
	codes := make([]string, 0, len(resp.Ac))
	for _, src := range resp.Ac {
		ac, ok := convert(src, coord)
		if !ok {
			continue
		}
		list = append(list, ac)
		codes = append(codes, airlineCode(ac.Flight))
	}

	names := p.airlines.resolve(ctx, codes)
	for i := range list {
		list[i].Airline = names[codes[i]]
	}
	return list, nil
}

// convert maps an airplanes.live record to the domain type. It returns false
// if the aircraft has no usable position.
func convert(src api.Aircraft, observer geocoord.Coordinate) (domain.Aircraft, bool) {
	ac := domain.Aircraft{
		Icao:                src.Hex,
		Flight:              strings.TrimSpace(src.Flight),
		Registration:        src.Registration,
		TypeCode:            src.TypeCode,
		Description:         src.Desc,
		Category:            src.Category,
		BarometricAltitude:  float64(src.AltBaro.Altitude),
		OnGround:            src.AltBaro.IsGround,
		GeometricAltitude:   float64(src.AltGeom),
		BarometricClimbRate: float64(src.BaroRate),
		GeometricClimbRate:  float64(src.GeomRate),
		Roll:                src.Roll,
		GroundSpeed:         src.GS,
		IndicatedAirSpeed:   float64(src.IAS),
		TrueAirSpeed:        float64(src.TAS),
		Mach:                src.Mach,
		Track:               src.Track,
		TrackRate:           src.TrackRate,
	}

	if !position(src, observer, &ac) {
		return ac, false
	}
	deriveSpeeds(&ac)
	return ac, true
}
