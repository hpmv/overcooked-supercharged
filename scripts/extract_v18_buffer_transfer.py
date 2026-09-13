"""Bounded, file-only fixture extraction. Original complete call-line bytes are retained."""
import gzip
import json
from pathlib import Path
from archive_closed_gate_logs import locked_read

root = Path(__file__).resolve().parents[1]
source = root / 'artifacts/native-prefix-v18-buffer1/trial001.jsonl.gz'
target = root / 'artifacts/v18-buffer1-transfer-native-fixtures.jsonl.gz'
assert not target.exists()
frames = set()
with locked_read(source) as raw, gzip.GzipFile(fileobj=raw) as stream, target.open('xb') as output, gzip.GzipFile(fileobj=output, mode='wb', mtime=0) as compressed:
    for line in stream:
        row = json.loads(line)
        if row.get('kind') != 'call':
            continue
        frame = row.get('response', {}).get('state', {}).get('gameplayFrame', -1)
        if 2093 <= frame <= 2155 or 2497 <= frame <= 2738:
            compressed.write(line)
            frames.add(frame)
print('Captured native frames:', len(frames))
