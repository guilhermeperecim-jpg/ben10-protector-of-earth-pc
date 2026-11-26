# Makes the zip a player downloads: dist\Ben10-Recomp-Windows.zip (nothing is published). The name stays the same in
# every release, so a link to releases/latest/download/<name> always works. Adapted from def-jam-vendetta-recomp's
# tools/make_release.py (GPL-3.0).
#   Ben 10 Recomp\
#     Ben 10 Recomp Launcher.exe   the launcher with its first-run setup (.NET built in)
#     LEIA-ME - READ ME.txt
#     files\                  everything else, so the top stays simple (the setup puts disc\, game\, the game … here):
#       python\               a portable Python (Python's own "embeddable" package; not installed on Windows)
#       source\               the build kit: this repository's files (as git would commit them), PS2Recomp at its
#                             pinned commit WITH our patches applied, paraLLEl-GS + the parts of Granite it uses, and
#                             deps\ = the libraries PS2Recomp's CMake would clone with git
# Nothing from the disc or made from the game's code goes in: PS2Recomp comes from git (its working copy holds the
# game's generated code), and every name is checked at the end. usage: python -I tools/make_release.py (needs git, the
# .NET SDK, and PS2Recomp built here once, for the libraries in build\ps2recomp-dj\_deps).
import hashlib
import io
import os
import shutil
import subprocess
import tarfile
import tempfile
import urllib.request
import zipfile

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DIST = os.path.join(REPO, 'dist')
TOP = 'Ben 10 Recomp'
ZIP_NAME = 'Ben10-Recomp-Windows.zip'
LAUNCHER = 'Ben 10 Recomp Launcher.exe'
FILES = TOP + '/files/'
SRC = FILES + 'source/'
PYTHON_VERSION = '3.13.9'
PS2RECOMP_BASE = '69cee92'  # ran-j/PS2Recomp 2c5fbb9 + def-jam-vendetta-recomp's patch (patches/README.md)
PARALLEL_GS_BASE = 'cc6184a'  # Arntzen-Software/parallel-gs, before def-jam-vendetta-recomp's patch
# the libraries FetchContent would clone (folder under source\deps\ <- where the build here has them), and what of
# them isn't needed (examples, tests: also the longest paths)
DEPS = {
    'raylib': ('raylib-src', ('examples/', 'projects/')),
    'imgui': ('imgui-src', ('examples/',)),
    'rlimgui': ('rlimgui-src', ('examples/',)),
    'elfio': ('elfio-src', ('tests/', 'examples/')),
    'toml11': ('toml11-src', ('tests/', 'examples/')),
    'fmt': ('fmt-src', ('test/',)),
    'libdwarf': ('libdwarf-src', ()),
    'rabbitizer': ('rabbitizer-src', ('tests/',)),
    'nlohmann_json': ('nlohmann_json-src', ('tests/', 'docs/')),
}
FORBIDDEN = ('disc/', 'game/', 'generated/', 'ghidra/', 'build/', 'refs/', 'shots/', 'mc0/', 'mc1/', 'logs/', 'dist/')
FORBIDDEN_SUFFIX = ('.iso', '.chd', '.elf', '.irx', '.wad', '.pss', '.psm', '.bik', '.p2s')

