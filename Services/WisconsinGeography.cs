using System.Globalization;
using System.Text;
using System.Text.Json;
using JobBoard.Models;

namespace JobBoard.Services
{
    /// <summary>
    /// Everything the app knows about Wisconsin's shape: the county boundaries it draws the map
    /// from, the projection that turns a click on that map back into a latitude/longitude, and
    /// the list of named places the search box offers.
    /// </summary>
    /// <remarks>
    /// Loaded once at startup from wwwroot/data (see tools/build_wisconsin_data.py for how those
    /// files are generated and where the geometry comes from).
    /// </remarks>
    public class WisconsinGeography
    {
        // A click within this many degrees (~1.1 km) of the state edge still counts as Wisconsin.
        // The rendered map is about 230 view units per degree, so this is a couple of pixels of
        // slop along a shoreline - enough to forgive a click aimed at the coast, and small enough
        // that a click across the Mississippi is still correctly called out-of-state.
        private const double EdgeToleranceDegrees = 0.01;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly List<WisconsinCounty> _counties;
        private readonly List<WisconsinPlace> _places;

        public WisconsinGeography(IWebHostEnvironment environment, ILogger<WisconsinGeography> logger)
        {
            var dataDirectory = Path.Combine(environment.WebRootPath ?? "wwwroot", "data");

            _counties = Load<CountyRecord>(Path.Combine(dataDirectory, "wisconsin-counties.json"), logger)
                .Select(record => new WisconsinCounty(
                    record.Fips,
                    record.Name,
                    record.LandAreaSqMi,
                    new GeoPoint(record.Centroid[0], record.Centroid[1]),
                    record.Rings))
                .OrderBy(county => county.Name, StringComparer.Ordinal)
                .ToList();

            // The two source datasets disagree about a few dozen counties - partly spelling
            // ("Fond Du Lac" against the Census "Fond du Lac"), partly towns that straddle a county
            // line. The boundaries win: a place is in whichever county actually contains it, so the
            // search list and the forecast panel can never name two different counties for a point.
            _places = Load<PlaceRecord>(Path.Combine(dataDirectory, "wisconsin-places.json"), logger)
                .Select(record =>
                {
                    var point = new GeoPoint(record.Lat, record.Lon);
                    return new WisconsinPlace(
                        record.Name,
                        CountyAt(point)?.Name ?? record.County,
                        record.Lat,
                        record.Lon);
                })
                .OrderBy(place => place.Name, StringComparer.Ordinal)
                .ToList();

            Map = MapProjection.Fit(_counties);

            logger.LogInformation("Wisconsin geography loaded: {Counties} counties, {Places} places",
                _counties.Count, _places.Count);
        }

        public IReadOnlyList<WisconsinCounty> Counties => _counties;

        public IReadOnlyList<WisconsinPlace> Places => _places;

        /// <summary>The projection shared by the rendered SVG and the browser's click handler.</summary>
        public MapProjection Map { get; }

        /// <summary>The county containing the point, or null when it falls outside Wisconsin.</summary>
        public WisconsinCounty? CountyAt(GeoPoint point)
        {
            foreach (var county in _counties)
            {
                if (county.Rings.Any(ring => RingContains(ring, point)))
                {
                    return county;
                }
            }

            // Just off a simplified shoreline or state line: snap to the nearest county instead of
            // telling somebody who clicked on Door County's coast that they missed the state.
            WisconsinCounty? nearest = null;
            var nearestDistance = double.MaxValue;
            foreach (var county in _counties)
            {
                foreach (var ring in county.Rings)
                {
                    var distance = DistanceToRing(ring, point);
                    if (distance < nearestDistance)
                    {
                        nearestDistance = distance;
                        nearest = county;
                    }
                }
            }

            return nearestDistance <= EdgeToleranceDegrees ? nearest : null;
        }

