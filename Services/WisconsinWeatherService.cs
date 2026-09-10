using System.Collections.Concurrent;
using JobBoard.Models;

namespace JobBoard.Services
{
    /// <summary>
    /// Sits between the pages and <see cref="NationalWeatherService"/>: resolves a coordinate to a
    /// Wisconsin county, caches what comes back, and keeps one shared statewide snapshot so that a
    /// hundred open browser tabs still only cost one round of API calls.
    /// </summary>
    public class WisconsinWeatherService
    {
        /// <summary>
        /// The reporting stations behind the live ribbon, roughly north to south. These are the
        /// airport observation sites for each city; if one of them goes quiet the ribbon shows a
        /// dash for it rather than failing the whole row.
        /// </summary>
        private static readonly (string Label, string StationId)[] RibbonStations =
        [
            ("Superior", "KSUW"),
            ("Ashland", "KASX"),
            ("Rhinelander", "KRHI"),
            ("Wausau", "KAUW"),
            ("Eau Claire", "KEAU"),
            ("Green Bay", "KGRB"),
            ("La Crosse", "KLSE"),
            ("Appleton", "KATW"),
            ("Madison", "KMSN"),
            ("Milwaukee", "KMKE")
        ];

        // Forecasts are issued a few times an hour and observations land hourly, so caching for a
        // few minutes costs nothing in freshness and keeps a click-happy visitor off the API.
        private static readonly TimeSpan PointCacheLifetime = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan SnapshotCacheLifetime = TimeSpan.FromMinutes(1);

        private readonly NationalWeatherService _nws;
        private readonly WisconsinGeography _geography;
        private readonly ILogger<WisconsinWeatherService> _logger;

        private readonly ConcurrentDictionary<string, (DateTimeOffset Expires, PointForecast Forecast)> _pointCache = new();
        private readonly SemaphoreSlim _snapshotLock = new(1, 1);
        private StatewideSnapshot? _snapshot;
        private DateTimeOffset _snapshotExpires = DateTimeOffset.MinValue;

        public WisconsinWeatherService(
            NationalWeatherService nws,
            WisconsinGeography geography,
            ILogger<WisconsinWeatherService> logger)
        {
            _nws = nws;
            _geography = geography;
            _logger = logger;
        }

        /// <summary>
        /// The weather at any point in Wisconsin. Never throws: a point outside the state or an
        /// unreachable API both come back as a panel that explains itself.
        /// </summary>
        public async Task<PointPanel> GetPanelAsync(GeoPoint point, string? label, CancellationToken cancellationToken)
        {
            var county = _geography.CountyAt(point);
            if (county is null)
            {
                return new PointPanel(
                    point,
                    label ?? point.ToString(),
                    string.Empty,
                    null,
                    "That point is outside Wisconsin. Click anywhere inside the state - or search for a place by name.",
                    OutsideWisconsin: true);
            }

            try
            {
                var forecast = await GetPointForecastAsync(point, county.Name, cancellationToken);
                var name = !string.IsNullOrWhiteSpace(label)
                    ? label!
                    : !string.IsNullOrWhiteSpace(forecast.PlaceName)
                        ? forecast.PlaceName
                        : point.ToString();

                return new PointPanel(point, name, county.Name, forecast, null, OutsideWisconsin: false);
            }
            catch (Exception exception) when (exception is WeatherUnavailableException or HttpRequestException)
            {
                _logger.LogWarning(exception, "Could not load a forecast for {Point}", point);
                return new PointPanel(
                    point,
                    label ?? point.ToString(),
                    county.Name,
                    null,
                    "The National Weather Service did not answer just now. Try that spot again in a moment.",
                    OutsideWisconsin: false);
            }
        }

        /// <summary>The live statewide ribbon, shared by every connected client.</summary>
        public async Task<StatewideSnapshot> GetStatewideSnapshotAsync(CancellationToken cancellationToken)
        {
            if (_snapshot is not null && DateTimeOffset.UtcNow < _snapshotExpires)
            {
                return _snapshot;
            }

            await _snapshotLock.WaitAsync(cancellationToken);
            try
            {
                // Another request may have refreshed it while this one waited for the lock.
                if (_snapshot is not null && DateTimeOffset.UtcNow < _snapshotExpires)
                {
                    return _snapshot;
                }

                // The refresh itself is deliberately not tied to the caller's token: this snapshot
                // is shared, and one browser tab closing mid-refresh should not cancel the fetch
                // every other tab is waiting on. HttpClient's timeout still bounds it.
                var readings = await Task.WhenAll(RibbonStations.Select(async station =>
                    new StationReading(
                        station.Label,
                        station.StationId,
                        _geography.FindPlace(station.Label)?.Point,
                        await _nws.GetStationObservationAsync(station.StationId, CancellationToken.None))));

                var alerts = await _nws.GetStateAlertsAsync(CancellationToken.None);
                var reporting = readings.Count(reading => reading.Current?.Temperature is not null);

                _snapshot = new StatewideSnapshot(
                    readings,
                    alerts,
                    DateTimeOffset.Now,
                    reporting == 0 ? "No station in Wisconsin is reporting to this app right now." : null);
                _snapshotExpires = DateTimeOffset.UtcNow.Add(SnapshotCacheLifetime);
                return _snapshot;
            }
            finally
            {
                _snapshotLock.Release();
            }
        }

        private async Task<PointForecast> GetPointForecastAsync(GeoPoint point, string countyName, CancellationToken cancellationToken)
        {
            // NWS forecast grids are about 2.5 km across, so rounding the key to two decimals
            // (~1 km) lets nearby clicks share an answer without ever crossing a grid cell.
            var key = string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"{point.Latitude:0.##},{point.Longitude:0.##}");

            if (_pointCache.TryGetValue(key, out var cached) && DateTimeOffset.UtcNow < cached.Expires)
            {
                return cached.Forecast;
            }

            var forecast = await _nws.GetPointForecastAsync(point, countyName, cancellationToken);
            _pointCache[key] = (DateTimeOffset.UtcNow.Add(PointCacheLifetime), forecast);

            if (_pointCache.Count > 500)
            {
                foreach (var expired in _pointCache.Where(entry => entry.Value.Expires < DateTimeOffset.UtcNow))
                {
                    _pointCache.TryRemove(expired.Key, out _);
                }
            }

            return forecast;
        }
    }
}
