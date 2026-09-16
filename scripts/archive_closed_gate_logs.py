"""Lossless byte-stream archival of closed gate20-b/c combined logs only.

Never deletes input logs and never rewrites individual run files. JSON is not
parsed or reformatted during compression. Uses the Python standard library.
"""
from __future__ import annotations
import argparse
import contextlib
import ctypes
import gzip
import hashlib
import io
import json
import lzma
import os
from pathlib import Path
import re
import sys
import time

ROOT = Path(__file__).resolve().parents[1]
SOURCE_PATTERN = re.compile(r"(gate20-[bc])-process(\d{2})-all-calls\.jsonl\.gz\Z")
CHUNK = 1024 * 1024


def require(value, detail):
    if not value:
        raise ValueError(detail)


def sha_stream(stream):
    digest = hashlib.sha256()
    size = 0
    while block := stream.read(CHUNK):
        digest.update(block)
        size += len(block)
    return digest.hexdigest(), size


def file_proof(path):
    with path.open("rb") as stream:
        digest, size = sha_stream(stream)
    return {"path": str(path), "sha256": digest, "bytes": size}


@contextlib.contextmanager
def locked_read(path):
    # Windows: allow other readers, but refuse an existing or new writer/delete
    # handle for the duration. This also rejects an unexpectedly open live log.
    if os.name == "nt":
        import msvcrt
        kernel = ctypes.WinDLL("kernel32", use_last_error=True)
        create = kernel.CreateFileW
        create.argtypes = [ctypes.c_wchar_p, ctypes.c_uint32, ctypes.c_uint32,
                           ctypes.c_void_p, ctypes.c_uint32, ctypes.c_uint32, ctypes.c_void_p]
        create.restype = ctypes.c_void_p
        handle = create(str(path), 0x80000000, 1, None, 3, 0x80, None)
        if handle == ctypes.c_void_p(-1).value:
            raise ctypes.WinError(ctypes.get_last_error())
        try:
            fd = msvcrt.open_osfhandle(handle, os.O_RDONLY | os.O_BINARY)
        except BaseException:
            kernel.CloseHandle(ctypes.c_void_p(handle))
            raise
        with os.fdopen(fd, "rb") as stream:
            yield stream
    else:
        with path.open("rb") as stream:
            yield stream


def closed_source(source, root=ROOT):
    source, artifacts = Path(source).resolve(), (root / "artifacts").resolve()
    match = SOURCE_PATTERN.fullmatch(source.name)
    require(source.parent == artifacts and match,
            "Only workspace artifacts/gate20-[bc]-processNN-all-calls.jsonl.gz may be archived.")
    series, ordinal = match.group(1), int(match.group(2))
    require(1 <= ordinal <= 5, "Only the five frozen processes per series are supported.")
    summary_path = artifacts / f"{series}-summary.json"
    result_path = artifacts / f"{series}-process{ordinal:02d}-result.json"
    summary = json.loads(summary_path.read_text(encoding="utf-8-sig"))
    result = json.loads(result_path.read_text(encoding="utf-8-sig"))
    require(summary.get("executed") is True and summary.get("passed") is True,
            "The matching series summary must establish a completed passed series.")
    process = next((p for p in summary.get("processes", []) if p.get("ordinal") == ordinal), None)
    require(process and process.get("result", {}).get("ok") is True and result.get("ok") is True,
            "Closed process result is absent or failed.")
    runs = result.get("runs", [])
    require(len(runs) == 4 and all(r.get("passedExecutionGate") is True for r in runs),
            "All four individual run results must be completed.")
    require(process["result"].get("runs") == runs, "Summary and process result disagree.")
    run_proofs = []
    for number, run in enumerate(runs, 1):
        expected = artifacts / f"{series}-process{ordinal:02d}-run{number:02d}.jsonl.gz"
        require(Path(run["trace"]).resolve() == expected and expected.is_file(),
                "An original individual run file is missing or its identity differs.")
        run_proofs.append(file_proof(expected))
    return source, {"seriesSummary": file_proof(summary_path), "processResult": file_proof(result_path),
                    "series": series, "processOrdinal": ordinal, "individualRunsPreserved": run_proofs}


