"""Join continuous, neutral-boundary recordings; reject missing simulation frames."""
import argparse,gzip,json,hashlib
from pathlib import Path
def open_trace(path): return gzip.open(path,'rt',encoding='utf-8-sig') if path.suffix=='.gz' else path.open(encoding='utf-8-sig')
def main():
    p=argparse.ArgumentParser();p.add_argument('sources',nargs='+',type=Path);p.add_argument('--out',required=True,type=Path);args=p.parse_args()
    if args.out.exists(): raise FileExistsError(args.out)
    previous=None;last_inputs=None;count=0
    with gzip.open(args.out,'wt',encoding='utf-8',compresslevel=1) as output:
        output.write(json.dumps({'kind':'header','format':'continuous-recording-composition','sources':[{'path':str(s.resolve()),'sha256':hashlib.sha256(s.read_bytes()).hexdigest()} for s in args.sources]})+'\n')
        for ordinal,path in enumerate(args.sources):
            first=True
            with open_trace(path) as source:
                for line in source:
                    d=json.loads(line)
                    if 'response' not in d: continue
                    r=d['request'];response=d['response'];state=response['state'];frame=state['gameplayFrame']
                    if not response['ok']: raise ValueError('Failed protocol response')
                    if first and ordinal:
                        if r['command'] not in ['inspect','state'] or frame!=previous: raise ValueError('Boundary must inspect the same game frame')
                        for inputs in [last_inputs,response['inputs']]:
                            if any(any(i.get(k,False) for k in ['x','y','pickup','use','dash']) for i in inputs): raise ValueError('Non-neutral recording boundary')
                    elif previous is not None:
                        delta=r.get('steps',1) if r['command']=='step' else 0
                        if frame!=previous+delta: raise ValueError(f'Missing frames: {previous} -> {frame}, request {r}')
                    previous=frame;last_inputs=response['inputs'];first=False;count+=1
                    output.write(json.dumps(d,separators=(',',':'))+'\n')
    print(json.dumps({'calls':count,'lastGameplayFrame':previous,'output':str(args.out)}))
if __name__=='__main__':main()
