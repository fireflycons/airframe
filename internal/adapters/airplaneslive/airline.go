package airplaneslive

import (
	"context"
	"log/slog"
	"regexp"
	"sync"

	api "github.com/fireflycons/airplaneslive"
)

// maxNewLookups caps airline API calls per refresh so a cold cache does not
// burst the airplanes.live API; remaining names fill in on later polls.
const maxNewLookups = 3

var airlineCallsign = regexp.MustCompile(`^[A-Z]{3}\d`)

// airlineCode returns the ICAO airline designator for an airline-style
// callsign, or "" for anything else (e.g. registration callsigns).
func airlineCode(flight string) string {
	if !airlineCallsign.MatchString(flight) {
		return ""
	}
	return api.Aircraft{Flight: flight}.AirlineIcao()
}

// airlineCache caches successful airline name lookups by ICAO code.
// A successful lookup with no match is cached as "".
type airlineCache struct {
	client client
	mu     sync.Mutex
	names  map[string]string
}

func newAirlineCache(c client) *airlineCache {
	return &airlineCache{client: c, names: map[string]string{}}
}

// resolve returns airline names for the given codes, looking up at most
// maxNewLookups uncached codes.
func (c *airlineCache) resolve(ctx context.Context, codes []string) map[string]string {
	result := map[string]string{}
	lookups := 0

	for _, code := range codes {
		if code == "" {
			continue
		}
		if _, done := result[code]; done {
			continue
		}

		c.mu.Lock()
		name, ok := c.names[code]
		c.mu.Unlock()

		if !ok {
			if lookups >= maxNewLookups {
				continue
			}
			lookups++
			var err error
			if name, err = c.lookup(ctx, code); err != nil {
				slog.Warn("airline lookup failed", "icao", code, "error", err)
				continue
			}
			c.mu.Lock()
			c.names[code] = name
			c.mu.Unlock()
		}
		result[code] = name
	}
	return result
}

func (c *airlineCache) lookup(ctx context.Context, code string) (string, error) {
	list, err := c.client.Airlines(ctx, "", "", "", "", code, api.DefaultListOpts)
	if err != nil {
		return "", err
	}
	if len(list.Results) == 0 {
		return "", nil
	}
	return list.Results[0].Name, nil
}
