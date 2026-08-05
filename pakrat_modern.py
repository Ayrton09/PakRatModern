#!/usr/bin/env python3
"""
Modern replacement for PakRat focused on Source BSP PAK lump management.

Features:
- list embedded files
- extract selected/all files
- add/update files
- remove files
- verify pak lump integrity

This tool updates only the PAKFILE lump and preserves the original BSP layout,
which is safer than rebuilding all lumps from scratch.
"""

from __future__ import annotations

import argparse
import fnmatch
import io
import os
import re
import struct
import sys
import zipfile
from collections.abc import MutableMapping
from dataclasses import dataclass
from pathlib import Path, PurePosixPath
from typing import Iterable, List, Tuple

LUMP_COUNT = 64
PAK_LUMP_INDEX = 40
GAME_LUMP_INDEX = 35
IDENT = b"VBSP"
HEADER_SIZE = 4 + 4 + (LUMP_COUNT * 16) + 4
MAX_BSP_BYTES = 1024 * 1024 * 1024
MAX_PAK_ENTRY_BYTES = 512 * 1024 * 1024
MAX_PAK_TOTAL_BYTES = 1536 * 1024 * 1024
MAX_PAK_ENTRIES = 20000
# Fecha fija para toda entrada escrita. Los mapas reales traen fecha DOS 0, que
# no es una fecha valida; preservarla hacia que la salida dependiera del archivo
# de origen y no coincidiera con la del GUI ni la del core en C#.
CANONICAL_DATE_TIME = (1980, 1, 1, 0, 0, 0)
_UNSAFE_ARCHIVE_CHARS = re.compile(r'[\x00-\x1f<>:"|?*]')
_RESERVED_WINDOWS_NAMES = re.compile(r"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\..*)?$", re.IGNORECASE)


@dataclass
class Lump:
    fileofs: int
    filelen: int
    version: int
    fourcc: bytes


@dataclass
class BSPFile:
    raw: bytes
    version: int
    map_revision: int
    lumps: List[Lump]


class PakEntries(MutableMapping):
    """Entradas del PAK con claves insensibles a mayusculas.

    El motor Source resuelve rutas sin distinguir mayusculas, asi que
    materials/Custom/a.vmt y materials/custom/a.vmt son la misma entrada para el
    juego: dejar las dos dentro del ZIP hace impredecible cual gana. Se conserva
    la capitalizacion de la primera insercion, igual que hace el GUI (que usa un
    [ordered]@{} de PowerShell, tambien insensible a mayusculas).
    """

    def __init__(self) -> None:
        self._items: "dict[str, Tuple[str, object]]" = {}
        # Entradas descartadas al leer, como (nombre_original, motivo). Ver
        # read_pak_entries: una entrada con ruta insegura no invalida el resto.
        self.skipped: List[Tuple[str, str]] = []
        # Entradas repetidas que quedaron unificadas. Es comun: 11 de 115 mapas
        # de CS:S probados traen duplicados, siempre de contenido identico.
        self.duplicates: List[str] = []

    def __setitem__(self, key: str, value) -> None:
        lowered = key.lower()
        existing = self._items.get(lowered)
        self._items[lowered] = (existing[0] if existing else key, value)

    def __getitem__(self, key: str):
        return self._items[key.lower()][1]

    def __delitem__(self, key: str) -> None:
        del self._items[key.lower()]

    def __contains__(self, key: object) -> bool:
        return isinstance(key, str) and key.lower() in self._items

    def __iter__(self):
        return (name for name, _ in self._items.values())

    def __len__(self) -> int:
        return len(self._items)


def _norm_archive_path(path: str) -> str:
    p = path.replace("\\", "/").strip()
    if p.startswith("/"):
        raise ValueError(f"Ruta absoluta invalida dentro del BSP: {path}")
    if p in {"", "."}:
        raise ValueError("Ruta dentro del BSP invalida")
    if _UNSAFE_ARCHIVE_CHARS.search(p):
        raise ValueError(f"Caracteres invalidos en ruta dentro del BSP: {path}")
    for part in p.split("/"):
        if part in {"", ".", ".."} or part.endswith(".") or part.endswith(" "):
            raise ValueError(f"Ruta invalida dentro del BSP: {path}")
        if _RESERVED_WINDOWS_NAMES.match(part):
            raise ValueError(f"Nombre reservado en ruta dentro del BSP: {path}")
    return str(PurePosixPath(p))


def _align4(value: int) -> int:
    return (value + 3) & ~3


