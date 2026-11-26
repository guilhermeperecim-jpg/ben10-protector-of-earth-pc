# Microsoft's C++ compiler, the Windows SDK, CMake and Ninja in a folder of their own, without Visual Studio's
# installer: only the packages the build uses (~1 GB instead of 3-5 GB), nothing installed in Windows, no
# administrator. The same way as the "portable-msvc" approach: Visual Studio's own package list (its channel
# manifest) gives each package's files on Microsoft's servers, with their SHA-256; the compiler's packages are zips
# (.vsix), the SDK's are MSIs unpacked with an administrative install (msiexec /a, which needs no administrator).
# The player's own PC downloads them from Microsoft (its licence: https://aka.ms/vs/17/release/license), as Visual
# Studio's installer would. Used by tools/setup_game.py; by hand:
#   python -I -X utf8 tools/portable_msvc.py <folder>      (prints the build environment's PATH, INCLUDE, LIB)
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
import urllib.parse
import urllib.request
import zipfile

CHANNEL = 'https://aka.ms/vs/17/release/channel'
SDK = '22621'  # the SDK the game is built and tested with (Windows 11 SDK 10.0.22621); else the newest one listed
# CMake and Ninja from their projects' own releases on GitHub, pinned with their SHA-256
CMAKE = ('https://github.com/Kitware/CMake/releases/download/v3.31.6/cmake-3.31.6-windows-x86_64.zip',
         'd163cd3ab4959b0a53fa8988f2ddbd2e6c501658201e6a154386bad9dbe4f836')
NINJA = ('https://github.com/ninja-build/ninja/releases/download/v1.12.1/ninja-win.zip',
         'f550fec705b6d6ff58f2db3c374c2277a37691678d6aba463adcbb129108467a')
SDK_MSIS = [
    'Windows SDK for Windows Store Apps Tools-x86_en-us.msi',      # rc.exe, mt.exe
    'Windows SDK for Windows Store Apps Headers-x86_en-us.msi',
    'Windows SDK for Windows Store Apps Headers OnecoreUap-x86_en-us.msi',
    'Windows SDK for Windows Store Apps Libs-x86_en-us.msi',
    'Universal CRT Headers Libraries and Sources-x86_en-us.msi',
    'Windows SDK Desktop Headers x86-x86_en-us.msi',      # the desktop headers common to every target (dinput.h ...)
    'Windows SDK Desktop Headers x64-x86_en-us.msi',
    'Windows SDK Desktop Libs x64-x86_en-us.msi',
]
DONE = 'portable-msvc.done'


def say(text):
    print(text, flush=True)


def fetch(url):
    with urllib.request.urlopen(url, timeout=120) as r:
        return r.read()


class Progress:
    """Prints "Getting the C++ tools: N of T MB" as the downloads go (the launcher shows it)."""
    def __init__(self, total):
        self.total, self.done, self.shown = max(1, total), 0, -1

    def add(self, n):
        self.done += n
        mb = self.done >> 20
        if mb // 25 != self.shown // 25:
            self.shown = mb
            say(f'Getting the C++ tools: {mb} of about {max(mb, self.total >> 20)} MB')
            say(f'[progress] {min(0.85, 0.85 * self.done / self.total):.3f}')


def download(url, path, sha256, progress=None):
    """A file to path, checked against its SHA-256 (when one is given); kept when already there and right."""
    if os.path.isfile(path) and sha256 and file_sha256(path) == sha256.lower():
        if progress:
            progress.add(os.path.getsize(path))
        return path
    os.makedirs(os.path.dirname(path), exist_ok=True)
    digest = hashlib.sha256()
    with urllib.request.urlopen(url, timeout=300) as r, open(path + '.part', 'wb') as f:
        while True:
            chunk = r.read(1 << 20)
            if not chunk:
                break
            f.write(chunk)
            digest.update(chunk)
            if progress:
                progress.add(len(chunk))
    if sha256 and digest.hexdigest() != sha256.lower():
        os.remove(path + '.part')
        raise RuntimeError(f'{os.path.basename(path)}: the download is damaged (SHA-256 differs)')
    os.replace(path + '.part', path)
    return path


def file_sha256(path):
    digest = hashlib.sha256()
    with open(path, 'rb') as f:
        for chunk in iter(lambda: f.read(1 << 20), b''):
            digest.update(chunk)
    return digest.hexdigest()


def manifest():
    channel = json.loads(fetch(CHANNEL))
    item = next(i for i in channel['channelItems'] if i['id'] == 'Microsoft.VisualStudio.Manifests.VisualStudio')
    return json.loads(fetch(item['payloads'][0]['url']))


def pick(packages, pid):
    """The package with this id (any case), the English or language-neutral one."""
    found = [p for p in packages.get(pid.lower(), []) if p.get('language') in (None, 'en-US', 'neutral')]
    return found[0] if found else None