README = r"""Ben 10 Recomp
=============

[Português]
Ben 10: Protector of Earth (PS2, EUA, SLUS-21661 v1.01) como um programa nativo de PC, montado no seu PC a partir do
seu próprio disco. Nenhum arquivo do jogo vem neste download: o jogo é montado aqui, a partir da sua cópia, e nunca
sai do seu PC.

Para jogar:
  1. Descompacte esta pasta num lugar com caminho curto (Área de Trabalho ou C:\Jogos). Precisa de uns 15 GB.
  2. Abra "Ben 10 Recomp Launcher.exe".
  3. Ele procura o seu disco no PC sozinho (um .iso da versão americana 1.01, SLUS-21661) e confere. Se não
     achar, clique em "Procurar..." ou arraste o arquivo para a janela.
  4. Aperte "Próximo: montar o jogo". O compilador de C++ gratuito da Microsoft é baixado para dentro desta pasta
     (uns 500 MB, sem instalar nada no Windows). A primeira montagem leva de 30 a 60 minutos (precisa de internet
     uma vez).
  5. Aperte JOGAR. O atalho "Ben 10 Recomp" nesta pasta abre o jogo direto.

Se o Windows disser "O Windows protegeu o computador": clique em "Mais informações" e depois em "Executar assim mesmo"
(o programa ainda não é assinado com um certificado pago).

[English]
Ben 10: Protector of Earth (PS2, USA, SLUS-21661 v1.01) as a native PC program, built on your PC from your own disc.
No game files come with this download: the game is built here, from your copy, and never leaves your PC.

To play:
  1. Unzip this folder somewhere with a short path (your Desktop or C:\Games). It needs about 15 GB.
  2. Open "Ben 10 Recomp Launcher.exe".
  3. It looks for your disc on this PC by itself (an .iso of the USA disc, version 1.01, SLUS-21661) and checks it.
     If it can't find it, click "Browse..." or drop the file onto the window.
  4. Press "Next: build the game". Microsoft's free C++ compiler is downloaded into this folder (about 500 MB,
     nothing installed in Windows). The first build takes 30 to 60 minutes (it needs the internet once).
  5. Press PLAY. The "Ben 10 Recomp" shortcut in this folder starts the game straight away.

If Windows says "Windows protected your PC": click "More info", then "Run anyway" (the program isn't signed with a
paid certificate yet).

Free software under the GNU GPL version 3: files\source\LICENSE; every part it uses and its licence:
files\source\THIRD-PARTY-NOTICES.txt. Built on PS2Recomp and on def-jam-vendetta-recomp's
runtime. Not affiliated with Cartoon Network or D3Publisher; Ben 10 is (c) its owners. This download includes none of it.
"""


def git(args, cwd):
    return subprocess.run(['git'] + args, cwd=cwd, check=True, capture_output=True).stdout


def file_date():
    """Every file in the zip is dated two days before this commit: a zip keeps local clock times without a time zone,
    and files dated in the future make Ninja stop ("build.ninja still dirty")."""
    import datetime
    commit = int(git(['log', '-1', '--format=%ct'], REPO).decode().strip())
    return datetime.datetime.fromtimestamp(commit - 2 * 86400, datetime.timezone.utc).timetuple()[:6]


class ReleaseZip(zipfile.ZipFile):
    """A zip whose every file is dated file_date()."""
    date = None

    def write(self, filename, arcname=None, compress_type=None, compresslevel=None):
        info = zipfile.ZipInfo.from_file(filename, arcname)
        with open(filename, 'rb') as f:
            self.writestr(info, f.read(), compress_type, compresslevel)

    def writestr(self, zinfo_or_arcname, data, compress_type=None, compresslevel=None):
        info = zinfo_or_arcname if isinstance(zinfo_or_arcname, zipfile.ZipInfo) else zipfile.ZipInfo(zinfo_or_arcname)
        if ReleaseZip.date is None:
            ReleaseZip.date = file_date()
        info.date_time = ReleaseZip.date
        if not isinstance(zinfo_or_arcname, zipfile.ZipInfo):
            info.external_attr = 0o600 << 16
        super().writestr(info, data, compress_type if compress_type is not None else self.compression,
                         compresslevel if compresslevel is not None else self.compresslevel)


def check(name):
    """Refuses any name that could be the game's: its folders, its file types, its executable's name."""
    low = name.lower()
    rel = low[len(SRC.lower()):] if low.startswith(SRC.lower()) else low
    base = os.path.basename(low)
    if rel.startswith(FORBIDDEN) or base.startswith('slus_') or low.endswith(FORBIDDEN_SUFFIX):
        raise SystemExit('refusing: ' + name)


def add_tree(z, folder, prefix, skip=()):
    n = 0
    for root, dirs, files in os.walk(folder):
        dirs[:] = [d for d in dirs if d != '.git']
        for f in files:
            path = os.path.join(root, f)
            rel = os.path.relpath(path, folder).replace(os.sep, '/')
            if rel.startswith(skip):
                continue
            check(prefix + rel)
            z.write(path, prefix + rel)
            n += 1
    return n


def add_archive(z, repo, prefix, ref='HEAD'):
    data = git(['archive', '--format=tar', ref], repo)
    n = 0
    with tarfile.open(fileobj=io.BytesIO(data)) as tar:
        for m in tar:
            if m.isfile():
                check(prefix + m.name)
                z.writestr(prefix + m.name, tar.extractfile(m).read())
                n += 1
    return n


