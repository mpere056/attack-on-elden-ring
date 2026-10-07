"""Talks to the Elden Ring side of the bridge through its shared memory.

    python tools/erctl.py status        is the bridge loaded, where is the Tarnished
    python tools/erctl.py aim           live: distance to whatever the camera points at
    python tools/erctl.py reach         one sweep of rays in every direction, saved to runtime/
    python tools/erctl.py survey        hands-off: records aim rays and sweeps while you play
    python tools/erctl.py reload        swap in a freshly built dist/aoer_core.dll without restarting
    python tools/erctl.py testpattern on|off   compositor check: checkerboard in the top-left corner
    python tools/erctl.py shot          screenshot of the Elden Ring window into runtime/
    python tools/erctl.py anim          record which animation the Tarnished plays, with its speed

Milestone 1 uses `aim` and `reach` to measure how far away Elden Ring has collision loaded,
which decides how far ODM hooks can reach. Rays use Elden Ring's terrain filter: map geometry
and props, not characters.
"""
import csv
import math
import mmap
import struct
import sys
import time
from datetime import datetime
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SHM_NAME = 'Local\\AoER_bridge_v1'
SHM_SIZE = 8 * 1024 * 1024
MAGIC = 0x52454F41  # "AOER"

OFF_STATE = 0x100
OFF_RAYS = 0x100000
MAX_RAYS = 8192
OFF_RAYS_ARR = OFF_RAYS + 0x20
OFF_HITS_ARR = OFF_RAYS_ARR + MAX_RAYS * 24


def open_bridge():
    m = mmap.mmap(-1, SHM_SIZE, tagname=SHM_NAME)
    magic, version = struct.unpack_from('<II', m, 0)
    if magic != MAGIC:
        sys.exit('The bridge is not running. Start Elden Ring with Play-EldenRing.bat and load your character.')
    return m


def header(m):
    hb = struct.unpack_from('<Q', m, 0x10)[0]
    pid = struct.unpack_from('<I', m, 0x20)[0]
    gen, status = struct.unpack_from('<Ii', m, 0x40)
    return dict(heartbeat=hb, host_pid=pid, core_generation=gen, core_status=status)


def state(m):
    """Consistent copy of the game state block (seqlock)."""
    for _ in range(200):
        a = struct.unpack_from('<I', m, OFF_STATE)[0]
        blob = bytes(m[OFF_STATE:OFF_STATE + 0x114])
        b = struct.unpack_from('<I', m, OFF_STATE)[0]
        if a == b and not a & 1:
            break
        time.sleep(0.001)
    _, flags, frame = struct.unpack_from('<IIQ', blob, 0)
    cam = struct.unpack_from('<3f', blob, 0x10)
    target = struct.unpack_from('<3f', blob, 0x1C)
    player = struct.unpack_from('<3f', blob, 0x48)
    stage = struct.unpack_from('<I', blob, 0x7C)[0]
    return dict(flags=flags, frame=frame, cam=cam, target=target, player=player, stage=stage,
                player_valid=bool(flags & 2), camera_valid=bool(flags & 1), busy=bool(flags & 0x100))


def cast(m, rays, timeout=10.0):
    """rays: list of ((x,y,z), (x,y,z)). Returns list of hit position or None, same order."""
    out = []
    for i in range(0, len(rays), MAX_RAYS):
        out += _cast_batch(m, rays[i:i + MAX_RAYS], timeout)
    return out


