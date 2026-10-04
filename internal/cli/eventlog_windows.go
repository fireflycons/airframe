//go:build windows

package cli

import (
	"bytes"
	"context"
	"errors"
	"fmt"
	"log/slog"
	"strings"
	"sync"

	"golang.org/x/sys/windows"
	"golang.org/x/sys/windows/svc/eventlog"
)

// eventID is the ID of every event airframe writes. The source is registered
// with EventCreate.exe as its message file, which accepts IDs 1 to 1000.
const eventID = 1

// installEventSource registers serviceName as an Application event log source,
// replacing any stale registration left behind by an earlier install.
func installEventSource() error {
	if err := removeEventSource(); err != nil {
		return err
	}
	if err := eventlog.InstallAsEventCreate(serviceName, eventlog.Error|eventlog.Warning|eventlog.Info); err != nil {
		return fmt.Errorf("registering event log source %q: %w", serviceName, err)
	}
	return nil
}

// removeEventSource removes the event log source. It is not an error if it was
// never registered, which is the case for services installed by older versions.
func removeEventSource() error {
	if err := eventlog.Remove(serviceName); err != nil && !errors.Is(err, windows.ERROR_FILE_NOT_FOUND) {
		return fmt.Errorf("removing event log source %q: %w", serviceName, err)
	}
	return nil
}

// eventWriter is the part of *eventlog.Log the handler uses.
type eventWriter interface {
	Info(eid uint32, msg string) error
	Warning(eid uint32, msg string) error
	Error(eid uint32, msg string) error
}

// eventLogHandler is a slog.Handler that writes each record to the Windows
// event log as text. The event log records the time and the level itself, so
// they are left out of the text.
type eventLogHandler struct {
	w    eventWriter
	mu   *sync.Mutex
	buf  *bytes.Buffer
	text slog.Handler
}

func newEventLogHandler(w eventWriter) *eventLogHandler {
	buf := &bytes.Buffer{}
	return &eventLogHandler{
		w:   w,
		mu:  &sync.Mutex{},
		buf: buf,
		text: slog.NewTextHandler(buf, &slog.HandlerOptions{
			ReplaceAttr: func(groups []string, a slog.Attr) slog.Attr {
				if len(groups) == 0 && (a.Key == slog.TimeKey || a.Key == slog.LevelKey) {
					return slog.Attr{}
				}
				return a
			},
		}),
	}
}

func (h *eventLogHandler) Enabled(ctx context.Context, level slog.Level) bool {
	return h.text.Enabled(ctx, level)
}

func (h *eventLogHandler) Handle(ctx context.Context, r slog.Record) error {
	h.mu.Lock()
	defer h.mu.Unlock()

	h.buf.Reset()
	if err := h.text.Handle(ctx, r); err != nil {
		return err
	}
	msg := strings.TrimSuffix(h.buf.String(), "\n")

	switch {
	case r.Level >= slog.LevelError:
		return h.w.Error(eventID, msg)
	case r.Level >= slog.LevelWarn:
		return h.w.Warning(eventID, msg)
	default:
		return h.w.Info(eventID, msg)
	}
}

func (h *eventLogHandler) WithAttrs(attrs []slog.Attr) slog.Handler {
	c := *h
	c.text = h.text.WithAttrs(attrs)
	return &c
}

func (h *eventLogHandler) WithGroup(name string) slog.Handler {
	c := *h
	c.text = h.text.WithGroup(name)
	return &c
}
