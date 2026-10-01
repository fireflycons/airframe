//go:build windows

package cli

import (
	"context"
	"errors"
	"fmt"
	"log/slog"
	"os"
	"strconv"
	"time"

	"github.com/fireflycons/airframe/internal/app"
	"github.com/spf13/cobra"
	"golang.org/x/sys/windows"
	"golang.org/x/sys/windows/svc"
	"golang.org/x/sys/windows/svc/mgr"
)

const serviceName = "airframe"

// stopTimeout bounds how long uninstall waits for a running service to stop.
// The app's HTTP shutdown takes up to 5 seconds.
const stopTimeout = 20 * time.Second

// handler runs the app under the Windows service control manager.
type handler struct {
	cfg app.Config
}

func (h *handler) Execute(_ []string, requests <-chan svc.ChangeRequest, status chan<- svc.Status) (bool, uint32) {
	status <- svc.Status{State: svc.StartPending}

	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()
	done := make(chan error, 1)
	go func() { done <- app.Run(ctx, h.cfg) }()

	status <- svc.Status{State: svc.Running, Accepts: svc.AcceptStop | svc.AcceptShutdown}

	for {
		select {
		case err := <-done:
			// The app stopped by itself, which only happens on failure.
			slog.Error("airframe stopped", "error", err)
			status <- svc.Status{State: svc.StopPending}
			return true, 1
		case req := <-requests:
			switch req.Cmd {
			case svc.Interrogate:
				status <- req.CurrentStatus
			case svc.Stop, svc.Shutdown:
				status <- svc.Status{State: svc.StopPending}
				cancel()
				if err := <-done; err != nil {
					slog.Error("airframe shutdown", "error", err)
					return true, 1
				}
				return false, 0
			}
		}
	}
}

func init() {
	rootCmd.AddCommand(
		&cobra.Command{
			Use:   "install",
			Short: "Install airframe as a Windows service using the given flags",
			Args:  cobra.NoArgs,
			RunE:  func(*cobra.Command, []string) error { return install() },
		},
		&cobra.Command{
			Use:   "uninstall",
			Short: "Remove the airframe Windows service",
			Args:  cobra.NoArgs,
			RunE:  func(*cobra.Command, []string) error { return uninstall() },
		},
	)
}

func install() error {
	cfg, err := config()
	if err != nil {
		return err
	}
	exe, err := os.Executable()
	if err != nil {
		return err
	}

	args := []string{
		"--radius", strconv.FormatFloat(cfg.Radius, 'g', -1, 64),
		"--interval", cfg.Interval.String(),
		"--listen", cfg.Listen,
	}
	if cfg.Location != nil {
		args = append(args, "--location", cfg.Location.String())
	}

	m, err := mgr.Connect()
	if err != nil {
		return fmt.Errorf("connecting to service manager (run as administrator): %w", err)
	}
	defer func() {
		_ = m.Disconnect()
	}()

	s, err := m.CreateService(serviceName, exe, mgr.Config{
		StartType:   mgr.StartAutomatic,
		DisplayName: "Airframe",
		Description: "Serves nearby aircraft data as JSON",
	}, args...)
	if err != nil {
		if errors.Is(err, windows.ERROR_SERVICE_EXISTS) {
			return fmt.Errorf("service %q already exists", serviceName)
		}
		return err
	}
	defer func() {
		_ = s.Close()
	}()

	fmt.Printf("service %q installed\n", serviceName)
	return nil
}

func uninstall() error {
	m, err := mgr.Connect()
	if err != nil {
		return fmt.Errorf("connecting to service manager (run as administrator): %w", err)
	}
	defer func() {
		_ = m.Disconnect()
	}()

	s, err := m.OpenService(serviceName)
	if err != nil {
		return fmt.Errorf("service %q is not installed: %w", serviceName, err)
	}
	defer func() {
		_ = s.Close()
	}()

	if err := stopService(s); err != nil {
		return err
	}
	if err := s.Delete(); err != nil {
		return err
	}
	fmt.Printf("service %q removed\n", serviceName)
	return nil
}

// stopService stops s if it is running and waits for it to reach the Stopped state.
func stopService(s *mgr.Service) error {
	st, err := s.Query()
	if err != nil {
		return fmt.Errorf("querying service %q: %w", serviceName, err)
	}
	if st.State == svc.Stopped {
		return nil
	}
	if st.State != svc.StopPending {
		if _, err := s.Control(svc.Stop); err != nil && !errors.Is(err, windows.ERROR_SERVICE_NOT_ACTIVE) {
			return fmt.Errorf("stopping service %q: %w", serviceName, err)
		}
		fmt.Printf("stopping service %q\n", serviceName)
	}

	deadline := time.Now().Add(stopTimeout)
	for st.State != svc.Stopped {
		if time.Now().After(deadline) {
			return fmt.Errorf("service %q did not stop within %s", serviceName, stopTimeout)
		}
		time.Sleep(250 * time.Millisecond)
		if st, err = s.Query(); err != nil {
			return fmt.Errorf("querying service %q: %w", serviceName, err)
		}
	}
	return nil
}
