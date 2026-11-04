"""Refresh the bundled GSI municipality names (no runtime network lookup needed).
Source: https://maps.gsi.go.jp/js/muni.js ; see README for attribution.
"""
import re
import urllib.request
from pathlib import Path

source = urllib.request.urlopen("https://maps.gsi.go.jp/js/muni.js", timeout=20).read().decode("utf-8-sig")
rows = []
for key, value in re.findall(r"GSI\.MUNI_ARRAY\[\"(\d+)\"\] = '([^']+)';", source):
    parts = value.split(",")
    if len(parts) == 4:
        rows.append(key + "\t" + parts[1] + parts[3].replace("　", ""))
if len(rows) < 1500:
    raise RuntimeError("Unexpected municipality data; existing asset left unchanged")
(Path(__file__).resolve().parents[1] / "src" / "municipalities.tsv").write_text("\n".join(rows) + "\n", encoding="utf-8")
print(f"Updated {len(rows)} municipality names")
