// The Wisconsin map's projection, mirrored from MapProjection on the server.
//
// The server draws the county paths; the browser needs the same maths in reverse so that a click
// anywhere on the state becomes a latitude/longitude, and so the marker can follow the $lat/$lon
// signals without asking the server where to sit. The constants come from the server as
// window.wisconsinMap - see Pages/Weather.cshtml.
(function () {
    'use strict';

    function config() {
        return window.wisconsinMap || null;
    }

    function project(latitude, longitude) {
        var c = config();
        if (!c) {
            return { x: 0, y: 0 };
        }

        return {
            x: (longitude - c.lonMin) * c.cos * c.scale,
            y: (c.latMax - latitude) * c.scale
        };
    }

    function unproject(x, y) {
        var c = config();
        if (!c) {
            return { lat: 0, lon: 0 };
        }

        return {
            lat: c.latMax - y / c.scale,
            lon: c.lonMin + x / (c.cos * c.scale)
        };
    }

    // Client coordinates to the SVG's own user space, which is what the projection expects.
    function toUserSpace(event, svg) {
        var matrix = svg.getScreenCTM();
        if (!matrix) {
            return null;
        }

        if (typeof DOMPoint === 'function') {
            return new DOMPoint(event.clientX, event.clientY).matrixTransform(matrix.inverse());
        }

        var legacy = svg.createSVGPoint();
        legacy.x = event.clientX;
        legacy.y = event.clientY;
        return legacy.matrixTransform(matrix.inverse());
    }

    /** Where the marker should sit for the current $lat/$lon signals. */
    window.wiMarkerTransform = function (latitude, longitude) {
        var point = project(latitude, longitude);
        return 'translate(' + point.x.toFixed(2) + ' ' + point.y.toFixed(2) + ')';
    };

    /** The coordinate a click landed on, rounded to about 10 m. */
    window.wiMapPoint = function (event, element) {
        var svg = element.ownerSVGElement || element;
        var local = toUserSpace(event, svg);
        if (!local) {
            return { lat: 0, lon: 0 };
        }

        var geo = unproject(local.x, local.y);
        return {
            lat: Math.round(geo.lat * 10000) / 10000,
            lon: Math.round(geo.lon * 10000) / 10000
        };
    };

    /** A coordinate formatted the way the header shows it, e.g. "43.075° N, 89.384° W". */
    window.wiFormatPoint = function (latitude, longitude) {
        return Math.abs(latitude).toFixed(3) + '° ' + (latitude < 0 ? 'S' : 'N') + ', ' +
            Math.abs(longitude).toFixed(3) + '° ' + (longitude < 0 ? 'W' : 'E');
    };
})();