def plan(vsman):
    packages = {}
    for p in vsman['packages']:
        packages.setdefault(p['id'].lower(), []).append(p)
    versions = sorted((pid[len('microsoft.vc.'):-len('.tools.hostx64.targetx64.base')] for pid in packages
                       if re.fullmatch(r'microsoft\.vc\.[\d.]+\.tools\.hostx64\.targetx64\.base', pid)),  # not ".premium"
                      key=lambda v: [int(x) if x.isdigit() else 0 for x in v.split('.')])
    if not versions:
        raise RuntimeError("Microsoft's package list has no x64 C++ compiler")
    v = versions[-1]
    msvc = [pick(packages, pid) for pid in (
        f'microsoft.vc.{v}.crt.headers.base', f'microsoft.vc.{v}.crt.x64.desktop.base',
        f'microsoft.vc.{v}.crt.x64.store.base',  # msvcrt.lib, vcruntime.lib, msvcprt.lib (and the d ones) live here
        f'microsoft.vc.{v}.tools.hostx64.targetx64.base', f'microsoft.vc.{v}.tools.hostx64.targetx64.res.base',
        f'microsoft.vc.{v}.premium.tools.hostx64.targetx64.base', f'microsoft.vc.{v}.crt.redist.x64.base')]
    msvc = [p for p in msvc if p]
    sdks = sorted({pid.split('_10.0.')[1] for pid in packages if re.match(r'win1[01]sdk_10\.0\.\d+$', pid)}, key=int)
    if not sdks:
        raise RuntimeError("Microsoft's package list has no Windows SDK")
    sdk_ver = SDK if SDK in sdks else sdks[-1]
    sdk = pick(packages, f'win11sdk_10.0.{sdk_ver}') or pick(packages, f'win10sdk_10.0.{sdk_ver}')
    return v, msvc, sdk


def unpack_vsix(path, out):
    with zipfile.ZipFile(path) as z:
        for info in z.infolist():
            name = urllib.parse.unquote(info.filename)
            if not name.startswith('Contents/') or name.endswith('/'):
                continue
            target = os.path.join(out, *name[len('Contents/'):].split('/'))
            os.makedirs(os.path.dirname(target), exist_ok=True)
            with z.open(info) as src, open(target, 'wb') as dst:
                shutil.copyfileobj(src, dst)


def stop_our_processes(folder):
    """The compiler's helpers left running from this folder (mspdbsrv.exe, vctip.exe hold its files after a build):
    stopped, so the folder can be replaced. Only processes started from inside it: never a Visual Studio's."""
    script = ("Get-Process | Where-Object { $_.Path -and $_.Path.StartsWith($env:BEN10_TOOLS, "
              "[System.StringComparison]::OrdinalIgnoreCase) } | Stop-Process -Force -ErrorAction SilentlyContinue")
    subprocess.run(['powershell', '-NoProfile', '-NonInteractive', '-Command', script],
                   env=dict(os.environ, BEN10_TOOLS=os.path.abspath(folder) + os.sep), capture_output=True)


def replace_folder(new, old):
    """new put in old's place; an old one still held by something is moved aside (removed on a later run)."""
    if os.path.exists(old):
        shutil.rmtree(old, ignore_errors=True)
    if os.path.exists(old):
        aside = f'{old}.old{os.getpid()}'
        os.replace(old, aside)
        shutil.rmtree(aside, ignore_errors=True)
    os.replace(new, old)


