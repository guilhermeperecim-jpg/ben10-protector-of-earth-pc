# Prepares your own Ben 10: Protector of Earth (USA, SLUS-21661 v1.01) disc image for the recomp. The game's files
# never go in git or in a release: they stay on the player's PC.
#   disc/SLUS-21661.iso  the disc image the recompiled game reads by sector (PS2X_CD_IMAGE: the IOP modules, GAME.WAD,
#                        the movies)
#   game/                the disc's files but GAME.WAD (read by sector from the image): the runtime opens cdrom0:
#                        paths here (MODULES\*.IRX, IOPRP300.IMG, SYSTEM.CNF, GAME.DIR); SLUS_216.61 is the executable
#                        the recompiler, tools/vu1_recomp.py and the game read
# Adapted from def-jam-vendetta-recomp's tools/prepare_disc.py (GPL-3.0); .iso only.
# usage: python -I tools/prepare_disc.py [--check] <disc.iso>
#   --check: only says whether it is the right disc ("ok SLUS-21661 <sectors> sectors"), writes nothing
#   BEN10_DATA=<folder>: where disc/ and game/ go (default: the repository)
import hashlib
import os
import sys

ROOT = os.environ.get('BEN10_DATA') or os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SECTOR = 2048
ELF_NAME = 'SLUS_216.61'
ELF_SHA256 = 'f9f1efe049d17729ce9c4a57dcdfd4a82d28c481f4193be207c9da84e3008f03'  # USA v1.01
ISO_NAME = 'SLUS-21661.iso'


def read(f, lba, count):
    f.seek(lba * SECTOR)
    return f.read(count * SECTOR)


def walk(f, lba, size, path=''):
    """(path, lba, size, is_dir) of an ISO-9660 directory, recursively; paths as on the disc ("MODULES/CDARB.IRX")."""
    data = read(f, lba, (size + SECTOR - 1) // SECTOR)[:size]
    off = 0
    while off < len(data):
        length = data[off]
        if length == 0:
            off = (off // SECTOR + 1) * SECTOR
            continue
        name = data[off + 33:off + 33 + data[off + 32]]
        e_lba = int.from_bytes(data[off + 2:off + 6], 'little')
        e_size = int.from_bytes(data[off + 10:off + 14], 'little')
        is_dir = bool(data[off + 25] & 2)
        off += length
        if name in (b'\0', b'\1'):
            continue
        full = (path + '/' if path else '') + name.decode('latin1').split(';')[0]
        yield full, e_lba, e_size, is_dir
        if is_dir:
            yield from walk(f, e_lba, e_size, full)


def root_files(f):
    """{NAME: (lba, size)} of every file on the disc (upper-case paths), and the image's sectors; None when it isn't
    an ISO-9660 image."""
    pvd = read(f, 16, 1)
    if pvd[1:6] != b'CD001':
        return None, 0
    blocks = int.from_bytes(pvd[80:84], 'little')
    lba = int.from_bytes(pvd[158:162], 'little')
    size = int.from_bytes(pvd[166:170], 'little')
    files = {p.upper(): (l, s) for p, l, s, d in walk(f, lba, size) if not d}
    return files, blocks


def check(path):
    """(the executable's bytes, the image's sectors), or exits with what is wrong, in plain words."""
    try:
        f = open(path, 'rb')
    except OSError as error:
        sys.exit(f'the disc image could not be opened: {error}')
    with f:
        files, blocks = root_files(f)
        if files is None:
            sys.exit('not a PS2 disc image (.iso): no ISO-9660 volume at sector 16')
        if ELF_NAME not in files:
            sys.exit(f'{ELF_NAME} is not on this disc: the recomp is for Ben 10: Protector of Earth (USA, SLUS-21661) only')
        lba, size = files[ELF_NAME]
        elf = read(f, lba, (size + SECTOR - 1) // SECTOR)[:size]
    if hashlib.sha256(elf).hexdigest() != ELF_SHA256:
        sys.exit('this is Ben 10: Protector of Earth (USA), but not version 1.01 (or the image is damaged): '
                 'the recomp needs the 1.01 disc')
    return elf, blocks


def main():
    args = sys.argv[1:]
    check_only = bool(args) and args[0] == '--check'
    if check_only:
        args = args[1:]
    if len(args) != 1:
        sys.exit('usage: python -I tools/prepare_disc.py [--check] <disc.iso>')
    path = args[0]
    elf, blocks = check(path)
    if check_only:
        print(f'ok SLUS-21661 {blocks} sectors')
        return

    out_iso = os.path.join(ROOT, 'disc', ISO_NAME)
    os.makedirs(os.path.dirname(out_iso), exist_ok=True)
    same = os.path.normcase(os.path.abspath(path)) == os.path.normcase(os.path.abspath(out_iso))
    copied = os.path.isfile(out_iso) and os.path.getsize(out_iso) == os.path.getsize(path)
    if copied and not same:
        print('the disc image is already there')
    if not same and not copied:
        total = os.path.getsize(path) // SECTOR
        chunk = 4096  # sectors (8 MB)
        with open(path, 'rb') as src, open(out_iso + '.part', 'wb') as dst:
            done = 0
            while True:
                data = src.read(chunk * SECTOR)
                if not data:
                    break
                dst.write(data)
                done += len(data) // SECTOR
                if done // chunk % 16 == 0:
                    print(f'{done}/{total} sectors', flush=True)  # whole lines: the settings program's progress bar
        os.replace(out_iso + '.part', out_iso)
    print(f'disc image: {out_iso}')

    # the disc's files (GAME.WAD stays in the image: CDARB reads it there by sector)
    game = os.path.join(ROOT, 'game')
    with open(out_iso, 'rb') as f:
        files, _ = root_files(f)
        for name, (lba, size) in sorted(files.items()):
            if name == 'GAME.WAD':
                continue
            dst = os.path.join(game, *name.split('/'))
            os.makedirs(os.path.dirname(dst), exist_ok=True)
            with open(dst, 'wb') as out:
                out.write(read(f, lba, (size + SECTOR - 1) // SECTOR)[:size])
            print(f'{name} {size}')
    print(f'game files: {game}')


if __name__ == '__main__':
    main()
