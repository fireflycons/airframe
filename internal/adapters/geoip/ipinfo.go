// Package geoip locates the host by public IP address using ipinfo.io.
package geoip

import (
	"context"
	"encoding/json"
	"fmt"
	"net/http"
	"time"

	"github.com/fireflycons/airframe/internal/core/ports"
	"github.com/fireflycons/geocoord"
)

// IPInfo implements ports.Locator using https://ipinfo.io.
type IPInfo struct {
	BaseURL string
	Client  *http.Client
}

var _ ports.Locator = (*IPInfo)(nil)

// New returns a locator for the public ipinfo.io service.
func New() *IPInfo {
	return &IPInfo{
		BaseURL: "https://ipinfo.io",
		Client:  &http.Client{Timeout: 10 * time.Second},
	}
}

// Locate returns the approximate coordinate of this host's public IP address.
func (l *IPInfo) Locate(ctx context.Context) (geocoord.Coordinate, error) {
	req, err := http.NewRequestWithContext(ctx, http.MethodGet, l.BaseURL+"/json", nil)
	if err != nil {
		return geocoord.Coordinate{}, err
	}
	req.Header.Set("Accept", "application/json")

	resp, err := l.Client.Do(req)
	if err != nil {
		return geocoord.Coordinate{}, fmt.Errorf("ipinfo request: %w", err)
	}
	defer func () {
		_ = resp.Body.Close()
	}()

	if resp.StatusCode != http.StatusOK {
		return geocoord.Coordinate{}, fmt.Errorf("ipinfo request: status %d", resp.StatusCode)
	}

	var body struct {
		Loc string `json:"loc"`
	}
	if err := json.NewDecoder(resp.Body).Decode(&body); err != nil {
		return geocoord.Coordinate{}, fmt.Errorf("ipinfo response: %w", err)
	}

	coord, err := geocoord.NewCoordinateFromString(body.Loc)
	if err != nil {
		return geocoord.Coordinate{}, fmt.Errorf("ipinfo location %q: %w", body.Loc, err)
	}
	return coord, nil
}
