#!/usr/bin/env python3
"""
Modern replacement for PakRat focused on Source BSP PAK lump management.

Features:
- list embedded files
- extract selected/all files
- add/update files
- remove files
- repack the PAK uncompressed
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
import stat
import struct
import sys
import tempfile
import zipfile
from collections.abc import MutableMapping
from dataclasses import dataclass
from pathlib import Path, PurePosixPath
from typing import Iterable, List, Optional, Tuple

__version__ = "1.3.2"

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
_UTF8_FLAG = 0x800

# Orden de los campos de cada entrada de la tabla de lumps. Left 4 Dead 2 pone
# la version primero; leida en el orden normal, sus offsets caen dentro de la
# cabecera.
LAYOUT_STANDARD = "standard"
LAYOUT_VERSION_FIRST = "version_first"


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
    layout: str = LAYOUT_STANDARD


class PakEntries(MutableMapping):
    """Entradas del PAK con claves insensibles a mayusculas.

    El motor Source resuelve rutas sin distinguir mayusculas, asi que
    materials/Custom/a.vmt y materials/custom/a.vmt son la misma entrada para el
    juego: dejar las dos dentro del ZIP hace impredecible cual gana. Se conserva
    la capitalizacion de la primera insercion, igual que hace el GUI.
    """

    def __init__(self) -> None:
        self._items: "dict[str, Tuple[str, bytes]]" = {}
        # Claves (en minusculas) cuyo nombre llego sin el flag UTF-8, como lo
        # escriben vbsp y bspzip. Se reescriben con los mismos bytes: el motor
        # busca los bytes crudos y pasarlos a UTF-8 cambia el archivo que encuentra.
        self._legacy: "set[str]" = set()
        # Entradas descartadas al leer, como (nombre_original, motivo). Ver
        # read_pak_entries: una entrada con ruta insegura no invalida el resto.
        self.skipped: List[Tuple[str, str]] = []
        # Entradas repetidas que quedaron unificadas. Es comun: 11 de 115 mapas
        # de CS:S probados traen duplicados. Gana la ultima copia; si su
        # contenido difiere de la anterior se anota, porque el usuario pierde
        # una version y tiene que saberlo.
        self.duplicates: List[str] = []

    def __setitem__(self, key: str, value) -> None:
        lowered = key.lower()
        existing = self._items.get(lowered)
        self._items[lowered] = (existing[0] if existing else key, value)

    def __getitem__(self, key: str):
        return self._items[key.lower()][1]

    def __delitem__(self, key: str) -> None:
        del self._items[key.lower()]
        self._legacy.discard(key.lower())

    def __contains__(self, key: object) -> bool:
        return isinstance(key, str) and key.lower() in self._items

    def __iter__(self):
        return (name for name, _ in self._items.values())

    def __len__(self) -> int:
        return len(self._items)

    def set_legacy(self, key: str) -> None:
        """Marca un nombre para escribirlo en Latin-1 y sin flag UTF-8, como vino."""
        name = self._items[key.lower()][0]
        if any(ord(c) > 127 for c in name) and all(ord(c) <= 0xFF for c in name):
            self._legacy.add(key.lower())

    def is_legacy(self, key: str) -> bool:
        return key.lower() in self._legacy


def _norm_archive_path(path: str) -> str:
    p = path.replace("\\", "/").strip()
    if p.startswith("/"):
        raise ValueError(f"Unsafe absolute path inside the BSP: {path}")
    if p in {"", "."}:
        raise ValueError("Invalid path inside the BSP")
    if _UNSAFE_ARCHIVE_CHARS.search(p):
        raise ValueError(f"Invalid characters in path inside the BSP: {path}")
    for part in p.split("/"):
        if part in {"", ".", ".."} or part.endswith(".") or part.endswith(" "):
            raise ValueError(f"Unsafe path inside the BSP: {path}")
        if _RESERVED_WINDOWS_NAMES.match(part):
            raise ValueError(f"Reserved Windows name in path inside the BSP: {path}")
    return str(PurePosixPath(p))


def _align4(value: int) -> int:
    return (value + 3) & ~3


def _read_lump_table(raw: bytes, layout: str) -> Tuple[Optional[List[Lump]], Optional[str]]:
    """Tabla de lumps en ese orden de campos, o (None, error) si no es coherente."""
    lumps: List[Lump] = []
    offset = 8
    for i in range(LUMP_COUNT):
        first, second, third, fourcc = struct.unpack_from("<iii4s", raw, offset)
        offset += 16
        if layout == LAYOUT_STANDARD:
            fileofs, filelen, lump_version = first, second, third
        else:
            lump_version, fileofs, filelen = first, second, third
        if filelen < 0:
            return None, f"Lump {i} has a negative length"
        if filelen > 0:
            # Un lump que empieza dentro de la cabecera no puede ser valido: al
            # guardar se reescribe la cabecera encima de el.
            if fileofs < HEADER_SIZE or fileofs + filelen > len(raw):
                return None, f"Lump {i} out of range (ofs={fileofs}, len={filelen})"
        lumps.append(Lump(fileofs, filelen, lump_version, fourcc))
    return lumps, None


def parse_bsp(path: Path) -> BSPFile:
    size = path.stat().st_size
    if size > MAX_BSP_BYTES:
        raise ValueError(f"BSP is too large ({size} bytes; limit {MAX_BSP_BYTES})")
    return parse_bsp_bytes(path.read_bytes())


def parse_bsp_bytes(raw: bytes) -> BSPFile:
    if len(raw) < HEADER_SIZE:
        raise ValueError("File is too small for a Source BSP")

    ident = raw[0:4]
    if ident != IDENT:
        raise ValueError("Invalid BSP identifier (expected VBSP)")

    version = struct.unpack_from("<i", raw, 4)[0]
    lumps, error = _read_lump_table(raw, LAYOUT_STANDARD)
    layout = LAYOUT_STANDARD
    if lumps is None:
        # Una tabla danada leida en el otro orden casi siempre parece "todo
        # vacio" (el campo de version suele ser 0). Una de L4D2 de verdad
        # conserva todos sus lumps: tantos no vacios como offsets no nulos.
        alternative, _ = _read_lump_table(raw, LAYOUT_VERSION_FIRST)
        if alternative is not None:
            non_empty = sum(1 for lump in alternative if lump.filelen > 0)
            positive = sum(1 for i in range(LUMP_COUNT) if struct.unpack_from("<i", raw, 8 + i * 16 + 4)[0] > 0)
            if non_empty > 0 and non_empty >= positive:
                lumps, layout = alternative, LAYOUT_VERSION_FIRST
    if lumps is None:
        raise ValueError(error)

    map_revision = struct.unpack_from("<i", raw, 8 + LUMP_COUNT * 16)[0]
    return BSPFile(raw=raw, version=version, map_revision=map_revision, lumps=lumps, layout=layout)


def _serialize_header(version: int, map_revision: int, lumps: List[Lump], layout: str = LAYOUT_STANDARD) -> bytes:
    header = io.BytesIO()
    header.write(IDENT)
    header.write(struct.pack("<i", version))
    for lump in lumps:
        if layout == LAYOUT_VERSION_FIRST:
            header.write(struct.pack("<iii4s", lump.version, lump.fileofs, lump.filelen, lump.fourcc))
        else:
            header.write(struct.pack("<iii4s", lump.fileofs, lump.filelen, lump.version, lump.fourcc))
    header.write(struct.pack("<i", map_revision))
    return header.getvalue()


def _get_lump_bytes(bsp: BSPFile, index: int) -> bytes:
    lump = bsp.lumps[index]
    if lump.filelen == 0:
        return b""
    return bsp.raw[lump.fileofs:lump.fileofs + lump.filelen]


class UnsupportedCompression(ValueError):
    """Una entrada usa un metodo que zipfile no sabe descomprimir."""


_METHOD_NAMES = {0: "Stored", 8: "Deflate", 9: "Deflate64", 12: "BZip2", 14: "LZMA", 95: "XZ", 98: "PPMd"}
_READABLE_METHODS = (zipfile.ZIP_STORED, zipfile.ZIP_DEFLATED, zipfile.ZIP_BZIP2, zipfile.ZIP_LZMA)


def _check_limits(entry_count: int, sizes: Iterable[Tuple[str, int]]) -> None:
    """Limites compartidos por lectura, escritura y verify, con el mismo texto."""
    if entry_count > MAX_PAK_ENTRIES:
        raise ValueError(f"PAK has too many entries ({entry_count}; limit {MAX_PAK_ENTRIES})")
    total = 0
    for name, size in sizes:
        if size > MAX_PAK_ENTRY_BYTES:
            raise ValueError(f"PAK entry is too large: {name} ({size} bytes)")
        total += size
        if total > MAX_PAK_TOTAL_BYTES:
            raise ValueError(f"PAK is too large uncompressed (limit {MAX_PAK_TOTAL_BYTES} bytes)")


def _check_supported(infos: List[zipfile.ZipInfo]) -> None:
    """Se comprueba antes de leer nada, como hace el GUI: abrir a medias un
    PAK con entradas que no se pueden descomprimir haria que guardar las
    borrara del mapa sin aviso."""
    bad: "dict[int, int]" = {}
    for info in infos:
        if info.compress_type not in _READABLE_METHODS:
            bad[info.compress_type] = bad.get(info.compress_type, 0) + 1
    if bad:
        detail = ", ".join(f"{n} x {_METHOD_NAMES.get(m, f'method {m}')}" for m, n in bad.items())
        raise UnsupportedCompression(
            f"The PAK uses a compression method that cannot be read ({detail}, out of {len(infos)} entries)."
        )


def _entry_name(info: zipfile.ZipInfo) -> str:
    """Nombre de la entrada con los mismos caracteres que ve el GUI.

    Sin el flag UTF-8, zipfile decodifica como CP437; se vuelve a los bytes y
    se lee como Latin-1, que los conserva uno a uno. Se parte de orig_filename,
    que no esta cortado en el primer byte nulo: asi un nombre con '\\0' se
    rechaza igual que en el GUI en lugar de aceptarse truncado.
    """
    if info.flag_bits & _UTF8_FLAG:
        return info.orig_filename
    return info.orig_filename.encode("cp437").decode("latin-1")


def read_pak_entries(pak_bytes: bytes) -> PakEntries:
    entries = PakEntries()
    if not pak_bytes:
        return entries

    with zipfile.ZipFile(io.BytesIO(pak_bytes), "r") as zf:
        infos = [info for info in zf.infolist() if not info.is_dir()]
        _check_supported(infos)
        _check_limits(len(infos), ((info.filename, info.file_size) for info in infos))
        for info in infos:
            raw_name = _entry_name(info)

            # Hay mapas publicados con rutas absolutas dentro del PAK
            # ("C:/Program Files/.../x.vmt", "/sound/y.mp3"). Se descartan esas
            # entradas, no el mapa entero: rechazar todo dejaba inaccesibles
            # cientos de archivos validos por una sola entrada mal empaquetada.
            try:
                name = _norm_archive_path(raw_name)
            except ValueError as exc:
                entries.skipped.append((raw_name, str(exc)))
                continue

            # Por ZipInfo y no por nombre: con nombres repetidos, zf.read(nombre)
            # devuelve siempre la ultima copia y la primera queda inobservable.
            data = zf.read(info)
            if name in entries:
                if entries[name] == data:
                    entries.duplicates.append(raw_name)
                else:
                    entries.duplicates.append(f"{raw_name} (different content; last copy kept)")
                entries[name] = data
                continue
            entries[name] = data
            if not info.flag_bits & _UTF8_FLAG:
                entries.set_legacy(name)
    return entries


class _RawNameZipInfo(zipfile.ZipInfo):
    """Entrada cuyo nombre se escribe con sus bytes originales (Latin-1) y sin flag UTF-8."""

    __slots__ = ()

    def _encodeFilenameFlags(self):  # noqa: N802 (nombre de zipfile)
        return self.filename.encode("latin-1"), self.flag_bits & ~_UTF8_FLAG


def write_pak_entries(entries: PakEntries) -> bytes:
    _check_limits(len(entries), ((name, len(entries[name])) for name in entries))
    buffer = io.BytesIO()
    with zipfile.ZipFile(buffer, mode="w") as zf:
        # Orden por unidades UTF-16, que es lo que hace StringComparer.Ordinal
        # en C#. sorted() ordena por code point y difiere fuera del BMP.
        for name in sorted(entries, key=lambda n: n.encode("utf-16-be")):
            data = entries[name]
            info_class = _RawNameZipInfo if entries.is_legacy(name) else zipfile.ZipInfo
            new_info = info_class(filename=name, date_time=CANONICAL_DATE_TIME)
            # Forma canonica fija, sin copiar nada del PAK de origen: antes se
            # arrastraban comment, internal_attr, create_system y external_attr,
            # asi que el resultado dependia de con que herramienta se habia
            # creado el mapa y no coincidia con el del GUI. El motor Source
            # ignora todos esos campos.
            new_info.comment = b""
            new_info.internal_attr = 0
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
        raise ValueError(f"Invalid extraction path: {arcname}")
    if os.path.commonpath([str(root), str(target)]) != str(root):
        raise ValueError(f"Unsafe extraction path: {arcname}")
    return target


def list_pak(bsp: BSPFile) -> List[Tuple[str, int]]:
    entries = read_pak_entries(_get_lump_bytes(bsp, PAK_LUMP_INDEX))
    _warn_skipped(entries)
    return [(name, len(data)) for name, data in sorted(entries.items())]


def extract_pak(
    bsp: BSPFile,
    out_dir: Path,
    patterns: Optional[Iterable[str]] = None,
    overwrite: bool = False,
) -> Tuple[int, int]:
    """Devuelve (extraidos, saltados). Sin overwrite, un archivo que ya existe
    en el destino se conserva en vez de pisarse."""
    entries = read_pak_entries(_get_lump_bytes(bsp, PAK_LUMP_INDEX))
    _warn_skipped(entries)
    # fnmatch es insensible a mayusculas en Windows y sensible en Linux; se
    # fuerza insensible en ambos, que es como resuelve rutas el motor.
    pattern_list = [pat.lower() for pat in (patterns or [])]
    count = 0
    skipped = 0

    for name, data in entries.items():
        if pattern_list and not any(fnmatch.fnmatchcase(name.lower(), pat) for pat in pattern_list):
            continue
        target = _safe_extract_target(out_dir, name)
        if target.exists() and not overwrite:
            skipped += 1
            continue
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(data)
        count += 1

    return count, skipped


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
            raise ValueError(f"File not found: {file_path}")

        try:
            rel = file_path.relative_to(base)
        except ValueError as exc:
            raise ValueError(f"{file_path} is not inside --base {base}") from exc

        arcname = _norm_archive_path(str(rel))
        size = file_path.stat().st_size
        if size > MAX_PAK_ENTRY_BYTES:
            raise ValueError(f"File is too large for the PAK: {file_path} ({size} bytes)")
        data = file_path.read_bytes()

        if arcname in entries:
            replaced += 1
        else:
            added += 1
        entries[arcname] = data

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


def repack_pak(bsp: BSPFile) -> Tuple[int, bytes]:
    """Reescribe el PAK sin compresion y sin agregar ni quitar nada. Es lo que
    necesita un mapa con entradas BZip2 para abrirse en el GUI."""
    entries = read_pak_entries(_get_lump_bytes(bsp, PAK_LUMP_INDEX))
    _warn_skipped(entries)
    return len(entries), write_pak_entries(entries)


def _warn_skipped(entries: PakEntries) -> None:
    """Avisa por stderr de lo que se descarto o unifico al leer el PAK."""
    if entries.skipped:
        print(
            f"Warning: {len(entries.skipped)} PAK entr{'y was' if len(entries.skipped) == 1 else 'ies were'} "
            "skipped because of unsafe paths.",
            file=sys.stderr,
        )
        for name, reason in entries.skipped[:10]:
            print(f"  - {name}  ({reason})", file=sys.stderr)
        if len(entries.skipped) > 10:
            print(f"  ... and {len(entries.skipped) - 10} more", file=sys.stderr)
        print("  Saving the BSP will remove them from the map.", file=sys.stderr)

    if entries.duplicates:
        print(
            f"Warning: {len(entries.duplicates)} duplicate entr{'y was' if len(entries.duplicates) == 1 else 'ies were'} "
            "merged (the engine ignores case and can only load one).",
            file=sys.stderr,
        )
        for name in entries.duplicates[:10]:
            print(f"  - {name}", file=sys.stderr)
        if len(entries.duplicates) > 10:
            print(f"  ... and {len(entries.duplicates) - 10} more", file=sys.stderr)


def verify_pak(bsp: BSPFile) -> Tuple[bool, str]:
    pak_bytes = _get_lump_bytes(bsp, PAK_LUMP_INDEX)
    if not pak_bytes:
        return True, "PAK lump is empty (no embedded files)."

    # Mismo criterio que el GUI: leer con las reglas de rutas, reescribir y
    # volver a leer. Asi "valido" significa "lo que se guardaria se puede
    # abrir", y no "el ZIP crudo pasa testzip" aunque luego se descarten
    # entradas al guardar.
    try:
        with zipfile.ZipFile(io.BytesIO(pak_bytes), "r") as zf:
            _check_supported([info for info in zf.infolist() if not info.is_dir()])
            bad = zf.testzip()
            if bad:
                return False, f"Damaged ZIP; first file with an error: {bad}"
        entries = read_pak_entries(pak_bytes)
        back = read_pak_entries(write_pak_entries(entries))
    except zipfile.BadZipFile as exc:
        return False, f"PAK lump is not a valid ZIP: {exc}"
    except (NotImplementedError, UnsupportedCompression) as exc:
        return False, f"PAK uses unsupported compression: {exc}"
    except ValueError as exc:
        return False, str(exc)

    notes = []
    if entries.skipped:
        notes.append(f"{len(entries.skipped)} entr{'y' if len(entries.skipped) == 1 else 'ies'} with unsafe paths would be dropped on save")
    if entries.duplicates:
        notes.append(f"{len(entries.duplicates)} duplicate entr{'y' if len(entries.duplicates) == 1 else 'ies'} would be merged")
    suffix = f" ({'; '.join(notes)})" if notes else ""
    return True, f"ZIP valid with {len(back)} entries{suffix}"


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
                "Cannot resize the PAKFILE because LUMP_GAME_LUMP comes after it in the file; "
                "its internal offsets are absolute and would break."
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
        raise ValueError(f"Resulting BSP is too large ({len(updated_raw)} bytes; limit {MAX_BSP_BYTES})")

    header = _serialize_header(bsp.version, bsp.map_revision, lumps, bsp.layout)
    return header + updated_raw[HEADER_SIZE:]


def _default_mode() -> int:
    """Permisos de un archivo nuevo segun el umask, como haria open()."""
    umask = os.umask(0)
    os.umask(umask)
    return 0o666 & ~umask


def _write_bytes_atomic(target: Path, data: bytes, mode: Optional[int] = None) -> None:
    """Escribe primero a un temporal del mismo directorio y luego reemplaza.

    Un corte a mitad de escritura no puede dejar el BSP destino truncado: o se
    ve el contenido viejo o el nuevo, nunca uno a medias. Es el mismo esquema
    que usa el GUI (AtomicFile).
    """
    target = Path(target)
    if mode is None and os.name == "posix":
        mode = stat.S_IMODE(target.stat().st_mode) if target.exists() else _default_mode()

    # Nombre unico y creacion exclusiva (O_EXCL): un temporal predecible se
    # podia truncar o redirigir con un enlace colocado de antemano.
    fd, tmp_name = tempfile.mkstemp(prefix=f"{target.name}.", suffix=".tmp", dir=str(target.parent))
    tmp_path = Path(tmp_name)
    try:
        with os.fdopen(fd, "wb") as handle:
            handle.write(data)
            handle.flush()
            os.fsync(handle.fileno())
        if os.name == "posix":
            # mkstemp crea el temporal con 0600: sin esto, cada BSP reescrito
            # quedaba ilegible para otros usuarios (FastDL, srcds con otra cuenta).
            os.chmod(tmp_path, mode)
        os.replace(tmp_path, target)
    except BaseException:
        try:
            os.unlink(tmp_path)
        except OSError:
            pass
        raise


def _write_output(target: Path, data: bytes, backup: bool) -> None:
    """Respalda siempre que se vaya a pisar un archivo existente, sea el
    original (--inplace) o cualquier --out que ya exista; es lo que hace el
    GUI. El .bak se reemplaza en cada guardado: es un deshacer de un paso."""
    if backup and target.exists():
        backup_path = target.with_name(target.name + ".bak")
        mode = stat.S_IMODE(target.stat().st_mode) if os.name == "posix" else None
        _write_bytes_atomic(backup_path, target.read_bytes(), mode)
    _write_bytes_atomic(target, data)


def _resolve_output(args, default_suffix: str) -> Path:
    if args.inplace:
        return args.bsp
    # with_name y no with_stem: with_stem no existe en Python 3.8.
    return args.out or args.bsp.with_name(args.bsp.stem + default_suffix + args.bsp.suffix)


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
        raise ValueError(f"Path not found: {value}")
    return sorted(set(result))


def main(argv: Optional[List[str]] = None) -> int:
    parser = argparse.ArgumentParser(
        prog="pakrat_modern",
        description="Manage the files embedded in a Source BSP (PAKFILE lump).",
    )
    parser.add_argument("--version", action="version", version=f"%(prog)s {__version__}")
    sub = parser.add_subparsers(dest="cmd", required=True)

    p_list = sub.add_parser("list", help="List embedded files")
    p_list.add_argument("bsp", type=Path)

    p_extract = sub.add_parser("extract", help="Extract embedded files")
    p_extract.add_argument("bsp", type=Path)
    p_extract.add_argument("--out", type=Path, default=Path("extracted_pak"))
    p_extract.add_argument("--overwrite", action="store_true", help="Replace files that already exist in --out")
    p_extract.add_argument("patterns", nargs="*", help="Optional globs (case-insensitive)")

    def add_output_options(p: argparse.ArgumentParser) -> None:
        dest = p.add_mutually_exclusive_group()
        dest.add_argument("--inplace", action="store_true", help="Overwrite the original BSP")
        dest.add_argument("--out", type=Path, help="Output path")
        p.add_argument("--no-backup", action="store_true", help="Do not create a .bak when overwriting an existing file")

    p_add = sub.add_parser("add", help="Add or replace files in the PAK")
    p_add.add_argument("bsp", type=Path)
    p_add.add_argument("paths", nargs="+", help="Files or folders to add")
    p_add.add_argument("--base", type=Path, required=True, help="Folder that internal paths are relative to")
    add_output_options(p_add)

    p_remove = sub.add_parser("remove", help="Remove files by internal path")
    p_remove.add_argument("bsp", type=Path)
    p_remove.add_argument("names", nargs="+")
    add_output_options(p_remove)

    p_repack = sub.add_parser("repack", help="Rewrite the PAK uncompressed without adding or removing files")
    p_repack.add_argument("bsp", type=Path)
    add_output_options(p_repack)

    p_verify = sub.add_parser("verify", help="Check that the PAK can be read and rewritten")
    p_verify.add_argument("bsp", type=Path)

    args = parser.parse_args(argv)

    if args.cmd == "add" and not args.base.is_dir():
        parser.error(f"--base is not a folder: {args.base}")

    try:
        bsp = parse_bsp(args.bsp)

        if args.cmd == "list":
            items = list_pak(bsp)
            if not items:
                print("No embedded files")
                return 0
            total = 0
            for name, size in items:
                total += size
                print(f"{size:10d}  {name}")
            print(f"\nTotal: {len(items)} files, {total} bytes")
            return 0

        if args.cmd == "extract":
            args.out.mkdir(parents=True, exist_ok=True)
            count, skipped = extract_pak(bsp, args.out, args.patterns, overwrite=args.overwrite)
            print(f"Extracted {count} file(s) to: {args.out}")
            if skipped:
                print(f"{skipped} file(s) already existed and were kept (use --overwrite to replace them)")
            return 0

        if args.cmd == "verify":
            ok, msg = verify_pak(bsp)
            print(msg)
            return 0 if ok else 2

        if args.cmd == "add":
            files = _iter_files_from_args(args.paths)
            added, replaced, new_pak = update_pak_add(bsp, args.base.resolve(), [p.resolve() for p in files])
            new_bytes = apply_pak_to_bsp(bsp, new_pak)

            target = _resolve_output(args, "_packed")
            _write_output(target, new_bytes, backup=not args.no_backup)
            print(f"PAK updated: {added} added, {replaced} replaced. Output: {target}")
            return 0

        if args.cmd == "remove":
            removed, new_pak = update_pak_remove(bsp, args.names)
            new_bytes = apply_pak_to_bsp(bsp, new_pak)

            target = _resolve_output(args, "_stripped")
            _write_output(target, new_bytes, backup=not args.no_backup)
            print(f"PAK updated: {removed} removed. Output: {target}")
            return 0

        if args.cmd == "repack":
            count, new_pak = repack_pak(bsp)
            new_bytes = apply_pak_to_bsp(bsp, new_pak)

            target = _resolve_output(args, "_repacked")
            _write_output(target, new_bytes, backup=not args.no_backup)
            print(f"PAK rewritten uncompressed: {count} file(s). Output: {target}")
            return 0

        raise AssertionError(f"unhandled command: {args.cmd}")

    except UnsupportedCompression as exc:
        print(f"Error: {exc}", file=sys.stderr)
        return 2
    except Exception as exc:
        print(f"Error: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
