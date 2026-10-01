LDFLAGS := -s -w
PKG     := ./cmd/airframe

.PHONY: lint lint-windows lint-linux test build build-windows-amd64 build-linux-amd64 build-darwin-arm64

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
	$(MAKE) build-windows-amd64 build-linux-amd64 build-darwin-arm64

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
