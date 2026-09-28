"""Pruebas del CLI (pakrat_modern.py).

Se corren con `python -m unittest discover -s tests`. Cubren los casos que
antes pasaban sin control: metadatos del ZIP origen filtrandose a la salida,
verify con falsos positivos, backups, duplicados con contenido distinto, lumps
que solapan la cabecera, nombres sin flag UTF-8, la tabla de lumps de L4D2,
repack, el nombre de salida por defecto y los permisos en Linux.
"""

from __future__ import annotations

import contextlib
import hashlib
import io
import os
import re
import stat
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


def stored_zip_with_raw_names(files) -> bytes:
    """ZIP Stored armado a mano: (nombre_bytes, flags, datos). zipfile no deja
    escribir un nombre no ASCII sin el flag UTF-8, que es como lo deja vbsp."""
    import zlib

    body = bytearray()
    central = bytearray()
    for name, flags, data in files:
        crc = zlib.crc32(data) & 0xFFFFFFFF
        offset = len(body)
        body += struct.pack("<IHHHHHIIIHH", 0x04034B50, 20, flags, 0, 0, 33, crc, len(data), len(data), len(name), 0)
        body += name + data
        central += struct.pack("<IHHHHHHIIIHHHHHII", 0x02014B50, 20, 20, flags, 0, 0, 33, crc,
                               len(data), len(data), len(name), 0, 0, 0, 0, 0, offset)
        central += name
    eocd = struct.pack("<IHHHHIIH", 0x06054B50, 0, 0, len(files), len(files), len(central), len(body), 0)
    return bytes(body + central + eocd)


