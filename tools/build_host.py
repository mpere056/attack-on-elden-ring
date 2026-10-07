"""Builds the Elden Ring side of the bridge with the project-local LLVM-MinGW.

    python tools/build_host.py

Outputs dist/aoer_host.dll (the loader me3 loads) and dist/aoer_core.dll (the bridge itself,
hot-reloadable). Adapted from Minecraft-Ring's tools/build_native.py.
"""
from pathlib import Path
from concurrent.futures import ThreadPoolExecutor
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]
HOST = ROOT / 'host'
try:
    CXX = next((ROOT / '.tools/compiler').glob('*/bin/clang++.exe'))
except StopIteration:
    sys.exit('LLVM-MinGW not found in .tools/compiler')
CC = CXX.with_name('clang.exe')
OUT = ROOT / 'dist'
OBJ = ROOT / 'build/host'
OUT.mkdir(exist_ok=True)
OBJ.mkdir(parents=True, exist_ok=True)

flags = ['--target=x86_64-w64-mingw32', '-O2', '-std=c++17', '-Wall', '-Wno-unknown-pragmas',
         '-DWIN32_LEAN_AND_MEAN', '-DNOMINMAX',
         '-I' + str(HOST / 'include'), '-I' + str(HOST / 'third_party/minhook/include')]
cpp = ['loader', 'log', 'shm', 'core', 'crash', 'memutil', 'debugcmd', 'frame', 'game', 'compositor', 'input']


def run(cmd):
    r = subprocess.run(cmd, capture_output=True, text=True)
    if r.stdout or r.stderr:
        print(r.stdout + r.stderr, end='')
    if r.returncode:
        sys.exit(f'build failed: {Path(cmd[0]).name} exited {r.returncode}')


def compile_cpp(name):
    run([str(CXX), *flags, '-c', str(HOST / 'src' / f'{name}.cpp'), '-o', str(OBJ / f'{name}.o')])


with ThreadPoolExecutor(max_workers=4) as pool:
    list(pool.map(compile_cpp, cpp))

mh = []
for name in ['buffer.c', 'hook.c', 'trampoline.c', 'hde/hde64.c']:
    obj = OBJ / ('mh_' + Path(name).stem + '.o')
    run([str(CC), '--target=x86_64-w64-mingw32', '-O2', '-I' + str(HOST / 'third_party/minhook/include'),
         '-c', str(HOST / 'third_party/minhook/src' / name), '-o', str(obj)])
    mh.append(str(obj))

link = ['--target=x86_64-w64-mingw32', '-shared', '-static', '-s']
objs = lambda names: [str(OBJ / f'{x}.o') for x in names]
run([str(CXX), *link, *objs(['core', 'crash', 'log', 'shm', 'memutil', 'debugcmd', 'frame', 'game', 'compositor', 'input']),
     *mh, '-luser32', '-lkernel32', '-ld3d12', '-ldxgi', '-ldxguid', '-o', str(OUT / 'aoer_core.dll')])
# The loader stays loaded while Elden Ring runs; it rarely changes. The core is hot-swappable
# (python tools/erctl.py reload).
r = subprocess.run([str(CXX), *link, *objs(['loader', 'log', 'shm']), '-luser32', '-lkernel32',
                    '-o', str(OUT / 'aoer_host.dll')], capture_output=True, text=True)
if r.returncode:
    print('note: dist/aoer_host.dll is in use (Elden Ring is running), so the loader was not relinked. '
          'Restart Elden Ring if the loader itself changed.')
print('Built', OUT / 'aoer_host.dll', 'and', OUT / 'aoer_core.dll')
