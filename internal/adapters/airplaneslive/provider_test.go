package airplaneslive

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"testing"

	api "github.com/fireflycons/airplaneslive"
	"github.com/fireflycons/geocoord"
	"github.com/stretchr/testify/require"
)

type fakeClient struct {
	aircraft    []api.Aircraft
	airlines    map[string]string
	airlineErr  error
	lookupCalls []string
}

func (f *fakeClient) AircraftWithinRadius(_ context.Context, _ geocoord.Coordinate, _ float64) (*api.V2Response, error) {
	return &api.V2Response{Ac: f.aircraft}, nil
}

func (f *fakeClient) Airlines(_ context.Context, _, _, _, _, icao string, _ api.ListOpts) (*api.AirlineList, error) {
	f.lookupCalls = append(f.lookupCalls, icao)
	if f.airlineErr != nil {
		return nil, f.airlineErr
	}
	list := &api.AirlineList{}
	if name, ok := f.airlines[icao]; ok {
		list.Results = []api.Airline{{Name: name}}
	}
	return list, nil
}

var observer = geocoord.MustNewCoordinate(51.47, -0.4543)

func TestMapping(t *testing.T) {
	fc := &fakeClient{
		aircraft: []api.Aircraft{{
			Hex:          "4ca1d3",
			Flight:       "RYR12AB ",
			Registration: "EI-DCL",
			TypeCode:     "B738",
			Desc:         "BOEING 737-800",
			Category:     "A3",
			AltBaro:      api.AltBaro{Altitude: 12000},
			AltGeom:      12250,
			BaroRate:     -640,
			GeomRate:     -600,
			Roll:         -2.5,
			GS:           310.2,
			IAS:          280,
			TAS:          330,
			Mach:         0.52,
			Track:        87.1,
			TrackRate:    0.1,
			Lat:          51.6,
			Lon:          -0.2,
		}},
		airlines: map[string]string{"RYR": "Ryanair"},
	}

	got, err := newProvider(fc, nil).Aircraft(t.Context(), observer, 25)
	require.NoError(t, err)
	require.Len(t, got, 1)

	ac := got[0]
	require.Equal(t, "4ca1d3", ac.Icao)
	require.Equal(t, "RYR12AB", ac.Flight)
	require.Equal(t, "EI-DCL", ac.Registration)
	require.Equal(t, "Ryanair", ac.Airline)
	require.Equal(t, "B738", ac.TypeCode)
	require.Equal(t, "BOEING 737-800", ac.Description)
	require.Equal(t, "A3", ac.Category)
	require.Equal(t, 12000.0, ac.BarometricAltitude)
	require.False(t, ac.OnGround)
	require.Equal(t, 12250.0, ac.GeometricAltitude)
	require.Equal(t, -640.0, ac.BarometricClimbRate)
	require.Equal(t, -600.0, ac.GeometricClimbRate)
	require.Equal(t, -2.5, ac.Roll)
	require.Equal(t, 310.2, ac.GroundSpeed)
	require.Equal(t, 280.0, ac.IndicatedAirSpeed)
	require.Equal(t, 330.0, ac.TrueAirSpeed)
	require.Equal(t, 0.52, ac.Mach)
	require.Equal(t, 87.1, ac.Track)
	require.Equal(t, 0.1, ac.TrackRate)

	pos := geocoord.MustNewCoordinate(51.6, -0.2)
	require.Equal(t, pos, ac.Position)
	require.InDelta(t, observer.DistanceTo(pos), ac.Distance, 1e-9)
	require.InDelta(t, observer.HeadingTo(pos), ac.Bearing, 1e-9)
}

func TestOnGround(t *testing.T) {
	fc := &fakeClient{aircraft: []api.Aircraft{{
		Hex: "abc", AltBaro: api.AltBaro{IsGround: true}, Lat: 51.47, Lon: -0.45, Mach: 0.1,
	}}}
	got, err := newProvider(fc, nil).Aircraft(t.Context(), observer, 25)
	require.NoError(t, err)
	require.True(t, got[0].OnGround)
	require.Zero(t, got[0].BarometricAltitude)
	require.Zero(t, got[0].TrueAirSpeed, "speeds are not derived on the ground")
}

func TestPositionFallbackAndSkip(t *testing.T) {
	fc := &fakeClient{aircraft: []api.Aircraft{
		{Hex: "last", LastPosition: &api.Pos{Lat: 52, Lon: 0}},
		{Hex: "none"},
		{Hex: "bad", Lat: 95, Lon: 0},
	}}
	got, err := newProvider(fc, nil).Aircraft(t.Context(), observer, 25)
	require.NoError(t, err)
	require.Len(t, got, 1)

	pos := geocoord.MustNewCoordinate(52, 0)
	require.Equal(t, "last", got[0].Icao)
	require.Equal(t, pos, got[0].Position)
	require.InDelta(t, observer.DistanceTo(pos), got[0].Distance, 1e-9)
	require.InDelta(t, observer.HeadingTo(pos), got[0].Bearing, 1e-9)
}

