# Turns the SDK functions ps2xAnalyzer recognised (its embedded SCE signature DB) into a symbols CSV for Ghidra:
# `stubs` (names the runtime implements) and `entry_points` (SDK names without a runtime handler; the game's code runs).
# Output format is what tools/ghidra/ApplySymbolsCsv.java reads (address,name,confidence,evidence).
# usage: python -I tools/sdk_symbols.py config/analyzer.toml config/symbols_sdk.csv
import re, sys

text = open(sys.argv[1], encoding='utf-8').read()
rows = {}
for section in ('stubs', 'entry_points'):
    body = re.search(rf'^{section} = \[(.*?)^\]', text, re.S | re.M)
    assert body, f'no [{section}] list in {sys.argv[1]}'
    for name, addr in re.findall(r'"([^"@]+)@0x([0-9A-Fa-f]+)"', body.group(1)):
        if name.startswith(('sub_', 'ret0', 'ret1', 'reta0')):
            continue  # generic handlers / unnamed, not SDK identities
        rows.setdefault(int(addr, 16), (name, section))
assert rows, 'analyzer found no SDK functions'
with open(sys.argv[2], 'w', newline='\n') as out:
    out.write('address,name,confidence,evidence\n')
    for a, (n, s) in sorted(rows.items()):
        out.write(f'0x{a:08x},{n},high,"ps2xAnalyzer SCE signature DB ({s})"\n')
print(f'{len(rows)} SDK symbols', file=sys.stderr)
