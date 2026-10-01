package httpapi

import (
	"context"
	"encoding/json"
	"errors"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
	"time"

	"github.com/fireflycons/airframe/internal/core/domain"
	"github.com/fireflycons/geocoord"
	"github.com/stretchr/testify/require"
)

type fakeService struct {
	data     domain.AircraftData
	err      error
	observer *domain.Observer
}

func (f *fakeService) Aircraft(context.Context) (*domain.AircraftData, error) {
	if f.err != nil {
		return nil, f.err
	}
	return &f.data, nil
}

func (f *fakeService) SetObserver(o domain.Observer) error {
	if err := o.Validate(); err != nil {
		return err
	}
	f.observer = &o
	return nil
}

var location = geocoord.MustNewCoordinate(51.47, -0.4543)

func serve(svc *fakeService, method, path, body string) *httptest.ResponseRecorder {
	rec := httptest.NewRecorder()
	New(svc, 3*time.Second).Handler().ServeHTTP(rec, httptest.NewRequest(method, path, strings.NewReader(body)))
	return rec
}

func TestIndex(t *testing.T) {
	rec := serve(&fakeService{}, http.MethodGet, "/", "")

	require.Equal(t, http.StatusOK, rec.Code)
	require.Equal(t, "text/html; charset=utf-8", rec.Header().Get("Content-Type"))
	require.Contains(t, rec.Body.String(), `data-interval-ms="3000"`)
	require.Contains(t, rec.Body.String(), `/static/app.js`)
}

func TestStatic(t *testing.T) {
	tests := []struct {
		path        string
		contentType string
	}{
		{"/static/app.js", "javascript"},
		{"/static/app.css", "text/css"},
	}
	for _, tt := range tests {
		t.Run(tt.path, func(t *testing.T) {
			rec := serve(&fakeService{}, http.MethodGet, tt.path, "")

			require.Equal(t, http.StatusOK, rec.Code)
			require.Contains(t, rec.Header().Get("Content-Type"), tt.contentType)
		})
	}

	require.Equal(t, http.StatusNotFound, serve(&fakeService{}, http.MethodGet, "/static/nope", "").Code)
	require.Equal(t, http.StatusNotFound, serve(&fakeService{}, http.MethodGet, "/nope", "").Code)
}

func TestAircraft(t *testing.T) {
	list := []domain.Aircraft{{Icao: "4ca1d3"}}
	svc := &fakeService{data: domain.AircraftData{Location: location, Radius: 25, Aircraft: &list}}
	rec := serve(svc, http.MethodGet, "/aircraft", "")

	require.Equal(t, http.StatusOK, rec.Code)
	require.Equal(t, "application/json", rec.Header().Get("Content-Type"))

	var got domain.AircraftData
	require.NoError(t, json.Unmarshal(rec.Body.Bytes(), &got))
	require.Equal(t, location, got.Location)
	require.Equal(t, 25.0, got.Radius)
	require.Equal(t, "4ca1d3", (*got.Aircraft)[0].Icao)
}

func TestAircraftError(t *testing.T) {
	rec := serve(&fakeService{err: errors.New("no data")}, http.MethodGet, "/aircraft", "")

	require.Equal(t, http.StatusServiceUnavailable, rec.Code)
	require.JSONEq(t, `{"error":"no data"}`, rec.Body.String())
}

func TestMethodNotAllowed(t *testing.T) {
	require.Equal(t, http.StatusMethodNotAllowed, serve(&fakeService{}, http.MethodPost, "/aircraft", "").Code)
	require.Equal(t, http.StatusMethodNotAllowed, serve(&fakeService{}, http.MethodGet, "/observer", "").Code)
}

func TestSetObserver(t *testing.T) {
	svc := &fakeService{}
	rec := serve(svc, http.MethodPost, "/observer", `{"location":{"lat":40.64,"lon":-73.78},"radius":50}`)

	require.Equal(t, http.StatusOK, rec.Code)
	require.Equal(t, "application/json", rec.Header().Get("Content-Type"))
	require.JSONEq(t, `{"location":{"lat":40.64,"lon":-73.78},"radius":50}`, rec.Body.String())
	require.Equal(t, &domain.Observer{Location: geocoord.MustNewCoordinate(40.64, -73.78), Radius: 50}, svc.observer)
}

func TestSetObserverBadRequest(t *testing.T) {
	tests := []struct {
		name    string
		body    string
		wantErr string
	}{
		{"latitude out of range", `{"location":{"lat":90.5,"lon":0},"radius":50}`, "latitude"},
		{"longitude out of range", `{"location":{"lat":0,"lon":-180.5},"radius":50}`, "longitude"},
		{"zero radius", `{"location":{"lat":0,"lon":0},"radius":0}`, "radius"},
		{"radius too large", `{"location":{"lat":0,"lon":0},"radius":251}`, "radius"},
		{"missing radius", `{"location":{"lat":0,"lon":0}}`, "required"},
		{"missing location", `{"radius":50}`, "required"},
		{"missing longitude", `{"location":{"lat":0},"radius":50}`, "lat and lon"},
		{"unknown field", `{"location":{"lat":0,"lon":0},"radius":50,"x":1}`, "unknown field"},
		{"malformed", `{"location":`, "invalid request body"},
		{"wrong type", `{"location":{"lat":0,"lon":0},"radius":"50"}`, "invalid request body"},
	}
	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			svc := &fakeService{}
			rec := serve(svc, http.MethodPost, "/observer", tt.body)

			require.Equal(t, http.StatusBadRequest, rec.Code)
			require.Equal(t, "application/json", rec.Header().Get("Content-Type"))
			var got map[string]string
			require.NoError(t, json.Unmarshal(rec.Body.Bytes(), &got))
			require.Len(t, got, 1)
			require.Contains(t, got["error"], tt.wantErr)
			require.Nil(t, svc.observer)
		})
	}
}