def _cast_batch(m, rays, timeout):
    req, resp = struct.unpack_from('<II', m, OFF_RAYS)
    deadline = time.time() + timeout
    while req != resp:  # a previous request is still being served
        if time.time() > deadline:
            raise TimeoutError('the bridge is busy with an earlier ray request')
        time.sleep(0.01)
        req, resp = struct.unpack_from('<II', m, OFF_RAYS)
    for k, (s, e) in enumerate(rays):
        struct.pack_into('<6f', m, OFF_RAYS_ARR + k * 24, *s, *e)
    struct.pack_into('<II', m, OFF_RAYS + 8, len(rays), 0)  # count, flags = terrain filter
    struct.pack_into('<I', m, OFF_RAYS + 0x10, 0)           # processed
    struct.pack_into('<I', m, OFF_RAYS, req + 1)            # publish the request last
    while struct.unpack_from('<I', m, OFF_RAYS + 4)[0] != req + 1:
        if time.time() > deadline:
            raise TimeoutError('no answer: is your character loaded and standing in the world?')
        time.sleep(0.005)
    hits = []
    for k in range(len(rays)):
        px, py, pz, nx, ny, nz, hit, attr = struct.unpack_from('<6fII', m, OFF_HITS_ARR + k * 32)
        hits.append((px, py, pz) if hit else None)
    return hits


def dist(a, b):
    return math.sqrt(sum((x - y) ** 2 for x, y in zip(a, b)))


def cmd_status(m):
    h = header(m)
    time.sleep(0.5)
    live = header(m)['heartbeat'] > h['heartbeat']
    s = state(m)
    print(f"bridge: pid {h['host_pid']}, core generation {h['core_generation']}, "
          f"core status {h['core_status']} (1 = running), {'LIVE' if live else 'NOT ticking'}")
    if s['player_valid']:
        x, y, z = s['player']
        print(f"Tarnished at x {x:.1f}  y {y:.1f}  z {z:.1f}   (zone {s['stage']:#x})")
    else:
        print('no character in the world (title screen or loading)')


def cmd_aim(m, length=1000.0):
    print(f'Point the camera at things. Each line: distance to the first surface along the camera '
          f'(rays up to {length:.0f} m). Ctrl+C to stop.')
    last = None
    while True:
        s = state(m)
        if not (s['camera_valid'] and s['player_valid']):
            print('waiting for the character...', end='\r')
            time.sleep(0.5)
            continue
        c, t = s['cam'], s['target']
        d = [t[i] - c[i] for i in range(3)]
        n = math.sqrt(sum(v * v for v in d)) or 1.0
        d = [v / n for v in d]
        end = tuple(c[i] + d[i] * length for i in range(3))
        try:
            hit = cast(m, [(tuple(c), end)])[0]
        except TimeoutError as e:
            print(e)
            time.sleep(1)
            continue
        if hit:
            line = f'HIT at {dist(c, hit):7.1f} m from the camera, {dist(s["player"], hit):7.1f} m from the Tarnished'
        else:
            line = f'no surface within {length:.0f} m'
        if line != last:
            print(f'{datetime.now():%H:%M:%S}  {line}')
            last = line
        time.sleep(0.25)


def cmd_reach(m, length=600.0):
    s = state(m)
    if not s['player_valid']:
        sys.exit('no character in the world')
    eye = (s['player'][0], s['player'][1] + 1.6, s['player'][2])
    elevations = [-10, 0, 10, 20, 35, 50, 70]
    rays, meta = sweep_rays(eye, length, elevations, step=5)
    t0 = time.time()
    hits = cast(m, rays, timeout=30)
    took = time.time() - t0
    out = ROOT / 'runtime' / f'reach-{datetime.now():%Y%m%d-%H%M%S}.csv'
    out.parent.mkdir(exist_ok=True)
    with out.open('w', newline='') as f:
        w = csv.writer(f)
        w.writerow(['zone', 'eye_x', 'eye_y', 'eye_z', 'azimuth', 'elevation', 'hit', 'distance'])
        for (az, el), h in zip(meta, hits):
            w.writerow([hex(s['stage']), *(f'{v:.2f}' for v in eye), az, el, int(h is not None),
                        f'{dist(eye, h):.1f}' if h else ''])
    print(f'{len(rays)} rays of {length:.0f} m from the Tarnished in {took:.2f} s, saved to {out.name}')
    print('elevation   hits   nearest  median  farthest')
    for el in elevations:
        ds = sorted(dist(eye, h) for (a, e), h in zip(meta, hits) if e == el and h)
        n = sum(1 for (a, e) in meta if e == el)
        if ds:
            print(f'{el:>6} deg  {len(ds):>3}/{n}  {ds[0]:6.1f}  {ds[len(ds) // 2]:6.1f}  {ds[-1]:7.1f} m')
        else:
            print(f'{el:>6} deg    0/{n}  (nothing within {length:.0f} m)')
    ds = [dist(eye, h) for h in hits if h]
    buckets = [25, 50, 100, 200, 400, length]
    print('hit distances:', '  '.join(f'{lo_:.0f}-{hi:.0f} m: {sum(1 for d in ds if lo_ <= d < hi)}'
                                       for lo_, hi in zip([0] + buckets[:-1], buckets)))


