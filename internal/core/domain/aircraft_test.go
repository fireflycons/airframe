package domain

import (
	"encoding/json"
	"testing"

	"github.com/fireflycons/geocoord"
	"github.com/stretchr/testify/require"
)

func TestAircraftDataMarshal(t *testing.T) {
	list := []Aircraft{{
		Icao:              "4ca1d3",
		Flight:            "RYR12AB",
		IndicatedAirSpeed: 250,
		OnGround:          true,
		Position:          geocoord.MustNewCoordinate(51.5, -0.5),
	}}
	data := AircraftData{
		Location: geocoord.MustNewCoordinate(51.47, -0.4543),
		Radius:   25,
		Aircraft: &list,
	}

	b, err := json.Marshal(data)
	require.NoError(t, err)

	var got map[string]any
	require.NoError(t, json.Unmarshal(b, &got))

	require.Equal(t, map[string]any{"lat": 51.47, "lon": -0.4543}, got["location"])
	require.Equal(t, 25.0, got["radius"])

	ac := got["aircraft"].([]any)[0].(map[string]any)
	require.Equal(t, "4ca1d3", ac["icao"])
	require.Equal(t, "RYR12AB", ac["flight"])
	require.Equal(t, 250.0, ac["indicatedAirSpeed"])
	require.Equal(t, true, ac["onGround"])
	require.Equal(t, map[string]any{"lat": 51.5, "lon": -0.5}, ac["position"])
	require.NotContains(t, ac, "description")
}
