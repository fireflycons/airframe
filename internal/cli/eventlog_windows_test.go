//go:build windows

package cli

import (
	"log/slog"
	"testing"

	"github.com/stretchr/testify/require"
)

type event struct {
	kind string
	id   uint32
	msg  string
}

type fakeEventWriter struct {
	events []event
}

func (f *fakeEventWriter) Info(eid uint32, msg string) error {
	f.events = append(f.events, event{"info", eid, msg})
	return nil
}

func (f *fakeEventWriter) Warning(eid uint32, msg string) error {
	f.events = append(f.events, event{"warning", eid, msg})
	return nil
}

func (f *fakeEventWriter) Error(eid uint32, msg string) error {
	f.events = append(f.events, event{"error", eid, msg})
	return nil
}

func TestEventLogHandler(t *testing.T) {
	w := &fakeEventWriter{}
	log := slog.New(newEventLogHandler(w))

	log.Debug("hidden")
	log.Info("starting", "radius", 15)
	log.With("component", "poller").Warn("slow")
	log.WithGroup("req").Error("failed", "status", 503)

	require.Equal(t, []event{
		{"info", eventID, `msg=starting radius=15`},
		{"warning", eventID, `msg=slow component=poller`},
		{"error", eventID, `msg=failed req.status=503`},
	}, w.events)
}