def parse_bsp(path: Path) -> BSPFile:
    size = path.stat().st_size
    if size > MAX_BSP_BYTES:
        raise ValueError(f"BSP demasiado grande ({size} bytes; limite {MAX_BSP_BYTES})")
    raw = path.read_bytes()
    if len(raw) < HEADER_SIZE:
        raise ValueError("Archivo demasiado pequeno para ser BSP Source")

    ident = raw[0:4]
    if ident != IDENT:
        raise ValueError("Identificador BSP invalido (se esperaba VBSP)")

    version = struct.unpack_from("<i", raw, 4)[0]
    lumps: List[Lump] = []

    offset = 8
    for i in range(LUMP_COUNT):
        fileofs, filelen, lump_version, fourcc = struct.unpack_from("<iii4s", raw, offset)
        offset += 16
        if filelen < 0:
            raise ValueError(f"Lump {i} tiene longitud negativa")
        if filelen > 0:
            start = fileofs
            end = start + filelen
            if start < 0 or end > len(raw) or start > end:
                raise ValueError(f"Lump {i} fuera de rango (ofs={start}, len={filelen})")
        lumps.append(Lump(fileofs, filelen, lump_version, fourcc))

    map_revision = struct.unpack_from("<i", raw, offset)[0]

    return BSPFile(raw=raw, version=version, map_revision=map_revision, lumps=lumps)


def _serialize_header(version: int, map_revision: int, lumps: List[Lump]) -> bytes:
    header = io.BytesIO()
    header.write(IDENT)
    header.write(struct.pack("<i", version))
    for lump in lumps:
        header.write(struct.pack("<iii4s", lump.fileofs, lump.filelen, lump.version, lump.fourcc))
    header.write(struct.pack("<i", map_revision))
    return header.getvalue()


def _get_lump_bytes(bsp: BSPFile, index: int) -> bytes:
    lump = bsp.lumps[index]
    if lump.filelen == 0:
        return b""
    return bsp.raw[lump.fileofs:lump.fileofs + lump.filelen]


def read_pak_entries(pak_bytes: bytes) -> PakEntries:
    entries = PakEntries()
    if not pak_bytes:
        return entries

    with zipfile.ZipFile(io.BytesIO(pak_bytes), "r") as zf:
        infos = zf.infolist()
        if len(infos) > MAX_PAK_ENTRIES:
            raise ValueError(f"PAK tiene demasiadas entradas ({len(infos)}; limite {MAX_PAK_ENTRIES})")
        total = 0
        for info in infos:
            if info.is_dir():
                continue

            # Hay mapas publicados con rutas absolutas dentro del PAK
            # ("C:/Program Files/.../x.vmt", "/sound/y.mp3"). Se descartan esas
            # entradas, no el mapa entero: rechazar todo dejaba inaccesibles
            # cientos de archivos validos por una sola entrada mal empaquetada.
            try:
                name = _norm_archive_path(info.filename)
            except ValueError as exc:
                entries.skipped.append((info.filename, str(exc)))
                continue

            if info.file_size > MAX_PAK_ENTRY_BYTES:
                raise ValueError(f"Entrada PAK demasiado grande: {name} ({info.file_size} bytes)")
            total += info.file_size
            if total > MAX_PAK_TOTAL_BYTES:
                raise ValueError(f"PAK demasiado grande descomprimido (limite {MAX_PAK_TOTAL_BYTES} bytes)")
            data = zf.read(info.filename)
            if name in entries:
                entries.duplicates.append(info.filename)
            entries[name] = (info, data)
    return entries


def write_pak_entries(entries: PakEntries) -> bytes:
    if len(entries) > MAX_PAK_ENTRIES:
        raise ValueError(f"PAK tiene demasiadas entradas ({len(entries)}; limite {MAX_PAK_ENTRIES})")
    total = 0
    buffer = io.BytesIO()
    with zipfile.ZipFile(buffer, mode="w") as zf:
        # Orden ordinal, igual que el GUI: sin ordenar, la salida dependia del
        # orden de insercion y dos PAKs con el mismo contenido no coincidian.
        for name in sorted(entries):
            info, data = entries[name]
            if len(data) > MAX_PAK_ENTRY_BYTES:
                raise ValueError(f"Entrada PAK demasiado grande: {name} ({len(data)} bytes)")
            total += len(data)
            if total > MAX_PAK_TOTAL_BYTES:
                raise ValueError(f"PAK demasiado grande descomprimido (limite {MAX_PAK_TOTAL_BYTES} bytes)")
            new_info = zipfile.ZipInfo(filename=name, date_time=CANONICAL_DATE_TIME)
            new_info.comment = info.comment
            new_info.internal_attr = info.internal_attr
            # Forma canonica fija: antes se copiaban create_system y external_attr
            # del PAK de origen, asi que el resultado dependia de con que
            # herramienta y en que sistema se habia creado el mapa. El motor
            # Source ignora los tres campos.
            new_info.create_system = 0
            new_info.extract_version = 20
            # 0o600<<16 es lo que zipfile fuerza en _open_to_write cuando el campo
            # vale 0: ponerlo en 0 no sirve de nada. Se fija explicito para que el
            # GUI pueda emitir el mismo byte y ambos salgan identicos.
            new_info.external_attr = 0o600 << 16
            # El motor Source solo lee entradas STORE del lump PAKFILE; forzamos
            # sin compresion aunque la entrada original viniera deflateada/bzip2/lzma.
            new_info.compress_type = zipfile.ZIP_STORED
            zf.writestr(new_info, data)
    return buffer.getvalue()


