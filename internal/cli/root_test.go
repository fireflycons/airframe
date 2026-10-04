package cli

import (
	"path/filepath"
	"testing"
	"time"

	"github.com/fireflycons/geocoord"
	"github.com/stretchr/testify/require"
)

func TestConfig(t *testing.T) {
	tests := []struct {
		name     string
		location string
		radius   float64
		interval time.Duration
		wantErr  string
	}{
		{name: "valid", location: "51.47, -0.4543", radius: 25, interval: time.Second},
		{name: "auto location", radius: 250, interval: time.Second},
		{name: "bad location", location: "51.47", radius: 25, interval: time.Second, wantErr: "--location"},
		{name: "out of range location", location: "91,0", radius: 25, interval: time.Second, wantErr: "--location"},
		{name: "zero radius", radius: 0, interval: time.Second, wantErr: "--radius"},
		{name: "radius too large", radius: 251, interval: time.Second, wantErr: "--radius"},
		{name: "zero interval", radius: 25, wantErr: "--interval"},
	}
	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			flags.location, flags.radius, flags.interval, flags.listen = tt.location, tt.radius, tt.interval, ":0"
			cfg, err := config()
			if tt.wantErr != "" {
				require.ErrorContains(t, err, tt.wantErr)
				return
			}
			require.NoError(t, err)
			if tt.location == "" {
				require.Nil(t, cfg.Location)
			} else {
				require.Equal(t, geocoord.MustNewCoordinate(51.47, -0.4543), *cfg.Location)
			}
		})
	}
}

func TestConfigPath(t *testing.T) {
	flags.location, flags.radius, flags.interval, flags.listen = "", 25, time.Second, ":0"
	t.Cleanup(func() { flags.config = "" })

	flags.config = "custom.json"
	cfg, err := config()
	require.NoError(t, err)
	require.Equal(t, "custom.json", cfg.ConfigPath)

	flags.config = ""
	cfg, err = config()
	require.NoError(t, err)
	want, err := defaultConfigPath()
	require.NoError(t, err)
	require.Equal(t, want, cfg.ConfigPath)
	require.Equal(t, "airframe.json", filepath.Base(cfg.ConfigPath))
}
