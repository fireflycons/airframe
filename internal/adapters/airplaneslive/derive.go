package airplaneslive

import (
	"math"

	"github.com/fireflycons/airframe/internal/core/domain"
	api "github.com/fireflycons/airplaneslive"
	"github.com/fireflycons/geocoord"
)

// position sets Position, Distance and Bearing on ac. It uses the current
// position if present, otherwise the last known position. It returns false
// if neither is usable.
//
// Omitted lat/lon decode as 0,0, which is a valid coordinate, so 0,0 is
// treated as "no position".
func position(src api.Aircraft, observer geocoord.Coordinate, ac *domain.Aircraft) bool {
	if src.Lat != 0 || src.Lon != 0 {
		pos, err := src.Location()
		if err == nil {
			dist, derr := src.DistanceFrom(observer)
			brg, berr := src.BearingFrom(observer)
			if derr == nil && berr == nil {
				ac.Position, ac.Distance, ac.Bearing = pos, dist, brg
				return true
			}
		}
	}

	if lp := src.LastPosition; lp != nil && (lp.Lat != 0 || lp.Lon != 0) {
		pos, err := geocoord.NewCoordinate(lp.Lat, lp.Lon)
		if err == nil {
			ac.Position = pos
			ac.Distance = observer.DistanceTo(pos)
			ac.Bearing = observer.HeadingTo(pos)
			return true
		}
	}
	return false
}

// deriveSpeeds fills in Mach or true air speed when only one is reported,
// using the ISA speed of sound at the barometric altitude.
func deriveSpeeds(ac *domain.Aircraft) {
	if ac.OnGround {
		return
	}
	switch {
	case ac.Mach == 0 && ac.TrueAirSpeed > 0:
		ac.Mach = ac.TrueAirSpeed / isaSpeedOfSound(ac.BarometricAltitude)
	case ac.TrueAirSpeed == 0 && ac.Mach > 0:
		ac.TrueAirSpeed = ac.Mach * isaSpeedOfSound(ac.BarometricAltitude)
	}
}

// isaSpeedOfSound returns the speed of sound in knots at the given pressure
// altitude (feet) in the ISA troposphere / lower stratosphere.
func isaSpeedOfSound(altitudeFt float64) float64 {
	const (
		seaLevelTempK   = 288.15
		lapseRateKPerFt = 0.0019812
		tropopauseTempK = 216.65
		knotsPerSqrtK   = 38.967854 // sqrt(gamma*R) in knots
	)
	t := max(seaLevelTempK-lapseRateKPerFt*altitudeFt, tropopauseTempK)
	return knotsPerSqrtK * math.Sqrt(t)
}
