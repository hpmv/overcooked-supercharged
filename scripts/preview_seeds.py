"""Enumerate native weighted recipe previews without advancing the live game."""
import argparse, hashlib, json, socket, struct
from pathlib import Path

def exact(stream, count):
    data=bytearray()
    while len(data)<count:
        chunk=stream.recv(count-len(data))
        if not chunk: raise ConnectionError('Game closed during native preview')
        data.extend(chunk)
    return bytes(data)

def main():
    p=argparse.ArgumentParser();p.add_argument('--start',type=int,default=0);p.add_argument('--seeds',type=int,default=128)
    p.add_argument('--count',type=int,default=60);p.add_argument('--out',required=True);args=p.parse_args()
    target=Path(args.out)
    if target.exists(): raise FileExistsError(target)
    previews=[]
    with socket.create_connection(('127.0.0.1',17634),timeout=60) as stream, target.open('w',encoding='utf-8') as out:
        for seed in range(args.start,args.start+args.seeds):
            raw=json.dumps(dict(version=1,command='preview',seed=seed,count=args.count),separators=(',',':')).encode()
            stream.sendall(struct.pack('<I',len(raw))+raw)
            response=json.loads(exact(stream,struct.unpack('<I',exact(stream,4))[0]))
            if not response['ok']: raise RuntimeError(response['error'])
            preview=response['preview']
            for flag in ['ambientRngRestored','isolatedRngRestored','observationsRestored','liveInstanceUnchanged','frameUnchanged']:
                if not preview[flag]: raise RuntimeError('Native preview restoration failure: '+flag)
            score=0;target_count=None
            for i,recipe in enumerate(preview['recipes']):
                score+=recipe['baseValue']+[8,8,16,24][i] if i<4 else recipe['baseValue']+32
                if target_count is None and score>=5000: target_count=i+1
            preview['freshFifoTargetDeliveries']=target_count
            out.write(json.dumps(preview,separators=(',',':'))+'\n');out.flush()
            previews.append((target_count or 999,seed))
    print(json.dumps(dict(path=str(target.resolve()),sha256=hashlib.sha256(target.read_bytes()).hexdigest(),best=sorted(previews)[:20],interpretation='Score bound assumes native full tips and ordered deliveries; these seeds have not yet been evaluated as routes.')))

if __name__=='__main__':main()
