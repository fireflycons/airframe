# airframe

A Go server with a hexagonal (ports and adapters) design. It polls airplanes.live for aircraft near an observer location and serves them as JSON on `GET /aircraft`. `POST /observer` changes the location and radius. An embedded web UI is served on `/`. It runs on Linux/macOS with signal handling, and on Windows either as a service or interactively. A Windows installer (NSIS) and an optional Windows screensaver (.NET/WebView2, under `screensaver/windows`) ship alongside it, and there is a Docker image and a Helm chart.

This file is the single source of truth for the project: the original requirements, what was built and why, and how to work on it. **When the design changes, update the relevant section here.** `README.md` is the user-facing install and usage guide; keep it in step with user-visible changes.

## Commands

```sh
go test -race ./...                            # all tests; fakes only, no network
go test -count=20 ./internal/core/service      # timing-based; run after changing the service
go vet ./... && GOOS=linux go vet ./...        # vet both platform variants
GOOS=windows go build ./... && GOOS=linux go build ./...
go run ./cmd/airframe --location 51.47,-0.4543 --radius 10   # then: curl localhost:7700/aircraft
make help                                      # list make targets
make lint                                      # golangci-lint for GOOS=windows and GOOS=linux
make test                                      # lint, then the two test commands above
make build                                     # test, then binaries for windows/amd64, linux/amd64, darwin/arm64 (+ screensaver on Windows)
make image                                     # Linux Docker image tagged fireflycons/airframe:<VERSION.txt>
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
- Makefile recipes must work under both cmd.exe and sh: set `GOOS`/`GOARCH` as exported target-specific variables (not inline `VAR=x cmd`), and read files with `$(file <)`.

## Checking live data

- When checking live data, **always** create a temporary client using the project's own client. `curl` will not work due to CloudFlare browser verification.

---

# Original requirements

These are the briefs the project was built from, condensed. The code has since moved past them in places; see [Deviations from the requirements](#deviations-from-the-requirements).

## Service

* A Go server with a hexagonal architecture. It queries aircraft data through an API and produces a JSON structure for another application to render. The data provider is abstracted so the back end can be switched.
* **Command line (Cobra):** `--location` is a comma-separated `latitude,longitude`; if it's omitted, the location is auto-detected from the public IP. `--radius` is the radius from the location in NM.
* **Aircraft data:** a **list** of `Aircraft` is built, cached and returned. The cache is a single pointer to the list, updated by an atomic pointer swap. `Aircraft` holds: ICAO hex address, flight/callsign, registration, airline name, ICAO type code, optional long description, barometric and geometric altitude (ft), barometric and geometric climb rate (ft/min), roll (deg, negative is left), on-ground flag, ground speed / IAS / TAS (kt), Mach, track (deg), track rate (deg/s), position (`geocoord.Coordinate`), and distance (NM) and initial bearing (deg) from the observer.
* **Provider:** the first provider uses `github.com/fireflycons/airplaneslive`. It takes a `geocoord.Coordinate` and a radius in NM (float64) and returns `[]Aircraft`. Successful airline lookups are cached to reduce airplanes.live traffic. Fields not in the back end's data are calculated where possible.
* **Service:** a background goroutine polls at a configurable frequency (default 5s) and caches the result. Clients are served from the cache. If no client request arrives for more than 60s, the poller sleeps until the next request, then wakes, refreshes, serves that client and goes back to polling.
* **HTTP:** `/aircraft` returns `AircraftData{Location, Radius, Aircraft}`.
* **Platforms:** use build tags. Linux registers signal handlers for shutdown. Windows has a Windows Service wrapper that shuts down on service commands.

## Web UI (SPA)

* Served from `/` and embedded in the binary. Use HTML5 and CSS as much as possible and keep JS dependencies to a minimum; any that are needed must be the leanest, most modern choice.
* Poll `/aircraft` at the `--interval` rate.
* The craft nearest the observer is shown prominently, in 50% of the window (styled after "flights near me" hardware displays).
* Icons show climb, descent and level flight.
* The other craft are shown in a table ordered by distance.
* A radar widget, centred on the observer, shows each craft's relative position and direction of travel, labelled with its flight number.
* A settings panel shows the observer coordinate and radius in input fields, with a button that POSTs them to `/observer`.

## Windows installer

* An NSIS script in its own directory. NSIS is installed at `C:\Program Files (x86)\NSIS`.
* A dialog asks for the observer location, the radius and the port (default 7700). Defaults come from the host's public IP via NScurl/nsJSON, with city, region and country shown. If the lookup fails, the fields are left empty. A checkbox chooses whether to install the screensaver.
* After extraction, the service is set to start automatically and is started. If chosen, the screensaver is installed and made the current user's active screensaver. Elevation is requested as needed.
* The `Makefile` gets NSIS targets when the OS is Windows.

## GitHub workflows

* **CI:** runs on development branches only.
* **Pull request:** fail if a tag matching `VERSION.txt` already exists. Compile, lint and test every target on suitable runners. Build all targets, including the installer. Save the outputs (the Docker image as OCI) as artifacts with a 24-hour lifetime. A failed PR workflow must block the merge.
* **Merge to the default branch:** create a GitHub release from `VERSION.txt`. The release notes summarise the commits since the previous release (or since the start if there is none). Build in release mode, with static Go binaries where possible. Attach the NSIS installer as `airframe-setup-windows_amd64.exe`, plus a compressed binary per platform (ZIP for Windows, tarball for the rest) named by architecture. Push the Docker image to Docker Hub.

---

# Implementation notes

What was built, how it works, and why.

## Package layout

| Package | Role |
|---|---|
| `cmd/airframe` | `main`; calls `cli.Execute()`. |
| `internal/core/domain` | `Aircraft`, `AircraftData` (the `GET /aircraft` payload) and `Observer` (location and radius, `Validate()`, `MaxRadius = 250`). |
| `internal/core/ports` | Interfaces. Driven: `AircraftProvider`, `Locator`, `ObserverStore` (`SaveObserver`) and `SettingsStore` (`Load`/`Save` of one component's own settings). Driving: `AircraftService` (`Aircraft(ctx)` and `SetObserver(Observer)`). |
| `internal/core/service` | The aircraft data service: background poller, cache, idle/wake logic and observer changes. |
| `internal/adapters/airplaneslive` | `AircraftProvider` for airplanes.live: maps records, derives missing fields, caches airline names. |
| `internal/adapters/configfile` | The JSON config file: the saved observer and per-provider settings sections. Implements `ObserverStore`; `Section(name)` gives a `SettingsStore`. |
| `internal/adapters/geoip` | `Locator` using `https://ipinfo.io/json` (`"loc":"lat,lon"`). No API key. |
| `internal/adapters/httpapi` | HTTP server: the web UI (`GET /`, `/static/`), `GET /aircraft`, `POST /observer` and `GET /healthz`, with graceful shutdown. The UI's files are embedded from `web/`. |
| `internal/app` | `Config` and `Run(ctx, cfg)`: opens the config file, picks the initial observer, wires adapters to the service, blocks until ctx is cancelled. Logs `shutting down` and `airframe stopped`. |
| `internal/cli` | Cobra root command and flags; platform runners (`run_other.go` is `!windows`, `run_windows.go`); `service_windows.go`. |

