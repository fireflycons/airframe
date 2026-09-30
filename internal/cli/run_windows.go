//go:build windows

package cli

import (
	"context"
	"os"
	"os/signal"
	"syscall"

	"github.com/fireflycons/airframe/internal/app"
	"golang.org/x/sys/windows/svc"
)

// run executes the app as a Windows service when started by the service
// control manager, otherwise interactively until Ctrl+C / Ctrl+Break
// (os.Interrupt) or the console is closed, or the user logs off or shuts
// down (SIGTERM).
func run(cfg app.Config) error {
	isService, err := svc.IsWindowsService()
	if err != nil {
		return err
	}
	if isService {
		return svc.Run(serviceName, &handler{cfg: cfg})
	}

	ctx, stop := signal.NotifyContext(context.Background(), os.Interrupt, syscall.SIGTERM)
	defer stop()
	return app.Run(ctx, cfg)
}