def _safe_extract_target(out_dir: Path, arcname: str) -> Path:
    target = (out_dir / Path(arcname)).resolve()
    root = out_dir.resolve()
    if target == root:
        raise ValueError(f"Ruta de extraccion invalida: {arcname}")
    if os.path.commonpath([str(root), str(target)]) != str(root):
        raise ValueError(f"Ruta de extraccion insegura: {arcname}")
    return target


def list_pak(bsp: BSPFile) -> List[Tuple[str, int]]:
    entries = read_pak_entries(_get_lump_bytes(bsp, PAK_LUMP_INDEX))
    _warn_skipped(entries)
    return [(name, len(data)) for name, (_, data) in sorted(entries.items())]


def extract_pak(
    bsp: BSPFile,
    out_dir: Path,
    patterns: Iterable[str] | None = None,
) -> int:
    entries = read_pak_entries(_get_lump_bytes(bsp, PAK_LUMP_INDEX))
    _warn_skipped(entries)
    pattern_list = list(patterns or [])
    count = 0

    for name, (_, data) in entries.items():
        if pattern_list and not any(fnmatch.fnmatch(name, pat) for pat in pattern_list):
            continue
        target = _safe_extract_target(out_dir, name)
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(data)
        count += 1

    return count


def update_pak_add(
    bsp: BSPFile,
    base: Path,
    files: Iterable[Path],
) -> Tuple[int, int, bytes]:
    entries = read_pak_entries(_get_lump_bytes(bsp, PAK_LUMP_INDEX))
    _warn_skipped(entries)
    added = 0
    replaced = 0

    for file_path in files:
        if not file_path.is_file():
            raise ValueError(f"No existe archivo: {file_path}")

        try:
            rel = file_path.relative_to(base)
        except ValueError as exc:
            raise ValueError(f"{file_path} no esta dentro de --base {base}") from exc

        arcname = _norm_archive_path(str(rel))
        size = file_path.stat().st_size
        if size > MAX_PAK_ENTRY_BYTES:
            raise ValueError(f"Archivo demasiado grande para PAK: {file_path} ({size} bytes)")
        data = file_path.read_bytes()

        info = zipfile.ZipInfo(filename=arcname, date_time=CANONICAL_DATE_TIME)
        info.compress_type = zipfile.ZIP_STORED

        if arcname in entries:
            replaced += 1
        else:
            added += 1
        entries[arcname] = (info, data)

    new_pak = write_pak_entries(entries)
    return added, replaced, new_pak


def update_pak_remove(bsp: BSPFile, names: Iterable[str]) -> Tuple[int, bytes]:
    entries = read_pak_entries(_get_lump_bytes(bsp, PAK_LUMP_INDEX))
    _warn_skipped(entries)
    removed = 0

    for name in names:
        key = _norm_archive_path(name)
        if key in entries:
            del entries[key]
            removed += 1

    new_pak = write_pak_entries(entries)
    return removed, new_pak


def _warn_skipped(entries: PakEntries) -> None:
    """Avisa por stderr de lo que se descarto o unifico al leer el PAK."""
    if entries.skipped:
        print(
            f"Aviso: {len(entries.skipped)} entrada(s) del PAK se ignoraron por tener rutas inseguras.",
            file=sys.stderr,
        )
        for name, reason in entries.skipped[:10]:
            print(f"  - {name}  ({reason})", file=sys.stderr)
        if len(entries.skipped) > 10:
            print(f"  ... y {len(entries.skipped) - 10} mas", file=sys.stderr)
        print("  Guardar el BSP las quitara del mapa.", file=sys.stderr)

    if entries.duplicates:
        print(
            f"Aviso: {len(entries.duplicates)} entrada(s) repetidas se unificaron "
            "(el motor no distingue mayusculas y solo puede cargar una).",
            file=sys.stderr,
        )
        for name in entries.duplicates[:10]:
            print(f"  - {name}", file=sys.stderr)
        if len(entries.duplicates) > 10:
            print(f"  ... y {len(entries.duplicates) - 10} mas", file=sys.stderr)


