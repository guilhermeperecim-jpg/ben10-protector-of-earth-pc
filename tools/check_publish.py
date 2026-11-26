"""Stops game files from reaching git. Checks every file git would commit (staged, or with --all every tracked and
untracked-not-ignored file): no disc / executable formats (an IOP module is an ELF too), no file over 1 MB (5 MB for a .diff), no file that contains a stretch of the
game's executable (game/SLUS_216.61, when present), no PS2 module or ISO signatures.
usage: python -I tools/check_publish.py [--all]      (the pre-commit hook runs it on the staged files)
exit 1 and the reasons when something must not be published."""
import os
import subprocess
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
BAD_EXT = {'.iso', '.chd', '.cso', '.bin', '.img', '.elf', '.irx', '.wad', '.pss', '.png', '.jpg', '.jpeg', '.bmp',
           '.wav', '.pcm', '.ps2', '.mcd', '.ico', '.sys'}
MAX_BYTES = 1 << 20
CHUNK = 64  # bytes of the executable that must not appear in a published file


def git_files(all_files):
    args = ['git', 'ls-files', '-z', '--cached', '--others', '--exclude-standard'] if all_files else \
        ['git', 'diff', '--cached', '--name-only', '-z', '--diff-filter=ACMR']
    out = subprocess.run(args, cwd=ROOT, capture_output=True, check=True).stdout
    return [p for p in out.decode('utf-8').split('\0') if p]


def exe_chunks():
    path = os.path.join(ROOT, 'game', 'SLUS_216.61')
    if not os.path.exists(path):
        return set()
    data = open(path, 'rb').read()
    # every 64-byte window starting at a 4096-byte step that is not mostly zero (a published file holding any longer
    # copy of the executable contains one of them)
    return {data[i:i + CHUNK] for i in range(0x1000, len(data) - CHUNK, 4096) if data[i:i + CHUNK].count(0) < CHUNK // 2}


def problems(path, chunks):
    full = os.path.join(ROOT, path)
    if not os.path.isfile(full):
        return []
    found = []
    ext = os.path.splitext(path)[1].lower()
    if ext in BAD_EXT:
        found.append(f'extension {ext} (disc / game media)')
    size = os.path.getsize(full)
    limit = MAX_BYTES * 5 if ext == '.diff' else MAX_BYTES  # text patches of the runtimes (patches/)
    if size > limit:
        found.append(f'{size // 1024} KB (over {limit >> 20} MB)')
    data = open(full, 'rb').read()
    if data[:4] == b'\x7fELF':
        found.append('an ELF executable')
    if data[0x8001:0x8006] == b'CD001':  # the primary volume descriptor, sector 16
        found.append('an ISO 9660 image')
    if chunks and size >= CHUNK and any(c in data for c in chunks):
        found.append("bytes copied from the game's executable")
    return found


def main():
    files = git_files('--all' in sys.argv)
    chunks = exe_chunks()
    bad = [(p, why) for p in files for why in [problems(p, chunks)] if why]
    for path, why in bad:
        print(f'check_publish: {path}: ' + '; '.join(why))
    if bad:
        print(f'check_publish: {len(bad)} file(s) must not be published (see .gitignore)')
        return 1
    print(f'check_publish: {len(files)} file(s) checked, nothing from the game' +
          ('' if chunks else ' (game/SLUS_216.61 not found: the executable comparison was skipped)'))
    return 0


if __name__ == '__main__':
    sys.exit(main())