def get(folder):
    """Everything into folder (msvc/, cmake/, ninja/); a finished folder is used as it is."""
    if os.path.isfile(os.path.join(folder, DONE)):
        return environment(folder)
    work = os.path.join(folder, 'downloads')
    os.makedirs(work, exist_ok=True)
    stop_our_processes(folder)
    for leftover in os.listdir(folder):  # folders moved aside by an earlier run
        if '.old' in leftover:
            shutil.rmtree(os.path.join(folder, leftover), ignore_errors=True)
    say("Reading Microsoft's list of the C++ tools")
    version, msvc, sdk = plan(manifest())
    payloads = [pl for p in msvc for pl in p['payloads']]
    sdk_payloads = {pl['fileName'].split('\\')[-1]: pl for pl in sdk['payloads']}
    msis = [sdk_payloads[name] for name in SDK_MSIS if name in sdk_payloads]
    # the SDK's .cab files are only known once its MSIs are read: about 400 MB of them, guessed in
    total = sum(pl.get('size', 0) for pl in payloads + msis) + (460 << 20)
    progress = Progress(total)
    say(f'the C++ compiler {version}, the Windows SDK {sdk["id"]}')

    msvc_out = os.path.join(folder, 'msvc.part')  # put in place of msvc\\ once all of it is unpacked
    shutil.rmtree(msvc_out, ignore_errors=True)
    for pl in payloads:
        path = download(pl['url'], os.path.join(work, 'vc', pl['fileName'].replace('\\', '_')), pl.get('sha256'), progress)
        unpack_vsix(path, msvc_out)

    # the SDK: its MSIs and the .cab files each one names, then one administrative install each
    sdk_dir = os.path.join(work, 'sdk')
    for pl in msis:
        msi = download(pl['url'], os.path.join(sdk_dir, pl['fileName'].split('\\')[-1]), pl.get('sha256'), progress)
        with open(msi, 'rb') as f:
            data = f.read()
        for cab in sorted(set(re.findall(rb'[0-9a-f]{32}\.cab', data))):
            cab = cab.decode()
            if cab in sdk_payloads:
                c = sdk_payloads[cab]
                download(c['url'], os.path.join(sdk_dir, cab), c.get('sha256'), progress)
    say('Unpacking the Windows SDK')
    for pl in msis:
        msi = os.path.join(sdk_dir, pl['fileName'].split('\\')[-1])
        code = subprocess.run(['msiexec.exe', '/a', msi, '/quiet', '/qn', f'TARGETDIR={msvc_out}']).returncode
        if code != 0:
            raise RuntimeError(f'the Windows SDK could not be unpacked ({os.path.basename(msi)}: msiexec {code})')
    for leftover in os.listdir(msvc_out):  # the administrative installs leave a copy of each .msi at the top
        if leftover.lower().endswith('.msi'):
            os.remove(os.path.join(msvc_out, leftover))

    replace_folder(msvc_out, os.path.join(folder, 'msvc'))

    # CMake and Ninja
    for (url, sha), name in ((CMAKE, 'cmake'), (NINJA, 'ninja')):
        z = download(url, os.path.join(work, url.rsplit('/', 1)[1]), sha, progress)
        target = os.path.join(folder, name)
        with zipfile.ZipFile(z) as zf:
            zf.extractall(target + '.part')
        replace_folder(target + '.part', target)
    env = environment(folder)
    with open(os.path.join(folder, DONE), 'w') as f:
        f.write(version + '\n')
    shutil.rmtree(work, ignore_errors=True)  # the downloads, now unpacked
    say('[progress] 0.900')
    return env


def environment(folder):
    """The build's environment (what vcvars64.bat would set) for the tools in folder."""
    msvc_out = os.path.join(folder, 'msvc')
    tools = os.path.join(msvc_out, 'VC', 'Tools', 'MSVC')
    vc = os.path.join(tools, sorted(os.listdir(tools))[-1])
    kits = os.path.join(msvc_out, 'Windows Kits', '10')
    sdk_ver = sorted(d for d in os.listdir(os.path.join(kits, 'Include')) if d.startswith('10.'))[-1]
    cmake_bin = next(os.path.join(r, 'bin') for r, d, _ in os.walk(os.path.join(folder, 'cmake')) if 'bin' in d)
    redist = os.path.join(msvc_out, 'VC', 'Redist', 'MSVC')
    env = dict(os.environ)
    env['PATH'] = os.pathsep.join([os.path.join(vc, 'bin', 'Hostx64', 'x64'), os.path.join(kits, 'bin', sdk_ver, 'x64'),
                                   cmake_bin, os.path.join(folder, 'ninja'), env.get('PATH', env.get('Path', ''))])
    env.pop('Path', None)
    env['INCLUDE'] = os.pathsep.join([os.path.join(vc, 'include')] + [os.path.join(kits, 'Include', sdk_ver, s)
                                     for s in ('ucrt', 'shared', 'um', 'winrt', 'cppwinrt')])
    env['LIB'] = os.pathsep.join([os.path.join(vc, 'lib', 'x64'), os.path.join(kits, 'Lib', sdk_ver, 'ucrt', 'x64'),
                                  os.path.join(kits, 'Lib', sdk_ver, 'um', 'x64')])
    env['WindowsSdkDir'] = kits + os.sep
    env['WindowsSDKVersion'] = sdk_ver + os.sep
    env['VCToolsInstallDir'] = vc + os.sep
    if os.path.isdir(redist):
        env['VCToolsRedistDir'] = os.path.join(redist, sorted(os.listdir(redist))[-1]) + os.sep
    return env


if __name__ == '__main__':
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
    if len(sys.argv) != 2:
        sys.exit('usage: python -I -X utf8 tools/portable_msvc.py <folder>')
    e = get(os.path.abspath(sys.argv[1]))
    for key in ('PATH', 'INCLUDE', 'LIB', 'VCToolsRedistDir'):
        print(f'{key}={e.get(key, "")}')