def verify_pak(bsp: BSPFile) -> Tuple[bool, str]:
    pak_bytes = _get_lump_bytes(bsp, PAK_LUMP_INDEX)
    if not pak_bytes:
        return True, "PAK lump vacio (sin recursos embebidos)."

    try:
        with zipfile.ZipFile(io.BytesIO(pak_bytes), "r") as zf:
            infos = zf.infolist()
            if len(infos) > MAX_PAK_ENTRIES:
                return False, f"PAK tiene demasiadas entradas ({len(infos)}; limite {MAX_PAK_ENTRIES})"
            total = 0
            for info in infos:
                if info.file_size > MAX_PAK_ENTRY_BYTES:
                    return False, f"Entrada PAK demasiado grande: {info.filename} ({info.file_size} bytes)"
                total += info.file_size
                if total > MAX_PAK_TOTAL_BYTES:
                    return False, f"PAK demasiado grande descomprimido (limite {MAX_PAK_TOTAL_BYTES} bytes)"
            bad = zf.testzip()
            if bad:
                return False, f"ZIP corrupto; primer archivo con error: {bad}"
            return True, f"ZIP valido con {len(zf.infolist())} entradas"
    except zipfile.BadZipFile as exc:
        return False, f"PAK lump no es ZIP valido: {exc}"


def apply_pak_to_bsp(bsp: BSPFile, new_pak: bytes) -> bytes:
    raw = bsp.raw
    lumps = [Lump(l.fileofs, l.filelen, l.version, l.fourcc) for l in bsp.lumps]

    pak = lumps[PAK_LUMP_INDEX]
    game = lumps[GAME_LUMP_INDEX]

    if pak.filelen == 0:
        insert_at = _align4(len(raw))
        pad = insert_at - len(raw)
        updated_raw = raw + (b"\x00" * pad) + new_pak
        pak.fileofs = insert_at
        pak.filelen = len(new_pak)
    else:
        old_start = pak.fileofs
        old_end = pak.fileofs + pak.filelen

        # El PAK se rellena con ceros hasta que el desplazamiento de todo lo que
        # viene despues sea multiplo de 4. Sin esto, un PAK que no es el ultimo
        # lump mueve a los siguientes por un delta arbitrario y les rompe la
        # alineacion a 4 bytes que el motor Source da por sentada.
        padding = (pak.filelen - len(new_pak)) % 4
        delta = (len(new_pak) + padding) - pak.filelen

        if delta != 0 and game.filelen > 0 and game.fileofs > old_start:
            raise ValueError(
                "No se puede redimensionar el PAKFILE porque LUMP_GAME_LUMP esta despues en el archivo; "
                "eso puede corromper offsets internos. Prueba con un BSP donde PAK sea el ultimo lump."
            )

        updated_raw = raw[:old_start] + new_pak + (b"\x00" * padding) + raw[old_end:]

        if delta != 0:
            for i, lump in enumerate(lumps):
                if i == PAK_LUMP_INDEX or lump.filelen == 0:
                    continue
                if lump.fileofs > old_start:
                    lump.fileofs += delta

        # filelen queda con el tamano real del ZIP; el relleno es espacio muerto
        # entre lumps, que es exactamente como lo emite vbsp.
        pak.filelen = len(new_pak)

    if len(updated_raw) > MAX_BSP_BYTES:
        raise ValueError(f"BSP resultante demasiado grande ({len(updated_raw)} bytes; limite {MAX_BSP_BYTES})")

    header = _serialize_header(bsp.version, bsp.map_revision, lumps)
    return header + updated_raw[HEADER_SIZE:]


def _write_bytes_atomic(target: Path, data: bytes) -> None:
    """Escribe primero a un temporal del mismo directorio y luego reemplaza.

    Un corte a mitad de escritura no puede dejar el BSP destino truncado: o se
    ve el contenido viejo o el nuevo, nunca uno a medias. Es el mismo esquema
    que usa el GUI (Write-FileBytesResponsive).
    """
    target = Path(target)
    tmp_path = target.with_name(f"{target.name}.{os.getpid()}.tmp")
    try:
        with open(tmp_path, "wb") as handle:
            handle.write(data)
            handle.flush()
            os.fsync(handle.fileno())
        os.replace(tmp_path, target)
    except BaseException:
        try:
            os.unlink(tmp_path)
        except OSError:
            pass
        raise


