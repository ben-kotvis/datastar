# Wisconsin geography data

Two generated files, both read once at startup by `Services/WisconsinGeography.cs`.

| File | Contents |
| --- | --- |
| `wisconsin-counties.json` | All 72 counties: FIPS code, name, land area, centroid, and simplified boundary rings as flat `[lon, lat, lon, lat, ...]` arrays. |
| `wisconsin-places.json` | 756 Wisconsin cities, villages and towns with their county and coordinates. |

Regenerate them with:

```bash
curl -o counties.json https://raw.githubusercontent.com/plotly/datasets/master/geojson-counties-fips.json
curl -o us_cities.csv https://raw.githubusercontent.com/kelvins/US-Cities-Database/main/csv/us_cities.csv
python3 tools/build_wisconsin_data.py counties.json us_cities.csv
```

## Where the data comes from

- **County boundaries** are derived from the US Census Bureau's cartographic boundary files
  (TIGER/Line), which are works of the US government and in the public domain. They arrive here by
  way of [plotly/datasets](https://github.com/plotly/datasets) (MIT), which republishes them as
  GeoJSON keyed by FIPS code. The generator keeps only state FIPS 55, simplifies each ring with
  Douglas-Peucker at a 0.001 degree tolerance, and drops rings under about 3 sq km - which is why
  Door County keeps Washington Island but loses its unnamed rocks.
- **Place names and coordinates** come from
  [kelvins/US-Cities-Database](https://github.com/kelvins/US-Cities-Database) (MIT), filtered to
  `STATE_CODE = WI`.

They live under `wwwroot` rather than in a content folder so they need no csproj changes to be
copied on build or publish, and so the browser can fetch them too if you ever want to.

## Weather data

Nothing about the weather itself is stored here. Forecasts, observations and alerts are read live
from the [National Weather Service API](https://www.weather.gov/documentation/services-web-api),
which needs no API key but does ask that every caller identify itself. Set your own contact string
in `appsettings.json`:

```json
"Weather": { "UserAgent": "(your-app, you@example.com)" }
```
