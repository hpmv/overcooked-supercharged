# Closed combined-log archival

`archive_closed_gate_logs.py` losslessly compresses the **decompressed byte stream** of the five closed `artifacts/gate20-b-processNN-all-calls.jsonl.gz` combined logs into `.jsonl.xz`. It does not parse JSON, normalize numbers, reorder keys, change encodings or newlines, discard records, delete originals, or write individual run files. Active `gate20-c` files and individual-run source names are rejected.

Archival requires the passed, completed gate20-b summary and matching four-run process result. On Windows, a read-only file handle excludes existing or new concurrent writers/deleters while the original is hashed, read and verified. Each separate individual-run file is hashed before and after. Existing archives, partial outputs and restore outputs are never overwritten.

```powershell
python scripts/archive_closed_gate_logs.py archive artifacts/gate20-b-process01-all-calls.jsonl.gz --dictionary-mib 32
python scripts/archive_closed_gate_logs.py verify artifacts/gate20-b-process01-all-calls.jsonl.xz
```

XZ uses the standard library's LZMA2 fast preset, a 32 MiB dictionary by default (16/32/64 MiB supported), and CRC64. Compression streams directly from gzip into XZ; no full uncompressed file is created. Before publishing the archive, the tool decompresses it and requires its byte count and SHA256 to equal the original decompressed stream. The manifest pins:

- Original gzip file SHA256, byte count and modification time.
- Exact uncompressed byte-stream SHA256 and byte count.
- XZ file SHA256, byte count and compression settings.
- Closed-series and process-result hashes, plus unchanged individual-run file hashes.
- Completed roundtrip verification and an explicit statement that originals were not deleted.

Restore either the exact JSONL byte stream or a gzip containing that same stream to a new file:

```powershell
python scripts/archive_closed_gate_logs.py restore artifacts/gate20-b-process01-all-calls.jsonl.xz artifacts/restored-process01.jsonl
python scripts/archive_closed_gate_logs.py restore artifacts/gate20-b-process01-all-calls.jsonl.xz artifacts/restored-process01.jsonl.gz
```

Restores verify both the archive manifest and the materialized output's decompressed bytes before publishing the output. **Recreated gzip container bytes may differ** because gzip metadata/compression encoding can differ. The manifest's original gzip SHA256 preserves the identity of the input container; the uncompressed SHA256 is the lossless-content restoration guarantee. Exact original gzip container reconstruction is not claimed.

No deletion command is provided. Any later removal of a redundant original needs a separate explicit operation after its archive and manifest have been verified. Failure may leave a `.partial` output for inspection; original files remain present.

## First closed-file experiment

Process 01 finished in 74.47 seconds with a 32 MiB dictionary:

| Item | Bytes |
| --- | ---: |
| Original combined gzip | 381,902,591 |
| Exact decompressed JSONL | 4,087,256,770 |
| XZ archive | 4,315,156 |
| Potential saved bytes after separately authorized removal | 377,587,435 |

The first archive was 98.8701% smaller than the gzip. Its entire decompressed stream passed SHA256 comparison; the original gzip and all four individual-run files were unchanged during archival. The other four closed combined logs were then archived and independently verified in the same way. After explicit authorization and another original/archive/individual-file hash check, only the five redundant combined gzip files were removed. The five XZ files and manifests total 21,660,027 bytes, replacing 1,907,884,698 gzip bytes: **1,886,224,671 bytes (1.757 GiB) reclaimed**. All twenty individual run gzip files remain unchanged.

`artifacts/gate20-b-closed-combined-archive-index.json` records each original/archive/uncompressed hash, exact byte counts, all individual-run hashes, and a restore command for each log. `artifacts/gate20-b-closed-duplicate-removal.json` records the five exact removed paths and preserved archive identities. The index's `originalGzipPresentAtVerification` field describes the independent verification before that removal. The index can be regenerated after removal without needing the original gzip files:

```powershell
python scripts/index_closed_gate_archives.py
```

The 17 small offline tests cover arbitrary byte preservation (including whitespace, CRLF, BOM and non-text bytes), both restore formats, gzip container differences, corrupt archive rejection, unchanged originals, concurrent-writer refusal, incomplete-series refusal, and prohibited names/paths/overwrites:

```powershell
python scripts/test_archive_closed_gate_logs.py
```

Their report is `artifacts/closed-log-archive-tests.json`; synthetic fixtures stay under their own `artifacts/archive-fixtures-*` directory and do not touch original logs.