def _write_output(target: Path, data: bytes, inplace: bool, backup: bool) -> None:
    if inplace and backup and target.exists():
        backup_path = target.with_suffix(target.suffix + ".bak")
        backup_path.write_bytes(target.read_bytes())
    _write_bytes_atomic(target, data)


def _iter_files_from_args(values: List[str]) -> List[Path]:
    result: List[Path] = []
    for value in values:
        p = Path(value)
        if p.is_file():
            result.append(p)
            continue
        if p.is_dir():
            for sub in p.rglob("*"):
                if sub.is_file():
                    result.append(sub)
            continue
        raise ValueError(f"Ruta no encontrada: {value}")
    return sorted(set(result))


def main(argv: List[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        prog="pakrat_modern",
        description="Gestion de recursos embebidos en BSP (lump PAKFILE).",
    )
    sub = parser.add_subparsers(dest="cmd", required=True)

    p_list = sub.add_parser("list", help="Listar archivos embebidos")
    p_list.add_argument("bsp", type=Path)

    p_extract = sub.add_parser("extract", help="Extraer archivos embebidos")
    p_extract.add_argument("bsp", type=Path)
    p_extract.add_argument("--out", type=Path, default=Path("extracted_pak"))
    p_extract.add_argument("patterns", nargs="*", help="Globs opcionales")

    p_add = sub.add_parser("add", help="Agregar o reemplazar archivos en PAK")
    p_add.add_argument("bsp", type=Path)
    p_add.add_argument("paths", nargs="+", help="Archivos o directorios a agregar")
    p_add.add_argument("--base", type=Path, required=True, help="Raiz para rutas internas")
    p_add.add_argument("--inplace", action="store_true", help="Sobrescribir BSP original")
    p_add.add_argument("--no-backup", action="store_true", help="No crear .bak en modo inplace")
    p_add.add_argument("--out", type=Path, help="Ruta de salida (si no es inplace)")

    p_remove = sub.add_parser("remove", help="Quitar archivos por ruta interna")
    p_remove.add_argument("bsp", type=Path)
    p_remove.add_argument("names", nargs="+")
    p_remove.add_argument("--inplace", action="store_true")
    p_remove.add_argument("--no-backup", action="store_true")
    p_remove.add_argument("--out", type=Path)

    p_verify = sub.add_parser("verify", help="Verificar integridad del PAK")
    p_verify.add_argument("bsp", type=Path)

    args = parser.parse_args(argv)

    try:
        bsp = parse_bsp(args.bsp)

        if args.cmd == "list":
            items = list_pak(bsp)
            if not items:
                print("Sin archivos embebidos")
                return 0
            total = 0
            for name, size in items:
                total += size
                print(f"{size:10d}  {name}")
            print(f"\nTotal: {len(items)} archivos, {total} bytes")
            return 0

        if args.cmd == "extract":
            args.out.mkdir(parents=True, exist_ok=True)
            count = extract_pak(bsp, args.out, args.patterns)
            print(f"Extraidos {count} archivo(s) a: {args.out}")
            return 0

        if args.cmd == "verify":
            ok, msg = verify_pak(bsp)
            print(msg)
            return 0 if ok else 2

        if args.cmd == "add":
            files = _iter_files_from_args(args.paths)
            added, replaced, new_pak = update_pak_add(bsp, args.base.resolve(), [p.resolve() for p in files])
            new_bytes = apply_pak_to_bsp(bsp, new_pak)

            target = args.bsp if args.inplace else (args.out or args.bsp.with_stem(args.bsp.stem + "_packed"))
            _write_output(target, new_bytes, inplace=args.inplace, backup=not args.no_backup)
            print(
                f"PAK actualizado: +{added} nuevo(s), {replaced} reemplazado(s). "
                f"Salida: {target}"
            )
            return 0

        if args.cmd == "remove":
            removed, new_pak = update_pak_remove(bsp, args.names)
            new_bytes = apply_pak_to_bsp(bsp, new_pak)

            target = args.bsp if args.inplace else (args.out or args.bsp.with_stem(args.bsp.stem + "_stripped"))
            _write_output(target, new_bytes, inplace=args.inplace, backup=not args.no_backup)
            print(f"PAK actualizado: {removed} eliminado(s). Salida: {target}")
            return 0

        parser.error("Comando no soportado")
        return 1

    except Exception as exc:
        print(f"Error: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
