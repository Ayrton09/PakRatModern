#!/usr/bin/env python3
"""Genera los fixtures LZMA que usan los tests del core en C#.

.NET Framework no trae un compresor LZMA, asi que los datos comprimidos se
producen aca y se commitean. El contenido original es determinista (semilla
fija) y su tamano y SHA-256 quedan en manifest.txt: el test descomprime y
compara contra eso. Volver a correr este script puede dar bytes comprimidos
distintos con otra version de liblzma, pero el contenido sera el mismo.
"""

import hashlib
import io
import lzma
import random
import struct
import zipfile
from pathlib import Path

OUT = Path(__file__).resolve().parent.parent / "src" / "testdata" / "lzma"


def source_block(data: bytes, filters=None) -> bytes:
    """Formato de Source: 'LZMA' + actualSize + lzmaSize + propiedades[5] + stream."""
    kwargs = {"format": lzma.FORMAT_ALONE}
    if filters:
        kwargs["filters"] = filters
    alone = lzma.compress(data, **kwargs)   # propiedades(5) + tamano(8) + stream
    return b"LZMA" + struct.pack("<II", len(data), len(alone) - 13) + alone[:5] + alone[13:]


def mixed(rng: random.Random, size: int) -> bytes:
    """Texto, rafagas, bytes al azar y copias lejanas: ejercita todos los caminos del decodificador."""
    words = [b"materials/", b"models/props/", b"sound/ambient/", b".vmt", b".vtf",
             b'"$basetexture" ', b"LightmappedGeneric", b"{\n", b"}\n"]
    out = bytearray()
    while len(out) < size:
        kind = rng.randrange(5)
        if kind == 0:
            for _ in range(rng.randrange(5, 40)):
                out += words[rng.randrange(len(words))]
        elif kind == 1 and len(out) > 1000:
            start = rng.randrange(0, len(out) - 500)
            out += out[start:start + rng.randrange(3, 300)]
        elif kind == 2:
            out += bytes([rng.randrange(256)]) * rng.randrange(1, 600)
        elif kind == 3:
            out += bytes(rng.getrandbits(8) for _ in range(rng.randrange(1, 200)))
        elif len(out) > 16:
            distance = rng.choice([1, 2, 4, 8, 16])
            out += bytes(out[-distance:]) * rng.randrange(1, 20)
    return bytes(out[:size])


ENTITIES = b"""{
"classname" "worldspawn"
}
{
"classname" "prop_dynamic"
"model" "models/props/lz/crate.mdl"
}
{
"classname" "env_sprite"
"model" "sprites/lz/glow.vmt"
}
"""


def static_props(names) -> bytes:
    data = struct.pack("<i", len(names))
    for name in names:
        data += name.encode("ascii").ljust(128, b"\0")
    return data + struct.pack("<i", 0) + struct.pack("<i", 0)   # leafs y props vacios


def main() -> int:
    OUT.mkdir(parents=True, exist_ok=True)
    rng = random.Random(20260928)
    manifest = []

    def record(name: str, plain: bytes) -> None:
        manifest.append(f"{name} {len(plain)} {hashlib.sha256(plain).hexdigest()}")

    blocks = {
        "mixed.lzma": (mixed(rng, 200_000), None),
        "literals.lzma": (bytes(rng.getrandbits(8) for _ in range(16_384)),
                          [{"id": lzma.FILTER_LZMA1, "lc": 0, "lp": 2, "pb": 0, "dict_size": 1 << 16}]),
        "entities.lzma": (ENTITIES, None),
        "sprp.lzma": (static_props(["models/props/lz/tree.mdl", "models/props/lz/rock.mdl"]), None),
    }
    for name, (plain, filters) in blocks.items():
        (OUT / name).write_bytes(source_block(plain, filters))
        record(name, plain)

    # PAK con entradas LZMA (metodo 14), como lo deja bspzip -repack -compress
    files = {
        "materials/lz/a.vmt": b'"LightmappedGeneric"\n{\n"$basetexture" "lz/a"\n}\n',
        "models/lz/b.mdl": mixed(rng, 40_000),
        "sound/lz/empty.wav": b"",
    }
    buf = io.BytesIO()
    with zipfile.ZipFile(buf, "w", zipfile.ZIP_LZMA) as zf:
        for name, data in files.items():
            info = zipfile.ZipInfo(name, date_time=(1980, 1, 1, 0, 0, 0))
            info.compress_type = zipfile.ZIP_LZMA
            zf.writestr(info, data)
            record(f"pak-lzma.zip:{name}", data)
    (OUT / "pak-lzma.zip").write_bytes(buf.getvalue())

    (OUT / "manifest.txt").write_text("\n".join(manifest) + "\n", encoding="ascii")
    print("\n".join(manifest))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
