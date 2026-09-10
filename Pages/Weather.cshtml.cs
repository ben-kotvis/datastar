using System.Text.Json;
using System.Text.Json.Serialization;
using JobBoard.Models;
using JobBoard.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StarFederation.Datastar.DependencyInjection;

namespace JobBoard.Pages
{
    /// <summary>
    /// A live weather map of Wisconsin, driven entirely by Datastar: every click, keystroke and
    /// background refresh comes back from the server as finished HTML over server-sent events.
    /// </summary>
    /// <remarks>
    /// The initial GET renders only the shell. Both panels fill themselves in from the handlers
    /// below via <c>data-init</c>, which keeps first paint instant even when api.weather.gov is
    /// having a slow afternoon.
    /// </remarks>
    public class WeatherModel : PageModel
    {
        /// <summary>Where the map starts: the State Capitol in Madison.</summary>
        public static readonly GeoPoint DefaultPoint = new(43.0747, -89.3844);

        private const string DefaultLabel = "State Capitol, Madison";

        /// <summary>How often the live ribbon refreshes for a connected browser.</summary>
        private static readonly TimeSpan LiveInterval = TimeSpan.FromSeconds(60);

        private readonly WisconsinGeography _geography;
        private readonly WisconsinWeatherService _weather;
        private readonly RazorViewToStringRenderer _renderer;
        private readonly IDatastarService _datastar;
        private readonly ILogger<WeatherModel> _logger;

        public WeatherModel(
            WisconsinGeography geography,
            WisconsinWeatherService weather,
            RazorViewToStringRenderer renderer,
            IDatastarService datastar,
            ILogger<WeatherModel> logger)
        {
            _geography = geography;
            _weather = weather;
            _renderer = renderer;
            _datastar = datastar;
            _logger = logger;
        }

        public WisconsinGeography Geography => _geography;

        public string StartLabel => DefaultLabel;

        /// <summary>The projection constants the browser needs to invert a click into a coordinate.</summary>
        public string MapConfigJson => JsonSerializer.Serialize(new
        {
            lonMin = _geography.Map.LonMin,
            latMax = _geography.Map.LatMax,
            cos = _geography.Map.CosLatitude,
            scale = _geography.Map.Scale
        });

        public void OnGet()
        {
        }

        /// <summary>The weather at whatever point the $lat/$lon signals now hold.</summary>
        public async Task<IActionResult> OnGetPointAsync(CancellationToken cancellationToken)
        {
            var signals = await _datastar.ReadSignalsAsync<WeatherSignals>(cancellationToken);
            var point = new GeoPoint(
                signals?.Lat ?? DefaultPoint.Latitude,
                signals?.Lon ?? DefaultPoint.Longitude);

            var panel = await _weather.GetPanelAsync(point, signals?.Label, cancellationToken);
            await PatchAsync("_WeatherPoint", panel, cancellationToken);
            return new EmptyResult();
        }

        /// <summary>Somewhere in Wisconsin, chosen at random. 756 places to land on.</summary>
        public async Task<IActionResult> OnGetRandomAsync(CancellationToken cancellationToken)
        {
            var place = _geography.RandomPlace();
            var panel = place is null
                ? await _weather.GetPanelAsync(DefaultPoint, DefaultLabel, cancellationToken)
                : await _weather.GetPanelAsync(place.Point, place.Display, cancellationToken);

            await PatchAsync("_WeatherPoint", panel, cancellationToken);
            return new EmptyResult();
        }

        /// <summary>Typeahead over every named place in the state.</summary>
        public async Task<IActionResult> OnGetSearchAsync(CancellationToken cancellationToken)
        {
            var signals = await _datastar.ReadSignalsAsync<WeatherSignals>(cancellationToken);
            var matches = _geography.SearchPlaces(signals?.Query);
            await PatchAsync("_WeatherPlaces", matches, cancellationToken);
            return new EmptyResult();
        }

        /// <summary>
        /// A stream that stays open for as long as the tab does, pushing the statewide ribbon and
        /// the active alert banner every minute.
        /// </summary>
        public async Task<IActionResult> OnGetLiveAsync(CancellationToken cancellationToken)
        {
            await _datastar.StartServerEventStreamAsync(cancellationToken);
            _logger.LogDebug("Live weather stream opened");

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var snapshot = await _weather.GetStatewideSnapshotAsync(cancellationToken);
                    var banner = new AlertBanner(
                        snapshot.Alerts,
                        _geography.CountyFipsUnderAlert(snapshot.Alerts),
                        snapshot.RetrievedAt);

                    await PatchAsync("_WeatherRibbon", snapshot, cancellationToken);
                    await PatchAsync("_WeatherAlerts", banner, cancellationToken);

                    await Task.Delay(LiveInterval, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                // The browser closed the tab or navigated away. Nothing to clean up.
                _logger.LogDebug("Live weather stream closed by the client");
            }

            return new EmptyResult();
        }

        private async Task PatchAsync<TModel>(string view, TModel model, CancellationToken cancellationToken)
        {
            var html = await _renderer.RenderViewToStringAsync(view, model, PageContext);
            await _datastar.PatchElementsAsync(html, cancellationToken);
        }

        /// <summary>The signals this page sends up with every request.</summary>
        public record WeatherSignals
        {
            [JsonPropertyName("lat")]
            public double? Lat { get; init; }

            [JsonPropertyName("lon")]
            public double? Lon { get; init; }

            [JsonPropertyName("label")]
            public string? Label { get; init; }

            [JsonPropertyName("query")]
            public string? Query { get; init; }
        }
    }
}
