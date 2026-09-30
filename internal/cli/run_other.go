//go:build !windows

package cli

import (
	"context"
	"os"
	"os/signal"
	"syscall"

	"github.com/fireflycons/airframe/internal/app"
)

// run executes the app until SIGINT or SIGTERM is received.
func run(cfg app.Config) error {
	ctx, stop := signal.NotifyContext(context.Background(), os.Interrupt, syscall.SIGTERM)
	defer stop()
	return app.Run(ctx, cfg)
}
