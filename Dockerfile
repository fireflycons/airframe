# Linux image for airframe. The binary is built in a Go stage and copied into
# distroless/static, which holds only CA certificates, tzdata and a nonroot user.
# The CA certificates are needed for the HTTPS calls to airplanes.live and ipinfo.io.

FROM golang:1.26 AS build
WORKDIR /src
COPY go.mod go.sum ./
RUN go mod download
COPY cmd ./cmd
COPY internal ./internal
RUN CGO_ENABLED=0 GOOS=linux go build -trimpath -ldflags "-s -w" -o /out/airframe ./cmd/airframe
# distroless has no shell, so the config directory is made here. A named volume
# mounted on /data starts with this ownership, so nonroot can write to it.
RUN mkdir /data

FROM gcr.io/distroless/static-debian12:nonroot
COPY --from=build /out/airframe /airframe
COPY --from=build --chown=65532:65532 /data /data
VOLUME /data
EXPOSE 7700
# A --config given after the image name overrides this one.
ENTRYPOINT ["/airframe", "--config", "/data/airframe.json"]