def sweep_rays(eye, length, elevations=(-10, 0, 10, 20, 35, 50, 70), step=10):
    rays, meta = [], []
    for el in elevations:
        for az in range(0, 360, step):
            a, e = math.radians(az), math.radians(el)
            d = (math.cos(e) * math.sin(a), math.sin(e), math.cos(e) * math.cos(a))
            rays.append((eye, tuple(eye[i] + d[i] * length for i in range(3))))
            meta.append((az, el))
    return rays, meta


def cmd_survey(m, length=600.0):
    """Hands-off logging while the user plays: an aim ray 4x a second and a full sweep around the
    Tarnished every 3 seconds, all saved to runtime/survey-*.csv for later analysis."""
    out = ROOT / 'runtime' / f'survey-{datetime.now():%Y%m%d-%H%M%S}.csv'
    out.parent.mkdir(exist_ok=True)
    print(f'Recording to {out.name}. Play normally and look at near and far things. Ctrl+C to stop.')
    rows = sweeps = 0
    next_sweep = 0.0
    with out.open('w', newline='') as f:
        w = csv.writer(f)
        w.writerow(['time', 'kind', 'zone', 'x', 'y', 'z', 'azimuth', 'elevation', 'dx', 'dy', 'dz',
                    'hit', 'distance', 'cast_ms'])
        while True:
            s = state(m)
            if not (s['camera_valid'] and s['player_valid']) or s['busy']:
                time.sleep(0.5)
                continue
            now = time.time()
            stamp = f'{now:.2f}'
            c, t = s['cam'], s['target']
            d = [t[i] - c[i] for i in range(3)]
            n = math.sqrt(sum(v * v for v in d)) or 1.0
            d = [v / n for v in d]
            try:
                t0 = time.time()
                hit = cast(m, [(tuple(c), tuple(c[i] + d[i] * length for i in range(3)))])[0]
                ms = (time.time() - t0) * 1000
                w.writerow([stamp, 'aim', hex(s['stage']), *(f'{v:.2f}' for v in c), '', '',
                            *(f'{v:.3f}' for v in d), int(bool(hit)), f'{dist(c, hit):.1f}' if hit else '',
                            f'{ms:.1f}'])
                rows += 1
                if now >= next_sweep:
                    eye = (s['player'][0], s['player'][1] + 1.6, s['player'][2])
                    rays, meta = sweep_rays(eye, length)
                    t0 = time.time()
                    hits = cast(m, rays, timeout=30)
                    ms = (time.time() - t0) * 1000
                    for (az, el), h in zip(meta, hits):
                        w.writerow([stamp, 'sweep', hex(s['stage']), *(f'{v:.2f}' for v in eye), az, el,
                                    '', '', '', int(bool(h)), f'{dist(eye, h):.1f}' if h else '', f'{ms:.1f}'])
                    sweeps += 1
                    next_sweep = now + 3.0
                    f.flush()
                    print(f'{datetime.now():%H:%M:%S}  {sweeps} sweeps, {rows} aim rays recorded', end='\r')
            except TimeoutError:
                time.sleep(1)
                continue
            time.sleep(0.25)