def verify_archive(archive, manifest):
    expected = json.loads(Path(manifest).read_text(encoding="utf-8-sig"))
    require(expected.get("format") == "oc2-closed-gate-byte-archive" and expected.get("version") == 1,
            "Unknown archive manifest.")
    actual = file_proof(Path(archive))
    require(actual["sha256"] == expected["archive"]["sha256"] and actual["bytes"] == expected["archive"]["bytes"],
            "XZ container does not match its manifest.")
    with lzma.open(archive, "rb") as stream:
        digest, size = sha_stream(stream)
    require(digest == expected["uncompressed"]["sha256"] and size == expected["uncompressed"]["bytes"],
            "Restored byte stream differs from the original uncompressed log.")
    return expected, {"verifiedUncompressedSha256": digest, "verifiedUncompressedBytes": size}


def archive(source, dictionary_mib=32, root=ROOT):
    require(dictionary_mib in (16, 32, 64), "Dictionary must be 16, 32 or 64 MiB.")
    source, closed = closed_source(source, root)
    destination = source.with_suffix(".xz")
    partial = Path(str(destination) + ".partial")
    manifest = Path(str(destination) + ".manifest.json")
    require(not destination.exists() and not partial.exists() and not manifest.exists(),
            "Output or partial archive already exists; nothing will be overwritten.")
    before = source.stat()
    started = last_report = time.monotonic()
    filters = [{"id": lzma.FILTER_LZMA2, "preset": 1, "dict_size": dictionary_mib * 1024 * 1024}]
    digest, raw_size = hashlib.sha256(), 0
    with locked_read(source) as locked:
        original_hash, original_size = sha_stream(locked)
        locked.seek(0)
        with gzip.GzipFile(fileobj=locked, mode="rb") as incoming, partial.open("xb") as output:
            compressor = lzma.LZMACompressor(format=lzma.FORMAT_XZ, check=lzma.CHECK_CRC64, filters=filters)
            while block := incoming.read(CHUNK):
                digest.update(block)
                raw_size += len(block)
                output.write(compressor.compress(block))
                now = time.monotonic()
                if now - last_report >= 10:
                    print(f"compress: {raw_size / 2**20:.0f} MiB raw, {output.tell() / 2**20:.1f} MiB XZ, "
                          f"{100 * locked.tell() / original_size:.1f}% source, {now-started:.0f}s", file=sys.stderr, flush=True)
                    last_report = now
            output.write(compressor.flush())
            output.flush()
            os.fsync(output.fileno())
        locked.seek(0)
        require(sha_stream(locked) == (original_hash, original_size), "Original gzip bytes changed during archival.")
        after = source.stat()
        require((before.st_size, before.st_mtime_ns, before.st_ino) == (after.st_size, after.st_mtime_ns, after.st_ino),
                "Original file identity changed during archival.")
        print("Verifying restored XZ byte stream against original uncompressed SHA256...", file=sys.stderr, flush=True)
        with lzma.open(partial, "rb") as restored:
            require(sha_stream(restored) == (digest.hexdigest(), raw_size), "XZ roundtrip verification failed.")
        for run in closed["individualRunsPreserved"]:
            require(file_proof(Path(run["path"])) == run, "An individual run file changed; archive will remain partial.")
        require(not destination.exists(), "Destination appeared; refusing replacement.")
        partial.rename(destination)
        proof = {"format": "oc2-closed-gate-byte-archive", "version": 1,
                 "createdUtc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
                 "source": {"path": str(source), "sha256": original_hash, "bytes": original_size,
                            "mtimeNs": before.st_mtime_ns},
                 "archive": file_proof(destination),
                 "uncompressed": {"sha256": digest.hexdigest(), "bytes": raw_size},
                 "compression": {"format": "XZ", "filter": "LZMA2", "preset": 1,
                                 "dictionaryBytes": dictionary_mib * 1024 * 1024, "check": "CRC64"},
                 "closedSourceEvidence": closed,
                 "verification": {"xzDecompressedBytesEqualOriginal": True,
                                  "sourceGzipUnchanged": True, "individualRunFilesUnchanged": True},
                 "containerIdentity": "The original gzip SHA256 is pinned. Recreated gzip containers may have different bytes; exact uncompressed JSONL bytes are preserved.",
                 "deletion": "No source or individual run files were deleted.",
                 "wallSeconds": time.monotonic() - started}
        with manifest.open("x", encoding="utf-8", newline="\n") as output:
            json.dump(proof, output, indent=2)
            output.write("\n")
    return proof


