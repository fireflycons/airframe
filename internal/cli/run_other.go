//go:build !windows

package cli

import (
	"context"
	"os"
	"os/signal"
	"path/filepath"
	"syscall"

	"github.com/fireflycons/airframe/internal/app"
)

const defaultConfigHelp = "$XDG_CONFIG_HOME/airframe/airframe.json, ~/.config/airframe/airframe.json or, on macOS, ~/Library/Application Support/airframe/airframe.json"

// defaultConfigPath returns the config file in the user's config directory.
func defaultConfigPath() (string, error) {
	dir, err := os.UserConfigDir()
	if err != nil {
		return "", err
	}
	return filepath.Join(dir, "airframe", "airframe.json"), nil
}

// run executes the app until SIGINT or SIGTERM is received.
func run(cfg app.Config) error {
	ctx, stop := signal.NotifyContext(context.Background(), os.Interrupt, syscall.SIGTERM)
	defer stop()
	return app.Run(ctx, cfg)
}
