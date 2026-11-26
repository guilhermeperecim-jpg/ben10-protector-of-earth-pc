# Names engine functions from the strat-script binding table in SLUS_216.61: the table holds pairs
# (pointer to an "ass_*" name, pointer to the C function), so each pair is a free symbol on a stripped ELF.
# Output is the CSV that refs/ps2_recomp/tools/ghidra/ApplySymbolsCsv.java reads (address,name,confidence,evidence).
# usage: python -I tools/script_bindings.py game/SLUS_216.61 [config/symbols_script.csv]
import re, struct, sys

def load(path):
    d = open(path, 'rb').read()
    assert d[:4] == b'\x7fELF', 'not an ELF'
    phoff, = struct.unpack_from('<I', d, 0x1C)
    _, off, va, _, filesz, _, _, _ = struct.unpack_from('<8I', d, phoff)  # the game has one PT_LOAD
    shoff, = struct.unpack_from('<I', d, 0x20)
    shnum, shstrndx = struct.unpack_from('<HH', d, 0x30)
    secs = [struct.unpack_from('<10I', d, shoff + i * 40) for i in range(shnum)]
    so = secs[shstrndx][4]
    text = next(s for s in secs if d[so + s[0]:d.index(b'\0', so + s[0])] == b'.text')
    return d, off, va, filesz, (text[3], text[3] + text[5])

def bindings(path):
    d, off, va, filesz, (t0, t1) = load(path)
    img = d[off:off + filesz]
    names = {va + m.start(): m.group().decode() for m in re.finditer(rb'ass_[A-Za-z0-9_]+(?=\0)', img)}
    found = {}
    for o in range(0, len(img) - 8, 4):
        p, f = struct.unpack_from('<II', img, o)
        if p in names and t0 <= f < t1 and f % 4 == 0:
            found.setdefault(f, (names[p], va + o))  # one C function can back several script names: keep the first
    word = lambda a: struct.unpack_from('<I', img, a - va)[0]
    # self-check: a function start follows the previous function's "jr ra" (+ delay slot) or alignment nops
    odd = [hex(a) for a in found if 0x03E00008 not in (word(a - 8), word(a - 4)) and word(a - 4) != 0]
    assert not odd, f'not function starts: {odd[:5]}'
    return found, len(names)

if __name__ == '__main__':
    found, total = bindings(sys.argv[1])
    assert found, 'no binding table found: wrong ELF?'
    assert all(a % 4 == 0 for a in found), 'misaligned function address'
    rows = ['address,name,confidence,evidence'] + [
        f'0x{a:08x},{n},medium,"strat binding table entry at 0x{t:08x}"' for a, (n, t) in sorted(found.items())]
    out = sys.argv[2] if len(sys.argv) > 2 else None
    open(out, 'w', newline='\n').write('\n'.join(rows) + '\n') if out else print('\n'.join(rows[:6]))
    print(f'{len(found)} functions named from {total} ass_* strings', file=sys.stderr)
