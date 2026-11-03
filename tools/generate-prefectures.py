"""Refresh the public-domain Natural Earth Japan admin-1 boundary resource."""
import json
from pathlib import Path
import struct
from urllib.request import urlopen

SOURCE = 'https://raw.githubusercontent.com/nvkelso/natural-earth-vector/master/geojson/ne_10m_admin_1_states_provinces_lines.geojson'

def main():
    with urlopen(SOURCE, timeout=60) as response:
        features = json.load(response)['features']
    lines = []
    for feature in features:
        if feature['properties'].get('ADM0_A3') != 'JPN':
            continue
        geometry = feature['geometry']
        if geometry['type'] == 'LineString':
            lines.append(geometry['coordinates'])
        elif geometry['type'] == 'MultiLineString':
            lines.extend(geometry['coordinates'])
    if len(lines) < 20:
        raise ValueError('Japan boundary data is missing; resource was not changed')
    result = bytearray(struct.pack('<i', len(lines)))
    for line in lines:
        result.extend(struct.pack('<i', len(line)))
        for longitude, latitude, *_ in line:
            if not (120 < longitude < 155 and 20 < latitude < 50):
                raise ValueError('Unexpected coordinate; resource was not changed')
            result.extend(struct.pack('<ff', longitude, latitude))
    target = Path(__file__).resolve().parents[1] / 'src' / 'prefectures.bin'
    target.write_bytes(result)
    print(f'{len(lines)} lines, {len(result)} bytes: {target}')

if __name__ == '__main__':
    main()
