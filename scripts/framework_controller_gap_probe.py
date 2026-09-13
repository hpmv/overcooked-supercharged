"""Observe paused bridge responsiveness while the controller is disconnected."""
import argparse
import json
from pathlib import Path
import time
from framework_rpc import Client


def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--out',type=Path,required=True)
    p.add_argument('--samples',type=int,default=20)
    a=p.parse_args()
    if a.out.exists():p.error('Evidence file already exists')
    if not 5<=a.samples<=100:p.error('Use5..100 samples')
    rows=[];b=Client(17636)
    try:
        b.call({'command':'pause'})
        # An outstanding old socket may need its bounded timeout to retire.
        deadline=time.monotonic()+5
        while True:
            r=b.call({'command':'status'})
            if r['bridge']['inputExchange']['controllerConnected'] is False:break
            if time.monotonic()>deadline:raise RuntimeError('Controller still connected')
            time.sleep(.05)
        for _ in range(a.samples):
            began=time.monotonic();r=b.call({'command':'status'});duration=time.monotonic()-began
            rows.append({'wallSeconds':duration,'response':r});time.sleep(.05)
    finally:b.close()
    first=rows[0]['response']['bridge'];last=rows[-1]['response']['bridge'];durations=sorted(r['wallSeconds'] for r in rows)
    elapsed=[r['response']['bridge']['nativeRound']['elapsed'] for r in rows]
    result={'passed':all(r['response']['bridge']['paused'] and r['response']['bridge']['inputBlocked'] and not r['response']['bridge']['inputExchange']['controllerConnected'] for r in rows)
            and len(set(elapsed))==1 and last['unityFrame']>first['unityFrame']
            and last['inputExchange']['runningBlockingTimeouts']==first['inputExchange']['runningBlockingTimeouts']
            and durations[-1]<.5,
            'samples':len(rows),'medianResponseMs':durations[len(durations)//2]*1000,'maximumResponseMs':durations[-1]*1000,
            'nativeElapsedUnchanged':len(set(elapsed))==1,'unityCallbacksAdvanced':last['unityFrame']-first['unityFrame'],
            'runningTimeoutDelta':last['inputExchange']['runningBlockingTimeouts']-first['inputExchange']['runningBlockingTimeouts'],
            'classification':'Observed local RPC responsiveness while paused without a controller; not an OS window input-latency benchmark','records':rows}
    a.out.parent.mkdir(parents=True,exist_ok=True);a.out.write_text(json.dumps(result,indent=2))
    print(json.dumps({k:v for k,v in result.items() if k!='records'},indent=2))
    if not result['passed']:raise SystemExit(1)


if __name__=='__main__':main()
