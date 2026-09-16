"""File-only generation from the unchanged Story11 native inspection receipt."""
import hashlib
import json
from pathlib import Path

root = Path(__file__).resolve().parents[1]
source = root / 'artifacts/framework-migration/story11-discovery-b/load.json'
raw = source.read_bytes()
inspection = json.loads(raw)['steps'][6]['response']
initial = {e['EntityId']: e for e in inspection['initialRegistry']}
latest = {e['EntityId']: e for e in inspection['registry']}
assert set(initial) == set(range(1, 51)) == set(latest)
entities = []
for id, e in sorted(latest.items()):
    entities.append(dict(id=id, name=e['Name'], requiredComponents=[c for c in e['Components'] if c not in ('TriggerRecorder', 'SpawnableEntityCollection')],
                         primarySpawnName=(e.get('SpawnNames') or [None])[0]))
reference = dict(version=1, kind='actual-native-story11-name-and-component-reference', source=str(source.relative_to(root)),
                 sourceSha256=hashlib.sha256(raw).hexdigest(), scene='s_sushi_1_1', entities=entities)
reference['settledChefPositions'] = [dict(id=id, position=latest[id]['Pos']) for id in range(43, 47)]
(root/'framework/headless/Reference/Story11NativeReference.json').write_text(json.dumps(reference, indent=2)+'\n')
(root/'framework/headless/Reference/Story11NativeRegistryFixture.json').write_text(json.dumps({k: inspection[k] for k in ['freshLevelLoadObserved','initialRegistry','registry']}, indent=2)+'\n')
lines = []
for id, e in sorted(initial.items()):
    p=e['Pos']; coords=', '.join(format(float(p[a]), '.17g')+'f' for a in ['X','Y','Z'])
    lines.append(f'            entityRecords.CapturedInitialPositions[{id}] = new Vector3({coords});')
path = root/'framework/controller/Data/Levels/Story11Four.cs'
template = path.read_text()
template = template.replace('            // GENERATED_NATIVE_POSITIONS', '\n'.join(lines))
path.write_text(template)
