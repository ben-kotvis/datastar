using System.Globalization;
using System.Text;
using JobBoard.Models;

namespace JobBoard.Helpers
{
    /// <summary>
    /// Small helpers for writing Datastar expressions from Razor. Datastar expressions are
    /// JavaScript, so numbers must be written with a dot no matter what culture the server runs
    /// under, and strings have to survive being embedded in a quoted expression.
    /// </summary>
    public static class ViewHelpers
    {
        /// <summary>A number as JavaScript sees it, regardless of server culture.</summary>
        public static string Js(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);

        /// <summary>A single-quoted JavaScript string literal, escaped.</summary>
        public static string Js(string? value)
        {
            var escaped = (value ?? string.Empty)
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("'", "\\'", StringComparison.Ordinal)
                .Replace("\r", " ", StringComparison.Ordinal)
                .Replace("\n", " ", StringComparison.Ordinal);

            return $"'{escaped}'";
        }

        /// <summary>"3:42 PM" in the forecast location's own time zone, when NWS supplies one.</summary>
        public static string LocalTime(DateTimeOffset? instant, string? timeZone = null)
        {
            if (instant is null)
            {
                return "unknown";
            }

            var value = instant.Value;
            if (!string.IsNullOrEmpty(timeZone))
            {
                try
                {
                    value = TimeZoneInfo.ConvertTime(value, TimeZoneInfo.FindSystemTimeZoneById(timeZone));
                }
                catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
                {
                    // Fall back to whatever offset the API sent.
                }
            }

            return value.ToString("h:mm tt", CultureInfo.InvariantCulture);
        }

        /// <summary>"12 minutes ago", for observation timestamps.</summary>
        public static string Ago(DateTimeOffset? instant)
        {
            if (instant is null)
            {
                return "at an unknown time";
            }

            var elapsed = DateTimeOffset.UtcNow - instant.Value.ToUniversalTime();
            return elapsed switch
            {
                { TotalMinutes: < 2 } => "just now",
                { TotalMinutes: < 60 } => $"{(int)elapsed.TotalMinutes} min ago",
                { TotalHours: < 24 } => $"{(int)elapsed.TotalHours} hr ago",
                _ => $"{(int)elapsed.TotalDays} days ago"
            };
        }
    }

    /// <summary>
    /// Turns the hourly forecast into an SVG polyline. Server-rendered on purpose: the whole point
    /// of the exercise is that the browser receives finished hypermedia, not data plus a chart
    /// library.
    /// </summary>
    /// <remarks>
    /// The line is drawn once for both unit systems. Celsius is an affine transform of
    /// Fahrenheit, so the normalised shape is identical - only the end labels differ.
    /// </remarks>
    public record Sparkline(
        string Line,
        string Area,
        string HighF,
        string HighC,
        string LowF,
        string LowC,
        string FirstHour,
        string LastHour,
        double Width,
        double Height)
    {
        public static Sparkline? From(IReadOnlyList<HourlyPoint> hours, double width = 320, double height = 64)
        {
            if (hours.Count < 2)
            {
                return null;
            }

            var values = hours.Select(hour => hour.Temperature.Fahrenheit).ToList();
            var low = values.Min();
            var high = values.Max();
            var span = Math.Max(high - low, 1);
            const double padding = 6;

            var line = new StringBuilder();
            for (var i = 0; i < values.Count; i++)
            {
                var x = width * i / (values.Count - 1);
                var y = height - padding - (values[i] - low) / span * (height - 2 * padding);
                line.Append(i == 0 ? string.Empty : " ")
                    .Append(x.ToString("0.#", CultureInfo.InvariantCulture))
                    .Append(',')
                    .Append(y.ToString("0.#", CultureInfo.InvariantCulture));
            }

            var points = line.ToString();
            var coldest = hours[values.IndexOf(low)].Temperature;
            var warmest = hours[values.IndexOf(high)].Temperature;

            return new Sparkline(
                points,
                string.Create(CultureInfo.InvariantCulture, $"0,{height:0.#} {points} {width:0.#},{height:0.#}"),
                warmest.F,
                warmest.C,
                coldest.F,
                coldest.C,
                ViewHelpers.LocalTime(hours[0].Time),
                ViewHelpers.LocalTime(hours[hours.Count - 1].Time),
                width,
                height);
        }
    }
}