def restore(archive_path, output_path, root=ROOT):
    archive_path, output = Path(archive_path).resolve(), Path(output_path).resolve()
    artifacts = (root / "artifacts").resolve()
    require(archive_path.parent == artifacts and SOURCE_PATTERN.fullmatch(archive_path.name.removesuffix(".xz") + ".gz"),
            "Only the supported gate20-b/c archive files can be restored.")
    require(output.is_relative_to(artifacts) and output.suffix in (".jsonl", ".gz"),
            "Restore to a new .jsonl or .jsonl.gz file within workspace artifacts.")
    require(output.name.endswith(".jsonl") or output.name.endswith(".jsonl.gz"), "Unsupported restore extension.")
    require(not output.exists() and not Path(str(output)+".partial").exists(), "Restore output exists; refusing replacement.")
    require(not re.fullmatch(r"gate20-[bc]-process\d+-run\d+\.jsonl\.gz", output.name),
            "Individual run filenames are never restore targets.")
    expected, verified = verify_archive(archive_path, str(archive_path)+".manifest.json")
    partial = Path(str(output)+".partial")
    with lzma.open(archive_path, "rb") as source, partial.open("xb") as sink:
        if output.name.endswith(".gz"):
            with gzip.GzipFile(filename="", mode="wb", compresslevel=6, mtime=0, fileobj=sink) as restored:
                while block := source.read(CHUNK): restored.write(block)
        else:
            while block := source.read(CHUNK): sink.write(block)
    with (gzip.open(partial, "rb") if output.name.endswith(".gz") else partial.open("rb")) as stream:
        require(sha_stream(stream) == (expected["uncompressed"]["sha256"], expected["uncompressed"]["bytes"]),
                "Restored output bytes failed verification; output left partial.")
    require(not output.exists(), "Restore target appeared; refusing replacement.")
    partial.rename(output)
    return {"ok": True, "restored": file_proof(output), **verified,
            "gzipContainerMayDiffer": output.name.endswith(".gz"), "originalFilesUntouched": True}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="command", required=True)
    save = sub.add_parser("archive"); save.add_argument("source"); save.add_argument("--dictionary-mib", type=int, default=32)
    check = sub.add_parser("verify"); check.add_argument("archive")
    recover = sub.add_parser("restore"); recover.add_argument("archive"); recover.add_argument("output")
    args = parser.parse_args()
    if args.command == "archive": result = archive(args.source, args.dictionary_mib)
    elif args.command == "verify":
        _, result = verify_archive(args.archive, args.archive+".manifest.json"); result["ok"] = True
    else: result = restore(args.archive, args.output)
    print(json.dumps(result, indent=2))


if __name__ == "__main__":
    try: main()
    except (ValueError, OSError, EOFError, lzma.LZMAError, json.JSONDecodeError) as error:
        print(json.dumps({"ok": False, "error": str(error)}), file=sys.stderr)
        sys.exit(1)
