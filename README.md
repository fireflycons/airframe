# airframe

airframe shows the aircraft flying near you. It is a small server that polls [airplanes.live](https://airplanes.live) for aircraft within a radius of an observer location, and serves the results as JSON and as a live web page.

* **Web UI** on `http://localhost:7700/`: the nearest aircraft, a radar plot and a table of everything else in range. The settings dialog changes the location and radius.
* **JSON API**:
    * `GET /aircraft` returns the observer and the list of aircraft, with type, airline, altitude, speeds, track, and distance and bearing from the observer.
    * `POST /observer` changes the location and radius at runtime.
* **Low traffic.** airframe only polls while someone is using it. After 60 seconds with no requests it stops polling until the next request arrives.
* **Windows screensaver** (optional). It shows the web UI full screen, so your screen becomes a live display of the sky overhead.

It runs on Windows (as a service or from a console) and on Linux and macOS.

## Installing on Windows

### Requirements

* 64-bit Windows 10 or 11.
* Administrator rights, to install the service.
* For the screensaver only: [.NET Framework 4.8](https://dotnet.microsoft.com/download/dotnet-framework/net48) and the [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/). Both are already present on up-to-date Windows 10 and 11 machines.

### Run the installer

1. Download `airframe-setup-windows_amd64.exe` from the [latest release](https://github.com/fireflycons/airframe/releases/latest).
2. Run it and accept the elevation prompt.
3. On the configuration page, check or fill in:

    | Field | Meaning |
    |---|---|
    | **Latitude / Longitude** | Your location. The installer looks up your public IP and prefills these, showing the city, region and country it found. Correct them if they are wrong (IP location can be tens of miles out). Leave both blank to have airframe look up your location each time the service starts. |
    | **Radius** | How far out to look, in nautical miles, up to 250. Default 15. |
    | **Port** | The port for the web UI and API. Default 7700. |
    | **Install screensaver** | Installs the airframe screensaver and makes it your active screensaver. |
    | **Wait n minutes** | Shown when the screensaver is selected. How long the PC must be idle before the screensaver starts. |

4. Finish. The installer:
    * copies airframe to `C:\Program Files\Airframe`,
    * installs and starts the `airframe` Windows service, set to start automatically,
    * if selected, installs the screensaver and points it at `http://localhost:<port>/`.

5. Open the link on the final page, `http://localhost:7700/` by default, to see the web UI.

To upgrade, run the installer for the new version. It removes the existing service and installs it again with the settings you enter. The location and radius you enter replace any saved in the web UI.

### Notes

* **The screensaver is set for the account that elevated.** If you are a standard user and an administrator enters their credentials at the elevation prompt, the screensaver is configured for the administrator's account, not yours.
* If you later choose a different screensaver, Airframe may disappear from the list in Screen Saver Settings. Right-click `C:\Program Files\Airframe\Airframe.scr` and choose **Install**, or run the installer again.
* **There is no authentication.** Anyone who can reach the port can view the data and move the observer. If you don't want other machines on your network to use it, don't open the port in Windows Firewall.
* Changes made in the web UI's settings dialog are saved in `C:\ProgramData\Airframe\airframe.json` and survive restarts (see [Saved settings](#saved-settings)).
* The service logs to the Windows **Application** event log under the source `airframe`. Open Event Viewer → Windows Logs → Application, or run `Get-EventLog -LogName Application -Source airframe -Newest 20` in PowerShell.

### Uninstalling

Use **Settings > Apps** (or **Programs and Features**) and uninstall **Airframe**. This stops and removes the service and deletes the program files. If Airframe is still your screensaver it is switched off; the screensaver's own preferences are kept. The saved settings in `C:\ProgramData\Airframe` are kept too; delete that folder to remove them.

### Installing manually

Without the installer, download `airframe_<version>_windows_amd64.zip` from the release and extract `airframe.exe`. Then, from an elevated prompt:

```bat
airframe install --location 51.47,-0.4543 --radius 15 --listen :7700
sc start airframe
```

and to remove it:

```bat
airframe uninstall
```

The flags given to `install` are stored with the service. If `--location` is given, the location and radius are also written to the service's config file, replacing any saved there. The screensaver is only available through the installer.

## Running from a console

The same `airframe.exe` runs interactively when it is not started by the service manager. Stop the service first if it is using the same port.

```bat
airframe --location 51.47,-0.4543 --radius 10
```

Press Ctrl+C to stop. In Git Bash's default terminal Ctrl+C doesn't reach the program; use Windows Terminal or cmd instead.

| Flag | Default | Meaning |
|---|---|---|
| `--location` | auto-detect | `latitude,longitude` in decimal degrees. If omitted, it is looked up from your public IP. |
| `--radius` | `15` | Search radius in nautical miles, greater than 0 and at most 250. |
| `--interval` | `5s` | How often to poll airplanes.live while in use. |
| `--listen` | `:7700` | HTTP listen address. |
| `--config` | see below | Config file for saved settings. |

## Saved settings

airframe saves the location and radius whenever they are changed through the web UI or `POST /observer`, along with a cache of airline names, in a JSON config file. On the next start the saved location and radius are used, and `--location` and `--radius` only apply when nothing has been saved yet. Delete the file to go back to the command line values. If the file doesn't exist, airframe starts as if it were empty.

| Running as | Default config file |
|---|---|
| Windows service | `C:\ProgramData\Airframe\airframe.json` |
| Windows, from a console | `%AppData%\Airframe\airframe.json` |
| Linux | `$XDG_CONFIG_HOME/airframe/airframe.json`, or `~/.config/airframe/airframe.json` |
| macOS | `~/Library/Application Support/airframe/airframe.json` |
| Docker image | `/data/airframe.json`; mount a volume on `/data` to keep it, e.g. `docker run -v airframe-data:/data -p 7700:7700 fireflycons/airframe:<version>` |
| Helm chart | `/data/airframe.json` on a PersistentVolumeClaim (`persistence.*` in `values.yaml`) |

Use `--config <path>` to put it somewhere else.

## API

```sh
curl localhost:7700/aircraft

curl -X POST localhost:7700/observer \
  -d '{"location":{"lat":40.64,"lon":-73.78},"radius":20}'
```

`GET /aircraft` returns 503 if no data could be fetched yet. `POST /observer` returns 400 for missing or out-of-range values. Errors are returned as `{"error": "..."}`.

## Other platforms

Installation notes for Linux, macOS and Docker are still to come. Binaries for Linux (amd64) and macOS (arm64) are attached to each release.

## Building from source

Requires Go (see `go.mod` for the version).

```sh
go test -race ./...
go run ./cmd/airframe --location 51.47,-0.4543 --radius 10
```

`make help` lists the build targets. Building the Windows installer (`make installer`) needs a Windows host with Visual Studio's MSBuild and NSIS 3 with the NScurl and nsJSON plugins.

## Acknowledgements

* Aircraft data from [airplanes.live](https://airplanes.live).
* Location lookup from [ipinfo.io](https://ipinfo.io).
* The screensaver is based on [ZenProjects/Chromium-Web-Page-Screensaver](https://github.com/ZenProjects/Chromium-Web-Page-Screensaver).
