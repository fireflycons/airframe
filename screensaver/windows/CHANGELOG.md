# Changelog

All notable changes to this project will be documented in this file.

## [Unreleased]

- Forked from [muro-dot/Webview2_WebPage_Screensaver](https://github.com/muro-dot/Webview2_WebPage_Screensaver) at commit `352827d`. Many thanks to muro-dot for building and sharing this screensaver, which this project builds on.
- Removed the Korean localisation (the application is now English only) and the GitHub update checker.
- Renamed the build output to `Airframe_ScreenSaver.scr` and the settings dialog to "AirFrame Screensaver".
- Added an About button (below Delete) that credits the original screensaver.
- Added a per-screen "Multiple URLs" setting: cycle through all URLs (the default), or show the first available URL, falling back to the next one and then the clock, and retrying the first URL at the set interval until it responds.
