using System.Text.Json;
using JobBoard.Models;

namespace JobBoard.Services
{
    /// <summary>
    /// A thin client over api.weather.gov - the National Weather Service's public API. No key, no
    /// quota, and it covers every square metre of Wisconsin, which is exactly what this page needs.
    /// </summary>
    /// <remarks>
    /// The responses are read as <see cref="JsonDocument"/> rather than mapped to DTOs on purpose:
    /// almost every measurement in the NWS schema is a {"unitCode","value"} pair whose value is
    /// frequently null, and half the payload is links this app never follows.
    /// </remarks>
    public class NationalWeatherService
    {
        /// <summary>The named HttpClient configured in Program.cs.</summary>
        public const string HttpClientName = "nws";

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<NationalWeatherService> _logger;

        public NationalWeatherService(IHttpClientFactory httpClientFactory, ILogger<NationalWeatherService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        /// <summary>
        /// Resolves a coordinate to an NWS grid point, then pulls the forecast, the hourly
        /// forecast, the nearest station's latest observation and any active alerts for that spot.
        /// </summary>
        public async Task<PointForecast> GetPointForecastAsync(GeoPoint point, string countyName, CancellationToken cancellationToken)
        {
            using var pointDocument = await GetAsync($"points/{point.ToApiString()}", cancellationToken)
                ?? throw new WeatherUnavailableException(
                    "The National Weather Service could not resolve that coordinate to a forecast grid.");

            if (!pointDocument.RootElement.TryGetProperty("properties", out var properties))
            {
                throw new WeatherUnavailableException(
                    "The National Weather Service returned a forecast grid this app could not read.");
            }

            var forecastUrl = Text(properties, "forecast");
            var hourlyUrl = Text(properties, "forecastHourly");
            var stationsUrl = Text(properties, "observationStations");

            var placeName = properties.TryGetProperty("relativeLocation", out var relative)
                && relative.TryGetProperty("properties", out var relativeProperties)
                    ? $"{Text(relativeProperties, "city")}, {Text(relativeProperties, "state")}".Trim(',', ' ')
                    : string.Empty;

            // One round trip each, all independent, so let them race.
            var forecastTask = GetAsync(forecastUrl, cancellationToken);
            var hourlyTask = GetAsync(hourlyUrl, cancellationToken);
            var currentTask = GetNearestObservationAsync(stationsUrl, cancellationToken);
            var alertsTask = GetAlertsAsync($"alerts/active?point={point.ToApiString()}", cancellationToken);

            await Task.WhenAll(forecastTask, hourlyTask, currentTask, alertsTask);

            using var forecastDocument = await forecastTask;
            using var hourlyDocument = await hourlyTask;

            return new PointForecast(
                point,
                placeName,
                countyName,
                Text(properties, "gridId"),
                Text(properties, "radarStation"),
                Text(properties, "timeZone"),
                await currentTask,
                ReadPeriods(forecastDocument),
                ReadHourly(hourlyDocument),
                await alertsTask,
                DateTimeOffset.Now);
        }

        /// <summary>The latest observation from a station, by its four letter identifier.</summary>
        public async Task<CurrentConditions?> GetStationObservationAsync(string stationId, CancellationToken cancellationToken)
        {
            using var document = await GetAsync($"stations/{stationId}/observations/latest", cancellationToken);
            return document is null ? null : ReadObservation(document, stationId);
        }

        /// <summary>Every watch, warning and advisory currently active in Wisconsin.</summary>
        public Task<IReadOnlyList<WeatherAlert>> GetStateAlertsAsync(CancellationToken cancellationToken) =>
            GetAlertsAsync("alerts/active?area=WI", cancellationToken);

        private async Task<CurrentConditions?> GetNearestObservationAsync(string stationsUrl, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(stationsUrl))
            {
                return null;
            }

            using var stations = await GetAsync(stationsUrl, cancellationToken);
            if (stations is null
                || !stations.RootElement.TryGetProperty("features", out var features)
                || features.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            // Stations come back nearest first. The closest one is not always reporting, so walk
            // down the list a little way before giving up on current conditions.
            foreach (var feature in features.EnumerateArray().Take(4))
            {
                if (!feature.TryGetProperty("properties", out var stationProperties))
                {
                    continue;
                }

                var stationId = Text(stationProperties, "stationIdentifier");
                if (string.IsNullOrEmpty(stationId))
                {
                    continue;
                }

                using var observation = await GetAsync(
                    $"stations/{stationId}/observations/latest", cancellationToken);
                if (observation is null)
                {
                    continue;
                }

                var conditions = ReadObservation(observation, stationId, Text(stationProperties, "name"));
                if (conditions?.Temperature is not null)
                {
                    return conditions;
                }
            }

            return null;
        }

        private async Task<IReadOnlyList<WeatherAlert>> GetAlertsAsync(string url, CancellationToken cancellationToken)
        {
            using var document = await GetAsync(url, cancellationToken);
            if (document is null
                || !document.RootElement.TryGetProperty("features", out var features)
                || features.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var alerts = new List<WeatherAlert>();
            foreach (var feature in features.EnumerateArray())
            {
                if (!feature.TryGetProperty("properties", out var properties))
                {
                    continue;
                }

                alerts.Add(new WeatherAlert(
                    Text(properties, "id"),
                    Text(properties, "event"),
                    Text(properties, "severity"),
                    Text(properties, "headline"),
                    Text(properties, "areaDesc"),
                    Timestamp(properties, "onset") ?? Timestamp(properties, "effective"),
                    Timestamp(properties, "ends") ?? Timestamp(properties, "expires")));
            }

            return alerts;
        }

        private static CurrentConditions? ReadObservation(JsonDocument document, string stationId, string? stationName = null)
        {
            if (!document.RootElement.TryGetProperty("properties", out var properties))
            {
                return null;
            }

            return new CurrentConditions(
                stationId,
                string.IsNullOrEmpty(stationName) ? stationId : stationName,
                Timestamp(properties, "timestamp"),
                Text(properties, "textDescription"),
                Temperature.FromCelsius(Measurement(properties, "temperature")),
                Temperature.FromCelsius(Measurement(properties, "dewpoint")),
                Temperature.FromCelsius(Measurement(properties, "windChill") ?? Measurement(properties, "heatIndex")),
                Measurement(properties, "relativeHumidity"),
                Speed.FromKph(Measurement(properties, "windSpeed")),
                Speed.FromKph(Measurement(properties, "windGust")),
                Measurement(properties, "windDirection") is { } direction ? (int?)Math.Round(direction) : null,
                Measurement(properties, "barometricPressure"),
                Measurement(properties, "visibility"));
        }

        private static IReadOnlyList<ForecastPeriod> ReadPeriods(JsonDocument? document)
        {
            if (document is null
                || !document.RootElement.TryGetProperty("properties", out var properties)
                || !properties.TryGetProperty("periods", out var periods)
                || periods.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var result = new List<ForecastPeriod>();
            foreach (var period in periods.EnumerateArray().Take(10))
            {
                result.Add(new ForecastPeriod(
                    Text(period, "name"),
                    period.TryGetProperty("isDaytime", out var isDaytime)
                        && isDaytime.ValueKind == JsonValueKind.True,
                    Timestamp(period, "startTime"),
                    ReadForecastTemperature(period),
                    Measurement(period, "probabilityOfPrecipitation") is { } chance ? (int?)Math.Round(chance) : null,
                    $"{Text(period, "windSpeed")} {Text(period, "windDirection")}".Trim(),
                    Text(period, "shortForecast"),
                    Text(period, "detailedForecast")));
            }

            return result;
        }

        private static IReadOnlyList<HourlyPoint> ReadHourly(JsonDocument? document)
        {
            if (document is null
                || !document.RootElement.TryGetProperty("properties", out var properties)
                || !properties.TryGetProperty("periods", out var periods)
                || periods.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var result = new List<HourlyPoint>();
            foreach (var period in periods.EnumerateArray().Take(24))
            {
                var temperature = ReadForecastTemperature(period);
                var time = Timestamp(period, "startTime");
                if (temperature is null || time is null)
                {
                    continue;
                }

                result.Add(new HourlyPoint(
                    time.Value,
                    temperature,
                    Measurement(period, "probabilityOfPrecipitation") is { } chance ? (int?)Math.Round(chance) : null));
            }

            return result;
        }

        /// <summary>Forecast periods report whole degrees in the unit named alongside them.</summary>
        private static Temperature? ReadForecastTemperature(JsonElement period)
        {
            if (!period.TryGetProperty("temperature", out var value))
            {
                return null;
            }

            double? reading = value.ValueKind switch
            {
                JsonValueKind.Number => value.GetDouble(),
                JsonValueKind.Object => Value(value),
                _ => null
            };

            if (reading is null)
            {
                return null;
            }

            return Text(period, "temperatureUnit").Equals("C", StringComparison.OrdinalIgnoreCase)
                ? Temperature.FromCelsius(reading)
                : Temperature.FromFahrenheit(reading);
        }

        private async Task<JsonDocument?> GetAsync(string url, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(url))
            {
                return null;
            }

            try
            {
                using var client = _httpClientFactory.CreateClient(HttpClientName);
                using var response = await client.GetAsync(url, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("api.weather.gov returned {Status} for {Url}", (int)response.StatusCode, url);
                    return null;
                }

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            }
            catch (Exception exception) when (exception is HttpRequestException or JsonException or TaskCanceledException
                && !cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(exception, "Request to api.weather.gov failed: {Url}", url);
                return null;
            }
        }

        /// <summary>Reads a {"unitCode": ..., "value": ...} measurement, which is often null.</summary>
        private static double? Measurement(JsonElement parent, string name) =>
            parent.TryGetProperty(name, out var measurement) ? Value(measurement) : null;

        private static double? Value(JsonElement measurement) =>
            measurement.ValueKind == JsonValueKind.Object
                && measurement.TryGetProperty("value", out var value)
                && value.ValueKind == JsonValueKind.Number
                    ? value.GetDouble()
                    : null;

        private static string Text(JsonElement parent, string name) =>
            parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : string.Empty;

        private static DateTimeOffset? Timestamp(JsonElement parent, string name) =>
            parent.TryGetProperty(name, out var value)
                && value.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(value.GetString(), out var parsed)
                    ? parsed
                    : null;
    }

    /// <summary>Raised when api.weather.gov cannot answer at all, so the page can say so plainly.</summary>
    public class WeatherUnavailableException : Exception
    {
        public WeatherUnavailableException(string message) : base(message)
        {
        }
    }
}
