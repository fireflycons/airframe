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

FROM gcr.io/distroless/static-debian12:nonroot
COPY --from=build /out/airframe /airframe
EXPOSE 7700
ENTRYPOINT ["/airframe"]
