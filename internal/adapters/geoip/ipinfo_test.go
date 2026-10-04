package geoip

import (
	"net/http"
	"net/http/httptest"
	"testing"

	"github.com/fireflycons/geocoord"
	"github.com/stretchr/testify/require"
)

func serve(t *testing.T, status int, body string) *IPInfo {
	t.Helper()
	srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		require.Equal(t, "/json", r.URL.Path)
		w.WriteHeader(status)
		_, _ = w.Write([]byte(body))
	}))
	t.Cleanup(srv.Close)
	return &IPInfo{BaseURL: srv.URL, Client: srv.Client()}
}

func TestLocate(t *testing.T) {
	l := serve(t, http.StatusOK, `{"ip":"1.2.3.4","loc":"51.5085,-0.1257"}`)
	got, err := l.Locate(t.Context())
	require.NoError(t, err)
	require.Equal(t, geocoord.MustNewCoordinate(51.5085, -0.1257), got)
}

func TestLocateMalformed(t *testing.T) {
	for _, body := range []string{`{"loc":"nonsense"}`, `{"loc":"91,0"}`, `{}`, `not json`} {
		l := serve(t, http.StatusOK, body)
		_, err := l.Locate(t.Context())
		require.Error(t, err, body)
	}
}

func TestLocateHTTPError(t *testing.T) {
	l := serve(t, http.StatusTooManyRequests, `{}`)
	_, err := l.Locate(t.Context())
	require.ErrorContains(t, err, "429")
}