        /// <summary>Places whose name starts with, then merely contains, the search term.</summary>
        public IReadOnlyList<WisconsinPlace> SearchPlaces(string? term, int limit = 8)
        {
            if (string.IsNullOrWhiteSpace(term))
            {
                return [];
            }

            term = term.Trim();
            return _places
                .Where(place => place.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || place.County.Contains(term, StringComparison.OrdinalIgnoreCase))
                .OrderBy(place => place.Name.StartsWith(term, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(place => place.Name.Length)
                .ThenBy(place => place.Name, StringComparer.OrdinalIgnoreCase)
                .Take(limit)
                .ToList();
        }

        /// <summary>An exact-ish name match, used to resolve a typed search term to a place.</summary>
        public WisconsinPlace? FindPlace(string? name) =>
            string.IsNullOrWhiteSpace(name)
                ? null
                : _places.FirstOrDefault(place =>
                    place.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));

        /// <summary>Picks a place at random - the "surprise me" button on the map.</summary>
        public WisconsinPlace? RandomPlace() =>
            _places.Count == 0 ? null : _places[Random.Shared.Next(_places.Count)];

        /// <summary>
        /// The counties named by a set of alerts, as a ",55001,55003," style list the page can
        /// test with a single string comparison per county.
        /// </summary>
        /// <remarks>
        /// NWS puts affected areas in free text ("Dane; Rock; Green Lake"), so matching walks the
        /// longest county names first and blanks out what it consumes - otherwise "Green Lake"
        /// would also light up Green County.
        /// </remarks>
        public string CountyFipsUnderAlert(IEnumerable<WeatherAlert> alerts)
        {
            var areas = alerts
                .Select(alert => alert.AreaDescription)
                .Where(area => !string.IsNullOrWhiteSpace(area))
                .Select(area => new StringBuilder(area))
                .ToList();

            if (areas.Count == 0)
            {
                return string.Empty;
            }

            var matched = new SortedSet<string>();
            foreach (var county in _counties.OrderByDescending(county => county.Name.Length))
            {
                foreach (var area in areas)
                {
                    if (ConsumeCountyName(area, county.Name))
                    {
                        matched.Add(county.Fips);
                    }
                }
            }

            return matched.Count == 0 ? string.Empty : "," + string.Join(",", matched) + ",";
        }

        /// <summary>
        /// Looks for the county name as a whole word and overwrites it where found, so a later
        /// (shorter) county name cannot match inside text already claimed.
        /// </summary>
        private static bool ConsumeCountyName(StringBuilder area, string county)
        {
            var text = area.ToString();
            var found = false;

            for (var index = text.IndexOf(county, StringComparison.OrdinalIgnoreCase);
                 index >= 0;
                 index = text.IndexOf(county, index + county.Length, StringComparison.OrdinalIgnoreCase))
            {
                var startsWord = index == 0 || !char.IsLetter(text[index - 1]);
                var end = index + county.Length;
                var endsWord = end >= text.Length || !char.IsLetter(text[end]);
                if (!startsWord || !endsWord)
                {
                    continue;
                }

                found = true;
                for (var i = index; i < end; i++)
                {
                    area[i] = '\u0001';
                }
            }

            return found;
        }

        /// <summary>The SVG path data for a county, in projected view units.</summary>
        public string PathFor(WisconsinCounty county)
        {
            var path = new StringBuilder();
            foreach (var ring in county.Rings)
            {
                for (var i = 0; i < ring.Length; i += 2)
                {
                    var (x, y) = Map.ToView(ring[i + 1], ring[i]);
                    path.Append(i == 0 ? 'M' : 'L')
                        .Append(x.ToString("0.##", CultureInfo.InvariantCulture))
                        .Append(' ')
                        .Append(y.ToString("0.##", CultureInfo.InvariantCulture));
                }

                path.Append('Z');
            }

            return path.ToString();
        }

        /// <summary>Ray casting: counts ring crossings directly to the left of the point.</summary>
        private static bool RingContains(double[] ring, GeoPoint point)
        {
            var inside = false;
            var count = ring.Length / 2;
            for (int i = 0, j = count - 1; i < count; j = i++)
            {
                double xi = ring[i * 2], yi = ring[i * 2 + 1];
                double xj = ring[j * 2], yj = ring[j * 2 + 1];

                if (yi > point.Latitude != yj > point.Latitude
                    && point.Longitude < (xj - xi) * (point.Latitude - yi) / (yj - yi) + xi)
                {
                    inside = !inside;
                }
            }

            return inside;
        }

        /// <summary>Shortest distance from the point to a ring's edge, in degrees.</summary>
        private static double DistanceToRing(double[] ring, GeoPoint point)
        {
            var shortest = double.MaxValue;
            var count = ring.Length / 2;
            for (var i = 0; i < count; i++)
            {
                var j = (i + 1) % count;
                shortest = Math.Min(shortest, DistanceToSegment(
                    point.Longitude, point.Latitude,
                    ring[i * 2], ring[i * 2 + 1],
                    ring[j * 2], ring[j * 2 + 1]));
            }

            return shortest;
        }

        private static double DistanceToSegment(double px, double py, double x1, double y1, double x2, double y2)
        {
            var dx = x2 - x1;
            var dy = y2 - y1;
            var lengthSquared = dx * dx + dy * dy;

            var t = lengthSquared == 0 ? 0 : ((px - x1) * dx + (py - y1) * dy) / lengthSquared;
            t = Math.Clamp(t, 0, 1);

            var nearestX = x1 + t * dx;
            var nearestY = y1 + t * dy;
            return Math.Sqrt((px - nearestX) * (px - nearestX) + (py - nearestY) * (py - nearestY));
        }

        private static List<T> Load<T>(string path, ILogger logger)
        {
            if (!File.Exists(path))
            {
                logger.LogError("Wisconsin data file missing: {Path}. " +
                    "Regenerate it with tools/build_wisconsin_data.py.", path);
                return [];
            }

            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize<List<T>>(stream, JsonOptions) ?? [];
        }

        private sealed record CountyRecord(
            string Fips,
            string Name,
            double? LandAreaSqMi,
            double[] Centroid,
            double[][] Rings);

        private sealed record PlaceRecord(string Name, string County, double Lat, double Lon);
    }