Other top-level pieces:

| Path | Role |
|---|---|
| `installer/windows/airframe.nsi` | NSIS installer script. |
| `screensaver/windows/` | The WebView2 screensaver (C#, .NET Framework 4.8). |
| `chart/` | Helm chart. |
| `Dockerfile`, `.dockerignore` | Linux container image. |
| `.github/workflows/` | `ci.yml`, `pr.yml`, `release.yml`. |
| `.github/actions/setup-nsis` | Composite action: installs NSIS, the NScurl and nsJSON plugins, and GNU make. |
| `VERSION.txt` | Release version (no `v` prefix). Bump it for every PR to `main`. |

Dependencies point inwards: adapters import `core`, and `core` imports no adapters. To add a new data back end, implement `ports.AircraftProvider` in a new adapter package and select it in `app.Run`.

## Command line

| Flag | Default | Notes |
|---|---|---|
| `--location` | auto-detect | Parsed with `geocoord.NewCoordinateFromString`. If omitted, `geoip` looks up the public IP when the app starts. |
| `--radius` | 15 | NM. Must be greater than 0 and at most `domain.MaxRadius` (250). |
| `--interval` | 5s | Poll interval. |
| `--listen` | `:7700` | HTTP listen address. Not in the original outline. |
| `--config` | per platform | Config file path (see [Config file](#config-file)). Not in the original outline. |

The flags are persistent, so the Windows `install` subcommand accepts them too. All validation is in `cli.config()`.

## Config file

* **Purpose.** It saves the observer whenever it changes, and provider-specific settings. The airplaneslive provider keeps its airline-name cache there.
* **Format.** It is JSON: `{"observer": {...}, "providers": {"<name>": {...}}}`.
    * `providers` is held as `map[string]json.RawMessage`. A provider reads and writes only its own section (`Store.Section(name)`), and sections it doesn't know about are kept when it saves.
* **Precedence.** A saved observer wins over `--location`/`--radius`, which only seed the first run. If a saved observer exists, geoip isn't called, and startup logs `using saved observer`.
    * This lets the Windows service (whose flags are baked into its arguments) and the Helm pod (whose flags come from values) keep `POST /observer` changes across restarts.
    * To go back to the flags, delete the file.
* **Missing file.** It is not an error: the store starts empty. A malformed file, or an observer that fails `Validate()`, **fails startup**, so it is never silently overwritten.
* **Saving.**
    * Every save rewrites the whole file atomically (temp file in the same directory, `Sync`, `Rename`), creating the directory if needed.
    * A failed save is logged as a warning. It doesn't fail `POST /observer`, because the change is already applied.
    * `SetObserver` saves while holding `mu`, so concurrent changes are saved in order.
* **Default locations** (`defaultConfigPath()` in `run_windows.go` / `run_other.go`):
    * **Windows service:** `%ProgramData%\Airframe\airframe.json` (`serviceConfigPath()`, using `KnownFolderPath`). LocalSystem's own profile is under System32, so it isn't used.
    * **Windows interactive:** `%AppData%\Airframe\airframe.json`. It is kept separate because a standard user can't modify files the service created, and so an interactive run doesn't overwrite the service's settings.
    * **Linux/macOS:** `os.UserConfigDir()/airframe/airframe.json` (`$XDG_CONFIG_HOME` or `~/.config`; `~/Library/Application Support`).
    * **Docker and Helm:** `/data/airframe.json`.
    * If no default can be found, it warns and runs without saving.

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
* **`GET /screensaver`** serves the same UI in screensaver mode: the header shows "Press ESC to exit the screensaver". The Windows screensaver treats pages on this path as interactive (see [Windows screensaver](#windows-screensaver-screensaverwindows)).
* **`GET /healthz`** returns 200 with an empty body. It doesn't call the service, so it never wakes the poller. It is used by the Helm chart's probes.

## Web UI (`internal/adapters/httpapi/web`)

* `index.html`, `app.css` and `app.js` are embedded with `//go:embed web`, so the binary is self-contained.
* **There are no third-party JS dependencies and no build step.** The UI uses plain HTML, CSS, inline SVG and one vanilla ES module: `fetch`, `<dialog>`, HTML5 form validation, and SVG for the radar and icons.
* `index.html` is an `html/template`, parsed once at startup. It renders the poll interval as `<body data-interval-ms="…">`, so the page polls `/aircraft` at the `--interval` rate without an extra request.
    * It also gets a `Screensaver` flag, true only for `GET /screensaver`, which adds the ESC hint (`.hint`) to the header. The hint is hidden below 800 px wide, where it would push the settings button off the edge; the screensaver always runs full screen.
    * The observer is **not** rendered into the template. It can change at runtime, and each `/aircraft` response already carries the observer its data was fetched for, so the page always uses that.
* The index is routed as `GET /{$}`, not `GET /`. A catch-all `GET /` would also match `GET /observer`, turning its 405 into a 404.
* Layout:
    * The nearest aircraft fills the left half. The radar and the table of the other aircraft are on the right.
    * Below 800 px wide, or in portrait, the panels stack.
* Vertical-rate icons: climb above +200 ft/min, descent below −200 ft/min, otherwise level; aircraft on the ground get their own icon. The rate is `barometricClimbRate`, falling back to `geometricClimbRate`.
* Radar blips are placed from `distance`/`bearing`, not projected from lat/lon, and rotated to `track`.
    * Ground traffic, including vehicles, is dimmed and **unlabelled**, because it clusters at airports where labels overlap and can't be read. This departs from the SPA brief, which asks for every craft to be labelled.
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
* The header shows `location.origin`, the server the page was loaded from. When the screensaver falls back from one URL (e.g. a cluster instance) to another (the local service), this shows which one is on screen.
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
5. **Observer changes.** `SetObserver` validates the observer, then under `mu` sets it, clears the cache to nil and saves it to the `ObserverStore` (`WithObserverStore`), if one is set. The next GET therefore waits for data for the new observer.
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
    * The cache is persisted in the config file's `providers.airplaneslive` section (`{"airlines": {...}}`), through a `ports.SettingsStore` passed to `New`; nil disables this.
        * It is loaded when the provider is created. A section that won't decode is logged and ignored, because it's only a cache.
        * It is saved after a `resolve` that added names, so at most once per poll and only while the cache is warming.

## Platform variants

* **Non-Windows** (`run_other.go`, build tag `!windows` so it also builds on macOS): `signal.NotifyContext(SIGINT, SIGTERM)`.
* **Linux container** (`Dockerfile`, built with `make image`):
    * It is a multi-stage build. A `golang:1.26` stage builds a static binary with `CGO_ENABLED=0`, which is copied into `gcr.io/distroless/static-debian12:nonroot`. That base holds only CA certificates (needed for airplanes.live and ipinfo.io), tzdata and a nonroot user.
    * The entrypoint is `/airframe` and it exposes 7700, so flags go after the image name: `docker run -p 7700:7700 fireflycons/airframe:<ver> --location 51.47,-0.4543`. `docker stop` sends SIGTERM, which the non-Windows runner handles.
    * `make image` tags it `fireflycons/airframe:<VERSION.txt>` locally. The release workflow pushes `:<version>` and `:latest` to Docker Hub (linux/amd64 only).
    * The entrypoint passes `--config /data/airframe.json`, and `/data` is a `VOLUME` owned by 65532 (created in the build stage, because distroless has no shell), so a named volume is writable by nonroot. A `--config` after the image name overrides it.
    * `.dockerignore` is an allow-list: only `go.mod`, `go.sum`, `cmd/` and `internal/` are sent as build context.
* **Helm chart** (`chart/`): a Deployment, a Service (ClusterIP, port 80 → `http`) and an optional Gateway API `HTTPRoute` (`gateway.enabled`).
    * The chart doesn't create a Gateway. The route attaches to an existing one through `gateway.parentRefs`, and that Gateway must allow routes from the release's namespace. TLS belongs on the Gateway.
    * `gateway.hostnames` and `gateway.path` (a `PathPrefix`, default `/`) select the traffic; it goes to the Service's port.
    * The image is `image.repository:image.tag`, and the tag defaults to the chart's `appVersion`. It pulls from Docker Hub.
    * `airframe.location`, `airframe.radius` and `airframe.interval` become flags; `--listen` comes from `containerPort` (7700). With an empty location, auto-detect finds the cluster's egress IP.
    * Probes use `GET /healthz`, not `/aircraft`, which would wake the poller and keep it querying airplanes.live.
    * `replicaCount` defaults to 1. Each replica polls on its own, and `POST /observer` changes only the replica that receives it.
    * **Persistence** (`persistence.*`, enabled by default):
        * A PVC (`templates/pvc.yaml`, or `existingClaim`) is mounted at `/data`, and the pod runs with `--config=/data/airframe.json`. When persistence is disabled, `/data` is an `emptyDir` instead.
        * With persistence on, the Deployment uses the `Recreate` strategy, so an RWO volume never needs two pods. The template `fail`s if `replicaCount > 1`.
        * `podSecurityContext.fsGroup: 65532` makes the volume writable. The root filesystem stays read-only.
    * Pods run as UID 65532 (distroless nonroot) with a read-only root filesystem and all capabilities dropped.
* **Windows, interactive** (`run_windows.go`): `svc.IsWindowsService()` is false, so it uses `NotifyContext(os.Interrupt, SIGTERM)`.
    * Go delivers Ctrl+C and Ctrl+Break as `os.Interrupt`.
    * It delivers closing the console, logging off and system shutdown as `SIGTERM`. Windows allows about 5 seconds for cleanup, and the HTTP shutdown timeout is also 5 seconds.
    * Git Bash's default mintty terminal doesn't forward Ctrl+C to native programs; use Windows Terminal or cmd, or `winpty`.
* **Windows service** (`service_windows.go`): `svc.Run("airframe", handler)`.
    * `Execute` runs `app.Run` with a cancellable ctx and cancels it on `Stop` or `Shutdown`.
    * If the app exits by itself, the handler returns exit code 1.
    * `airframe install [flags]` creates an automatic-start service with the current flags baked into its arguments (`--config` only if given explicitly).
        * If `--location` is given, it also saves the observer to the service's config file (the explicit `--config` path, or ProgramData), so a reinstall overrides an observer saved there earlier.
        * It also registers `airframe` as an Application event log source (`eventlog.InstallAsEventCreate`, replacing any stale registration). If registering the source fails, the service is deleted again. `airframe uninstall` stops the service if it is running (waiting up to 20 seconds for it to reach Stopped), deletes it and removes the event source (a missing source is not an error, for services installed by older versions). Both need an elevated shell.
    * Under the service manager, stdout and stderr are discarded, so `run` sets the default slog logger to `eventLogHandler` (`eventlog_windows.go`). It formats each record with slog's text handler, minus time and level (the event log records both), and writes it as an Information, Warning or Error event with ID 1 (EventCreate.exe's message file accepts 1–1000). Interactive runs still log to stderr.
* **Windows installer** (`installer/windows/airframe.nsi`, built with `make installer` into `bin/windows-amd64/airframe-setup.exe`):
    * Uses NSIS 3 (at `C:\Program Files (x86)\NSIS`) with the NScurl and nsJSON plugins. It is a 32-bit Unicode stub (the amd64 plugin folder lacks nsDialogs and friends) that requires 64-bit Windows and installs to `Program Files\Airframe`. It asks for elevation once, at launch.
    * `make installer` depends on `build-windows-amd64` and `build-screensaver`, and passes `-DVERSION` (default `git describe`; CI passes `VERSION.txt`).
    * The configuration page gets default lat/lon from `https://ipinfo.io/json` (the same as `geoip`) and shows city, region and country. If the lookup fails the fields are left blank; a blank location means the service auto-detects when it starts. It also asks for the radius, the port and whether to install the screensaver. When the screensaver box is ticked, a "Wait n minutes" field (1–9999, prefilled from the current timeout, else 10) sets the inactivity delay.
    * The page pre-checks the input. The real validation is `airframe install`, which the installer runs with `--radius`, `--listen :port` and an optional `--location`; a non-zero exit aborts the install. It then runs `sc start airframe`. The finish page shows the UI's address, `http://localhost:<port>/`, as a clickable link.
    * An existing service is removed first, by running the *new* exe's `uninstall` from `$PLUGINSDIR`, so it doesn't matter where the old copy was installed.
    * The screensaver is installed as `$INSTDIR\Airframe.scr`, not System32.
        * A running `Airframe.scr` (screensaver, preview or settings dialog) locks the file, so both the installer and the uninstaller end it with `taskkill /F /IM Airframe.scr` first.
        * It is made active by writing its 8.3 path to HKCU `Control Panel\Desktop\SCRNSAVE.EXE`, then calling `SystemParametersInfo(SPI_SETSCREENSAVEACTIVE)`. The wait is written to `ScreenSaveTimeOut` (seconds) and applied with `SPI_SETSCREENSAVETIMEOUT`. The uninstaller leaves the timeout as it is.
        * `http://localhost:<port>/screensaver` is written to HKCU `Software\Airframe-Screensaver\Url`, or to `UrlScreen0` if that already holds a localhost URL (so an upgrade moves an old `/` URL to `/screensaver`). The finish-page link stays on `/`, because it opens a browser.
        * If the user picks another screensaver, Airframe may drop out of the Screen Saver Settings list. Right-click the `.scr` and choose **Install**, or re-run setup.
    * **HKCU is the elevating account.** If a standard user elevates with someone else's admin credentials, the screensaver is set for that admin account.
    * The uninstaller removes the service and the files. It keeps `%ProgramData%\Airframe` (the saved observer and airline cache). It clears `SCRNSAVE.EXE` only if it still points at Airframe, and keeps the screensaver's own settings.

## Windows screensaver (`screensaver/windows`)

* A fork of muro-dot/Webview2_WebPage_Screensaver (commit `352827d`), itself a fork of ZenProjects/Chromium-Web-Page-Screensaver. It shows web pages full screen with Microsoft Edge WebView2. `CHANGELOG.md` records the airframe changes: English only, no update checker, renamed output, an About button, and the per-screen "Multiple URLs" mode (cycle, or fallback: the first URL that responds, then the next, then the offline clock, retrying the first URL at the set interval).
* C# WinForms on .NET Framework 4.8 (`Airframe_Screensaver.csproj` / `.sln`). The assembly is `Airframe_ScreenSaver`. Packages come from `packages.config` (restored with the bundled `nuget.exe`); WebView2 is 1.0.3912.50.
* The `.scr` is self-contained: Costura/Fody embeds the managed WebView2 DLLs, and `EmbeddedWebView2Loader.cs` extracts the native `WebView2Loader.dll` at run time. At run time it needs the .NET Framework 4.8 and the WebView2 Runtime.
* Standard screensaver arguments in `Program.cs`: `/p` preview, `/c` settings, `/s` (or none) runs it.
* **Interactive mode.** When a page loads successfully, `ScreensaverForm.IsInteractive` is set if its path ends in `/screensaver`, and cleared when the fallback clock is shown.
    * On an interactive page, the global keyboard hook (`Program.cs`, key-down only) exits only on ESC; other keys reach the page. `ScreensaverInputFilter` and the WebView `KeyDown`/`PreviewKeyDown` handlers let input through. Mouse movement shows the cursor, and exits only if "Exit on mouse move" (`CloseOnActivity`, default off) is ticked. The X close button is not shown.
    * Every other page (the `/` UI, third-party URLs, the clock) exits on any input as before; `/` is for viewing in a browser.
    * The hook sees ESC first, so ESC exits even while the UI's settings dialog is open; its Cancel button closes it.
    * The default primary-screen URL is `http://localhost:7700/screensaver`.
* Settings are in HKCU `Software\Airframe-Screensaver` (`Program.KEY`, `PreferencesManager.cs`). WebView2 user data goes in `%LOCALAPPDATA%\Airframe-Screensaver`.
* **Build:** `make build-screensaver` (Windows only) finds MSBuild with `vswhere`, restores packages, builds Release with `-p:PostBuildEvent=` and copies `bin/Release/Airframe_ScreenSaver.exe` to `bin/windows-amd64/Airframe_ScreenSaver.scr`. The project's own post-build event is disabled there because it runs `/capture-assets` to regenerate the README screenshots in `assets/`; a Visual Studio build still runs it.

### Rules for working on the screensaver

* **Check UI changes visually, and fix them yourself.** After adding or changing UI, or regenerating the theme screenshots, read the generated images yourself. Look for clipped text, overlapping or colliding controls, too little spacing and awkward line breaks, and fix them before the user does. Repeat change → rebuild → regenerate → recheck until nothing is wrong, and only then report.
    * Make combo boxes wide enough that the selected text (e.g. "Bottom-Right (Default)") isn't elided.
    * Leave at least 16px between adjacent controls and around buttons. If space is short, enlarge the form or rearrange the layout.
    * Check both dark and light mode: background, foreground and border contrast must stay legible.
* **Screenshots:** `README.md` uses `assets/screenshot_dark.png` and `assets/screenshot_light.png`. The default thumbnail `assets/screenshot.png` stays the dark-mode image.
* **Its README** lists only user-facing features, as short one-line bullets. Layout tweaks, label changes and build changes go in `CHANGELOG.md` or commit messages, not the README.
* Comments explain why, not what. Split code into files by feature.

## CI and release (`.github/workflows`)

* **`ci.yml`:** on push to any branch except `main`. On ubuntu, lints with golangci-lint v2.12.2 for `GOOS=windows` and `GOOS=linux`, then runs `go test -race ./...` and `go test -count=20 ./internal/core/service`.
* **`pr.yml`:** on PRs to `main`.
    * `version` fails if `VERSION.txt` is empty or tag `v<version>` already exists on origin.
    * `test` lints and tests on ubuntu, windows and macos (with `CGO_ENABLED=1` for the race detector).
    * `binaries` (`make build-windows-amd64 build-linux-amd64 build-darwin-arm64`), `installer` (windows-latest, `make installer NUGET=nuget VERSION=…`, using `./.github/actions/setup-nsis`) and `image` (a linux/amd64 OCI tarball) upload artifacts kept for 1 day.
    * `pr-ok` ("PR checks passed") fails if any job failed, was cancelled or was skipped. **Make it the only required status check in branch protection** on `main`.
* **`release.yml`:** on push to `main`.
    * Checks the version tag again, then builds static (`CGO_ENABLED=0`, `-trimpath`, `-s -w`) binaries and the installer.
    * Release assets: `airframe_<ver>_windows_amd64.zip`, `airframe_<ver>_linux_amd64.tar.gz`, `airframe_<ver>_darwin_arm64.tar.gz` and `airframe-setup-windows_amd64.exe`.
    * Creates the GitHub release `v<ver>` ("airframe <ver>"). The notes are `git log --no-merges` since the previous `v*` tag, or all history if there is none.
    * Only after the release exists does it push `fireflycons/airframe:<ver>` and `:latest` to Docker Hub. That needs the repository secrets `DOCKERHUB_USERNAME` and `DOCKERHUB_TOKEN` (a Docker Hub personal access token with Read & Write scope).

## Deviations from the requirements

* The JSON uses camelCase tags. `IndiactedAirSpeed` in the outline has been corrected to `IndicatedAirSpeed` (`indicatedAirSpeed`).
* `Description` is `omitempty`.
* `Category` (`omitempty`) was added to `Aircraft` so clients can tell ground vehicles from aircraft.
* `AircraftData.Location` and `Radius` reflect the current observer, which can change at runtime through `POST /observer`, rather than always the command line.
* `POST /observer`, `GET /healthz`, `GET /screensaver`, and the `--listen` and `--config` flags were added.
* The observer is saved to a config file and, once saved, takes precedence over `--location`/`--radius`.
* Ground traffic on the radar is not labelled (see Web UI).
* The non-Windows runner uses `!windows` rather than `linux`.
* The installer also asks for the screensaver's inactivity delay.

## Dependencies and gotchas

* Go 1.26 (see `go.mod`).
* `github.com/fireflycons/geocoord` **v0.1.3+** is required. v0.1.2 added JSON marshalling, and v0.1.3 added `NewCoordinateFromString` and `String()`. airplaneslive's own go.mod still asks for v0.1.1, but because airframe requires the newer version, Go uses it.
* `github.com/fireflycons/airplaneslive` **v0.1.2+** is required; it's the first version whose geospatial `Aircraft` methods return errors instead of panicking.
* The airplanes.live point query allows a radius of up to 250 NM (`domain.MaxRadius`).
* Also uses: `spf13/cobra`, `golang.org/x/sys/windows/svc` (and `svc/mgr`), and `stretchr/testify` for tests.
* Building the installer locally needs Visual Studio's MSBuild, NSIS 3, and the NScurl and nsJSON plugins in NSIS's `Plugins\x86-unicode`.

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

* There is no authentication or CORS on either endpoint.
* A fresh install through the installer (`airframe-setup.exe`) has been checked: the service starts and its logs appear in the Application event log. Upgrade and uninstall, and the bare `install` → `sc start` → `sc stop` → `uninstall` cycle, have not been checked yet.
* `README.md` has no Linux, macOS or Docker installation notes yet.
* The Docker image is linux/amd64 only.
