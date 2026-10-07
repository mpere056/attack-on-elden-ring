r"""Builds the AoTTG2 plugin with the project-local .NET SDK.

    python tools/build_guest.py            build dist/guest/AoerBridge.dll
    python tools/build_guest.py --install  build, then copy it into AoTTG2's BepInEx\plugins\AoerBridge

NuGet packages (the .NET 6 reference pack) are cached in .tools/nuget.
"""
from pathlib import Path
import os
import shutil
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]
DOTNET = ROOT / '.tools/dotnet/dotnet.exe'
from paths import aottg2_dir  # AOTTG2_DIR or .local/aottg2_dir.txt (kept out of the repo)
AOTTG2 = aottg2_dir()
OUT = ROOT / 'dist/guest'
OBJ = ROOT / 'build/guest/obj'

env = dict(os.environ, DOTNET_CLI_TELEMETRY_OPTOUT='1', DOTNET_NOLOGO='1', DOTNET_ROOT=str(DOTNET.parent),
           NUGET_PACKAGES=str(ROOT / '.tools/nuget'))
cmd = [str(DOTNET), 'build', str(ROOT / 'guest/AoerBridge.csproj'), '-c', 'Release', '-o', str(OUT),
       '-nologo', '-v', 'q', f'-p:AoTTG2Dir={AOTTG2}', f'-p:BaseIntermediateOutputPath={OBJ}{os.sep}']
r = subprocess.run(cmd, env=env, capture_output=True, text=True)
print((r.stdout + r.stderr).strip())
if r.returncode:
    sys.exit('build failed')
dll = OUT / 'AoerBridge.dll'
print('Built', dll)
if '--install' in sys.argv:
    dest = AOTTG2 / 'BepInEx/plugins/AoerBridge'
    dest.mkdir(parents=True, exist_ok=True)
    try:
        shutil.copy2(dll, dest / dll.name)
        print('Installed to', dest)
    except PermissionError:
        print('AoTTG2 is running, so the plugin was not replaced. Play-AoTTG2.bat installs it on the next start.')
