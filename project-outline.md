# Project: airframe

* This project is a server application written in Go using a hexagonal architecture.
* It will query aircraft data via an API and produce a JSON data structure that will be rendered by another application.
* It should abstract the interface to the aircraft data provider in order to allow for switching of data back ends.

## Command line interface

* Use Cobra
* Create arguments for
    * --location: Comma separated latitude,longitude pair. If not provided, then auto-detect coordinates via public IP lookup.
    * --radius: A radius from the given location in NMI

## Aircraft data structure

* The data structure to be built, cached and returned to clients is a **list** of **Aircraft**
* The cache can be a single pointer to the list. Updating the cache can be an atomic pointer swap.
* An **Aircraft** is defined as follows. This should be able to be marshalled to JSON.

    ```go
    type Aircraft struct {
        // 24-bit Mode S / ICAO address (6 hex digits).
        Icao string

        // Callsign / flight id, up to 8 chars (DO-260B 2.2.8.2.6).
        Flight string

        // Aircraft registration
        Registration string

        // Name of airline
        Airline string

        // ICAO type code
        TypeCode string

        // Long type description (optional)
        Description string

        // Barometric altitude in feet
        BarometricAltitude float64

        // Geometric altitude in feet (WGS84).
        GeometricAltitude float64

        // Barometric climb rate, ft/min.
        BarometricClimbRate float64

        // Geometric climb rate, ft/min.
        GeometricClimbRate float64

        // Roll angle, degrees; negative is left roll.
        Roll float64

        // True if craft is on the ground
        OnGround bool

        // Ground speed in knots.
        GroundSpeed float64

        // Indicated air speed in knots.
        IndiactedAirSpeed float64

        // True air speed in knots.
        TrueAirSpeed float64

        // Mach number
        Mach float64

        // True track over ground, degrees.
        Track float64

        // Rate of change of track, degrees/second.
        TrackRate float64

        // Current position of craft
        Position geocoord.Coordinate

        // Distance of craft from observer location (command line) (NMI)
        Distance float64

        // Intiial bearing to craft from observer location  (command line) (degrees)
        Bearing float64
    }
    ```

## Aircraft data provider

* Initial provider will use `github.com/fireflycons/airplaneslive`
* Request to provider will accept a `github.com/fireflycons/geocoord.Coordinate` and a radius in NMI as a float64, and return a list of `Aircraft` as per the structure defined above.
* When making a query for airline data, build and use a cache of successful responses to reduce traffic on the airplanes.live API.
* Any fields in the `Aircraft` data structure not directly available in the backend dataset will be calculated where possible by the provider code.

## Aircraft data service

* The service will manage interaction with the provider and provide an interface for the main application (client) to request `Aircraft` records.
* A background goroutine will poll the aircraft data at a configurable frequency (default 5 seconds) and cache the response internally. It will use the coordinate and radius set on the command line.
* When a client requests data from this service, the cached response will be served.
* If no client requests have arrived for more than 60 seconds, the poll routine will sleep until the next client request arrives at which point it will wake, refresh the cache, serve the client and return to polling mode.

## HTTP server

* Expose route `/aircraft` which will call through the **Aircraft data service** and return a JSON payload from the following struct:

    ```go
    struct AircraftData {
        // From command line processor
        Location geocoord.Coordinate

        // From command line processor
        Radius float64

        Aircraft *[]Aircraft
    }
    ```


## Application variants

* Create code for Windows/Linux using conditional compilation tags
    * Linux - register signal handlers to control shutdown
    * Windows - create a Windows Service wrapper and control shutdown in response to service commands

---

# Implementation notes

