# Copies the recompiler's output (generated\) into PS2Recomp's runtime, touching only files whose content changed,
# so ninja recompiles just those. The two generated headers go to ps2xRuntime\include\; .cpp files no longer generated
# are removed from the runner folder. Our own runtime code (overrides\, when it exists) is copied along.
# Adapted from def-jam-vendetta-recomp's tools/sync_generated.py (GPL-3.0).
# usage: python -I tools/sync_generated.py
import filecmp, os, shutil

root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
gen = os.path.join(root, 'generated')
runtime = os.path.join(root, 'third_party', 'PS2Recomp', 'ps2xRuntime')
runner, include = os.path.join(runtime, 'src', 'runner'), os.path.join(runtime, 'include')
HEADERS = {'ps2_recompiled_functions.h', 'ps2_recompiled_stubs.h'}
assert os.path.isdir(gen) and os.listdir(gen), f'nothing to sync: run ps2_recomp first ({gen})'

sources = {n: os.path.join(gen, n) for n in os.listdir(gen)}
overrides = os.path.join(root, 'overrides')
if os.path.isdir(overrides):
    sources.update({n: os.path.join(overrides, n) for n in os.listdir(overrides)})
changed = added = 0
for i, (n, src) in enumerate(sorted(sources.items()), 1):
    if i % 2000 == 0:
        print(f'checked {i:,} of {len(sources):,}', flush=True)
    dst = os.path.join(include if n in HEADERS else runner, n)
    if os.path.exists(dst):
        if filecmp.cmp(src, dst, shallow=False):
            continue
        changed += 1
    else:
        added += 1
    shutil.copyfile(src, dst)
removed = 0
for n in os.listdir(runner):
    if n.endswith('.cpp') and n not in sources:
        os.remove(os.path.join(runner, n)); removed += 1
print(f'sync: {changed} changed, {added} added, {removed} removed, {len(sources) - changed - added} unchanged')
