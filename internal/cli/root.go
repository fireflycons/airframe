// Package cli implements the airframe command line.
package cli

import (
	"fmt"
	"os"
	"time"

	"github.com/fireflycons/airframe/internal/app"
	"github.com/fireflycons/airframe/internal/core/domain"
	"github.com/fireflycons/airframe/internal/core/service"
	"github.com/fireflycons/geocoord"
	"github.com/spf13/cobra"
)

var flags struct {
	location string
	radius   float64
	interval time.Duration
	listen   string
}

var rootCmd = &cobra.Command{
	Use:          "airframe",
	Short:        "Serve nearby aircraft data as JSON",
	SilenceUsage: true,
	Args:         cobra.NoArgs,
	RunE: func(cmd *cobra.Command, _ []string) error {
		cfg, err := config()
		if err != nil {
			return err
		}
		return run(cfg)
	},
}

func init() {
	pf := rootCmd.PersistentFlags()
	pf.StringVar(&flags.location, "location", "", `observer location as "latitude,longitude" (default: auto-detect from public IP)`)
	pf.Float64Var(&flags.radius, "radius", 5, fmt.Sprintf("search radius in nautical miles (max %d)", domain.MaxRadius))
	pf.DurationVar(&flags.interval, "interval", service.DefaultInterval, "aircraft data poll interval")
	pf.StringVar(&flags.listen, "listen", ":7700", "HTTP listen address")
}

// config validates the flags and builds the app configuration.
func config() (app.Config, error) {
	cfg := app.Config{
		Radius:   flags.radius,
		Interval: flags.interval,
		Listen:   flags.listen,
	}

	if flags.location != "" {
		coord, err := geocoord.NewCoordinateFromString(flags.location)
		if err != nil {
			return cfg, fmt.Errorf("invalid --location %q: %w", flags.location, err)
		}
		cfg.Location = &coord
	}
	if err := (domain.Observer{Radius: flags.radius}).Validate(); err != nil {
		return cfg, fmt.Errorf("invalid --radius: %w", err)
	}
	if flags.interval <= 0 {
		return cfg, fmt.Errorf("invalid --interval %s: must be positive", flags.interval)
	}
	return cfg, nil
}

// Execute runs the root command.
func Execute() {
	if err := rootCmd.Execute(); err != nil {
		os.Exit(1)
	}
}
