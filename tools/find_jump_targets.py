# Finds switch jump-table targets that have no entry in the function map. A table is a run of 4+ words in the ELF's
# data that all point into the same function; the recompiler only emits a resume point at mapped entries, so a "jr" to
# any other case stops the game ("guest-branch:missing-target", Ben 10: case 0x185CDC of the strat VM at 0x185BA0).
# Each missing target becomes a map row from it to the next mapped start (the same shape as Ghidra's entry_ rows).
# usage: python -I tools/find_jump_targets.py game/SLUS_216.61 config/ben10_functions.ghidra.csv --write config/jump_targets.csv
import bisect, csv, struct, sys

elf_path, csv_path = sys.argv[1], sys.argv[2]
out_path = sys.argv[sys.argv.index('--write') + 1] if '--write' in sys.argv else None
d = open(elf_path, 'rb').read()
phoff, = struct.unpack_from('<I', d, 0x1C)
_, off, va, _, filesz, *_ = struct.unpack_from('<8I', d, phoff)
img = d[off:off + filesz]
shoff, = struct.unpack_from('<I', d, 0x20)
shnum, shstrndx = struct.unpack_from('<HH', d, 0x30)
secs = [struct.unpack_from('<10I', d, shoff + i * 40) for i in range(shnum)]
so = secs[shstrndx][4]
name_of = lambda s: d[so + s[0]:d.index(b'\0', so + s[0])].decode()
text = next(s for s in secs if name_of(s) == '.text')
t0, t1 = text[3], text[3] + text[5]

rows = [r for r in csv.DictReader(open(csv_path)) if r['Start'].startswith('0x')]
starts = sorted({int(r['Start'], 16) for r in rows})
funcs = sorted((int(r['Start'], 16), int(r['End'], 16)) for r in rows if r['Name'].startswith('FUN_') or not r['Name'].startswith(('entry_', 'LAB_')))
fstarts = [f[0] for f in funcs]

def owner(a):  # the innermost real function containing a
    i = bisect.bisect_right(fstarts, a) - 1
    while i >= 0 and not (funcs[i][0] <= a < funcs[i][1]):
        i -= 1
    return funcs[i] if i >= 0 else None

found = {}
words = [struct.unpack_from('<I', img, o)[0] for o in range(0, filesz - 3, 4)]
run = []
for i, word in enumerate(words + [0]):
    f = owner(word) if t0 <= word < t1 and word % 4 == 0 else None
    if f and (not run or run[0][1] == f):
        run.append((va + 4 * i, f, word))
        continue
    if len(run) >= 4:
        for slot, f, target in run:
            if target not in starts and target != f[0]:
                found.setdefault(target, slot)
    run = [(va + 4 * i, f, word)] if f else []

for target in sorted(found):
    print(f'0x{target:08X}  table slot 0x{found[target]:08X}')
print(len(found), 'unmapped jump-table targets', file=sys.stderr)
if out_path:
    with open(out_path, 'w', newline='\n') as f:
        for t in sorted(found):
            end = starts[bisect.bisect_right(starts, t)] if bisect.bisect_right(starts, t) < len(starts) else t + 4
            f.write(f'entry_{t:08x},0x{t:08X},0x{end:08X},{end - t}\n')
