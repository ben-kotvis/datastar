using System.Globalization;

namespace JobBoard.Models
{
    /// <summary>
    /// A temperature carried in Celsius but renderable in either unit, so the page can flip
    /// between °F and °C without another trip to the server.
    /// </summary>
    public record Temperature(double Celsius)
    {
        public double Fahrenheit => Celsius * 9.0 / 5.0 + 32.0;

        public string F => Format(Fahrenheit);
        public string C => Format(Celsius);

        public static Temperature? FromCelsius(double? celsius) =>
            celsius.HasValue ? new Temperature(celsius.Value) : null;

        public static Temperature? FromFahrenheit(double? fahrenheit) =>
            fahrenheit.HasValue ? new Temperature((fahrenheit.Value - 32.0) * 5.0 / 9.0) : null;

        // Rounds to whole degrees. -0.4 rounds to negative zero, which .NET renders as "-0".
        private static string Format(double value)
        {
            var rounded = Math.Round(value, MidpointRounding.AwayFromZero);
            return rounded == 0
                ? "0°"
                : rounded.ToString("0", CultureInfo.InvariantCulture) + "°";
        }
    }

    /// <summary>A wind speed carried in km/h (the unit NWS observations report) and shown in mph.</summary>
    public record Speed(double Kph)
    {
        public double Mph => Kph * 0.6213711922;

        public string Imperial => Math.Round(Mph).ToString("0", CultureInfo.InvariantCulture) + " mph";
        public string Metric => Math.Round(Kph).ToString("0", CultureInfo.InvariantCulture) + " km/h";

        public static Speed? FromKph(double? kph) => kph.HasValue ? new Speed(kph.Value) : null;
    }

    /// <summary>The latest METAR-style observation from a real reporting station.</summary>
    public record CurrentConditions(
        string StationId,
        string StationName,
        DateTimeOffset? ObservedAt,
        string Summary,
        Temperature? Temperature,
        Temperature? DewPoint,
        Temperature? FeelsLike,
        double? RelativeHumidity,
        Speed? Wind,
        Speed? WindGust,
        int? WindDirectionDegrees,
        double? PressurePascals,
        double? VisibilityMeters)
    {
        /// <summary>Compass point for the wind direction, e.g. "WNW". Null when calm/unknown.</summary>
        public string? WindCompass
        {
            get
            {
                if (WindDirectionDegrees is null) return null;
                string[] points =
                [
                    "N", "NNE", "NE", "ENE", "E", "ESE", "SE", "SSE",
                    "S", "SSW", "SW", "WSW", "W", "WNW", "NW", "NNW"
                ];
                var index = (int)Math.Round(((WindDirectionDegrees.Value % 360) + 360) % 360 / 22.5) % points.Length;
                return points[index];
            }
        }

        public string? Humidity => RelativeHumidity is null
            ? null
            : Math.Round(RelativeHumidity.Value).ToString("0", CultureInfo.InvariantCulture) + "%";

        public string? Pressure => PressurePascals is null
            ? null
            : (PressurePascals.Value / 3386.389).ToString("0.00", CultureInfo.InvariantCulture) + " inHg";

        public string? Visibility => VisibilityMeters is null
            ? null
            : (VisibilityMeters.Value / 1609.344).ToString("0.#", CultureInfo.InvariantCulture) + " mi";
    }

    /// <summary>One named period of the NWS forecast ("Tonight", "Thursday", ...).</summary>
    public record ForecastPeriod(
        string Name,
        bool IsDaytime,
        DateTimeOffset? StartTime,
        Temperature? Temperature,
        int? PrecipitationChance,
        string Wind,
        string ShortForecast,
        string DetailedForecast);

    /// <summary>A single hour of the hourly forecast, used for the temperature trend line.</summary>
    public record HourlyPoint(DateTimeOffset Time, Temperature Temperature, int? PrecipitationChance);

    /// <summary>An active NWS watch, warning or advisory.</summary>
    public record WeatherAlert(
        string Id,
        string Event,
        string Severity,
        string Headline,
        string AreaDescription,
        DateTimeOffset? Onset,
        DateTimeOffset? Ends)
    {
        /// <summary>Severity bucket used for styling: warning, watch or advisory.</summary>
        public string Kind => Event.Contains("Warning", StringComparison.OrdinalIgnoreCase) ? "warning"
            : Event.Contains("Watch", StringComparison.OrdinalIgnoreCase) ? "watch"
            : "advisory";
    }

    /// <summary>Everything known about one point on the map.</summary>
    public record PointForecast(
        GeoPoint Point,
        string PlaceName,
        string CountyName,
        string GridId,
        string RadarStation,
        string TimeZone,
        CurrentConditions? Current,
        IReadOnlyList<ForecastPeriod> Periods,
        IReadOnlyList<HourlyPoint> Hourly,
        IReadOnlyList<WeatherAlert> Alerts,
        DateTimeOffset RetrievedAt);

    /// <summary>
    /// One city on the live statewide ribbon. <paramref name="Point"/> is the city itself rather
    /// than its reporting station, so clicking the chip selects the place a visitor recognises.
    /// </summary>
    public record StationReading(string Label, string StationId, GeoPoint? Point, CurrentConditions? Current);

    /// <summary>The statewide picture: a row of cities plus every active alert in Wisconsin.</summary>
    public record StatewideSnapshot(
        IReadOnlyList<StationReading> Stations,
        IReadOnlyList<WeatherAlert> Alerts,
        DateTimeOffset RetrievedAt,
        string? Error)
    {
        public StationReading? Warmest => Stations
            .Where(s => s.Current?.Temperature is not null)
            .OrderByDescending(s => s.Current!.Temperature!.Celsius)
            .FirstOrDefault();

        public StationReading? Coldest => Stations
            .Where(s => s.Current?.Temperature is not null)
            .OrderBy(s => s.Current!.Temperature!.Celsius)
            .FirstOrDefault();

        public double? SpreadFahrenheit
        {
            get
            {
                var warmest = Warmest?.Current?.Temperature;
                var coldest = Coldest?.Current?.Temperature;
                return warmest is null || coldest is null ? null : warmest.Fahrenheit - coldest.Fahrenheit;
            }
        }
    }

    /// <summary>View model for the statewide alert banner.</summary>
    public record AlertBanner(
        IReadOnlyList<WeatherAlert> Alerts,
        string CountyFipsList,
        DateTimeOffset RetrievedAt);

    /// <summary>View model for the point panel, including the failure and out-of-state cases.</summary>
    public record PointPanel(
        GeoPoint Point,
        string Label,
        string CountyName,
        PointForecast? Forecast,
        string? Error,
        bool OutsideWisconsin);
}
