"""Append native receipts once and stream the legacy JSON array at shutdown."""
import hashlib
import json
from pathlib import Path


class EvidenceJournal:
    """Append each receipt once; stream the legacy JSON array only at finalization."""
    def __init__(self,path):
        self.path=Path(path);self.stream=self.path.open('xb');self.count=0;self.bytes=0
        self.digest=hashlib.sha256();self.compatibility=None

    def append(self,record):
        if self.stream.closed:raise ValueError('Evidence journal is closed')
        encoded=(json.dumps(record,separators=(',',':'))+'\n').encode('utf8')
        self.stream.write(encoded);self.stream.flush()
        self.digest.update(encoded);self.bytes+=len(encoded);self.count+=1

    def describe(self):
        return {'path':str(self.path),'format':'compact-jsonl','records':self.count,'bytes':self.bytes,
                'sha256':self.digest.hexdigest(),'compatibility':self.compatibility}

    def finalize(self,target):
        if self.compatibility is not None:return self.compatibility
        self.stream.close();target=Path(target);temporary=target.with_suffix(target.suffix+'.tmp')
        checksum=hashlib.sha256();count=0;written=0
        with self.path.open('rb') as source,temporary.open('xb') as output:
            def write(data):
                nonlocal written
                output.write(data);checksum.update(data);written+=len(data)
            write(b'[')
            for line in source:
                if not line.endswith(b'\n'):raise ValueError('Incomplete evidence journal record; original bytes retained')
                if count:write(b',')
                write(line[:-1]);count+=1
            write(b']\n')
        if count!=self.count:raise ValueError('Evidence journal record count changed before finalization')
        if target.exists():raise ValueError('Compatibility observation output already exists')
        temporary.replace(target)
        self.compatibility={'path':str(target),'format':'compact-json-array','records':count,'bytes':written,'sha256':checksum.hexdigest()}
        return self.compatibility
