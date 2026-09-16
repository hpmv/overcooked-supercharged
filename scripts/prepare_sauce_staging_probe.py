"""File-only preparation of immutable V11 prefix and matched sauce experiments.

Preserves original requests through GF1346, then explicitly appends ONE new
neutral step. Common offmixer setup is a new legal-input experiment, not replay
equivalence or restored state. Never connects to the game or overwrites outputs.
"""
import gzip, hashlib, json
from pathlib import Path
from extract_inputs import encode, request_from, sha256_file

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'artifacts/native-round-v11/trial001.jsonl.gz'
EXPECTED = '2a21da1c5f245d734b1372022ed6b5cc3d22fe2573b4357083ff37e958bd6489'
OUT = ROOT / 'routes/probes'
PREFIX = OUT / 'sauce-staging-prefix-gf1346-neutral.jsonl.gz'
SETUP = OUT / 'sauce-staging-common-setup.json'
BASELINE = OUT / 'sauce-staging-source37.json'
NEAR = OUT / 'sauce-staging-near40.json'
RECEIPT = ROOT / 'artifacts/sauce-staging-probe-preparation.json'


def main():
    for path in (PREFIX, SETUP, BASELINE, NEAR, RECEIPT):
        if path.exists(): raise FileExistsError(path)
    before = SOURCE.stat()
    if sha256_file(SOURCE) != EXPECTED: raise ValueError('Frozen V11 trace hash changed')
    requests, last, source_hash = [], None, hashlib.sha256()
    with gzip.open(SOURCE, 'rt', encoding='utf8') as stream:
        for number, line in enumerate(stream, 1):
            if not line.startswith('{"kind":"call"'): continue
            row = json.loads(line); request = request_from(row, number)
            response = row['response']; state = response.get('state')
            if not response.get('ok') or not isinstance(state, dict): raise ValueError('Prefix lacks successful native state')
            frame = state['gameplayFrame']
            if frame > 1346: break
            if request['command'] not in ('restart', 'inspect', 'preview', 'step'): raise ValueError('Unexpected prefix command')
            if request['command'] == 'step' and request.get('steps', 1) != 1: raise ValueError('Do not split native step boundaries')
            if last is not None and frame != last['state']['gameplayFrame'] + (1 if request['command'] == 'step' else 0):
                raise ValueError('Prefix frame discontinuity')
            data = encode(request); source_hash.update(data); requests.append(request); last = response
            if frame == 1346: break
    after = SOURCE.stat()
    if (before.st_size, before.st_mtime_ns) != (after.st_size, after.st_mtime_ns): raise ValueError('Source changed while read')
    if last is None or last['state']['gameplayFrame'] != 1346 or len(requests) != 1349: raise ValueError('Unexpected prefix boundaries')
    es = {e['id']:e for e in last['state']['entities']}; chefs = {c['playerId']:c for c in last['state']['chefs']}
    assert es[37]['attachedEntityId'] == 151 and es[33]['attachedEntityId'] == 0 and es[40]['attachedEntityId'] == 0
    assert es[49]['attachedEntityId'] == 0 and chefs[3]['heldEntityId'] == 6 and chefs[0]['heldEntityId'] == 0
    assert {c['playerId'] for c in chefs.values()} == {0,1,2,3}
    neutral = {'version':1,'command':'step','steps':1,'inputs':[
        {'player':p,'x':0,'y':0,'pickup':False,'use':False,'dash':False} for p in range(4)]}
    movie_hash = hashlib.sha256()
    with PREFIX.open('xb') as file:
        with gzip.GzipFile(filename='', mode='wb', fileobj=file, mtime=0) as stream:
            for request in requests + [neutral]:
                data = encode(request);stream.write(data);movie_hash.update(data)
    def action(kind, station=None, **extra):
        a={'type':kind,'timeoutFrames':300,'dash':True,'shortDash':True}
        if station is not None:a['station']=str(station)
        a.update(extra);return a
    setup = {'description':'New identical legal-input setup after exact V11 prefix+one explicit neutral step. Let P3 dash settle, park held bowl6 on ordinary33, pick up bowl3 from mixer, then keep holding it. Observe both native offmixer identities/progress; do not assume Mixed. Other chefs neutral. No game-state restoration.',
        'timeoutFrames':360,'jobs':[{'id':'S00-neutral-and-offmixer-setup','player':3,'dependencies':[],
        'resources':['bowl6','bowl3','ordinary33'], 'actions':[
            action('wait', durationFrames=18, dash=False, shortDash=False),
            action('place',33,dash=False,shortDash=False),action('take',3,dash=False,shortDash=False),
            action('wait',durationFrames=2,dash=False,shortDash=False)]}]}
    def route(counter):
        return {'description':f'Matched serial sauce probe after independently replaying the SAME fresh prefix and SAME observed offmixer setup. P0 plate13/source37/temp{counter}/output49; all other chefs neutral. Ordinary native sauce order and checks, no score/delivery target. Abort before any observed native burn/overmix deadline.',
            'timeoutFrames':900,'jobs':[{'id':f'S01-serial-sauce-temp{counter}','player':0,'dependencies':[],
            'resources':['plate13','base151','source37',f'temp{counter}','dispenser72','switch79','output49'],
            'actions':[action('switch-condiment',index=0),action('take',13),action('assemble',37),
                action('apply',72,expectedIngredientId=17094),action('place',counter),action('switch-condiment',index=1),
                action('take',counter),action('apply',72,expectedIngredientId=158482),action('place',49)]}]}
    plans={SETUP:setup,BASELINE:route(37),NEAR:route(40)}
    for path,data in plans.items():
        with path.open('x',encoding='utf8',newline='\n') as stream:json.dump(data,stream,indent=2);stream.write('\n')
    receipt={'qualification':'Prepared inputs/plans only. No experiment executed by this script, no measured saving, no native state restoration.',
        'source':str(SOURCE),'sourceBytes':before.st_size,'sourceSha256':EXPECTED,'preservedRequests':len(requests),'preservedThroughGameplayFrame':1346,
        'originalCanonicalRequestsSha256':source_hash.hexdigest(),'appendedCommands':[neutral], 'movieRequests':len(requests)+1,
        'movieExpectedFinalGameplayFrame':1347,'movieCanonicalRequestsSha256':movie_hash.hexdigest(),
        'sourceBoundaryInputs':last['inputs'],'sourceBoundaryIsFullyNeutral':False,
        'sourceBoundaryProcessing':{str(i):{k:es[i].get(k)for k in ['observedOrdinal','cookingTime','cookingProgress','mixingTime','mixingProgress']}for i in [2,7,3,6]},
        'sourceBoundaryPotNativeBurnRemainingSeconds':{str(i):24-es[i]['cookingProgress']for i in [2,7]},
        'sourceBoundaryBowl3NativeOvermixRemainingSeconds':24-es[3]['mixingProgress'],
        'commonSetupMaximumFrames':360,'serialBranchMaximumFrames':900,'combinedSetupAndBranchMaximumSeconds':(1+360+900)/60,
        'requiredRuntimeChecks':[
            'Run each branch from a fresh restart via this exact prefix, never restore food/score/positions.',
            'Both must use the same executing controller/plugin hashes and same common setup inputs; retain actual trace headers/receipts.',
            'At setup completion require bowl6 attached33, bowl3 held by P3, original mixers empty, both offmixer progress/composition stable, all pads neutral and all chefs controlled.',
            'Compare observed pre-branch chef/plate/base/station/recipe/input/physics-phase/clock state before calling the branches comparable; raw drift must remain visible.',
            'Before branch and throughout, observe actual pot progress and any still-active mixer clock; reject/abort if remaining native burn/overmix budget cannot contain the branch timeout. The aggregate maximum is21.0167s fromGF1346 and is not a guaranteed path time.',
            'Require final exact plate13 dual-sauce recipe125780 at49, original base151 consumed, selected temporary counter empty, no deliveries/deductions/ruined food, and report actual P0 branch elapsed native frames/timer.',
            'Compare the two NEW branch measurements, not the original13.4833s full-planner interval whose other chefs kept working.'
        ],'files':{str(p):{'sha256':sha256_file(p),'bytes':p.stat().st_size}for p in [PREFIX,SETUP,BASELINE,NEAR]},
        'preparationScriptSha256':sha256_file(Path(__file__))}
    with RECEIPT.open('x',encoding='utf8',newline='\n') as stream:json.dump(receipt,stream,indent=2);stream.write('\n')
    print(json.dumps({'receipt':str(RECEIPT),'requests':len(requests)+1,'preservedThrough':1346,'appendedNeutralFrames':1,'files':receipt['files']},indent=2))


if __name__=='__main__':main()
