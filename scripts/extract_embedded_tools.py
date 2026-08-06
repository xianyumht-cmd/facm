#!/usr/bin/env python3
"""Read-only PE resource extractor used for recovering FACM's embedded tools.

The script never executes the input file or any extracted payload. It enumerates
PE resources, detects nested PE files and command scripts, deduplicates results,
and writes a hash manifest for review.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import shutil
import struct
from dataclasses import asdict, dataclass
from pathlib import Path
from typing import Iterable, Optional

import pefile


@dataclass
class ExtractedItem:
    file: str
    source: str
    kind: str
    size: int
    sha256: str
    original_filename: str = ""
    file_description: str = ""


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest().upper()


def safe_name(value: str) -> str:
    value = value.strip().replace("\x00", "")
    value = re.sub(r"[<>:\"/\\|?*]+", "_", value)
    value = re.sub(r"\s+", " ", value).strip(" .")
    return value[:120] or "unnamed"


def resource_label(entry: object) -> str:
    name = getattr(entry, "name", None)
    if name is not None:
        return safe_name(str(name))
    return str(getattr(entry, "id", "unknown"))


def iter_pe_resources(pe: pefile.PE, image: bytes) -> Iterable[tuple[str, bytes]]:
    root = getattr(pe, "DIRECTORY_ENTRY_RESOURCE", None)
    if root is None:
        return

    for type_entry in root.entries:
        type_label = resource_label(type_entry)
        type_dir = getattr(type_entry, "directory", None)
        if type_dir is None:
            continue
        for name_entry in type_dir.entries:
            name_label = resource_label(name_entry)
            name_dir = getattr(name_entry, "directory", None)
            if name_dir is None:
                continue
            for lang_entry in name_dir.entries:
                data_entry = getattr(lang_entry, "data", None)
                if data_entry is None:
                    continue
                rva = int(data_entry.struct.OffsetToData)
                size = int(data_entry.struct.Size)
                offset = pe.get_offset_from_rva(rva)
                blob = image[offset : offset + size]
                lang = str(getattr(lang_entry, "id", "0"))
                yield f"resource/{type_label}/{name_label}/{lang}", blob


def pe_metadata(data: bytes) -> tuple[str, str]:
    original = ""
    description = ""
    try:
        nested = pefile.PE(data=data, fast_load=False)
        for file_info_group in getattr(nested, "FileInfo", []) or []:
            for file_info in file_info_group:
                if getattr(file_info, "Key", b"") != b"StringFileInfo":
                    continue
                for table in getattr(file_info, "StringTable", []) or []:
                    entries = getattr(table, "entries", {}) or {}
                    for key, value in entries.items():
                        key_text = key.decode(errors="ignore") if isinstance(key, bytes) else str(key)
                        value_text = value.decode(errors="ignore") if isinstance(value, bytes) else str(value)
                        if key_text.lower() == "originalfilename":
                            original = safe_name(value_text)
                        elif key_text.lower() == "filedescription":
                            description = value_text.strip()
        nested.close()
    except Exception:
        pass
    return original, description


def valid_pe_at(data: bytes, offset: int) -> Optional[int]:
    if offset < 0 or offset + 0x40 > len(data) or data[offset : offset + 2] != b"MZ":
        return None
    try:
        e_lfanew = struct.unpack_from("<I", data, offset + 0x3C)[0]
        nt = offset + e_lfanew
        if nt + 24 > len(data) or data[nt : nt + 4] != b"PE\x00\x00":
            return None
        sections = struct.unpack_from("<H", data, nt + 6)[0]
        optional_size = struct.unpack_from("<H", data, nt + 20)[0]
        section_table = nt + 24 + optional_size
        if sections <= 0 or sections > 96 or section_table + sections * 40 > len(data):
            return None

        relative_end = max(0x200, section_table + sections * 40 - offset)
        for index in range(sections):
            header = section_table + index * 40
            raw_size = struct.unpack_from("<I", data, header + 16)[0]
            raw_ptr = struct.unpack_from("<I", data, header + 20)[0]
            if raw_ptr > len(data) or raw_size > len(data):
                return None
            relative_end = max(relative_end, raw_ptr + raw_size)

        if offset + relative_end > len(data):
            return None
        return relative_end
    except (ValueError, struct.error):
        return None


def nested_pes(data: bytes) -> Iterable[tuple[int, bytes]]:
    cursor = 0
    while True:
        offset = data.find(b"MZ", cursor)
        if offset < 0:
            break
        size = valid_pe_at(data, offset)
        if size:
            yield offset, data[offset : offset + size]
            cursor = offset + max(size, 2)
        else:
            cursor = offset + 2


def looks_like_batch(data: bytes) -> bool:
    sample = data[:65536]
    if b"\x00" in sample:
        return False
    text = sample.decode("utf-8", errors="ignore").lower()
    markers = ("@echo off", "cmd.exe", "powershell", "start ", "call ", "%~dp0")
    return any(marker in text for marker in markers)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("input", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()

    source = args.input.resolve()
    output = args.output.resolve()
    if not source.is_file():
        raise SystemExit(f"Input not found: {source}")

    if output.exists():
        shutil.rmtree(output)
    raw_dir = output / "raw-resources"
    tools_dir = output / "tools"
    raw_dir.mkdir(parents=True)
    tools_dir.mkdir(parents=True)

    image = source.read_bytes()
    source_hash = sha256(image)
    pe = pefile.PE(data=image, fast_load=False)
    pe.parse_data_directories(directories=[pefile.DIRECTORY_ENTRY["IMAGE_DIRECTORY_ENTRY_RESOURCE"]])

    resources = list(iter_pe_resources(pe, image))
    pe.close()

    manifest: list[ExtractedItem] = []
    seen: set[str] = set()

    def save_candidate(blob: bytes, source_label: str, kind: str, preferred_name: str = "") -> None:
        digest = sha256(blob)
        if digest in seen:
            return
        seen.add(digest)

        original = ""
        description = ""
        suffix = ".bin"
        if kind == "pe":
            original, description = pe_metadata(blob)
            suffix = Path(original).suffix if original else ".exe"
            if suffix.lower() not in {".exe", ".dll", ".sys", ".scr", ".cpl"}:
                suffix = ".exe"
        elif kind == "batch":
            suffix = ".bat"

        base = safe_name(preferred_name or original or f"embedded-{len(manifest) + 1:02d}")
        if not Path(base).suffix:
            base += suffix
        destination = tools_dir / base
        counter = 2
        while destination.exists():
            destination = tools_dir / f"{Path(base).stem}-{counter}{Path(base).suffix}"
            counter += 1
        destination.write_bytes(blob)
        manifest.append(
            ExtractedItem(
                file=destination.name,
                source=source_label,
                kind=kind,
                size=len(blob),
                sha256=digest,
                original_filename=original,
                file_description=description,
            )
        )

    for index, (label, blob) in enumerate(resources, start=1):
        raw_suffix = ".bin"
        if blob.startswith(b"MZ"):
            raw_suffix = ".pe"
        elif looks_like_batch(blob):
            raw_suffix = ".txt"
        raw_path = raw_dir / f"resource-{index:03d}-{safe_name(label.replace('/', '-'))}{raw_suffix}"
        raw_path.write_bytes(blob)

        if valid_pe_at(blob, 0):
            save_candidate(blob, label, "pe")
        elif looks_like_batch(blob):
            save_candidate(blob.rstrip(b"\x00"), label, "batch")

        for offset, nested in nested_pes(blob):
            save_candidate(nested, f"{label}@0x{offset:X}", "pe")

    # Last-resort scan of the complete executable. Offset zero is the wrapper itself,
    # so it is deliberately skipped.
    for offset, nested in nested_pes(image):
        if offset == 0:
            continue
        save_candidate(nested, f"whole-file@0x{offset:X}", "pe")

    report = {
        "input": str(source.name),
        "input_size": len(image),
        "input_sha256": source_hash,
        "resource_count": len(resources),
        "extracted_count": len(manifest),
        "items": [asdict(item) for item in manifest],
    }
    (output / "manifest.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")

    lines = [
        "# FACM embedded tool extraction",
        "",
        f"- Input: `{source.name}`",
        f"- Input SHA-256: `{source_hash}`",
        f"- PE resources enumerated: {len(resources)}",
        f"- Unique candidates extracted: {len(manifest)}",
        "",
        "| File | Kind | Size | SHA-256 | Source |",
        "|---|---:|---:|---|---|",
    ]
    for item in manifest:
        lines.append(f"| `{item.file}` | {item.kind} | {item.size} | `{item.sha256}` | `{item.source}` |")
    (output / "REPORT.md").write_text("\n".join(lines) + "\n", encoding="utf-8")

    print(json.dumps(report, ensure_ascii=False, indent=2))
    if not manifest:
        print("No embedded executable or batch candidates were found.")
        return 2
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