func TestDeriveSpeeds(t *testing.T) {
	// ISA at 36,089 ft and above: 216.65 K, a ≈ 573.6 kt.
	fc := &fakeClient{aircraft: []api.Aircraft{
		{Hex: "tas", AltBaro: api.AltBaro{Altitude: 38000}, TAS: 459, Lat: 51, Lon: 0},
		{Hex: "mach", AltBaro: api.AltBaro{Altitude: 38000}, Mach: 0.8, Lat: 51, Lon: 0},
	}}
	got, err := newProvider(fc, nil).Aircraft(t.Context(), observer, 25)
	require.NoError(t, err)
	require.InDelta(t, 0.8, got[0].Mach, 0.001)
	require.InDelta(t, 458.9, got[1].TrueAirSpeed, 0.5)

	// Sea level: 288.15 K, a ≈ 661.5 kt.
	require.InDelta(t, 661.5, isaSpeedOfSound(0), 0.1)
}

func TestAirlineCache(t *testing.T) {
	fc := &fakeClient{airlines: map[string]string{"BAW": "British Airways"}}
	c := newAirlineCache(fc, nil)
	ctx := t.Context()

	// Hit, no-match and non-airline callsigns; duplicates looked up once.
	names := c.resolve(ctx, []string{"BAW", "BAW", "ZZZ", ""})
	require.Equal(t, map[string]string{"BAW": "British Airways", "ZZZ": ""}, names)
	require.Equal(t, []string{"BAW", "ZZZ"}, fc.lookupCalls)

	// Both are now cached, including the no-match.
	names = c.resolve(ctx, []string{"BAW", "ZZZ"})
	require.Equal(t, "British Airways", names["BAW"])
	require.Len(t, fc.lookupCalls, 2)
}

func TestAirlineErrorsNotCached(t *testing.T) {
	fc := &fakeClient{airlineErr: errors.New("boom")}
	c := newAirlineCache(fc, nil)

	require.Empty(t, c.resolve(t.Context(), []string{"BAW"}))
	fc.airlineErr = nil
	fc.airlines = map[string]string{"BAW": "British Airways"}
	require.Equal(t, "British Airways", c.resolve(t.Context(), []string{"BAW"})["BAW"])
	require.Len(t, fc.lookupCalls, 2)
}

func TestAirlineLookupCap(t *testing.T) {
	fc := &fakeClient{airlines: map[string]string{}}
	c := newAirlineCache(fc, nil)

	codes := make([]string, 0, 7)
	for i := range 7 {
		codes = append(codes, fmt.Sprintf("A%02d", i))
	}
	c.resolve(t.Context(), codes)
	require.Len(t, fc.lookupCalls, maxNewLookups)
	c.resolve(t.Context(), codes)
	require.Len(t, fc.lookupCalls, 2*maxNewLookups)
}

func TestAirlineCode(t *testing.T) {
	require.Equal(t, "BAW", airlineCode("BAW123"))
	require.Equal(t, "RYR", airlineCode("RYR12AB"))
	require.Empty(t, airlineCode("GABCD"))
	require.Empty(t, airlineCode("N123AB"))
	require.Empty(t, airlineCode(""))
}

// fakeSettings is a ports.SettingsStore that holds the section as JSON.
type fakeSettings struct {
	data  []byte
	saves int
}

func (f *fakeSettings) Load(v any) (bool, error) {
	if f.data == nil {
		return false, nil
	}
	return true, json.Unmarshal(f.data, v)
}

func (f *fakeSettings) Save(v any) error {
	f.saves++
	var err error
	f.data, err = json.Marshal(v)
	return err
}

func TestAirlineCachePersisted(t *testing.T) {
	settings := &fakeSettings{data: []byte(`{"airlines":{"BAW":"British Airways","ZZZ":""}}`)}
	fc := &fakeClient{airlines: map[string]string{"RYR": "Ryanair"}}
	c := newAirlineCache(fc, settings)

	// Saved names, including the no-match, are not looked up again.
	names := c.resolve(t.Context(), []string{"BAW", "ZZZ"})
	require.Equal(t, map[string]string{"BAW": "British Airways", "ZZZ": ""}, names)
	require.Empty(t, fc.lookupCalls)
	require.Zero(t, settings.saves, "nothing new to save")

	c.resolve(t.Context(), []string{"RYR"})
	require.Equal(t, 1, settings.saves)
	require.JSONEq(t, `{"airlines":{"BAW":"British Airways","ZZZ":"","RYR":"Ryanair"}}`, string(settings.data))

	// A failed lookup is neither cached nor saved.
	fc.airlineErr = errors.New("boom")
	c.resolve(t.Context(), []string{"EZY"})
	require.Equal(t, 1, settings.saves)
}

func TestAirlineCacheBadSettings(t *testing.T) {
	settings := &fakeSettings{data: []byte(`{"airlines":[]}`)}
	fc := &fakeClient{airlines: map[string]string{"BAW": "British Airways"}}
	c := newAirlineCache(fc, settings)

	require.Equal(t, "British Airways", c.resolve(t.Context(), []string{"BAW"})["BAW"])
	require.JSONEq(t, `{"airlines":{"BAW":"British Airways"}}`, string(settings.data))
}
