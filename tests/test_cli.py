"""Pruebas del CLI (pakrat_modern.py).

Se corren con `python -m unittest discover -s tests`. Cubren lo que la
auditoria de 1.3.0 encontro sin red: metadatos del ZIP origen filtrandose a la
salida, verify con falsos positivos, backup solo en --inplace, duplicados con
contenido distinto y lumps que solapan la cabecera.
"""

from __future__ import annotations

import hashlib
import io
import os
import struct
import sys
import tempfile
import unittest
import zipfile
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(REPO))

import pakrat_modern as pm  # noqa: E402


def build_bsp(pak: bytes = b"", entities: bytes = b"{}\n") -> bytes:
    """BSP minimo: entidades, y el PAK como ultimo lump, alineados a 4."""
    lumps = [pm.Lump(0, 0, 0, b"\0\0\0\0") for _ in range(pm.LUMP_COUNT)]
    body = bytearray()

    def place(index: int, data: bytes) -> None:
        while len(body) % 4:
            body.append(0)
        lumps[index] = pm.Lump(pm.HEADER_SIZE + len(body), len(data), 0, b"\0\0\0\0")
        body.extend(data)

    place(0, entities)
    if pak:
        place(pm.PAK_LUMP_INDEX, pak)

    return pm._serialize_header(21, 1, lumps) + bytes(body)


def raw_zip(files, compression=zipfile.ZIP_STORED, comment=b"", internal_attr=0) -> bytes:
    buf = io.BytesIO()
    with zipfile.ZipFile(buf, "w", compression) as zf:
        for name, data in files:
            info = zipfile.ZipInfo(name, date_time=(2005, 6, 7, 8, 9, 10))
            info.compress_type = compression
            info.comment = comment
            info.internal_attr = internal_attr
            zf.writestr(info, data)
    return buf.getvalue()


class WriterParity(unittest.TestCase):
    def test_source_metadata_is_not_copied(self):
        """comment e internal_attr del PAK origen no deben llegar a la salida."""
        dirty = raw_zip([("materials/a.vmt", b"x")], comment=b"hello", internal_attr=1)
        clean = raw_zip([("materials/a.vmt", b"x")])

        out_dirty = pm.write_pak_entries(pm.read_pak_entries(dirty))
        out_clean = pm.write_pak_entries(pm.read_pak_entries(clean))

        self.assertEqual(out_dirty, out_clean)
        with zipfile.ZipFile(io.BytesIO(out_dirty)) as zf:
            info = zf.infolist()[0]
            self.assertEqual(info.comment, b"")
            self.assertEqual(info.internal_attr, 0)
            self.assertEqual(info.compress_type, zipfile.ZIP_STORED)
            self.assertEqual(info.date_time, pm.CANONICAL_DATE_TIME)

    def test_reference_hash_matches_committed_fixture(self):
        """El hash commiteado es el que el test de C# compara; debe salir de aqui."""
        sys.path.insert(0, str(REPO / "tools"))
        import make_pak_reference  # noqa: E402

        entries = pm.PakEntries()
        for name, data in make_pak_reference.FILES.items():
            entries[name] = data
        digest = hashlib.sha256(pm.write_pak_entries(entries)).hexdigest()

        committed = (REPO / "src" / "testdata" / "reference-pak.sha256").read_text().strip()
        self.assertEqual(digest, committed)

    def test_ordinal_sort_by_utf16_units(self):
        entries = pm.PakEntries()
        entries["a/\U0001F600.vmt"] = b"1"   # fuera del BMP: surrogates D83D DE00
        entries["a/\uFF01.vmt"] = b"2"       # FF01 > D83D en UTF-16, < 1F600 en code points
        with zipfile.ZipFile(io.BytesIO(pm.write_pak_entries(entries))) as zf:
            names = [i.filename for i in zf.infolist()]
        self.assertEqual(names, ["a/\U0001F600.vmt", "a/\uFF01.vmt"])

    def test_deterministic(self):
        entries = pm.PakEntries()
        entries["b.txt"] = b"b"
        entries["a.txt"] = b"a"
        self.assertEqual(pm.write_pak_entries(entries), pm.write_pak_entries(entries))


class Reader(unittest.TestCase):
    def test_duplicate_with_different_content_is_flagged(self):
        buf = io.BytesIO()
        with zipfile.ZipFile(buf, "w") as zf:
            zf.writestr("materials/dup.vmt", b"first")
            zf.writestr("materials/DUP.vmt", b"second")
            zf.writestr("materials/same.vmt", b"x")
            zf.writestr("materials/SAME.vmt", b"x")

        entries = pm.read_pak_entries(buf.getvalue())

        self.assertEqual(len(entries), 2)
        self.assertEqual(entries["materials/dup.vmt"], b"second")
        self.assertEqual(len(entries.duplicates), 2)
        self.assertTrue(any("DUP.vmt" in d and "contenido distinto" in d for d in entries.duplicates))
        self.assertIn("materials/SAME.vmt", entries.duplicates)

    def test_unsafe_entries_are_skipped_not_fatal(self):
        pak = raw_zip([("materials/ok.vmt", b"1"), ("../evil.vmt", b"2"), ("C:/abs.vmt", b"3")])
        entries = pm.read_pak_entries(pak)
        self.assertEqual(list(entries), ["materials/ok.vmt"])
        self.assertEqual(len(entries.skipped), 2)

    def test_unsupported_method_is_refused_up_front(self):
        pak = bytearray(raw_zip([("materials/a.vmt", b"aaaa")]))
        eocd = pak.rfind(b"PK\x05\x06")
        cd = struct.unpack_from("<I", pak, eocd + 16)[0]
        struct.pack_into("<H", pak, cd + 10, 9)   # Deflate64 en el directorio central
        with self.assertRaises(pm.UnsupportedCompression):
            pm.read_pak_entries(bytes(pak))

    def test_deflate_is_read_and_rewritten_stored(self):
        pak = raw_zip([("materials/a.vmt", b"A" * 5000)], compression=zipfile.ZIP_DEFLATED)
        entries = pm.read_pak_entries(pak)
        self.assertEqual(len(entries["materials/a.vmt"]), 5000)
        with zipfile.ZipFile(io.BytesIO(pm.write_pak_entries(entries))) as zf:
            self.assertTrue(all(i.compress_type == zipfile.ZIP_STORED for i in zf.infolist()))


