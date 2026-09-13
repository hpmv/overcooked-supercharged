"""Load or call an authoring helper in the already running, paused game."""
import argparse
import json
from pathlib import Path
import time

from framework_rpc import Client


def compact_console_result(value):
    """Keep selected inspection readbacks visible; full catalogs stay in the receipt."""
    if isinstance(value, list):
        return [compact_console_result(item) for item in value]
    if isinstance(value, dict):
        selected = bool(value.get('values')) and 'memberCatalog' in value
        return {key: compact_console_result(item) for key, item in value.items()
                if not selected or key not in ('components', 'memberCatalog')}
    return value


def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('command',choices=('load','call','retire','status'))
    p.add_argument('--slot',default='body-restore')
    p.add_argument('--manifest',type=Path)
    p.add_argument('--activate',action='store_true')
    p.add_argument('--operation')
    p.add_argument('--args',default='{}',help='JSON object; PowerShell callers should single-quote it.')
    p.add_argument('--args-file',type=Path)
    p.add_argument('--out',required=True,type=Path)
    p.add_argument('--port',type=int,default=17636)
    a=p.parse_args()
    if a.out.exists():p.error('Output already exists; choose a new evidence file.')
    if a.command=='load' and not a.manifest:p.error('load requires --manifest')
    if a.command=='call' and not a.operation:p.error('call requires --operation')
    args=json.loads(a.args_file.read_text(encoding='utf-8-sig') if a.args_file else a.args)
    if not isinstance(args,dict):p.error('Module arguments must be a JSON object')
    records=[];started=time.monotonic();b=None;report={'passed':False,'records':records}
    def call(request):
        began=time.monotonic();response=b.call(request)
        records.append({'request':request,'wallSeconds':time.monotonic()-began,'response':response})
        return response
    try:
        b=Client(a.port)
        call({'command':'pause'})
        if a.command=='load':
            m=json.loads(a.manifest.read_text(encoding='utf-8-sig'))
            result=call({'command':'hot-load','slot':a.slot,'path':m['dll'],'type':m['entryType'],
                         'sha256':m['sha256'],'coreSha256':m['coreSha256']})
            if a.activate:result=call({'command':'hot-call','slot':a.slot,'operation':'activate','args':{}})
        elif a.command=='call':result=call({'command':'hot-call','slot':a.slot,'operation':a.operation,'args':args})
        elif a.command=='retire':result=call({'command':'hot-unload','slot':a.slot})
        else:result=call({'command':'hot-status'})
        report.update(passed=True,result=result['detail'])
        # Modules can return an explicit failure receipt after a partial batch.
        payload=result.get('detail')
        if isinstance(payload,dict):
            nested=payload.get('result',payload)
            if isinstance(nested,dict) and nested.get('ok') is False:report['passed']=False
    except Exception as error:
        report['error']=str(error)
        try:
            receipt=json.loads(str(error))
            if isinstance(receipt,dict) and 'error' in receipt:
                report['errorReceipt']=receipt
                report['error']=receipt['error']
        except (ValueError,TypeError):pass
    finally:
        if b:b.close()
        report['wallSeconds']=time.monotonic()-started
        a.out.parent.mkdir(parents=True,exist_ok=True)
        a.out.write_text(json.dumps(report,indent=2),encoding='utf-8')
    printed=compact_console_result({k:v for k,v in report.items() if k not in ('records','errorReceipt')})
    printed['evidence']=str(a.out)
    if len(json.dumps(printed))>8000:
        printed={'passed':report['passed'],'wallSeconds':report['wallSeconds'],'evidence':str(a.out),'error':report.get('error'),
                 'resultSummary':'Full module receipt saved in the evidence file.'}
    print(json.dumps(printed,indent=2))
    if not report['passed']:raise SystemExit(1)


if __name__=='__main__':main()