def central_names(pak: bytes):
    """(nombre_bytes, flags) de cada entrada, leidos del directorio central."""
    eocd = pak.rfind(b"PK\x05\x06")
    count, _size, pos = struct.unpack_from("<HII", pak, eocd + 10)
    names = []
    for _ in range(count):
        flags = struct.unpack_from("<H", pak, pos + 8)[0]
        nlen, xlen, clen = struct.unpack_from("<HHH", pak, pos + 28)
        names.append((pak[pos + 46:pos + 46 + nlen], flags))
        pos += 46 + nlen + xlen + clen
    return names


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

        digest = hashlib.sha256(pm.write_pak_entries(make_pak_reference.build_entries())).hexdigest()

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
        self.assertTrue(any("DUP.vmt" in d and "different content" in d for d in entries.duplicates))
        self.assertIn("materials/SAME.vmt", entries.duplicates)

    def test_names_without_utf8_flag_keep_their_bytes(self):
        """vbsp guarda los nombres crudos y sin flag; el motor busca esos bytes."""
        legacy = "materials/café.vmt".encode("latin-1")
        utf8 = "materials/señal.vmt".encode("utf-8")
        pak = stored_zip_with_raw_names([(legacy, 0, b"latin1"), (utf8, 0x800, b"utf8")])

        entries = pm.read_pak_entries(pak)
        self.assertIn("materials/café.vmt", entries)
        self.assertTrue(entries.is_legacy("materials/café.vmt"))
        self.assertFalse(entries.is_legacy("materials/señal.vmt"))

        entries["materials/café.vmt"] = b"reemplazado"   # reemplazar no cambia el nombre
        names = dict(central_names(pm.write_pak_entries(entries)))
        self.assertEqual(names[legacy], 0)
        self.assertEqual(names[utf8], 0x800)

    def test_crc_mismatch_is_rejected(self):
        pak = bytearray(raw_zip([("materials/c.vmt", b"payload")]))
        pak[pak.find(b"payload")] ^= 0x20
        with self.assertRaises(zipfile.BadZipFile):
            pm.read_pak_entries(bytes(pak))

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
        self.assertIn("1 entries", msg)
        self.assertIn("unsafe", msg)

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
        self.assertIn("unsupported", msg)

    def test_left4dead2_lump_order_is_read_and_kept(self):
        """L4D2 pone la version primero en la tabla de lumps; antes se rechazaba."""
        bsp = pm.parse_bsp_bytes(build_bsp(raw_zip([("materials/a.vmt", b"a")])))
        lumps = [pm.Lump(l.fileofs, l.filelen, l.version, l.fourcc) for l in bsp.lumps]
        lumps[pm.PAK_LUMP_INDEX].version = 1
        raw = pm._serialize_header(bsp.version, bsp.map_revision, lumps, pm.LAYOUT_VERSION_FIRST) + bsp.raw[pm.HEADER_SIZE:]

        l4d2 = pm.parse_bsp_bytes(raw)
        self.assertEqual(l4d2.layout, pm.LAYOUT_VERSION_FIRST)

        entries = pm.read_pak_entries(pm._get_lump_bytes(l4d2, pm.PAK_LUMP_INDEX))
        entries["materials/nuevo.vmt"] = b"x" * 40
        saved = pm.apply_pak_to_bsp(l4d2, pm.write_pak_entries(entries))
        again = pm.parse_bsp_bytes(saved)

        at = 8 + pm.PAK_LUMP_INDEX * 16
        self.assertEqual(again.layout, pm.LAYOUT_VERSION_FIRST)
        self.assertEqual(struct.unpack_from("<ii", saved, at), (1, again.lumps[pm.PAK_LUMP_INDEX].fileofs))
        self.assertEqual(len(pm.read_pak_entries(pm._get_lump_bytes(again, pm.PAK_LUMP_INDEX))), 2)

    def test_damaged_table_is_not_mistaken_for_left4dead2(self):
        raw = bytearray(build_bsp())
        struct.pack_into("<i", raw, 8, 100)   # lump 0 dentro de la cabecera
        with self.assertRaises(ValueError):
            pm.parse_bsp_bytes(bytes(raw))


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

    def test_default_output_names(self):
        """Sin --inplace ni --out se escribe <mapa>_packed.bsp; no depende de with_stem (3.9+)."""
        self.assertEqual(self.run_cli("add", self.bsp, self.base / "materials", "--base", self.base), 0)
        self.assertTrue((self.root / "map_packed.bsp").is_file())
        self.assertEqual(self.run_cli("remove", self.bsp, "materials/old.vmt"), 0)
        self.assertTrue((self.root / "map_stripped.bsp").is_file())

    def test_repack_rewrites_uncompressed_without_adding_files(self):
        self.bsp.write_bytes(build_bsp(raw_zip([("materials/a.vmt", b"A" * 3000), ("models/b.mdl", b"mdl")],
                                               compression=zipfile.ZIP_BZIP2)))
        self.assertEqual(self.run_cli("repack", self.bsp, "--inplace"), 0)
        self.assertTrue((self.root / "map.bsp.bak").exists())

        pak = pm._get_lump_bytes(pm.parse_bsp(self.bsp), pm.PAK_LUMP_INDEX)
        with zipfile.ZipFile(io.BytesIO(pak)) as zf:
            self.assertEqual(sorted(i.filename for i in zf.infolist()), ["materials/a.vmt", "models/b.mdl"])
            self.assertTrue(all(i.compress_type == zipfile.ZIP_STORED for i in zf.infolist()))

    @unittest.skipUnless(os.name == "posix", "los permisos de archivo solo aplican en POSIX")
    def test_rewritten_files_keep_their_permissions(self):
        """mkstemp crea 0600: sin corregirlo, cada BSP reescrito quedaba ilegible para otros usuarios."""
        os.chmod(self.bsp, 0o644)
        self.assertEqual(self.run_cli("add", self.bsp, self.base / "materials", "--base", self.base, "--inplace"), 0)
        self.assertEqual(stat.S_IMODE(self.bsp.stat().st_mode), 0o644)
        self.assertEqual(stat.S_IMODE((self.root / "map.bsp.bak").stat().st_mode), 0o644)

        umask = os.umask(0)
        os.umask(umask)
        out = self.root / "new.bsp"
        self.assertEqual(self.run_cli("add", self.bsp, self.base / "materials", "--base", self.base, "--out", out), 0)
        self.assertEqual(stat.S_IMODE(out.stat().st_mode), 0o666 & ~umask)

    def test_version_flag_matches_the_application(self):
        stdout = io.StringIO()
        with contextlib.redirect_stdout(stdout), self.assertRaises(SystemExit):
            self.run_cli("--version")
        self.assertIn(pm.__version__, stdout.getvalue())

        csproj = (REPO / "src" / "PakRatModern.App" / "PakRatModern.App.csproj").read_text(encoding="utf-8")
        self.assertEqual(re.search(r"<Version>([^<]+)</Version>", csproj).group(1), pm.__version__)

    def test_exit_codes(self):
        """verify: 0 valido, 2 PAK invalido o metodo no soportado, 1 si el BSP no se puede leer."""
        self.assertEqual(self.run_cli("verify", self.bsp), 0)

        damaged = bytearray(self.bsp.read_bytes())
        damaged[damaged.find(b"old", pm.HEADER_SIZE + 30)] ^= 0x20   # rompe el CRC de la entrada
        self.bsp.write_bytes(bytes(damaged))
        self.assertEqual(self.run_cli("verify", self.bsp), 2)

        self.bsp.write_bytes(b"no es un bsp")
        self.assertEqual(self.run_cli("verify", self.bsp), 1)


if __name__ == "__main__":
    unittest.main()
