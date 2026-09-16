"""Extract unchanged native-W callbacks; no native or network access."""
import base64, hashlib, json
from pathlib import Path
from compare_framework_frames import records

trace=Path('artifacts/framework-migration/native-w-v10/exchange.jsonl')
folder=Path('artifacts/framework-paused-pose-fixtures');folder.mkdir(exist_ok=True)
epoch=0; selected={2:'candidate-0',5:'candidate-3'}; streams={}; metadata=[]
for row,where in records(trace):
    if row.get('kind')=='session':
        initial=base64.b64decode(row['initialSetupProtobuf']);assert hashlib.sha256(initial).hexdigest()==row['initialSetupSha256']
        (folder/'initial.pb').write_bytes(initial)
    if row.get('kind')=='exchange':
        if any(m['Type'] in (1,2) for m in row['output'].get('ServerMessages',[])):epoch+=1
    if epoch not in selected:continue
    if epoch not in streams:streams[epoch]=[]
    if row.get('kind') not in ('exchange','control'):continue
    if row.get('kind')=='control' and row['request'].get('command') not in ('actions-clear','step'):continue
    streams[epoch].append({'source':where,**row})
    if row.get('kind')=='exchange' and row['output'].get('FrameNumber')==31 and row['output']['LastFramePaused'] and row['output']['NextFramePaused']:
        dest=folder/(selected.pop(epoch)+'.jsonl')
        dest.write_text(''.join(json.dumps(r,separators=(',',':'))+'\n' for r in streams[epoch]),encoding='utf8')
        metadata.append({'file':str(dest),'sha256':hashlib.sha256(dest.read_bytes()).hexdigest(),'rows':len(streams[epoch]),'first':streams[epoch][0]['source'],'last':where})
    if not selected:break
manifest={'trace':str(trace),'traceSha256':hashlib.sha256(trace.read_bytes()).hexdigest(),'initialSha256':hashlib.sha256(initial).hexdigest(),'fixtures':metadata,'scope':'Exact decoded JSON fields from hash-verified trace blocks, with source row references. No field or native callback changed.'}
(folder/'manifest.json').write_text(json.dumps(manifest,indent=2),encoding='utf8')
print(json.dumps(manifest))
