#!/usr/bin/env python3
"""Genera el hash de referencia del PAK que usan los tests del core en C#.

El contenido debe coincidir con SampleEntries() de src/PakRatModern.Tests.
Si los dos escritores divergen, el test del core falla.
"""

import hashlib
import sys
import zipfile
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(REPO))

import pakrat_modern as pm  # noqa: E402

FILES = {
    "materials/_pre.vmt": b"pre",
    "materials/A.vmt": b"AAA",
    "materials/a_b.vmt": b"ab-",
    "materials/ab.vmt": b"ab",
    "models/de_dust2/x.mdl": b"mdl-data",
    "maps/de_dust2.nav": b"nav",
}


def main() -> int:
    entries = pm.PakEntries()
    for name, data in FILES.items():
        info = zipfile.ZipInfo(filename=name, date_time=(1980, 1, 1, 0, 0, 0))
        info.compress_type = zipfile.ZIP_STORED
        entries[name] = (info, data)

    pak = pm.write_pak_entries(entries)
    digest = hashlib.sha256(pak).hexdigest()

    out_dir = REPO / "src" / "testdata"
    out_dir.mkdir(parents=True, exist_ok=True)
    (out_dir / "reference-pak.sha256").write_text(digest + "\n", encoding="ascii")

    print(f"{digest}  ({len(pak)} bytes)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