class Bsp(unittest.TestCase):
    def test_lump_inside_header_is_rejected(self):
        raw = bytearray(build_bsp())
        struct.pack_into("<i", raw, 8, 100)   # lump 0 -> fileofs 100, dentro de la cabecera
        with tempfile.TemporaryDirectory() as tmp:
            p = Path(tmp) / "x.bsp"
            p.write_bytes(raw)
            with self.assertRaises(ValueError):
                pm.parse_bsp(p)

    def test_verify_reports_what_save_would_keep(self):
        pak = raw_zip([("materials/ok.vmt", b"1"), ("/abs.vmt", b"2")])
        with tempfile.TemporaryDirectory() as tmp:
            p = Path(tmp) / "x.bsp"
            p.write_bytes(build_bsp(pak))
            ok, msg = pm.verify_pak(pm.parse_bsp(p))
        self.assertTrue(ok)
        self.assertIn("1 entradas", msg)
        self.assertIn("insegura", msg)

    def test_verify_unsupported_method_returns_false_not_exception(self):
        pak = bytearray(raw_zip([("materials/a.vmt", b"aaaa")]))
        eocd = pak.rfind(b"PK\x05\x06")
        cd = struct.unpack_from("<I", pak, eocd + 16)[0]
        struct.pack_into("<H", pak, cd + 10, 98)   # PPMd
        with tempfile.TemporaryDirectory() as tmp:
            p = Path(tmp) / "x.bsp"
            p.write_bytes(build_bsp(bytes(pak)))
            ok, msg = pm.verify_pak(pm.parse_bsp(p))
        self.assertFalse(ok)
        self.assertIn("no soportada", msg)


class Commands(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = Path(self.tmp.name)
        self.bsp = self.root / "map.bsp"
        self.bsp.write_bytes(build_bsp(raw_zip([("materials/old.vmt", b"old")])))
        self.base = self.root / "content"
        (self.base / "materials").mkdir(parents=True)
        (self.base / "materials" / "new.vmt").write_bytes(b"new")

    def tearDown(self):
        self.tmp.cleanup()

    def run_cli(self, *args) -> int:
        return pm.main([str(a) for a in args])

    def test_add_out_over_existing_file_makes_backup(self):
        out = self.root / "other.bsp"
        out.write_bytes(b"previous")
        rc = self.run_cli("add", self.bsp, self.base / "materials", "--base", self.base, "--out", out)
        self.assertEqual(rc, 0)
        self.assertEqual((self.root / "other.bsp.bak").read_bytes(), b"previous")
        names = [n for n, _ in pm.list_pak(pm.parse_bsp(out))]
        self.assertEqual(names, ["materials/new.vmt", "materials/old.vmt"])

    def test_add_inplace_makes_backup_and_no_backup_disables_it(self):
        original = self.bsp.read_bytes()
        self.assertEqual(self.run_cli("add", self.bsp, self.base / "materials", "--base", self.base, "--inplace"), 0)
        self.assertEqual((self.root / "map.bsp.bak").read_bytes(), original)

        (self.root / "map.bsp.bak").unlink()
        self.assertEqual(self.run_cli("remove", self.bsp, "materials/new.vmt", "--inplace", "--no-backup"), 0)
        self.assertFalse((self.root / "map.bsp.bak").exists())

    def test_inplace_and_out_are_mutually_exclusive(self):
        with self.assertRaises(SystemExit):
            self.run_cli("add", self.bsp, self.base, "--base", self.base, "--inplace", "--out", self.root / "x.bsp")

    def test_base_must_be_a_directory(self):
        with self.assertRaises(SystemExit):
            self.run_cli("add", self.bsp, self.base, "--base", self.base / "materials" / "new.vmt")

    def test_extract_keeps_existing_unless_overwrite(self):
        out = self.root / "out"
        (out / "materials").mkdir(parents=True)
        (out / "materials" / "old.vmt").write_bytes(b"mine")

        self.assertEqual(self.run_cli("extract", self.bsp, "--out", out), 0)
        self.assertEqual((out / "materials" / "old.vmt").read_bytes(), b"mine")

        self.assertEqual(self.run_cli("extract", self.bsp, "--out", out, "--overwrite"), 0)
        self.assertEqual((out / "materials" / "old.vmt").read_bytes(), b"old")

    def test_extract_patterns_are_case_insensitive(self):
        out = self.root / "out2"
        count, _ = pm.extract_pak(pm.parse_bsp(self.bsp), out, ["MATERIALS/*.VMT"])
        self.assertEqual(count, 1)

    def test_atomic_write_leaves_no_temp_files(self):
        self.assertEqual(self.run_cli("remove", self.bsp, "materials/old.vmt", "--inplace"), 0)
        leftovers = [p.name for p in self.root.iterdir() if p.name.endswith(".tmp")]
        self.assertEqual(leftovers, [])
        self.assertEqual(pm.list_pak(pm.parse_bsp(self.bsp)), [])


if __name__ == "__main__":
    unittest.main()
