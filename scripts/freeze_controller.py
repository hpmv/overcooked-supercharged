"""Build a new controller candidate from a captured source tree, never the live tree.

This does not install a plugin, connect to a game, or assert that tests passed.
Build logs, captured sources and binary hashes remain together in a new directory.
"""
import argparse
import datetime
import hashlib
import json
import subprocess
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
PROJECT = 'OvercookedTAS.Controller.csproj'
ASSEMBLY = 'OvercookedTAS.Controller.dll'


def sha(data):
    return hashlib.sha256(data).hexdigest()


def capture(directory):
    # The current controller project has flat compiled sources. Reject future
    # nested sources instead of silently omitting them from the frozen build.
    for path in directory.rglob('*.cs'):
        relative = path.relative_to(directory)
        if len(relative.parts) > 1 and relative.parts[0] not in ('obj', 'bin', 'protocol-tests'):
            raise ValueError('Unaccounted nested controller source: ' + str(relative))
    paths = sorted([directory / PROJECT, *directory.glob('*.cs')], key=lambda path: path.name)
    return {'controller/' + path.name: path.read_bytes() for path in paths}


def write_capture(destination, sources):
    rows = []
    for relative, data in sorted(sources.items()):
        path = destination / 'source' / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        with path.open('xb') as stream:
            stream.write(data)
        rows.append({'path': relative, 'sha256': sha(data), 'bytes': len(data)})
    encoding = ''.join(row['path'] + '\0' + row['sha256'] + '\n' for row in rows)
    return rows, sha(encoding.encode('utf-8'))


def freeze(destination, candidate):
    destination = destination.resolve()
    artifact_root = (ROOT / 'artifacts').resolve()
    if not destination.is_relative_to(artifact_root) or destination == artifact_root:
        raise ValueError('Candidate output must be a new child of workspace artifacts.')
    if destination.exists():
        raise FileExistsError('Refusing to overwrite an existing candidate: ' + str(destination))
    sources = capture(ROOT / 'controller')
    destination.mkdir(parents=True, exist_ok=False)
    rows, tree_hash = write_capture(destination, sources)
    sdk = subprocess.run(['dotnet', '--version'], cwd=ROOT, capture_output=True, text=True, check=True).stdout.strip()
    # A moving edit is visible in this qualification. The compilation still
    # uses only the captured files, so later edits cannot alter its source set.
    source_changed = capture(ROOT / 'controller') != sources
    # An output directory that contains the project makes the SDK's default
    # output exclusions hide every source file. Build inside the copied
    # project's normal bin tree, then publish those exact bytes to the bundle.
    command = ['dotnet', 'build', str(destination / 'source/controller' / PROJECT),
               '-c', 'Release', '--nologo', '--no-incremental']
    with (destination / 'build.log').open('xb') as log:
        result = subprocess.run(command, cwd=ROOT, stdout=log, stderr=subprocess.STDOUT, check=False)
    manifest = {'format': 'oc2-controller-frozen-candidate', 'candidate': candidate,
        'createdUtc': datetime.datetime.now(datetime.timezone.utc).isoformat(),
        'buildCommand': command, 'buildExitCode': result.returncode,
        'dotnetSdkVersion': sdk, 'freezerSha256': sha(Path(__file__).read_bytes()),
        'buildSource': 'Captured source/controller, isolated from subsequent live source edits',
        'sourceChangedDuringCapture': source_changed,
        'controllerSourceTreeSha256': tree_hash,
        'sourceHashEncoding': 'SHA256 of sorted relative path, NUL, lowercase file SHA256, LF; includes compiled flat sources and project, excludes generated obj/bin and noncompiled protocol-tests',
        'sourceFiles': rows, 'validation': 'Build only; tests and native trials must be reported separately',
        'buildLogSha256': sha((destination / 'build.log').read_bytes())}
    if result.returncode == 0:
        compiled = destination / 'source/controller/bin/Release/net10.0'
        for path in sorted(compiled.glob('OvercookedTAS.Controller.*')):
            if path.is_file():
                with (destination / path.name).open('xb') as stream:
                    stream.write(path.read_bytes())
        manifest['controllerSha256'] = sha((destination / ASSEMBLY).read_bytes())
        manifest['binaryFiles'] = [{'path': path.name, 'sha256': sha(path.read_bytes()), 'bytes': path.stat().st_size}
                                   for path in sorted(destination.glob('OvercookedTAS.Controller.*')) if path.is_file()]
        for row in rows:
            if sha((destination / 'source' / row['path']).read_bytes()) != row['sha256']:
                raise RuntimeError('Captured source changed during compilation; candidate cannot be frozen.')
    with (destination / 'manifest.json').open('x', encoding='utf-8') as stream:
        json.dump(manifest, stream, indent=2)
        stream.write('\n')
    return manifest


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--out', required=True, type=Path)
    parser.add_argument('--candidate', required=True)
    args = parser.parse_args()
    result = freeze(args.out, args.candidate)
    print(json.dumps({key: result.get(key) for key in ('candidate', 'buildExitCode',
        'controllerSha256', 'controllerSourceTreeSha256', 'sourceChangedDuringCapture', 'validation')}, indent=2))
    return result['buildExitCode']


if __name__ == '__main__':
    raise SystemExit(main())
