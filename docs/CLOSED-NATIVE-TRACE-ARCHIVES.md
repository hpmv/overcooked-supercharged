# Closed native trace archives

`scripts/archive_closed_native_trace.py` archives finalized native trial logs independently of the stricter gate-series archive tool. A failed closed trial is eligible; its failure, score and completion flags remain recorded as originally reported. Archival never upgrades a trial to a passed round or high-score result.

The caller must name a finalized `oc2-native-candidate-search` summary and its exact trial ordinal. That result must contain a terminal integer `exitCode` and the exact source path and gzip SHA256. File age or a watcher snapshot is insufficient. Windows `locked_read` rejects an existing writer and prevents new writer/delete handles for the source and provenance throughout the operation. The tool refuses an unlocked non-Windows fallback.

The tool reads every gzip member to EOF, validating gzip length and CRC, and copies the resulting bytes directly into XZ. It never parses or reformats the JSONL payload. The manifest pins the original gzip container, provenance, exact uncompressed stream, XZ container, tool and lock implementation. A full XZ decompression verifies the original raw SHA256 before publication. Source gzip files are never deleted.

Read-only review of one exact source:

```powershell
python scripts/archive_closed_native_trace.py plan artifacts/native-round-v13/trial001.jsonl.gz --provenance artifacts/native-round-v13/summary.json --trial 1
```

After reviewing and authorizing the source set, archive each selected closed trial:

```powershell
python scripts/archive_closed_native_trace.py archive artifacts/native-round-v13/trial001.jsonl.gz --provenance artifacts/native-round-v13/summary.json --trial 1 --dictionary-mib 32
python scripts/archive_closed_native_trace.py verify artifacts/native-round-v13/trial001.jsonl.xz
```

The tool creates sibling `.jsonl.xz` and `.jsonl.xz.manifest.json` files. Existing output, manifest and partial files are refused. A failed operation retains any partial output for inspection; it does not remove an original log or replace a destination. There is no deletion command. Archival itself temporarily requires room for both gzip and XZ, so it does not immediately reclaim disk space.

Restore into a new path inside `artifacts`:

```powershell
python scripts/archive_closed_native_trace.py restore artifacts/native-round-v13/trial001.jsonl.xz artifacts/restored-v13-trial001.jsonl.gz
```

Restoration holds the archive and manifest against writers, verifies their compressed/raw hashes, writes a new partial file, and verifies its decoded bytes before publishing it. A `.jsonl` destination preserves the exact raw bytes; a `.jsonl.gz` destination preserves those decoded bytes but can have different gzip headers and compression bytes. The original gzip SHA256 remains in the archive manifest and is not falsely claimed for the recreated container.

`scripts/test_archive_closed_native_trace.py` uses small synthetic fixtures to check byte identity, concatenated gzip members, active-writer rejection, terminal provenance, failed-run qualification, CRC/truncation failures, XZ/raw corruption, path bounds and overwrite refusal. Its report is `artifacts/closed-native-archive-tests.json`. These fixtures do not archive any original game trace. Current or still-open recordings, including an active V16 experiment, are outside the closed-source gate.

## Archived native trials

The following two approved closed trials now retain their exact JSONL bytes in XZ. Their redundant original gzip containers were removed only after the absolute paths, source hashes, XZ hashes, manifest hashes and complete decoded bytes were reverified against the immutable archive index:

| Trial | Archive | Original gzip bytes removed | XZ bytes retained |
| --- | --- | ---: | ---: |
| V13, trial1 | `artifacts/native-round-v13/trial001.jsonl.xz` | 462,956,007 | 6,793,392 |
| V14 seed59, trial1 | `artifacts/native-round-v14-seed59/trial001.jsonl.xz` | 578,271,416 | 8,806,812 |

Both were failed native trials; their summaries and failure status remain unchanged. The [archive index](../artifacts/closed-native-traces-v13-v14seed59-archive-index.json) records original gzip, raw JSONL, XZ and manifest hashes. The permanent [removal receipt](../artifacts/closed-native-traces-v13-v14seed59-gzip-removal-receipt.json) records the exact two removed paths, authorization scope, final rechecks and per-trace restoration commands. That operation removed only those two traces. The historical index describes original presence before removal; the later receipt records the subsequent removal.

The examples above illustrate the tool interface; those original gzip paths are now absent. Use `verify` against the retained XZ or `restore` into a new file. Restoration preserves exact decoded bytes while explicitly allowing a different gzip container hash.

V15 trial1 was subsequently archived using the same closed-source and complete-byte checks. Its failed 1,800-point partial run remains failed. The 745,961,808-byte gzip duplicate was removed; the 11,364,336-byte `artifacts/native-round-v15/trial001.jsonl.xz` preserves all 7,013,847,846 decoded bytes, SHA256 `4c83275bac9a818999cc014e071c2a8e09b890d84baf1618a6de3b4aef1f567c`. Its [manifest](../artifacts/native-round-v15/trial001.jsonl.xz.manifest.json), [full reverification](../artifacts/native-round-v15-archive-reverified.json), and [removal receipt](../artifacts/native-round-v15-gzip-removal-receipt.json) retain provenance and the exact restoration command.

V17 trial1 was also archived after its read-only endgame analysis finished. Its completed native 2,500-point/23-delivery result remains below the target. The 979,439,584-byte gzip duplicate was removed after full reverification; `artifacts/native-round-v17/trial001.jsonl.xz` retains 14,419,848 bytes and reproduces the exact 9,183,717,246-byte JSONL stream, SHA256 `d498110443fa014b4c4e747a1737e50d750e7907fee14eae6c7e5bd76f766e8d`. The [manifest](../artifacts/native-round-v17/trial001.jsonl.xz.manifest.json), [full reverification](../artifacts/native-round-v17-archive-reverified.json), and [removal receipt](../artifacts/native-round-v17-gzip-removal-receipt.json) pin the original and archived bytes and retain the restoration command.

V20 trial1 was archived after its native score audit and V19 ledger comparison finished. Its completed 2,396-point/22-delivery result remains below target. The 969,272,867-byte original gzip duplicate was removed after full reverification. The 13,685,844-byte XZ retains all 9,118,324,541 decoded bytes, SHA256 `f0e049ba0911a30a1b301476ddae66aa25cb7ecdb9e6f8e5191be6c9068533ec`. The [manifest](../artifacts/native-round-v20/trial001.jsonl.xz.manifest.json), [full reverification](../artifacts/native-round-v20-archive-reverified.json), and [removal receipt](../artifacts/native-round-v20-gzip-removal-receipt.json) preserve the exact evidence and restoration command.
