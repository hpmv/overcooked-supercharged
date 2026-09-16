import gzip,json,re
from pathlib import Path
p=Path('artifacts/native-round-v15/trial001.jsonl.gz')
gf=-1;first=True
try:
 with gzip.open(p,'rt',encoding='utf-8-sig') as f:
  for line in f:
   if '"kind":"call"' in line[:100]:
    match=re.search(r'"gameplayFrame":(-?\d+)',line)
    if match:gf=int(match.group(1))
    if first:
     row=json.loads(line);print('CALLKEYS',list(row));print('RESPONSEKEYS',list(row.get('response',{})));first=False
   elif 'nativePreparedFlavor' in line or ('plannerJobStart' in line and 'prepared-flavor' in line):print(gf,line[:6000].strip())
   if gf>4500:break
except EOFError:pass