def cmd_reload(m):
    req, ack = struct.unpack_from('<II', m, 0x38)
    gen_before = struct.unpack_from('<I', m, 0x40)[0]
    struct.pack_into('<I', m, 0x38, req + 1)
    for _ in range(200):
        time.sleep(0.05)
        ack, gen, status = struct.unpack_from('<IIi', m, 0x3C)
        if ack == req + 1:
            print(f'core reloaded: generation {gen_before} -> {gen}, status {status} (1 = running)')
            return
    print('no answer from the loader within 10 s; see runtime/host.log')


def cmd_testpattern(m):
    on = len(sys.argv) > 2 and sys.argv[2] == 'on'
    flags = struct.unpack_from('<I', m, 0x64)[0]
    flags = (flags | 1) if on else (flags & ~1)
    struct.pack_into('<I', m, 0x64, flags)
    print('test pattern', 'on' if on else 'off')


def cmd_shot(m):
    from PIL import ImageGrab
    blob = bytes(m[OFF_STATE:OFF_STATE + 0x114])
    x, y, w, h = struct.unpack_from('<4i', blob, 0x64)
    if w <= 0 or h <= 0:
        sys.exit('Elden Ring has not published its window position yet')
    img = ImageGrab.grab(bbox=(x, y, x + w, y + h), all_screens=True)
    out = ROOT / 'runtime' / f'shot-{datetime.now():%H%M%S}.png'
    img.save(out)
    print(f'saved {out} ({w}x{h} at {x},{y})')


def cmd_anim(m):
    """Tarnished mode research: every animation change with the speed it happened at. Play Elden Ring
    normally (not linked): stand, walk, run, sprint, jump, fall, roll. Saved to runtime/anim-*.csv."""
    out = ROOT / 'runtime' / f'anim-{datetime.now():%Y%m%d-%H%M%S}.csv'
    out.parent.mkdir(exist_ok=True)
    print(f'Recording to {out.name}. Play normally; Ctrl+C to stop.')
    last_id, last_pos, last_t = None, None, None
    speeds = []
    with out.open('w', newline='') as f:
        w = csv.writer(f)
        w.writerow(['time', 'anim', 'idle_anim', 'ground_speed', 'vertical_speed'])
        while True:
            s = state(m)
            blob = bytes(m[OFF_STATE + 0x114:OFF_STATE + 0x120])
            anim, req, idle = struct.unpack('<3i', blob)
            now = time.time()
            p = s['player']
            if last_pos is not None and now > last_t:
                dt = now - last_t
                speeds.append((math.hypot(p[0] - last_pos[0], p[2] - last_pos[2]) / dt, (p[1] - last_pos[1]) / dt))
                speeds = speeds[-6:]
            last_pos, last_t = p, now
            if s['player_valid'] and anim != last_id:
                gs = sum(a for a, b in speeds) / len(speeds) if speeds else 0
                vs = sum(b for a, b in speeds) / len(speeds) if speeds else 0
                print(f'{datetime.now():%H:%M:%S.%f}'[:-3] + f'  anim {anim:8d}  ground {gs:5.1f} m/s  vertical {vs:+5.1f} m/s  (idle anim {idle})')
                w.writerow([f'{now:.3f}', anim, idle, f'{gs:.2f}', f'{vs:.2f}'])
                f.flush()
                last_id = anim
            time.sleep(1 / 30)


def main():
    sys.stdout.reconfigure(line_buffering=True)
    cmd = sys.argv[1] if len(sys.argv) > 1 else 'status'
    fn = {'status': cmd_status, 'aim': cmd_aim, 'reach': cmd_reach, 'survey': cmd_survey,
          'reload': cmd_reload, 'testpattern': cmd_testpattern, 'shot': cmd_shot, 'anim': cmd_anim}.get(cmd)
    if not fn:
        sys.exit(__doc__)
    m = open_bridge()
    try:
        fn(m)
    except KeyboardInterrupt:
        pass


if __name__ == '__main__':
    main()