What was built, how it works, and why. The sections above are the original specification. Where the code differs from them, see [Deviations from the outline](#deviations-from-the-outline).

## Package layout

| Package | Role |
|---|---|
| `cmd/airframe` | `main`; calls `cli.Execute()`. |
| `internal/core/domain` | `Aircraft`, `AircraftData` (the `GET /aircraft` payload) and `Observer` (location and radius, `Validate()`, `MaxRadius = 250`). |
| `internal/core/ports` | Interfaces. Driven: `AircraftProvider` and `Locator`. Driving: `AircraftService` (`Aircraft(ctx)` and `SetObserver(Observer)`). |
| `internal/core/service` | The aircraft data service: background poller, cache, idle/wake logic and observer changes. |
| `internal/adapters/airplaneslive` | `AircraftProvider` for airplanes.live: maps records, derives missing fields, caches airline names. |
| `internal/adapters/geoip` | `Locator` using `https://ipinfo.io/json` (`"loc":"lat,lon"`). No API key. |
| `internal/adapters/httpapi` | HTTP server: the web UI (`GET /`, `/static/`), `GET /aircraft` and `POST /observer`, with graceful shutdown. The UI's files are embedded from `web/`. |
| `internal/app` | `Config` and `Run(ctx, cfg)`: wires adapters to the service, blocks until ctx is cancelled. Logs `shutting down` and `airframe stopped`. |
| `internal/cli` | Cobra root command and flags; platform runners (`run_other.go` is `!windows`, `run_windows.go`); `service_windows.go`. |

Dependencies point inwards: adapters import `core`, and `core` imports no adapters. To add a new data back end, implement `ports.AircraftProvider` in a new adapter package and select it in `app.Run`.

## Command line

| Flag | Default | Notes |
|---|---|---|
| `--location` | auto-detect | Parsed with `geocoord.NewCoordinateFromString`. If omitted, `geoip` looks up the public IP when the app starts. |
| `--radius` | 15 | NM. Must be greater than 0 and at most `domain.MaxRadius` (250). |
| `--interval` | 5s | Poll interval. |
| `--listen` | `:7700` | HTTP listen address. Not in the original outline. |

The flags are persistent, so the Windows `install` subcommand accepts them too. All validation is in `cli.config()`.

## HTTP API

* **`GET /aircraft`** returns `domain.AircraftData`: `{"location":{"lat","lon"},"radius","aircraft":[...]}`.
    * `location` and `radius` are the observer **the data was fetched for**, taken from the cache, not from the command line.
    * It returns 503 with `{"error": "..."}` if no data could be obtained.
* **`POST /observer`** takes `{"location":{"lat":..,"lon":..},"radius":..}` and returns 200 with the observer it applied.
    * Both fields are required.
    * It returns 400 with `{"error": "..."}` for a missing field, an out-of-range lat, lon or radius, unknown fields, malformed JSON or a body over 4 KB.
    * Latitude and longitude range checks come from `geocoord.Coordinate.UnmarshalJSON`. The radius check comes from `Observer.Validate()`.
* **There is no authentication.** Anyone who can reach the port can move the observer.
* All JSON uses camelCase tags.
* **`GET /`** serves the web UI (see below). **`GET /static/*`** serves its CSS and JS.
* **`GET /healthz`** returns 200 with an empty body. It doesn't call the service, so it never wakes the poller. It is used by the Helm chart's probes.

## Web UI (`internal/adapters/httpapi/web`)

* `index.html`, `app.css` and `app.js` are embedded with `//go:embed web`, so the binary is self-contained.
* **There are no third-party JS dependencies and no build step.** The UI uses plain HTML, CSS, inline SVG and one vanilla ES module: `fetch`, `<dialog>`, HTML5 form validation, and SVG for the radar and icons.
* `index.html` is an `html/template`, parsed once at startup. It renders the poll interval as `<body data-interval-ms="…">`, so the page polls `/aircraft` at the `--interval` rate without an extra request.
    * The observer is **not** rendered into the template. It can change at runtime, and each `/aircraft` response already carries the observer its data was fetched for, so the page always uses that.
* The index is routed as `GET /{$}`, not `GET /`. A catch-all `GET /` would also match `GET /observer`, turning its 405 into a 404.
* Layout:
    * The nearest aircraft fills the left half. The radar and the table of the other aircraft are on the right.
    * Below 800 px wide, or in portrait, the panels stack.
* Vertical-rate icons: climb above +200 ft/min, descent below −200 ft/min, otherwise level; aircraft on the ground get their own icon. The rate is `barometricClimbRate`, falling back to `geometricClimbRate`.
* Radar blips are placed from `distance`/`bearing`, not projected from lat/lon, and rotated to `track`.
    * Ground traffic, including vehicles, is dimmed and **unlabelled**, because it clusters at airports where labels overlap and can't be read. This departs from `spa.md`, which asks for every craft to be labelled.
* Polling:
    * The next poll is scheduled only after the previous one finishes, so requests never overlap.
    * Polling pauses while the tab is hidden, which lets the service go idle.
    * On an error, the last good render stays on screen and the status dot turns red.
* **Ground vehicles** are recognised by emitter category C1 (surface emergency vehicle) or C2 (surface service vehicle), not by callsign. In live data, follow-me cars such as `LEADER 4` broadcast C2 with no aircraft type.
    * They are never shown as the nearest aircraft, but they stay in the table (type "Vehicle", altitude GND) and on the radar.
    * They get their own icons: a van in the table and a vehicle outline on the radar. Service vehicles are purple and emergency vehicles red.
    * Fixed ground stations with type code `TWR` have no category, so they are still shown as aircraft on the ground.
* **Radar symbols** come from the emitter category (`CATEGORY_SYMBOLS` in `app.js`). Colours and labels don't depend on it, and neither do the table and hero icons.
    * A1 (light, < 15,500 lb) is a straight-wing propeller aircraft. A2–A5 are the airliner, A6 is a fast jet and A7 a helicopter.
    * B1 is a glider, B2 a balloon, B3 a parachutist, B4 a hang glider, B6 a drone and B7 a rocket.
    * C1/C2 are vehicles, and C3–C5 (obstacles) are a hollow triangle.
    * **GA fallback:** if the category is missing or not in the table (A0, B0, B5, C0 and so on), a flight whose callsign equals its registration, ignoring hyphens and case (`G-ABCD` = `GABCD`), is drawn as a light aircraft. Otherwise it's the airliner.
* The settings dialog prefills from the latest response and POSTs to `/observer`. The server does the real validation, and its `{"error"}` text is shown in the dialog.

## Aircraft data service (`internal/core/service`)

State:

* `cache` is an `atomic.Pointer[domain.AircraftData]`. It holds the aircraft list together with the observer used to fetch it.
* `lastRequest` is an atomic UnixNano timestamp.
* The following are guarded by `mu`:
    * `observer`
    * `idle`
    * `waiters []chan struct{}`: clients blocked until the next refresh
* `wake` is a `chan struct{}` with capacity 1.

Behaviour:

1. **It starts idle.** Nothing is fetched until the first client request.
2. `Aircraft(ctx)` updates `lastRequest`.
    * If the service is active and the cache is non-nil, it returns the cache immediately.
    * Otherwise it registers a waiter, sends on `wake` without blocking, and blocks until the waiter is closed or `ctx` ends.
3. The poll loop `run()`:
    * **While idle** it blocks on `wake`, then refreshes, releases the waiters and resets the ticker. It logs `poller sleeping until next client request` and `poller woken by client request` at Info.
    * **While active** it refreshes on each tick. A `wake` refreshes immediately; this covers a request that arrives while active with an empty cache.
    * On a tick, if more than `idleTimeout` (60s) has passed since `lastRequest`, it goes idle instead of refreshing.
4. `release(idle)` closes the waiters and drains any leftover `wake` token. If it is about to go idle but a waiter has just registered, it stays active and leaves the waiter for the pending wake to serve, so nobody is released without a fresh fetch.
5. **Observer changes.** `SetObserver` validates the observer, then under `mu` sets it and clears the cache to nil. The next GET therefore waits for data for the new observer.
    * `refresh()` fetches using the observer it read under `mu`.
    * It stores the result only if the observer is still the same (checked under `mu`). If not, it loops and fetches again.
    * This guarantees that GET never returns aircraft labelled with an observer they weren't fetched for.
6. If a refresh fails, the error is logged and the previous cache is kept. A waiter that finds no cache gets `ErrNoData`, which becomes a 503.

## airplanes.live adapter (`internal/adapters/airplaneslive`)

* **It depends on a small local `client` interface**, not on `*airplaneslive.Api`. The library has no way to inject an HTTP client or base URL, so tests use a fake client.
* **Mapping:**
    * `Hex` becomes `Icao`.
    * `Category` (ADS-B emitter category, A0–D7) is passed through as is. The web UI uses it to recognise ground vehicles and light (GA) aircraft.
    * `Flight` is trimmed; the API pads it with spaces.
    * `AltBaro` is `{Altitude, IsGround}`. The API sends either a number or `"ground"`, and `IsGround` becomes `OnGround`.
    * Integer fields are converted to float64.
* **Position:**
    * Omitted `lat`/`lon` decode as (0,0), which is a valid coordinate, so (0,0) is treated as "no position".
    * It falls back to `LastPosition`, and skips the aircraft if neither position is usable.
    * `Distance` and `Bearing` come from `DistanceFrom`/`BearingFrom` in airplaneslive v0.1.2+, which return errors instead of panicking. In the fallback case they come from geocoord's `DistanceTo`/`HeadingTo`.
* **Derived speeds** (`derive.go`): for airborne aircraft with only one of Mach or TAS, the other is estimated using the ISA speed of sound at the barometric altitude.
    * The temperature is 288.15 K minus 0.0019812 K/ft, with a minimum of 216.65 K, and a = 38.967854·√T kt.
    * The reported OAT is **not** used, because `omitempty` makes 0 °C indistinguishable from "missing".
* **Airline names** (`airline.go`):
    * Only callsigns matching `^[A-Z]{3}\d` are looked up, using `AirlineIcao()` then `Airlines(ctx, "", "", "", "", icao, DefaultListOpts)`.
    * Successful lookups are cached in memory by ICAO code. A lookup with no match is cached as `""`. Errors are not cached.
    * At most `maxNewLookups = 3` uncached codes are looked up per refresh, so a cold start doesn't burst the API. Names fill in over the next few polls.
    * The cache is not persisted.

## Platform variants

* **Non-Windows** (`run_other.go`, build tag `!windows` so it also builds on macOS): `signal.NotifyContext(SIGINT, SIGTERM)`.
* **Linux container** (`Dockerfile`, built with `make image`):
    * It is a multi-stage build. A `golang:1.26` stage builds a static binary with `CGO_ENABLED=0`, which is copied into `gcr.io/distroless/static-debian12:nonroot`. That base holds only CA certificates (needed for airplanes.live and ipinfo.io), tzdata and a nonroot user.
    * The entrypoint is `/airframe` and it exposes 7700, so flags go after the image name: `docker run -p 7700:7700 fireflycons/airframe:<ver> --location 51.47,-0.4543`. `docker stop` sends SIGTERM, which the non-Windows runner handles.
    * The tag is `fireflycons/airframe:<VERSION.txt>`. It is only built locally for now; nothing pushes it to Docker Hub.
    * `.dockerignore` is an allow-list: only `go.mod`, `go.sum`, `cmd/` and `internal/` are sent as build context.
* **Helm chart** (`chart/`): a Deployment, a Service (ClusterIP, port 80 → `http`) and an optional Ingress (`ingress.enabled`).
    * The image is `image.repository:image.tag`, and the tag defaults to the chart's `appVersion`. It assumes the image is on Docker Hub.
    * `airframe.location`, `airframe.radius` and `airframe.interval` become flags; `--listen` comes from `containerPort` (7700). With an empty location, auto-detect finds the cluster's egress IP.
    * Probes use `GET /healthz`, not `/aircraft`, which would wake the poller and keep it querying airplanes.live.
    * `replicaCount` defaults to 1. Each replica polls on its own, and `POST /observer` changes only the replica that receives it.
    * Pods run as UID 65532 (distroless nonroot) with a read-only root filesystem and all capabilities dropped.
* **Windows, interactive** (`run_windows.go`): `svc.IsWindowsService()` is false, so it uses `NotifyContext(os.Interrupt, SIGTERM)`.
    * Go delivers Ctrl+C and Ctrl+Break as `os.Interrupt`.
    * It delivers closing the console, logging off and system shutdown as `SIGTERM`. Windows allows about 5 seconds for cleanup, and the HTTP shutdown timeout is also 5 seconds.
    * Git Bash's default mintty terminal doesn't forward Ctrl+C to native programs; use Windows Terminal or cmd, or `winpty`.
* **Windows service** (`service_windows.go`): `svc.Run("airframe", handler)`.
    * `Execute` runs `app.Run` with a cancellable ctx and cancels it on `Stop` or `Shutdown`.
    * If the app exits by itself, the handler returns exit code 1.
    * `airframe install [flags]` creates an automatic-start service with the current flags baked into its arguments. `airframe uninstall` stops the service if it is running (waiting up to 20 seconds for it to reach Stopped), then deletes it. Both need an elevated shell.
    * Under the service manager, stdout and stderr are discarded, so **logs aren't visible** (see gaps).
* **Windows installer** (`installer/windows/airframe.nsi`, built with `make installer` into `bin/windows-amd64/airframe-setup.exe`):
    * Uses NSIS 3 with the NScurl and nsJSON plugins. It is a 32-bit Unicode stub (the amd64 plugin folder lacks nsDialogs and friends) that requires 64-bit Windows and installs to `Program Files\Airframe`. It asks for elevation once, at launch.
    * The configuration page gets default lat/lon from `https://ipinfo.io/json` (the same as `geoip`) and shows city, region and country. If the lookup fails the fields are left blank; a blank location means the service auto-detects when it starts. It also asks for the radius, the port and whether to install the screensaver. When the screensaver box is ticked, a "Wait n minutes" field (1–9999, prefilled from the current timeout, else 10) sets the inactivity delay.
    * The page pre-checks the input. The real validation is `airframe install`, which the installer runs with `--radius`, `--listen :port` and an optional `--location`; a non-zero exit aborts the install. It then runs `sc start airframe`. The finish page shows the UI's address, `http://localhost:<port>/`, as a clickable link.
    * An existing service is removed first, by running the *new* exe's `uninstall` from `$PLUGINSDIR`, so it doesn't matter where the old copy was installed.
    * The screensaver is installed as `$INSTDIR\Airframe.scr`, not System32.
        * A running `Airframe.scr` (screensaver, preview or settings dialog) locks the file, so both the installer and the uninstaller end it with `taskkill /F /IM Airframe.scr` first.
        * It is made active by writing its 8.3 path to HKCU `Control Panel\Desktop\SCRNSAVE.EXE`, then calling `SystemParametersInfo(SPI_SETSCREENSAVEACTIVE)`. The wait is written to `ScreenSaveTimeOut` (seconds) and applied with `SPI_SETSCREENSAVETIMEOUT`. The uninstaller leaves the timeout as it is.
        * The port is written to HKCU `Software\Airframe-Screensaver\Url`, or to `UrlScreen0` if that already holds a localhost URL.
        * If the user picks another screensaver, Airframe may drop out of the Screen Saver Settings list. Right-click the `.scr` and choose **Install**, or re-run setup.
    * **HKCU is the elevating account.** If a standard user elevates with someone else's admin credentials, the screensaver is set for that admin account.
    * The uninstaller removes the service and the files. It clears `SCRNSAVE.EXE` only if it still points at Airframe, and keeps the screensaver's own settings.

## Deviations from the outline

* The JSON uses camelCase tags. `IndiactedAirSpeed` has been corrected to `IndicatedAirSpeed` (`indicatedAirSpeed`).
* `Description` is `omitempty`.
* `Category` (`omitempty`) was added to `Aircraft` so clients can tell ground vehicles from aircraft.
* `AircraftData.Location` and `Radius` reflect the current observer, which can change at runtime through `POST /observer`, rather than always the command line.
* `POST /observer` and the `--listen` flag were added.
* The web UI at `/` was added later; its specification is `spa.md`.
* The non-Windows runner uses `!windows` rather than `linux`.

## Dependencies and gotchas

* `github.com/fireflycons/geocoord` **v0.1.3+** is required. v0.1.2 added JSON marshalling, and v0.1.3 added `NewCoordinateFromString` and `String()`. airplaneslive's own go.mod still asks for v0.1.1, but because airframe requires the newer version, Go uses it.
* `github.com/fireflycons/airplaneslive` **v0.1.2+** is required; it's the first version whose geospatial `Aircraft` methods return errors instead of panicking.
* The airplanes.live point query allows a radius of up to 250 NM (`domain.MaxRadius`).
* Also uses: `spf13/cobra`, `golang.org/x/sys/windows/svc` (and `svc/mgr`), and `stretchr/testify` for tests.

## Testing

* `go test -race ./...` runs every test against fakes, with no network access. The race detector works on this Windows machine.
* Service tests use short intervals and idle timeouts through `WithInterval`/`WithIdleTimeout`. They are timing-based, so after changing the service, run them repeatedly with `go test -count=20 ./internal/core/service`.
* Tests use `t.Context()`, not `context.Background()`, so the background poller stops when each test ends.
* Cross-compile checks: `GOOS=linux go vet ./...` and `GOOS=windows go build ./...`.
* Manual run: `go run ./cmd/airframe --location 51.47,-0.4543 --radius 10`, then:
    * `curl localhost:7700/aircraft`
    * `curl -X POST localhost:7700/observer -d '{"location":{"lat":40.64,"lon":-73.78},"radius":20}'`
* To check Ctrl+C handling on Windows, start the process with `CREATE_NEW_PROCESS_GROUP` and send `GenerateConsoleCtrlEvent(CTRL_BREAK_EVENT, pid)`. Go treats Ctrl+Break the same as Ctrl+C.

## Known gaps and follow-ups

* There is no Windows Event Log output, so a running service's logs can't be seen.
* `POST /observer` changes aren't persisted; a restart goes back to the command-line flags.
* There is no authentication or CORS on either endpoint.
* The airline cache is in-memory only.
* The Windows `install` → `sc start` → `sc stop` → `uninstall` cycle has not been exercised yet. It needs an elevated shell. The installer (`airframe-setup.exe`) builds cleanly but has not yet been run through install, upgrade and uninstall.
