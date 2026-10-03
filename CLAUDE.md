# airframe

A Go server with a hexagonal (ports and adapters) design. It polls airplanes.live for aircraft near an observer location and serves them as JSON on `GET /aircraft`. `POST /observer` changes the location and radius. An embedded web UI is served on `/`. It runs on Linux with signal handling, and on Windows either as a service or interactively.

The specification and the implementation notes are in the project outline, imported below. Read its "Implementation notes" section before changing the service, the adapters or the platform code. When the design changes, update those notes.

@project-outline.md

## Commands

```sh
go test -race ./...                            # all tests; fakes only, no network
go test -count=20 ./internal/core/service      # timing-based; run after changing the service
go vet ./... && GOOS=linux go vet ./...        # vet both platform variants
GOOS=windows go build ./... && GOOS=linux go build ./...
go run ./cmd/airframe --location 51.47,-0.4543 --radius 10   # then: curl localhost:7700/aircraft
make installer                                 # Windows only: NSIS setup in bin/windows-amd64
```

## Conventions

- `internal/core` must not import any adapter. Back ends implement `ports.AircraftProvider`.
- Tests use testify `require` and fakes behind small interfaces. The airplaneslive adapter depends on a local `client` interface because the library can't be pointed at a test server.
- In tests, use `t.Context()`, not `context.Background()`.
- JSON fields are camelCase. Errors are returned as `{"error": "..."}`.
- Validate the observer through `domain.Observer.Validate()` and `domain.MaxRadius`; don't duplicate range checks.
- Use `geocoord.NewCoordinateFromString` and `Coordinate.String()` for every conversion between strings and coordinates.
- Platform code sits behind build tags: `//go:build !windows` and `//go:build windows` in `internal/cli`.
- Log with `log/slog`.

## Checking live data

- When checking live data, **always** create a temporary client using the project's own client. `curl` will not work due to CloudFlare browser verification.