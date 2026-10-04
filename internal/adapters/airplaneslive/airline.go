package airplaneslive

import (
	"context"
	"log/slog"
	"regexp"
	"sync"

	"github.com/fireflycons/airframe/internal/core/ports"
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
// A successful lookup with no match is cached as "". If settings is set, the
// cache is loaded from it and saved whenever new names are added.
type airlineCache struct {
	client   client
	settings ports.SettingsStore
	mu       sync.Mutex
	names    map[string]string
}

// savedSettings is the provider's section of the config file.
type savedSettings struct {
	Airlines map[string]string `json:"airlines"`
}

func newAirlineCache(c client, settings ports.SettingsStore) *airlineCache {
	cache := &airlineCache{client: c, settings: settings, names: map[string]string{}}
	if settings == nil {
		return cache
	}
	var saved savedSettings
	if _, err := settings.Load(&saved); err != nil {
		// It's only a cache; start empty and overwrite it on the next save.
		slog.Warn("loading saved airline names", "error", err)
		return cache
	}
	for code, name := range saved.Airlines {
		cache.names[code] = name
	}
	return cache
}

// resolve returns airline names for the given codes, looking up at most
// maxNewLookups uncached codes.
func (c *airlineCache) resolve(ctx context.Context, codes []string) map[string]string {
	result := map[string]string{}
	lookups := 0
	added := false

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
			added = true
		}
		result[code] = name
	}
	if added {
		c.save()
	}
	return result
}

// save writes the cache to settings, if set. Lookups are capped per refresh,
// so this is at most one small write per poll, and only while names are new.
func (c *airlineCache) save() {
	if c.settings == nil {
		return
	}
	c.mu.Lock()
	defer c.mu.Unlock()
	if err := c.settings.Save(savedSettings{Airlines: c.names}); err != nil {
		slog.Warn("saving airline names", "error", err)
	}
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
