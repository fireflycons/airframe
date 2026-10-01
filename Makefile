LDFLAGS := -s -w
PKG     := ./cmd/airframe

SCR_DIR  := screensaver/windows
SCR_NAME := Webview2_WebPage_Screensaver
SCR_OUT  := $(SCR_DIR)/bin/Release
VSWHERE  := C:/Program Files (x86)/Microsoft Visual Studio/Installer/vswhere.exe
MSBUILD  ?= $(shell "$(VSWHERE)" -nologo -latest -requires Microsoft.Component.MSBuild -find "MSBuild/**/Bin/MSBuild.exe")

.PHONY: lint lint-windows lint-linux test build build-windows-amd64 build-linux-amd64 build-darwin-arm64 build-screensaver

# Lint both platform variants whatever the host, so the build-tagged files
# in internal/cli are always checked.
lint: lint-windows lint-linux

lint-windows: private export GOOS = windows
lint-linux:   private export GOOS = linux

lint-windows lint-linux:
	golangci-lint run ./...

test: lint
	go test -v -race ./...
	go test -v -count=20 ./internal/core/service

build: test
	$(MAKE) build-windows-amd64 build-linux-amd64 build-darwin-arm64 build-screensaver

# GOOS/GOARCH are set as exported target-specific variables rather than
# inline "VAR=x cmd", so the recipes work under cmd.exe as well as sh.
build-windows-amd64: private export GOOS   = windows
build-windows-amd64: private export GOARCH = amd64
build-linux-amd64:   private export GOOS   = linux
build-linux-amd64:   private export GOARCH = amd64
build-darwin-arm64:  private export GOOS   = darwin
build-darwin-arm64:  private export GOARCH = arm64

build-windows-amd64:
	go build -ldflags "$(LDFLAGS)" -o bin/windows-amd64/airframe.exe $(PKG)

build-linux-amd64 build-darwin-arm64:
	go build -ldflags "$(LDFLAGS)" -o bin/$(GOOS)-$(GOARCH)/airframe $(PKG)

# The screensaver is .NET Framework 4.8 and needs Visual Studio's MSBuild, so
# it only builds on a Windows host. The project's post-build event is disabled
# because it regenerates the README screenshots in assets/; the rename to .scr
# is done here instead. The .scr is self-contained: Costura embeds the managed
# WebView2 DLLs, and the native WebView2Loader.dll is embedded and extracted at
# run time.
ifeq ($(OS),Windows_NT)
build-screensaver:
	$(if $(MSBUILD),,$(error MSBuild not found; install Visual Studio or set MSBUILD))
	"$(SCR_DIR)/nuget.exe" restore $(SCR_DIR)/packages.config -PackagesDirectory $(SCR_DIR)/packages -NonInteractive
	"$(MSBUILD)" $(SCR_DIR)/$(SCR_NAME).csproj -p:Configuration=Release -p:PostBuildEvent= -nologo -v:minimal
	powershell -NoProfile -Command "New-Item -ItemType Directory -Force bin/windows-amd64 | Out-Null; Copy-Item $(SCR_OUT)/$(SCR_NAME).exe bin/windows-amd64/$(SCR_NAME).scr"
else
build-screensaver:
	@echo "Skipping build-screensaver: requires a Windows host"
endif
