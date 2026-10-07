"""Reads and writes Elden Ring memory through the bridge's debug mailbox (research tool).

    python tools/ermem.py anim                show the Tarnished's animation modules
    python tools/ermem.py request <id>        ask the event module to play animation <id> once

Only touches the main player's own modules, found the same way the bridge does
(WorldChrMan -> main player -> module container), each checked by its owner pointer.
"""
import struct
import sys
import time

sys.path.insert(0, __file__.rsplit('\\', 1)[0].rsplit('/', 1)[0])
import erctl  # noqa: E402

C = 0x1000
WORLD_CHR_MAN = 0x3D69FF8
MAIN_PLAYER = 0x1E508
MODULES = 0x190
MOD_TIME_ACT, MOD_EVENT = 0x18, 0x58


def cmd(m, code, args=b''):
    req, resp = struct.unpack_from('<II', m, C)
    if req != resp:
        sys.exit('mailbox busy')
    m[C + 0x18:C + 0x18 + len(args)] = args
    struct.pack_into('<II', m, C + 8, code, len(args))
    struct.pack_into('<I', m, C, req + 1)
    t = time.time()
    while struct.unpack_from('<I', m, C + 4)[0] != req + 1:
        if time.time() - t > 3:
            sys.exit('no answer from the bridge')
        time.sleep(0.005)
    status, n = struct.unpack_from('<iI', m, C + 0x10)
    return status, bytes(m[0x2000:0x2000 + n])


def read(m, addr, n):
    status, data = cmd(m, 2, struct.pack('<QI', addr, n))
    if status != 0 or len(data) < n:
        sys.exit(f'cannot read {addr:#x}')
    return data


def write(m, addr, data):
    status, _ = cmd(m, 3, struct.pack('<QI', addr, len(data)) + data)
    if status != 0:
        sys.exit(f'cannot write {addr:#x}')


def q(m, addr):
    return struct.unpack('<Q', read(m, addr, 8))[0]


def base(m):
    status, data = cmd(m, 8)
    o = 0
    while o + 14 <= len(data):
        b, size, ln = struct.unpack_from('<QIH', data, o)
        o += 14
        name = data[o:o + ln].decode(errors='replace')
        o += ln
        if name.lower() == 'eldenring.exe':
            return b
    sys.exit('eldenring.exe not found')


def player_modules(m):
    wcm = q(m, base(m) + WORLD_CHR_MAN)
    ins = q(m, wcm + MAIN_PLAYER)
    mods = q(m, ins + MODULES)
    ev, ta = q(m, mods + MOD_EVENT), q(m, mods + MOD_TIME_ACT)
    for name, mod in (('event', ev), ('time_act', ta)):
        if q(m, mod + 8) != ins:
            sys.exit(f'{name} module does not point back at the player: offsets wrong for this version')
    return ins, ev, ta


def show(m):
    ins, ev, ta = player_modules(m)
    req, idle = struct.unpack('<ii', read(m, ev + 0x18, 8))
    queue = read(m, ta + 0x20, 160)
    w_idx, r_idx = struct.unpack('<II', read(m, ta + 0xC0, 8))
    anims = [struct.unpack_from('<iff f', queue, i * 16) for i in range(10)]
    print(f'player {ins:#x}  event {ev:#x}  time_act {ta:#x}')
    print(f'event: request_animation_id {req}, idle_anim_id {idle}')
    print(f'time_act: write_idx {w_idx}, read_idx {r_idx}')
    for i, (a, t, _, length) in enumerate(anims):
        mark = ' <- read' if i == r_idx else ''
        print(f'  [{i}] anim {a:8d}  time {t:6.2f} / {length:6.2f}{mark}')


def main():
    m = erctl.open_bridge()
    if len(sys.argv) > 2 and sys.argv[1] == 'request':
        anim = int(sys.argv[2])
        ins, ev, ta = player_modules(m)
        before = struct.unpack('<i', read(m, ev + 0x18, 4))[0]
        write(m, ev + 0x18, struct.pack('<i', anim))
        print(f'request_animation_id {before} -> {anim}')
        for _ in range(6):
            time.sleep(0.1)
            r = struct.unpack('<i', read(m, ev + 0x18, 4))[0]
            r_idx = struct.unpack('<I', read(m, ta + 0xC4, 4))[0]
            cur = struct.unpack('<i', read(m, ta + 0x20 + (r_idx % 10) * 16, 4))[0]
            print(f'  after {(_ + 1) * 100} ms: request field {r}, playing {cur}')
    else:
        show(m)


if __name__ == '__main__':
    main()
