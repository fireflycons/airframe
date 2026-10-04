//go:build windows

package cli

import (
	"context"
	"log/slog"
	"os"
	"os/signal"
	"path/filepath"
	"syscall"

	"github.com/fireflycons/airframe/internal/app"
	"golang.org/x/sys/windows"
	"golang.org/x/sys/windows/svc"
	"golang.org/x/sys/windows/svc/eventlog"
)

const defaultConfigHelp = `%ProgramData%\Airframe\airframe.json as a service, otherwise %AppData%\Airframe\airframe.json`

// defaultConfigPath returns the service's config file when running as a
// service, otherwise one in the user's profile. They are kept apart because a
// standard user can't modify files the service creates, and so an interactive
// run doesn't overwrite the service's settings.
func defaultConfigPath() (string, error) {
	isService, err := svc.IsWindowsService()
	if err != nil {
		return "", err
	}
	if isService {
		return serviceConfigPath()
	}
	dir, err := os.UserConfigDir()
	if err != nil {
		return "", err
	}
	return filepath.Join(dir, "Airframe", "airframe.json"), nil
}

// serviceConfigPath returns the service's default config file in ProgramData.
// LocalSystem's own profile is under System32, so it isn't used.
func serviceConfigPath() (string, error) {
	dir, err := windows.KnownFolderPath(windows.FOLDERID_ProgramData, 0)
	if err != nil {
		return "", err
	}
	return filepath.Join(dir, "Airframe", "airframe.json"), nil
}

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
		// stdout and stderr are discarded under the service manager, so log to
		// the event log instead.
		elog, err := eventlog.Open(serviceName)
		if err != nil {
			return err
		}
		defer func() {
			_ = elog.Close()
		}()
		slog.SetDefault(slog.New(newEventLogHandler(elog)))
		return svc.Run(serviceName, &handler{cfg: cfg})
	}

	ctx, stop := signal.NotifyContext(context.Background(), os.Interrupt, syscall.SIGTERM)
	defer stop()
	return app.Run(ctx, cfg)
}
