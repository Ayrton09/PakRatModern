#!/usr/bin/env python3
"""Genera el hash de referencia del PAK que usan los tests del core en C#.

El contenido debe coincidir con SampleEntries() de src/PakRatModern.Tests.
Si los dos escritores divergen, el test del core falla. El fixture incluye un
nombre con caracteres fuera de ASCII para cubrir el flag UTF-8 del ZIP, otro
que vino sin ese flag (se escribe con sus bytes crudos, como lo deja vbsp) y
un nombre que empieza con guion bajo para cubrir el orden ordinal.
"""

import hashlib
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(REPO))

import pakrat_modern as pm  # noqa: E402

FILES = {
    "materials/_pre.vmt": b"pre",
    "materials/A.vmt": b"AAA",
    "materials/a_b.vmt": b"ab-",
    "materials/ab.vmt": b"ab",
    "materials/custom/señal.vmt": b"utf8",
    "materials/legacy/café.vmt": b"latin1",
    "models/de_dust2/x.mdl": b"mdl-data",
    "maps/de_dust2.nav": b"nav",
}

# Nombres que llegaron sin flag UTF-8 y se reescriben con sus bytes originales.
LEGACY = {"materials/legacy/café.vmt"}


def build_entries() -> "pm.PakEntries":
    entries = pm.PakEntries()
    for name, data in FILES.items():
        entries[name] = data
        if name in LEGACY:
            entries.set_legacy(name)
    return entries


def main() -> int:
    entries = build_entries()

    pak = pm.write_pak_entries(entries)
    digest = hashlib.sha256(pak).hexdigest()

    out_dir = REPO / "src" / "testdata"
    out_dir.mkdir(parents=True, exist_ok=True)
    (out_dir / "reference-pak.sha256").write_text(digest + "\n", encoding="ascii")

    print(f"{digest}  ({len(pak)} bytes)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