def launcher_exe(work):
    # its own build folders cleared first: an old compiled program can be taken as up to date with Avalonia's XAML step
    # never run on it, and the exe then stops at once ("No precompiled XAML found")
    app = os.path.join(REPO, 'launcher')
    for stale in (os.path.join(app, 'obj', 'Release', 'net10.0', 'win-x64'),
                  os.path.join(app, 'bin', 'Release', 'net10.0', 'win-x64')):
        shutil.rmtree(stale, ignore_errors=True)
    out = os.path.join(work, 'publish')
    subprocess.run(['dotnet', 'publish', app, '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true',
                    '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:DebugType=none',
                    '-o', out, '-nologo', '-v', 'q'], check=True)
    exe = os.path.join(out, LAUNCHER)
    # started once as a player gets it: a page drawn without a window goes through the same start as the window
    shot = os.path.join(work, 'launcher-check.png')
    started = subprocess.run([exe, '--shot', 'home', shot], capture_output=True, text=True, timeout=120)
    if started.returncode != 0 or not os.path.isfile(shot):
        raise SystemExit('the launcher does not start (exit %d):\n%s' % (started.returncode,
                                                                       (started.stderr or started.stdout).strip()))
    print('launcher: starts')
    return exe


def python_zip():
    cache = os.path.join(DIST, 'cache', f'python-{PYTHON_VERSION}-embed-amd64.zip')
    if not os.path.isfile(cache):
        os.makedirs(os.path.dirname(cache), exist_ok=True)
        url = f'https://www.python.org/ftp/python/{PYTHON_VERSION}/python-{PYTHON_VERSION}-embed-amd64.zip'
        print('downloading', url)
        with urllib.request.urlopen(url) as r, open(cache + '.part', 'wb') as f:
            shutil.copyfileobj(r, f)
        os.replace(cache + '.part', cache)
    return cache


def extract(repo, ref, folder):
    os.makedirs(folder)
    with tarfile.open(fileobj=io.BytesIO(git(['archive', '--format=tar', ref], repo))) as tar:
        tar.extractall(folder, filter='data')


def patched_ps2recomp(work):
    """PS2Recomp at its pinned commit (with def-jam-vendetta-recomp's patch) plus ours, in a scratch folder."""
    folder = os.path.join(work, 'PS2Recomp')
    extract(os.path.join(REPO, 'third_party', 'PS2Recomp'), PS2RECOMP_BASE, folder)
    subprocess.run(['git', 'apply', '--whitespace=nowarn', os.path.join(REPO, 'patches', 'ben10-on-defjam.diff')],
                   cwd=folder, check=True)
    marker = os.path.join(folder, 'ps2xRuntime', 'src', 'lib', 'ps2_host_settings.cpp')
    with open(marker, encoding='utf-8') as f:
        if 'keyboard = 1' not in f.read():
            raise SystemExit('our patch did not apply (ps2_host_settings.cpp has no Ben 10 settings)')
    return folder


def patched_parallel_gs(work):
    """paraLLEl-GS at its pinned commit with def-jam-vendetta-recomp's patch applied. Not an archive of the local commit:
    the files the patch adds are not in it (git archive left gs/shaders/sample_circuit_scaled.hpp out, and the
    player's build stopped on it)."""
    folder = os.path.join(work, 'parallel-gs')
    extract(os.path.join(REPO, 'third_party', 'parallel-gs'), PARALLEL_GS_BASE, folder)
    subprocess.run(['git', 'apply', '--whitespace=nowarn', os.path.join(REPO, 'patches', 'defjam-parallel-gs-local.diff')],
                   cwd=folder, check=True)
    for added in ('sample_circuit_scaled.hpp', 'triangle_setup_upscale.hpp', 'ubershader_hd.hpp', 'ubershader_hd_iface.hpp'):
        if not os.path.isfile(os.path.join(folder, 'gs', 'shaders', added)):
            raise SystemExit(f'the paraLLEl-GS patch did not apply: gs/shaders/{added} is missing')
    return folder


