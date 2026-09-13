import gzip,hashlib,json,re
from pathlib import Path

for version,frames in ((16,{5730}),(17,{2824,9611})):
    path=Path(f'artifacts/native-round-v{version}/trial001.jsonl.gz');wanted=set(frames);pattern=re.compile(rb'"gameplayFrame"\s*:\s*(-?\d+)');proof=[]
    with gzip.open(path,'rb') as f:
        for line in f:
            if b'"kind":"call"' not in line[:100].replace(b' ',b''):continue
            m=pattern.search(line)
            if not m:continue
            frame=int(m.group(1))
            if frame in wanted:
                response=json.loads(line)['response'];out=Path(f'artifacts/v{version}-onion-chain-gf{frame}.json')
                data=json.dumps(response,separators=(',',':')).encode();out.write_bytes(data)
                proof.append({'frame':frame,'path':str(out),'sha256':hashlib.sha256(data).hexdigest(),'traceCallLineSha256':hashlib.sha256(line).hexdigest()});wanted.remove(frame)
            if not wanted:break
    if wanted:raise RuntimeError(f'Missing frames: {wanted}')
    Path(f'artifacts/v{version}-onion-chain-witnesses.json').write_text(json.dumps({'source':str(path),'qualification':'Unmodified response values extracted from closed native call records; no state restoration. Parent projection pins full compressed source hash.','snapshots':proof},indent=2))
    print(version,proof,flush=True)
