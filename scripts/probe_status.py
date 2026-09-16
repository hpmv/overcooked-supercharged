"""Bounded-memory live probe summaries; execution status is not cross-run proof."""
import json,sys,zlib
def latest(path):
    decoder=zlib.decompressobj(31);tail=b''
    with open(path,'rb') as source:
        while chunk:=source.read(1024*1024): tail=(tail+decoder.decompress(chunk))[-2*1024*1024:]
    records=tail.split(b'\n')[1:-1]
    for line in reversed(records):
        try: row=json.loads(line)
        except ValueError: continue
        if row.get('kind')=='call': return row['response']['state'],decoder.eof
    raise ValueError('No complete recent state')
for path in sys.argv[1:]:
    state,complete=latest(path)
    print(json.dumps(dict(path=path,closedTrace=complete,frame=state['gameplayFrame'],score=state['score'],timer=state['timer'],roundOrdinal=state.get('currentRecipeRoundInstanceOrdinal'),currentDraws=[(d.get('roundDrawIndex'),d['recipeIds']) for d in state.get('currentRecipeDraws',[])],deliveryEvents=[(e['gameplayFrame'],e['recipeId'],e['scoreDelta']) for e in state['gameEvents'] if e['kind']=='delivery'],observerError=state.get('gameEventsError'))))
