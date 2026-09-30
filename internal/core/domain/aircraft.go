// Package domain contains the core data types served by airframe.
package domain

import "github.com/fireflycons/geocoord"

// Aircraft is a single aircraft observed within the configured radius.
type Aircraft struct {
	// 24-bit Mode S / ICAO address (6 hex digits).
	Icao string `json:"icao"`

	// Callsign / flight id, up to 8 chars (DO-260B 2.2.8.2.6).
	Flight string `json:"flight"`

	// Aircraft registration
	Registration string `json:"registration"`

	// Name of airline
	Airline string `json:"airline"`

	// ICAO type code
	TypeCode string `json:"typeCode"`

	// Long type description (optional)
	Description string `json:"description,omitempty"`

	// Barometric altitude in feet
	BarometricAltitude float64 `json:"barometricAltitude"`

	// Geometric altitude in feet (WGS84).
	GeometricAltitude float64 `json:"geometricAltitude"`

	// Barometric climb rate, ft/min.
	BarometricClimbRate float64 `json:"barometricClimbRate"`

	// Geometric climb rate, ft/min.
	GeometricClimbRate float64 `json:"geometricClimbRate"`

	// Roll angle, degrees; negative is left roll.
	Roll float64 `json:"roll"`

	// True if craft is on the ground
	OnGround bool `json:"onGround"`

	// Ground speed in knots.
	GroundSpeed float64 `json:"groundSpeed"`

	// Indicated air speed in knots.
	IndicatedAirSpeed float64 `json:"indicatedAirSpeed"`

	// True air speed in knots.
	TrueAirSpeed float64 `json:"trueAirSpeed"`

	// Mach number
	Mach float64 `json:"mach"`

	// True track over ground, degrees.
	Track float64 `json:"track"`

	// Rate of change of track, degrees/second.
	TrackRate float64 `json:"trackRate"`

	// Current position of craft
	Position geocoord.Coordinate `json:"position"`

	// Distance of craft from observer location (command line) (NMI)
	Distance float64 `json:"distance"`

	// Initial bearing to craft from observer location (command line) (degrees)
	Bearing float64 `json:"bearing"`
}

// AircraftData is the payload returned to clients.
type AircraftData struct {
	// From command line processor
	Location geocoord.Coordinate `json:"location"`

	// From command line processor
	Radius float64 `json:"radius"`

	Aircraft *[]Aircraft `json:"aircraft"`
}
