package domain

import (
	"fmt"

	"github.com/fireflycons/geocoord"
)

// MaxRadius is the largest search radius in nautical miles
// (the limit of the airplanes.live point query).
const MaxRadius = 250

// Observer is the location and radius around which aircraft are reported.
type Observer struct {
	Location geocoord.Coordinate `json:"location"`
	Radius   float64             `json:"radius"`
}

// Validate checks that the radius is in range. The location needs no check
// because a geocoord.Coordinate can only be constructed with in-range values.
func (o Observer) Validate() error {
	if o.Radius <= 0 || o.Radius > MaxRadius {
		return fmt.Errorf("invalid radius %g: must be greater than 0 and at most %d", o.Radius, MaxRadius)
	}
	return nil
}
