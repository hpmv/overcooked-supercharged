"""Pin completed frozen-binary offline check output; never runs or qualifies a game round."""
import argparse
import hashlib
import json
import re
from pathlib import Path
from check_bowl_offmix import controller_evidence, pin, require

p = argparse.ArgumentParser(description=__doc__)
p.add_argument('bundle', type=Path)
p.add_argument('--fixture-assertions', type=int, required=True)
p.add_argument('--core-assertions', type=int, required=True)
a = p.parse_args()
evidence = controller_evidence(a.bundle)
manifest = json.loads((a.bundle / 'manifest.json').read_text(encoding='utf-8-sig'))
require(manifest['buildExitCode'] == 0 and manifest['sourceChangedDuringCapture'] is False, 'Unstable/failed frozen build')
fixture_text = (a.bundle / 'fixture-tests.txt').read_text(encoding='utf-8-sig')
fixture = json.loads(fixture_text[fixture_text.index('{'):])
require(fixture['ok'] is True and fixture['assemblyHash'].lower() == evidence['controller']['sha256'], 'Fixture output does not bind successful frozen binary')
require(fixture['total'] == sum(fixture['counts'].values()) == a.fixture_assertions, 'Unexpected fixture assertion count')
core = (a.bundle / 'core-tests.txt').read_text(encoding='utf-8-sig')
core_counts = list(map(int, re.findall(r'^PASS: (\d+) ', core, re.MULTILINE)))
require(len(core_counts) == 8 and sum(core_counts) == a.core_assertions and 'FAIL' not in core, 'Core selftest output incomplete or failed')
require(all((a.bundle / name).read_text(encoding='utf-8-sig').strip() == ''
            for name in ['fixture-tests-stderr.txt', 'core-tests-stderr.txt']), 'Test stderr is not empty')
paths = [a.bundle / name for name in ['manifest.json', 'build.log', 'fixture-build.log',
         'fixture-tests.txt', 'fixture-tests-stderr.txt', 'core-tests.txt', 'core-tests-stderr.txt']]
report = {'format': 'oc2-frozen-candidate-validation', 'candidate': manifest['candidate'],
          'controllerSha256': evidence['controller']['sha256'], 'controllerSourceTreeSha256': evidence['sourceTreeSha256'],
          'fixtureAssertions': fixture['total'], 'fixtureCounts': fixture['counts'],
          'coreAssertions': sum(core_counts), 'coreCounts': core_counts, 'allPassed': True,
          'files': [pin(path) for path in paths], 'pinningTool': pin(Path(__file__)),
          'qualification': 'Frozen binary/source and offline regression checks only; native trials and replay qualification are separate.'}
with (a.bundle / 'validation.json').open('x', encoding='utf-8') as dest:
    json.dump(report, dest, indent=2)
print(json.dumps({'allPassed': True, 'fixtureAssertions': fixture['total'], 'coreAssertions': sum(core_counts),
                  'controllerSha256': evidence['controller']['sha256']}))
