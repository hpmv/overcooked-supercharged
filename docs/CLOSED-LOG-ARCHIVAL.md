# Closed diagnostic log storage

The five combined logs from each passed gate20-b/c series have been archived as XZ. All twenty individual gzip run logs per series remain in place. The original combined gzip hashes are retained, and the XZ files reproduce the exact decompressed JSONL bytes, including whitespace and numeric spelling. A restored gzip container may have a different hash.

`scripts/archive_closed_gate_logs.py` accepts only the five combined-log names in either series, requires that series' passed summary and matching four-run process result, pins every individual run file, and refuses existing outputs. On Windows it holds a read handle that excludes writers and deletion during compression. It verifies the entire restored byte stream before committing an archive. The archive tool never deletes input files.

The gate20-c archive index is `artifacts/gate20-c-closed-combined-archive-index.json`. Its five verified archives and manifests occupy 24,669,469 bytes. After rechecking every archive, manifest, original combined gzip and individual run hash, only the five duplicate combined gzip files were removed; `artifacts/gate20-c-closed-combined-removal-receipt.json` records the exact paths and index hash. Net space recovered: 2,727,007,498 bytes. The earlier gate20-b index and removal receipt are retained separately.

Restore to a new artifact file before using a tool that expects gzip:

```powershell
python scripts/archive_closed_gate_logs.py restore artifacts/gate20-c-process01-all-calls.jsonl.xz artifacts/restored-gate20-c-process01-all-calls.jsonl.gz
```

The restore command verifies the container and raw-byte hash, writes a new file, and refuses any original individual-run filename. Twenty-one offline checks cover both series, arbitrary byte preservation, corrupted archives, name/path restrictions, incomplete series, overwrite refusal and Windows writer exclusion.
