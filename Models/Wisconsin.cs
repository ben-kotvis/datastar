namespace JobBoard.Models
{
    /// <summary>A WGS84 coordinate. Latitude first, the way the NWS API wants it.</summary>
    public record GeoPoint(double Latitude, double Longitude)
    {
        /// <summary>The form api.weather.gov expects in a /points/{lat},{lon} path.</summary>
        public string ToApiString() =>
            $"{Latitude.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)}," +
            $"{Longitude.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)}";

        public override string ToString() =>
            $"{Math.Abs(Latitude):0.###}° {(Latitude < 0 ? 'S' : 'N')}, " +
            $"{Math.Abs(Longitude):0.###}° {(Longitude < 0 ? 'W' : 'E')}";
    }

    /// <summary>
    /// One of Wisconsin's 72 counties. Rings are flat [lon, lat, lon, lat, ...] arrays;
    /// a county has more than one when it owns islands (Door, Bayfield) or a split shoreline.
    /// </summary>
    public record WisconsinCounty(
        string Fips,
        string Name,
        double? LandAreaSqMi,
        GeoPoint Centroid,
        IReadOnlyList<double[]> Rings);

    /// <summary>A named Wisconsin place: city, village or town.</summary>
    public record WisconsinPlace(string Name, string County, double Latitude, double Longitude)
    {
        public GeoPoint Point => new(Latitude, Longitude);
        public string Display => $"{Name}, {County} County";
    }
}
