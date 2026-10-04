// Package configfile persists the observer and provider settings in a JSON
// file. A missing file is not an error: the store starts empty.
package configfile

import (
	"encoding/json"
	"errors"
	"fmt"
	"io/fs"
	"os"
	"path/filepath"
	"sync"

	"github.com/fireflycons/airframe/internal/core/domain"
	"github.com/fireflycons/airframe/internal/core/ports"
)

// document is the file's content. Providers are kept as raw JSON so that a
// provider's section survives saves made while a different provider is in use.
type document struct {
	Observer  *domain.Observer           `json:"observer,omitempty"`
	Providers map[string]json.RawMessage `json:"providers,omitempty"`
}

// Store is the config file. Every save rewrites the whole file.
type Store struct {
	path string
	mu   sync.Mutex
	doc  document
}

var _ ports.ObserverStore = (*Store)(nil)

// Open loads the config file at path. A missing file gives an empty store.
// A malformed file is an error, so it is never silently overwritten.
func Open(path string) (*Store, error) {
	s := &Store{path: path}
	data, err := os.ReadFile(path)
	if errors.Is(err, fs.ErrNotExist) {
		return s, nil
	}
	if err != nil {
		return nil, fmt.Errorf("reading config %s: %w", path, err)
	}
	if err := json.Unmarshal(data, &s.doc); err != nil {
		return nil, fmt.Errorf("parsing config %s: %w", path, err)
	}
	if s.doc.Observer != nil {
		if err := s.doc.Observer.Validate(); err != nil {
			return nil, fmt.Errorf("config %s: %w", path, err)
		}
	}
	return s, nil
}

// Path returns the file's location.
func (s *Store) Path() string { return s.path }

// Observer returns the saved observer, if there is one.
func (s *Store) Observer() (domain.Observer, bool) {
	s.mu.Lock()
	defer s.mu.Unlock()
	if s.doc.Observer == nil {
		return domain.Observer{}, false
	}
	return *s.doc.Observer, true
}

// SaveObserver saves the observer.
func (s *Store) SaveObserver(observer domain.Observer) error {
	s.mu.Lock()
	defer s.mu.Unlock()
	s.doc.Observer = &observer
	return s.write()
}

// Section returns the settings section for the named provider.
func (s *Store) Section(name string) ports.SettingsStore {
	return &section{store: s, name: name}
}

// write saves the document atomically: a crash leaves either the old file or
// the new one. Must be called with mu held.
func (s *Store) write() error {
	data, err := json.MarshalIndent(s.doc, "", "  ")
	if err != nil {
		return err
	}
	dir := filepath.Dir(s.path)
	if err := os.MkdirAll(dir, 0o755); err != nil {
		return fmt.Errorf("creating config directory: %w", err)
	}
	tmp, err := os.CreateTemp(dir, filepath.Base(s.path)+".*.tmp")
	if err != nil {
		return fmt.Errorf("writing config: %w", err)
	}
	defer func() {
		_ = os.Remove(tmp.Name()) // fails harmlessly once renamed
	}()
	if _, err := tmp.Write(append(data, '\n')); err != nil {
		_ = tmp.Close()
		return fmt.Errorf("writing config: %w", err)
	}
	if err := tmp.Sync(); err != nil {
		_ = tmp.Close()
		return fmt.Errorf("writing config: %w", err)
	}
	if err := tmp.Close(); err != nil {
		return fmt.Errorf("writing config: %w", err)
	}
	if err := os.Rename(tmp.Name(), s.path); err != nil {
		return fmt.Errorf("writing config: %w", err)
	}
	return nil
}

type section struct {
	store *Store
	name  string
}

func (c *section) Load(v any) (bool, error) {
	c.store.mu.Lock()
	raw, ok := c.store.doc.Providers[c.name]
	c.store.mu.Unlock()
	if !ok {
		return false, nil
	}
	if err := json.Unmarshal(raw, v); err != nil {
		return false, fmt.Errorf("config section %q: %w", c.name, err)
	}
	return true, nil
}

func (c *section) Save(v any) error {
	raw, err := json.Marshal(v)
	if err != nil {
		return err
	}
	c.store.mu.Lock()
	defer c.store.mu.Unlock()
	if c.store.doc.Providers == nil {
		c.store.doc.Providers = map[string]json.RawMessage{}
	}
	c.store.doc.Providers[c.name] = raw
	return c.store.write()
}