    /// <summary>
    /// An equirectangular projection sized to Wisconsin's bounding box. Deliberately the simplest
    /// projection that keeps the state's proportions honest, because it inverts exactly - the
    /// browser needs to run it backwards to turn a click into a coordinate.
    /// </summary>
    public class MapProjection
    {
        private MapProjection(double lonMin, double latMax, double cosLatitude, double scale, double width, double height)
        {
            LonMin = lonMin;
            LatMax = latMax;
            CosLatitude = cosLatitude;
            Scale = scale;
            Width = width;
            Height = height;
        }

        public double LonMin { get; }
        public double LatMax { get; }
        public double CosLatitude { get; }
        public double Scale { get; }
        public double Width { get; }
        public double Height { get; }

        public static MapProjection Fit(IReadOnlyList<WisconsinCounty> counties, double width = 1000)
        {
            double lonMin = double.MaxValue, lonMax = double.MinValue;
            double latMin = double.MaxValue, latMax = double.MinValue;

            foreach (var ring in counties.SelectMany(county => county.Rings))
            {
                for (var i = 0; i < ring.Length; i += 2)
                {
                    lonMin = Math.Min(lonMin, ring[i]);
                    lonMax = Math.Max(lonMax, ring[i]);
                    latMin = Math.Min(latMin, ring[i + 1]);
                    latMax = Math.Max(latMax, ring[i + 1]);
                }
            }

            if (lonMin > lonMax || latMin > latMax)
            {
                // No geometry loaded; a square keeps the page renderable instead of dividing by zero.
                return new MapProjection(-92.9, 47.1, 1, width / 6, width, width);
            }

            var cosLatitude = Math.Cos((latMin + latMax) / 2 * Math.PI / 180);
            var scale = width / ((lonMax - lonMin) * cosLatitude);
            return new MapProjection(lonMin, latMax, cosLatitude, scale, width, (latMax - latMin) * scale);
        }

        /// <summary>Latitude/longitude to SVG view units.</summary>
        public (double X, double Y) ToView(double latitude, double longitude) =>
            ((longitude - LonMin) * CosLatitude * Scale, (LatMax - latitude) * Scale);

        /// <summary>SVG view units back to latitude/longitude - the same maths the browser runs.</summary>
        public GeoPoint ToGeo(double x, double y) =>
            new(LatMax - y / Scale, LonMin + x / (CosLatitude * Scale));

        public string ViewBox =>
            string.Create(CultureInfo.InvariantCulture, $"0 0 {Width:0.##} {Height:0.##}");
    }
}
