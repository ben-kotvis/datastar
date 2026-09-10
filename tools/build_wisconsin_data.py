#!/usr/bin/env python3
"""Builds the Wisconsin geography datasets used by the Weather Radar page.

Outputs (written into wwwroot/data/):
  wisconsin-counties.json  county name, FIPS, simplified boundary rings, centroid
  wisconsin-places.json    Wisconsin cities/villages with coordinates

Sources (both fetched, never vendored blindly -- see wwwroot/data/README.md):
  counties  https://raw.githubusercontent.com/plotly/datasets/master/geojson-counties-fips.json
            (derived from US Census TIGER/Line cartographic boundaries, public domain)
  places    https://raw.githubusercontent.com/kelvins/US-Cities-Database (MIT)

Usage: python3 tools/build_wisconsin_data.py counties.json us_cities.csv
"""
import csv
import json
import sys
from pathlib import Path

WI_FIPS = "55"
# Simplification tolerance in degrees (~0.005 deg ~ 400-500 m). Small enough that
# county shapes stay recognizable, large enough to keep the payload light.
TOLERANCE = 0.001
# Rings smaller than this (in square degrees) are dropped: slivers and unnamed
# rocks that would only add noise. Roughly 3 sq km.
MIN_RING_AREA = 3.0e-5


def perpendicular_distance(pt, start, end):
    (x, y), (x1, y1), (x2, y2) = pt, start, end
    dx, dy = x2 - x1, y2 - y1
    if dx == 0 and dy == 0:
        return ((x - x1) ** 2 + (y - y1) ** 2) ** 0.5
    return abs(dy * (x - x1) - dx * (y - y1)) / ((dx * dx + dy * dy) ** 0.5)


def simplify(points, tolerance):
    """Iterative Douglas-Peucker (recursion would blow the stack on long rings)."""
    if len(points) < 3:
        return list(points)
    keep = [False] * len(points)
    keep[0] = keep[-1] = True
    stack = [(0, len(points) - 1)]
    while stack:
        first, last = stack.pop()
        if last <= first + 1:
            continue
        worst, worst_dist = -1, 0.0
        for i in range(first + 1, last):
            d = perpendicular_distance(points[i], points[first], points[last])
            if d > worst_dist:
                worst, worst_dist = i, d
        if worst_dist > tolerance:
            keep[worst] = True
            stack.append((first, worst))
            stack.append((worst, last))
    return [p for p, k in zip(points, keep) if k]


def ring_area(ring):
    """Absolute shoelace area, in square degrees."""
    total = 0.0
    for i in range(len(ring)):
        x1, y1 = ring[i]
        x2, y2 = ring[(i + 1) % len(ring)]
        total += x1 * y2 - x2 * y1
    return abs(total) / 2.0


def ring_centroid(ring):
    """Area-weighted centroid; falls back to the mean vertex for degenerate rings."""
    cx = cy = signed = 0.0
    for i in range(len(ring)):
        x1, y1 = ring[i]
        x2, y2 = ring[(i + 1) % len(ring)]
        cross = x1 * y2 - x2 * y1
        signed += cross
        cx += (x1 + x2) * cross
        cy += (y1 + y2) * cross
    if abs(signed) < 1e-12:
        return (sum(p[0] for p in ring) / len(ring), sum(p[1] for p in ring) / len(ring))
    signed /= 2.0
    return (cx / (6.0 * signed), cy / (6.0 * signed))


def polygons_of(geometry):
    """Normalizes Polygon / MultiPolygon into a list of outer rings."""
    kind, coords = geometry["type"], geometry["coordinates"]
    if kind == "Polygon":
        return [coords[0]]
    if kind == "MultiPolygon":
        return [poly[0] for poly in coords]
    raise ValueError(f"unexpected geometry {kind}")


def build_counties(geojson_path):
    features = json.loads(Path(geojson_path).read_text())["features"]
    counties = []
    for feature in features:
        if not feature["id"].startswith(WI_FIPS):
            continue
        rings = []
        for raw in polygons_of(feature["geometry"]):
            ring = [(round(lon, 4), round(lat, 4)) for lon, lat in raw]
            if ring_area(ring) < MIN_RING_AREA:
                continue
            ring = simplify(ring, TOLERANCE)
            if len(ring) >= 4:
                rings.append(ring)
        if not rings:
            continue
        biggest = max(rings, key=ring_area)
        lon, lat = ring_centroid(biggest)
        counties.append({
            "fips": feature["id"],
            "name": feature["properties"]["NAME"],
            "landAreaSqMi": feature["properties"].get("CENSUSAREA"),
            "centroid": [round(lat, 4), round(lon, 4)],
            # Flat [lon, lat, lon, lat, ...] per ring: half the JSON of nested pairs.
            "rings": [[c for pt in ring for c in pt] for ring in rings],
        })
    counties.sort(key=lambda c: c["name"])
    return counties


def build_places(cities_csv):
    places = []
    with open(cities_csv, newline="", encoding="utf-8") as handle:
        for row in csv.DictReader(handle):
            if row["STATE_CODE"] != "WI":
                continue
            places.append({
                "name": row["CITY"],
                "county": row["COUNTY"],
                "lat": round(float(row["LATITUDE"]), 4),
                "lon": round(float(row["LONGITUDE"]), 4),
            })
    places.sort(key=lambda p: p["name"])
    return places


def main():
    if len(sys.argv) != 3:
        print(__doc__)
        return 1
    out = Path(__file__).resolve().parent.parent / "wwwroot" / "data"
    out.mkdir(exist_ok=True)

    counties = build_counties(sys.argv[1])
    (out / "wisconsin-counties.json").write_text(
        json.dumps(counties, separators=(",", ":")) + "\n")
    print(f"counties: {len(counties)} "
          f"({sum(len(r) // 2 for c in counties for r in c['rings'])} points)")

    places = build_places(sys.argv[2])
    (out / "wisconsin-places.json").write_text(
        json.dumps(places, separators=(",", ":")) + "\n")
    print(f"places: {len(places)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
