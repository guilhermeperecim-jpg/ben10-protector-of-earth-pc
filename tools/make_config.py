# Builds the recompiler's input config/ben10.toml (generated, git-ignored). Adapted from def-jam-vendetta-recomp's
# tools/make_config.py (GPL-3.0).
#   base   = config/ben10.ghidra.toml (tools/ghidra_analyze.ps1's export), with this PC's paths
#   stubs  = config/manual_stubs.txt "name@0xADDR" lines (found by hand; win)
#          + the Ghidra export's stubs + ps2xAnalyzer's (config/analyzer.toml), one name per address,
#          minus every address listed as "-name@0xADDR" in manual_stubs.txt (the game's own code runs there)
#   functions = config/ben10_functions.csv: Ghidra's map + config/missing_starts.csv (tools/find_missing_starts.py:
#               code reached only through data pointers, which Ghidra did not make functions)
#               + config/jump_targets.csv (tools/find_jump_targets.py: switch cases with no mapped entry)
# usage: python -I tools/make_config.py
import os, re

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CFG = os.path.join(ROOT, 'config')


def stubs_of(text):
    body = re.search(r'^stubs = \[(.*?)^\]', text, re.S | re.M)
    return re.findall(r'"([^"@]+)@0x([0-9A-Fa-f]+)"', body.group(1)) if body else []


def main():
    with open(os.path.join(CFG, 'ben10_functions.csv'), 'w', newline='\n') as out:
        for name in ('ben10_functions.ghidra.csv', 'missing_starts.csv', 'jump_targets.csv'):
            path = os.path.join(CFG, name)
            if os.path.exists(path):
                text = open(path, encoding='utf-8').read()
                out.write(text if text.endswith('\n') else text + '\n')
    ghidra = open(os.path.join(CFG, 'ben10.ghidra.toml'), encoding='utf-8').read()
    analyzer = open(os.path.join(CFG, 'analyzer.toml'), encoding='utf-8').read()
    manual_path = os.path.join(CFG, 'manual_stubs.txt')
    entries = [l.split('#')[0].strip() for l in open(manual_path, encoding='utf-8')] if os.path.exists(manual_path) else []
    entries = [e for e in entries if e]
    assert all(re.fullmatch(r'-?[A-Za-z_]\w*@0x[0-9A-Fa-f]{8}', e) for e in entries), 'bad manual_stubs.txt line'
    manual = [tuple(e.split('@0x')) for e in entries if not e.startswith('-')]
    unbound = {int(e.split('@0x')[1], 16): e[1:].split('@0x')[0] for e in entries if e.startswith('-')}

    by_addr = {}
    for name, addr in manual + stubs_of(ghidra) + stubs_of(analyzer):
        a = int(addr, 16)
        if a not in unbound:
            by_addr.setdefault(a, name)
    # "-name@0xADDR": the recompiler would otherwise still bind these by name (its runtime handler list)
    lines = ''.join(f'  "{n}@0x{a:08X}",\n' for a, n in sorted(by_addr.items()))
    lines += ''.join(f'  "-{n}@0x{a:08X}",\n' for a, n in sorted(unbound.items()))
    toml = re.sub(r'^stubs = \[.*?^\]', lambda m: 'stubs = [\n' + lines + ']', ghidra, count=1, flags=re.S | re.M)
    root = ROOT.replace(os.sep, '/')
    paths = {'input': f'{root}/game/SLUS_216.61', 'output': f'{root}/generated',
             'ghidra_output': f'{root}/config/ben10_functions.csv'}
    for key, value in paths.items():
        toml, n = re.subn(rf'^{key} = .*$', f'{key} = "{value}"', toml, count=1, flags=re.M)
        assert n == 1, f'no {key} in ben10.ghidra.toml'
    with open(os.path.join(CFG, 'ben10.toml'), 'w', newline='\n') as f:
        f.write(toml)
    print(f'make_config: {len(manual)} manual, {len(unbound)} unbound, {len(stubs_of(ghidra))} ghidra, '
          f'{len(stubs_of(analyzer))} analyzer -> {len(by_addr)} stubs')


if __name__ == '__main__':
    main()
