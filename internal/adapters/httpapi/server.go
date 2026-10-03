// Package httpapi exposes the aircraft service over HTTP.
package httpapi

import (
	"context"
	"embed"
	"encoding/json"
	"errors"
	"fmt"
	"html/template"
	"io/fs"
	"log/slog"
	"net"
	"net/http"
	"time"

	"github.com/fireflycons/airframe/internal/core/domain"
	"github.com/fireflycons/airframe/internal/core/ports"
	"github.com/fireflycons/geocoord"
)

// maxBodyBytes limits the size of request bodies.
const maxBodyBytes = 4096

// webFS holds the single page application. index.html is a template;
// everything else is served as is under /static/.
//
//go:embed web
var webFS embed.FS

var indexTemplate = template.Must(template.ParseFS(webFS, "web/index.html"))

// Server serves the web UI, GET /aircraft and POST /observer.
type Server struct {
	service ports.AircraftService
	// Poll interval, rendered into the web UI so it polls at the same rate.
	interval time.Duration
}

// New creates a Server.
func New(service ports.AircraftService, interval time.Duration) *Server {
	return &Server{service: service, interval: interval}
}

// Handler returns the HTTP routes.
func (s *Server) Handler() http.Handler {
	static, err := fs.Sub(webFS, "web")
	if err != nil {
		panic(err) // the embedded directory is fixed at compile time
	}

	mux := http.NewServeMux()
	// "/{$}" matches only the root. A bare "GET /" would also match
	// GET /observer, which must stay a 405.
	mux.HandleFunc("GET /{$}", s.index)
	mux.Handle("GET /static/", http.StripPrefix("/static/", http.FileServerFS(static)))
	mux.HandleFunc("GET /aircraft", s.aircraft)
	mux.HandleFunc("POST /observer", s.setObserver)
	// Liveness only: it doesn't call the service, so probes don't wake the poller.
	mux.HandleFunc("GET /healthz", func(http.ResponseWriter, *http.Request) {})
	return mux
}

// ListenAndServe serves on addr until ctx is cancelled, then shuts down gracefully.
func (s *Server) ListenAndServe(ctx context.Context, addr string) error {
	srv := &http.Server{
		Addr:              addr,
		Handler:           s.Handler(),
		ReadHeaderTimeout: 10 * time.Second,
		BaseContext:       func(net.Listener) context.Context { return ctx },
	}

	errc := make(chan error, 1)
	go func() {
		slog.Info("http server listening", "addr", addr)
		errc <- srv.ListenAndServe()
	}()

	select {
	case err := <-errc:
		return err
	case <-ctx.Done():
	}

	shutdownCtx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	defer cancel()
	if err := srv.Shutdown(shutdownCtx); err != nil {
		return err
	}
	if err := <-errc; !errors.Is(err, http.ErrServerClosed) {
		return err
	}
	return nil
}

func (s *Server) index(w http.ResponseWriter, _ *http.Request) {
	w.Header().Set("Content-Type", "text/html; charset=utf-8")
	w.Header().Set("Cache-Control", "no-cache")
	data := struct{ IntervalMs int64 }{s.interval.Milliseconds()}
	if err := indexTemplate.Execute(w, data); err != nil {
		slog.Error("rendering index", "error", err)
	}
}

func (s *Server) aircraft(w http.ResponseWriter, r *http.Request) {
	data, err := s.service.Aircraft(r.Context())
	if err != nil {
		writeError(w, http.StatusServiceUnavailable, err)
		return
	}
	writeJSON(w, http.StatusOK, data)
}

// setObserver changes the observer location and radius. The body has the
// same shape as the location and radius in the GET /aircraft response:
//
//	{"location": {"lat": 51.47, "lon": -0.4543}, "radius": 25}
func (s *Server) setObserver(w http.ResponseWriter, r *http.Request) {
	var body struct {
		Location *geocoord.Coordinate `json:"location"`
		Radius   *float64             `json:"radius"`
	}
	dec := json.NewDecoder(http.MaxBytesReader(w, r.Body, maxBodyBytes))
	dec.DisallowUnknownFields()
	if err := dec.Decode(&body); err != nil {
		writeError(w, http.StatusBadRequest, fmt.Errorf("invalid request body: %w", err))
		return
	}
	if body.Location == nil || body.Radius == nil {
		writeError(w, http.StatusBadRequest, errors.New("location and radius are required"))
		return
	}

	observer := domain.Observer{Location: *body.Location, Radius: *body.Radius}
	if err := s.service.SetObserver(observer); err != nil {
		writeError(w, http.StatusBadRequest, err)
		return
	}
	writeJSON(w, http.StatusOK, observer)
}

func writeError(w http.ResponseWriter, status int, err error) {
	writeJSON(w, status, map[string]string{"error": err.Error()})
}

func writeJSON(w http.ResponseWriter, status int, v any) {
	w.Header().Set("Content-Type", "application/json")
	w.WriteHeader(status)
	if err := json.NewEncoder(w).Encode(v); err != nil {
		slog.Error("writing response", "error", err)
	}
}
