# Single Page Application

* Serve a single page application from `/` of the service's HTTP server.
* The SPA code and dependencies are to be embedded within the compiled service.
* The SPA service should use CSS and HTML5 as much as possible and minimize javascript dependencies.
* Where javascript dependencies may be required, choose the leanest, most modern alternatives.

## Operation

* The SPA will poll the `/aircraft` endpoint at the frequency set by the `--interval` command line flag.

## Layout

* Prominently present the details of the craft nearest the observation coordinate. Use 50% of the window area. You may search the web for layout ideas based on hardware devices that "show flights close to me".
* Choose icons to indicate ascent/decent/level flight
* Present other craft in a table ordered by distance from observation point.
* Design a radar-like widget that indicates the relative positions and direction of travel of each craft centered on the observation coordinate, and label each craft with the flight number.
* Provide a settings panel displaying the observation coordinate and radius in input fields, with a button to post the values back to the `/observer` endpoint.