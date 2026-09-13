import json,sys,zlib
from collections import deque
path=sys.argv[1]
events=deque(maxlen=10)
last=None
buf=b''
decoder=zlib.decompressobj(31) if path.endswith('.gz') else None
# The live view needs only the end of a trace. Decompress with bounded memory
# and parse its final complete records instead of reparsing every world snapshot.
truncated=False
with open(path,'rb') as source:
    while chunk:=source.read(1024*1024):
        buf+=decoder.decompress(chunk) if decoder else chunk
        if len(buf)>4*1024*1024:buf=buf[-4*1024*1024:];truncated=True
lines=buf.split(b'\n')[:-1]
if truncated:lines=lines[1:]
for line in lines:
    if not line.strip():continue
    row=json.loads(line)
    if row.get('kind')=='call':last=row
    elif row.get('kind')=='event':
        value=row.get('value')
        if isinstance(value,dict):value={k:v for k,v in value.items() if k not in ('state','snapshot')}
        events.append({'event':row['name'],'value':value})
print(json.dumps(list(events),indent=2))
if last:
    s=last['response'].get('state',{})
    print(json.dumps({'call':last.get('index'),'frame':s.get('gameplayFrame'),'timer':s.get('timer'),'score':s.get('score'),'render':{k:s.get(k) for k in ('screenWidth','screenHeight','targetFrameRate','vSyncCount')},'request':last['request'],
      'chefs':[{k:c.get(k) for k in ('playerId','position','heldEntityId','pickupTargetId','placementTargetId','useTargetId','canAcceptInput')} for c in s.get('chefs',[])],
      'stations':[{k:e.get(k) for k in ('id','name','attachedEntityId','workProgress','cookingProgress','ingredientIds','contents')} for e in s.get('entities',[]) if e.get('id') in (7,23,56)]},indent=2))
