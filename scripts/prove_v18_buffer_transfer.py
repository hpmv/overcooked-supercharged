"""Pin the failed native witness and source-only fixed-deadline regression evidence."""
import gzip
import hashlib
import json
from pathlib import Path
from archive_closed_gate_logs import locked_read

root = Path(__file__).resolve().parents[1]
target = root / 'artifacts/v18-buffer1-transfer-budget-proof.json'
assert not target.exists()
source = root / 'artifacts/native-prefix-v18-buffer1/trial001.jsonl.gz'
fixture = root / 'artifacts/v18-buffer1-transfer-native-fixtures.jsonl.gz'
selected_hash = hashlib.sha256()
selected_count = 0
with locked_read(source) as raw:
    source_hash = hashlib.file_digest(raw, 'sha256').hexdigest()
    raw.seek(0)
    with gzip.GzipFile(fileobj=raw) as stream:
        for line in stream:
            row = json.loads(line)
            if row.get('kind') != 'call':
                continue
            frame = row.get('response', {}).get('state', {}).get('gameplayFrame', -1)
            if 2093 <= frame <= 2155 or 2497 <= frame <= 2738:
                selected_count += 1
                selected_hash.update(line)
with locked_read(fixture) as raw, gzip.GzipFile(fileobj=raw) as stream:
    fixture_hash = hashlib.file_digest(stream, 'sha256').hexdigest()
assert selected_count == 305 and fixture_hash == selected_hash.hexdigest()

lines = (root / 'artifacts/v18-buffer1-transfer-tests.txt').read_text(encoding='utf-8-sig').splitlines()
assert lines[:3] == ['Existing sausage lifecycle: 64', 'Fixed transfer budget: 47', 'Recorded transfer samples: 311']
evidence = json.loads(lines[3])
files = [
    'artifacts/native-prefix-v18-buffer1/summary.json',
    'artifacts/native-prefix-v18-buffer1/trial001.stderr.txt',
    'artifacts/planner-candidate-v18/OvercookedTAS.Controller.dll',
    'artifacts/v18-buffer1-gf2497.json', 'artifacts/v18-buffer1-gf2577.json',
    'artifacts/v18-buffer1-gf2724.json', 'artifacts/v18-buffer1-gf2738.json',
    'artifacts/v18-buffer1-lifecycle-events.json', 'artifacts/v18-buffer1-p3-route-events.json',
    'artifacts/v18-buffer1-transfer-native-fixtures.jsonl.gz', 'artifacts/v18-buffer1-transfer-tests.txt',
    'artifacts/sausage-transfer-check/OvercookedTAS.Controller.dll',
    'controller/CarnivalSausageBuffer.cs', 'controller/CarnivalSausageTransfer.cs',
    'controller/CarnivalSausageBufferTests.cs', 'controller/CarnivalSausageTransferTests.cs',
    'scripts/SausageTransferCheck/Program.cs', 'scripts/SausageTransferCheck/SausageTransferCheck.csproj',
    'scripts/extract_v18_buffer_transfer.py', 'scripts/prove_v18_buffer_transfer.py',
    'docs/SAUSAGE-BUFFER.md', 'docs/SAUSAGE-TRANSFER-BUDGET.md'
]
pins = [{'path': str(source), 'sha256': source_hash, 'bytes': source.stat().st_size}]
for relative in files:
    path = root / relative
    with locked_read(path) as raw:
        digest = hashlib.file_digest(raw, 'sha256').hexdigest()
    pins.append({'path': str(path), 'sha256': digest, 'bytes': path.stat().st_size})
summary = json.loads((root / files[0]).read_text(encoding='utf-8-sig'))
assert summary['controllerSha256'].lower() == next(p['sha256'] for p in pins if p['path'] == str(root / files[2]))
proof = {
    'format': 'oc2-sausage-transfer-fixed-budget-proof', 'version': 1,
    'qualification': 'Failed native prefix remains failed; source-only regression proof, no native rerun or score improvement established.',
    'nativeFailure': {'score': 156, 'delivered': 2, 'parkingStartFrame': 2497, 'pickupFrame': 2724,
                      'placementStartFrame': 2725, 'oldTimeoutFrame': 2738, 'cleanupFrame': 2739,
                      'oldAggregateFrames': 240, 'exactFood': 178, 'foodOrdinal': 177, 'owner': 3, 'source': 45, 'counter': 38},
    'newAdmission': evidence,
    'checks': {'existingLifecycle': 64, 'budgetAndMutation': 47, 'nativeSamplesAssertions': 311,
               'nativeSamples': 305, 'nativeRanges': [[2093, 2155], [2497, 2738]],
               'fixtureOriginalCallLineBytesVerified': True, 'fixtureUncompressedSha256': fixture_hash,
               'nativeParkingCompleted': False, 'syntheticParkingCompletionClearlySeparated': True},
    'artifacts': pins
}
with target.open('x', encoding='utf-8') as output:
    json.dump(proof, output, indent=2)
    output.write('\n')
print(target)
print(hashlib.sha256(target.read_bytes()).hexdigest())
