package configfile

import (
	"os"
	"path/filepath"
	"testing"

	"github.com/fireflycons/airframe/internal/core/domain"
	"github.com/fireflycons/geocoord"
	"github.com/stretchr/testify/require"
)

var observer = domain.Observer{Location: geocoord.MustNewCoordinate(51.47, -0.4543), Radius: 15}

func TestMissingFileIsEmpty(t *testing.T) {
	s, err := Open(filepath.Join(t.TempDir(), "airframe.json"))
	require.NoError(t, err)
	_, ok := s.Observer()
	require.False(t, ok)

	var v map[string]string
	found, err := s.Section("airplaneslive").Load(&v)
	require.NoError(t, err)
	require.False(t, found)
}

func TestRoundTripCreatesDirectory(t *testing.T) {
	dir := filepath.Join(t.TempDir(), "sub", "dir")
	path := filepath.Join(dir, "airframe.json")

	s, err := Open(path)
	require.NoError(t, err)
	require.NoError(t, s.SaveObserver(observer))
	require.NoError(t, s.Section("airplaneslive").Save(map[string]string{"BAW": "British Airways"}))

	s, err = Open(path)
	require.NoError(t, err)
	got, ok := s.Observer()
	require.True(t, ok)
	require.Equal(t, observer, got)

	var v map[string]string
	found, err := s.Section("airplaneslive").Load(&v)
	require.NoError(t, err)
	require.True(t, found)
	require.Equal(t, map[string]string{"BAW": "British Airways"}, v)

	entries, err := os.ReadDir(dir)
	require.NoError(t, err)
	require.Len(t, entries, 1, "no temp files are left behind")
}

func TestOtherSectionsKept(t *testing.T) {
	path := filepath.Join(t.TempDir(), "airframe.json")
	require.NoError(t, os.WriteFile(path, []byte(`{"providers":{"other":{"x":1}}}`), 0o600))

	s, err := Open(path)
	require.NoError(t, err)
	require.NoError(t, s.Section("airplaneslive").Save(map[string]string{}))
	require.NoError(t, s.SaveObserver(observer))

	s, err = Open(path)
	require.NoError(t, err)
	var v map[string]int
	found, err := s.Section("other").Load(&v)
	require.NoError(t, err)
	require.True(t, found)
	require.Equal(t, map[string]int{"x": 1}, v)
}

func TestInvalidFile(t *testing.T) {
	tests := map[string]string{
		"malformed":         `{"observer":`,
		"latitude range":    `{"observer":{"location":{"lat":91,"lon":0},"radius":10}}`,
		"radius range":      `{"observer":{"location":{"lat":51,"lon":0},"radius":0}}`,
		"wrong type":        `{"observer":"here"}`,
		"provider not json": `{"providers":[]}`,
	}
	for name, content := range tests {
		t.Run(name, func(t *testing.T) {
			path := filepath.Join(t.TempDir(), "airframe.json")
			require.NoError(t, os.WriteFile(path, []byte(content), 0o600))
			_, err := Open(path)
			require.Error(t, err)
		})
	}
}
