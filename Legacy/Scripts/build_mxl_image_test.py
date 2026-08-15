#!/usr/bin/env python3
"""Create a self-contained .mxl smoke test with a page-level PNG credit image.

This is intentionally CI-only. It does not change conversion semantics. The PNG is generated
with a tiny built-in bitmap font so the workflow needs no Pillow/ImageMagick dependency.
"""

from __future__ import annotations

import argparse
import binascii
import struct
import tempfile
import xml.etree.ElementTree as ET
import zipfile
import zlib
from pathlib import Path

FONT = {
    " ": ["00000"] * 7,
    "H": ["10001", "10001", "10001", "11111", "10001", "10001", "10001"],
    "E": ["11111", "10000", "10000", "11110", "10000", "10000", "11111"],
    "L": ["10000", "10000", "10000", "10000", "10000", "10000", "11111"],
    "O": ["01110", "10001", "10001", "10001", "10001", "10001", "01110"],
    "W": ["10001", "10001", "10001", "10101", "10101", "10101", "01010"],
    "R": ["11110", "10001", "10001", "11110", "10100", "10010", "10001"],
    "D": ["11110", "10001", "10001", "10001", "10001", "10001", "11110"],
}


def png_chunk(kind: bytes, data: bytes) -> bytes:
    return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", binascii.crc32(kind + data) & 0xFFFFFFFF)


def make_hello_png(path: Path) -> None:
    text = "HELLO WORLD"
    scale = 10
    margin_x = 30
    margin_y = 24
    char_w = 5 * scale
    gap = scale
    width = margin_x * 2 + len(text) * char_w + (len(text) - 1) * gap
    height = margin_y * 2 + 7 * scale

    # RGBA canvas: transparent white background, opaque black letters.
    pixels = bytearray(width * height * 4)
    for i in range(0, len(pixels), 4):
        pixels[i:i+4] = bytes((255, 255, 255, 0))

    x0 = margin_x
    for ch in text:
        glyph = FONT[ch]
        for gy, row in enumerate(glyph):
            for gx, bit in enumerate(row):
                if bit != "1":
                    continue
                for yy in range(margin_y + gy * scale, margin_y + (gy + 1) * scale):
                    for xx in range(x0 + gx * scale, x0 + (gx + 1) * scale):
                        p = (yy * width + xx) * 4
                        pixels[p:p+4] = bytes((0, 0, 0, 255))
        x0 += char_w + gap

    raw = bytearray()
    stride = width * 4
    for y in range(height):
        raw.append(0)  # PNG filter: None
        raw.extend(pixels[y * stride:(y + 1) * stride])

    png = bytearray(b"\x89PNG\r\n\x1a\n")
    png += png_chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0))
    png += png_chunk(b"IDAT", zlib.compress(bytes(raw), 9))
    png += png_chunk(b"IEND", b"")
    path.write_bytes(png)


def add_credit_image(source_xml: Path, target_xml: Path) -> None:
    tree = ET.parse(source_xml)
    root = tree.getroot()

    # MusicXML credit coordinates are in tenths from the page's bottom-left corner.
    # ~595 is the horizontal centre of a typical A4 MusicXML page; ~1540 puts the
    # image into the header area above the first system. MuseScore compatibility is
    # exactly what this smoke test is intended to verify.
    credit = ET.Element("credit", {"page": "1"})
    ET.SubElement(credit, "credit-image", {
        "source": "images/hello-world.png",
        "type": "image/png",
        "default-x": "595",
        "default-y": "1540",
        "halign": "center",
        "valign": "middle",
        "width": "430",
        "height": "78",
    })

    insert_at = 0
    for i, child in enumerate(list(root)):
        if child.tag in ("work", "movement-number", "movement-title", "identification", "defaults", "credit"):
            insert_at = i + 1
        else:
            break
    root.insert(insert_at, credit)
    tree.write(target_xml, encoding="utf-8", xml_declaration=True)


def build_mxl(source_xml: Path, output_mxl: Path, debug_dir: Path | None) -> None:
    output_mxl.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory() as tmp:
        root = Path(tmp)
        score = root / "score.musicxml"
        image = root / "images" / "hello-world.png"
        image.parent.mkdir(parents=True)
        make_hello_png(image)
        add_credit_image(source_xml, score)

        container = root / "META-INF" / "container.xml"
        container.parent.mkdir(parents=True)
        container.write_text(
            '<?xml version="1.0" encoding="UTF-8"?>\n'
            '<container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">\n'
            '  <rootfiles>\n'
            '    <rootfile full-path="score.musicxml" media-type="application/vnd.recordare.musicxml+xml"/>\n'
            '  </rootfiles>\n'
            '</container>\n',
            encoding="utf-8",
        )

        with zipfile.ZipFile(output_mxl, "w") as zf:
            # MusicXML 4.0 requires mimetype to be the first entry and stored uncompressed.
            zf.writestr("mimetype", "application/vnd.recordare.musicxml", compress_type=zipfile.ZIP_STORED)
            zf.write(container, "META-INF/container.xml", compress_type=zipfile.ZIP_DEFLATED)
            zf.write(score, "score.musicxml", compress_type=zipfile.ZIP_DEFLATED)
            zf.write(image, "images/hello-world.png", compress_type=zipfile.ZIP_DEFLATED)

        if debug_dir is not None:
            debug_dir.mkdir(parents=True, exist_ok=True)
            (debug_dir / "hello-world.png").write_bytes(image.read_bytes())
            (debug_dir / "image-test.musicxml").write_bytes(score.read_bytes())


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("source_musicxml", type=Path)
    parser.add_argument("output_mxl", type=Path)
    parser.add_argument("--debug-dir", type=Path)
    args = parser.parse_args()
    build_mxl(args.source_musicxml, args.output_mxl, args.debug_dir)


if __name__ == "__main__":
    main()
