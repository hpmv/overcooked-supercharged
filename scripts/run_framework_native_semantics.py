"""Run bounded CPU semantics checks; never launch/connect to a game."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess

ROOT = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser()
parser.add_argument('--native-source', type=Path, default=Path(r'M:\projects\AssetRipper\Source\0Bins\AssetRipper.Tools.SystemTester\Release\Ripped\ExportedProject\Assets\Scripts\Assembly-CSharp'))
args = parser.parse_args()
out = ROOT / 'artifacts/framework-native-semantics'
generated = out / 'native'
generated.mkdir(parents=True, exist_ok=True)
sources = []
def pin(path):
    return {'path': str(path.resolve()), 'sha256': hashlib.sha256(path.read_bytes()).hexdigest()}
for name in ['LogicalButtonBase.cs', 'GateLogicalButton.cs', 'ILogicalButton.cs', 'ILogicalElement.cs', 'RoundData.cs', 'RoundDataBase.cs']:
    source = args.native_source / name
    (generated / name).write_bytes(source.read_bytes())
    sources.append(pin(source))

array_source = args.native_source / 'ArrayUtils.cs'
array_text = array_source.read_text(encoding='utf-8-sig')
def method(signature):
    start = array_text.index(signature)
    brace = array_text.index('{', start)
    depth = 1
    end = brace + 1
    while depth:
        depth += (array_text[end] == '{') - (array_text[end] == '}')
        end += 1
    return array_text[start:end]
native_methods = method('public static KeyValuePair<int, T> GetWeightedRandomElement<T>(this T[] _items, Generic<float, int, T> _weight)') + '\n' + method('public static U Collapse<T, U>(this T[] _array, Generic<U, T, U> _converter)')
(generated / 'ArrayUtils.cs').write_text('using System.Collections.Generic;\npublic static class ArrayUtils\n{\n' + native_methods + '\n}\n', encoding='utf-8')
sources.append(pin(array_source))
project = ROOT / 'scripts/FrameworkNativeSemanticsCheck/FrameworkNativeSemanticsCheck.csproj'
run = subprocess.run(['dotnet', 'run', '--project', str(project), '--configuration', 'Release'], cwd=ROOT, text=True, capture_output=True)
(out / 'stdout.txt').write_text(run.stdout, encoding='utf-8')
(out / 'stderr.txt').write_text(run.stderr, encoding='utf-8')
report = {'exitCode': run.returncode, 'classification': 'Offline source mechanics only; Unity RNG/clock/focus/object APIs are test doubles.', 'nativeSourceFiles': sources, 'generatedNativeFiles': [pin(p) for p in sorted(generated.glob('*.cs'))], 'testedSources': [pin(ROOT / p) for p in ['framework/patch/TASLogicalButton.cs', 'framework/patch/AlteredComponents/WarpableRoundData.cs']], 'harness': [pin(p) for p in sorted(project.parent.glob('*')) if p.is_file()]}
(out / 'manifest.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
print(run.stdout, end='')
print(run.stderr, end='')
raise SystemExit(run.returncode)
