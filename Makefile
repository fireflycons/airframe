LDFLAGS := -s -w
PKG     := ./cmd/airframe

SCR_DIR  := screensaver/windows
SCR_PROJ := Airframe_Screensaver
SCR_NAME := Airframe_ScreenSaver
SCR_OUT  := $(SCR_DIR)/bin/Release
VSWHERE  := C:/Program Files (x86)/Microsoft Visual Studio/Installer/vswhere.exe
MSBUILD  ?= $(shell "$(VSWHERE)" -nologo -latest -requires Microsoft.Component.MSBuild -find "MSBuild/**/Bin/MSBuild.exe")
MAKENSIS ?= C:/Program Files (x86)/NSIS/makensis.exe
VERSION  ?= $(shell git describe --tags --always --dirty)

.PHONY: help
help: ## Display this help.
ifeq ($(OS),Windows_NT)
	@powershell -NoProfile -Command "\
		Write-Host 'Usage:' -ForegroundColor White; \
		Write-Host '  make ' -NoNewline; Write-Host '<target>' -ForegroundColor Cyan; \
		Select-String -Path '$(MAKEFILE_LIST)' -Pattern '^[a-zA-Z_0-9-]+:.*?##', '^##@' | ForEach-Object { \
			$$line = $$_.Line; \
			if ($$line -match '^([a-zA-Z_0-9-]+):.*?##\s*(.*)') { \
				Write-Host ('  {0,-15}' -f $$Matches[1]) -NoNewline -ForegroundColor Cyan; \
				Write-Host $$Matches[2]; \
			} elseif ($$line -match '^##@\s*(.*)') { \
				Write-Host ('`n' + $$Matches[1]) -ForegroundColor White; \
			} \
		}"
else
	@awk 'BEGIN {FS = ":.*##"; printf "\nUsage:\n  make \033[36m<target>\033[0m\n"} /^[a-zA-Z_0-9-]+:.*?##/ { printf "  \033[36m%-15s\033[0m %s\n", $$1, $$2 } /^##@/ { printf "\n\033[1m%s\033[0m\n", substr($$0, 5) } ' $(MAKEFILE_LIST)
endif

.PHONY: lint lint-windows lint-linux test build build-windows-amd64 build-linux-amd64 build-darwin-arm64 build-screensaver installer

all: lint test build installer ## Run all checks, tests, and builds.

# Lint both platform variants whatever the host, so the build-tagged files
# in internal/cli are always checked.
lint: lint-windows lint-linux  ## Run golangci-lint on all source files.

lint-windows: private export GOOS = windows
lint-linux:   private export GOOS = linux

lint-windows lint-linux:
	golangci-lint run ./...

test: lint ## Run all tests with the race detector enabled, and run the core service tests 20 times to catch flakiness.
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
	"$(MSBUILD)" $(SCR_DIR)/$(SCR_PROJ).csproj -p:Configuration=Release -p:PostBuildEvent= -nologo -v:minimal
	powershell -NoProfile -Command "New-Item -ItemType Directory -Force bin/windows-amd64 | Out-Null; Copy-Item $(SCR_OUT)/$(SCR_NAME).exe bin/windows-amd64/$(SCR_NAME).scr"

# The NSIS installer bundles the Windows binary and the screensaver into
# bin/windows-amd64/airframe-setup.exe. It needs the NScurl and nsJSON plugins.
installer: build-windows-amd64 build-screensaver
	"$(MAKENSIS)" -V2 -WX -DVERSION=$(VERSION) installer/windows/airframe.nsi
else
build-screensaver installer:
	@echo "Skipping $@: requires a Windows host"
endif