def main():
    if git(['status', '--porcelain'], REPO).decode().strip():
        print('note: uncommitted changes go in too (a real release should come from a commit)')
    os.makedirs(DIST, exist_ok=True)
    out = os.path.join(DIST, ZIP_NAME)
    deps_root = os.path.join(REPO, 'build', 'ps2recomp-dj', '_deps')
    pgs = os.path.join(REPO, 'third_party', 'parallel-gs')
    counts = {}
    # outside the repository: git apply run inside a repository's subfolder skips the patch's paths without a word
    with tempfile.TemporaryDirectory(prefix='ben10-release-') as work:
        exe = launcher_exe(work)
        pyzip = python_zip()
        ps2recomp = patched_ps2recomp(work)
        pgs_patched = patched_parallel_gs(work)
        with ReleaseZip(out + '.part', 'w', zipfile.ZIP_DEFLATED, compresslevel=6) as z:
            z.write(exe, TOP + '/' + LAUNCHER)
            z.writestr(TOP + '/LEIA-ME - READ ME.txt', README.replace('\n', '\r\n'))
            with zipfile.ZipFile(pyzip) as py:
                for n in py.namelist():
                    z.writestr(FILES + 'python/' + n, py.read(n))
            # this repository: what git would commit (the game's files are kept out by .gitignore)
            listed = git(['ls-files', '-co', '--exclude-standard', '-z'], REPO).decode().split('\0')
            n = 0
            for rel in listed:
                if not rel or rel.startswith(('launcher/bin/', 'launcher/obj/', 'dist/')):
                    continue
                path = os.path.join(REPO, rel)
                if os.path.isfile(path):
                    check(SRC + rel)
                    z.write(path, SRC + rel)
                    n += 1
            counts['ours'] = n
            counts['PS2Recomp'] = add_tree(z, ps2recomp, SRC + 'third_party/PS2Recomp/')
            counts['parallel-gs'] = add_tree(z, pgs_patched, SRC + 'third_party/parallel-gs/')
            granite = os.path.join(pgs, 'Granite')
            counts['Granite'] = add_archive(z, granite, SRC + 'third_party/parallel-gs/Granite/')
            for sub in ('third_party/volk', 'third_party/khronos/vulkan-headers'):
                counts[sub] = add_archive(z, os.path.join(granite, *sub.split('/')),
                                          SRC + 'third_party/parallel-gs/Granite/' + sub + '/')
            for name, (where, skip) in DEPS.items():
                folder = os.path.join(deps_root, where)
                if not os.path.isdir(folder):
                    raise SystemExit(f'{folder} is missing: build PS2Recomp here once (tools\\build.ps1)')
                counts['deps/' + name] = add_tree(z, folder, SRC + 'deps/' + name + '/', skip)
        add_manifest(out + '.part')
        os.replace(out + '.part', out)
    print(counts)

    with zipfile.ZipFile(out) as z:
        infos = z.infolist()
        for i in infos:
            check(i.filename)
            if i.file_size > 20 * 1024 * 1024 and not i.filename.endswith(LAUNCHER):
                raise SystemExit('too big: ' + i.filename)
            if '/ps2xRuntime/src/runner/' in i.filename:
                text = z.read(i).decode('utf-8', 'replace')
                if 'FUN_00' in text or 'entry_00' in text:
                    raise SystemExit('game code in ' + i.filename)
        longest = max(infos, key=lambda i: len(i.filename))
        print(f'{len(infos)} files, {os.path.getsize(out) / 1e6:.1f} MB zipped, '
              f'{sum(i.file_size for i in infos) / 1e6:.1f} MB unzipped; longest name {len(longest.filename)}: '
              f'{longest.filename}')
    print('->', out)


def version():
    """The version, from the launcher's project (the one place it is set)."""
    with open(os.path.join(REPO, 'launcher', 'Ben10Launcher.csproj'), encoding='utf-8') as f:
        text = f.read()
    start = text.index('<Version>') + len('<Version>')
    return text[start:text.index('</Version>', start)].strip()


def add_manifest(path):
    """files/release-manifest.txt: every file of the zip with its SHA-256 and size. The updater compares it with what is
    installed, so an update replaces only the files that changed (and keeps the build's work for the rest)."""
    lines = [f'# {TOP} {version()}', f'# sha256 size path (below "{TOP}/")']
    with zipfile.ZipFile(path) as z:
        for info in z.infolist():
            if info.is_dir():
                continue
            digest = hashlib.sha256()
            with z.open(info) as f:
                for chunk in iter(lambda: f.read(1 << 20), b''):
                    digest.update(chunk)
            lines.append(f'{digest.hexdigest()} {info.file_size} {info.filename[len(TOP) + 1:]}')
    with ReleaseZip(path, 'a', zipfile.ZIP_DEFLATED) as z:
        z.writestr(FILES + 'release-manifest.txt', '\n'.join(lines) + '\n')
    print(f'release-manifest.txt: {len(lines) - 2} files, version {version()}')


if __name__ == '__main__':
    main()
