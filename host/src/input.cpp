// Tarnished mode: a virtual controller. While AoTTG2 drives the Tarnished with
// ERMC_CTRL_VIRTUAL_PAD set, AoTTG2 sends a left-stick position (its hero's movement relative to
// the camera, which Elden Ring renders from) and Elden Ring's own locomotion picks the animation:
// idle, walk, jog, turning. The stand-in still pins the Tarnished to the hero's position every
// tick, so the two can't drift apart; Elden Ring only supplies the animation.
//
// Hook: XInputGetState in xinput1_4.dll (a Windows library, not Elden Ring's code). The virtual
// stick is added to whatever a real controller reports on pad 0; with no controller plugged in,
// pad 0 reports as connected while the virtual pad is active.
#include "common.h"
#include "MinHook.h"
#include <math.h>

namespace mb {

typedef struct {
    WORD wButtons;
    BYTE bLeftTrigger, bRightTrigger;
    SHORT sThumbLX, sThumbLY, sThumbRX, sThumbRY;
} PadGamepad;
typedef struct {
    DWORD dwPacketNumber;
    PadGamepad Gamepad;
} PadState;
typedef DWORD(WINAPI* XInputGetState_t)(DWORD, PadState*);

static XInputGetState_t g_origGetState = nullptr;
static void* g_getStateAddr = nullptr;
static DWORD g_packet = 0x40000000;
static uint64_t g_lastFrameSeen = 0, g_lastFrameMs = 0;
static volatile LONG g_padActive = 0;
static volatile LONG g_calls[5] = {}, g_capsCalls = 0, g_virtualServed = 0;

typedef struct {
    BYTE Type, SubType;
    WORD Flags;
    PadGamepad Gamepad;
    struct { WORD wLeftMotorSpeed, wRightMotorSpeed; } Vibration;
} PadCaps;
typedef DWORD(WINAPI* XInputGetCapabilities_t)(DWORD, DWORD, PadCaps*);
static XInputGetCapabilities_t g_origGetCaps = nullptr;
static void* g_getCapsAddr = nullptr;

// The virtual pad is used only while AoTTG2 keeps publishing fresh control blocks with the flag.
static bool virtual_pad(float* lx, float* ly, uint32_t* buttons) {
    ErmcControl c;
    if (!control_snapshot(&c) || !(c.flags & ERMC_CTRL_VIRTUAL_PAD) || !(c.flags & ERMC_CTRL_MOVE_HUNTER)) return false;
    uint64_t now = now_ms();
    if (c.mcFrame != g_lastFrameSeen) {
        g_lastFrameSeen = c.mcFrame;
        g_lastFrameMs = now;
    }
    if (now - g_lastFrameMs > 500) return false;  // AoTTG2 stopped updating
    *lx = c.stickX;
    *ly = c.stickY;
    *buttons = c.padButtons;
    return true;
}

static SHORT to_axis(float v) {
    if (!(v == v)) return 0;  // NaN
    if (v > 1.0f) v = 1.0f;
    if (v < -1.0f) v = -1.0f;
    return (SHORT)lroundf(v * 32767.0f);
}

static SHORT add_axis(SHORT real, SHORT virt) {
    int s = (int)real + (int)virt;
    return (SHORT)(s > 32767 ? 32767 : s < -32768 ? -32768 : s);
}

static DWORD WINAPI hk_XInputGetState(DWORD user, PadState* state) {
    InflightGuard guard;
    InterlockedIncrement(&g_calls[user < 4 ? user : 4]);
    DWORD r = g_origGetState(user, state);
    float lx, ly;
    uint32_t buttons;
    if (user != 0 || !state || !virtual_pad(&lx, &ly, &buttons)) {
        if (InterlockedExchange(&g_padActive, 0)) log("input: virtual controller released");
        return r;
    }
    if (!InterlockedExchange(&g_padActive, 1)) log("input: virtual controller active (pad 0)");
    if (r != ERROR_SUCCESS) {
        memset(state, 0, sizeof(*state));  // no real controller: report a connected, idle one
        r = ERROR_SUCCESS;
    }
    state->Gamepad.sThumbLX = add_axis(state->Gamepad.sThumbLX, to_axis(lx));
    state->Gamepad.sThumbLY = add_axis(state->Gamepad.sThumbLY, to_axis(ly));
    state->Gamepad.wButtons |= (WORD)buttons;
    state->dwPacketNumber = ++g_packet;  // always "changed", so the game reads it
    InterlockedIncrement(&g_virtualServed);
    return r;
}

// While the virtual pad is in use, pad 0 reports as an ordinary connected gamepad.
static DWORD WINAPI hk_XInputGetCapabilities(DWORD user, DWORD flags, PadCaps* caps) {
    InflightGuard guard;
    InterlockedIncrement(&g_capsCalls);
    DWORD r = g_origGetCaps(user, flags, caps);
    float lx, ly;
    uint32_t b;
    if (user == 0 && caps && r != ERROR_SUCCESS && virtual_pad(&lx, &ly, &b)) {
        memset(caps, 0, sizeof(*caps));
        caps->Type = 1;     // XINPUT_DEVTYPE_GAMEPAD
        caps->SubType = 1;  // XINPUT_DEVSUBTYPE_GAMEPAD
        caps->Gamepad.wButtons = 0xF3FF;
        caps->Gamepad.sThumbLX = caps->Gamepad.sThumbLY = (SHORT)0xFFC0;
        r = ERROR_SUCCESS;
    }
    return r;
}

// Once every few seconds (worker thread): how often the game asks for controller input, and
// whether its window has the focus. Answers "does Elden Ring read the virtual pad at all?".
void input_poll() {
    static uint64_t next = 0;
    uint64_t now = now_ms();
    if (now < next) return;
    next = now + 5000;
    LONG c0 = InterlockedExchange(&g_calls[0], 0), c1 = InterlockedExchange(&g_calls[1], 0) +
              InterlockedExchange(&g_calls[2], 0) + InterlockedExchange(&g_calls[3], 0);
    LONG caps = InterlockedExchange(&g_capsCalls, 0), served = InterlockedExchange(&g_virtualServed, 0);
    HWND fg = GetForegroundWindow();
    bool focused = fg && fg == game_hwnd();
    ErmcControl c;
    bool want = control_snapshot(&c) && (c.flags & ERMC_CTRL_VIRTUAL_PAD);
    // Elden Ring checks for controllers when it starts and, it seems, when Windows announces a device
    // change; with no pad at start-up it never polls again (test: 0 calls while linked). So announce
    // one: with GetCapabilities now reporting pad 0 connected, the game should start reading it.
    if (want && c0 == 0 && game_hwnd()) {
        PostMessageA(game_hwnd(), 0x0219 /* WM_DEVICECHANGE */, 0x0007 /* DBT_DEVNODES_CHANGED */, 0);
        log("input: told Elden Ring that devices changed (it is not polling the pad yet)");
    }
    if (c0 || c1 || caps || want)
        log("input: 5 s: XInputGetState pad0 %ld, pads1-3 %ld, GetCapabilities %ld, virtual stick served %ld; "
            "AoTTG2 asks for the pad: %s; Elden Ring window focused: %s",
            c0, c1, caps, served, want ? "yes" : "no", focused ? "yes" : "no");
}

bool input_init() {
    HMODULE x = GetModuleHandleA("xinput1_4.dll");
    if (!x) {
        log("input: xinput1_4.dll not loaded; no virtual controller (Tarnished stays unanimated)");
        return false;
    }
    g_getStateAddr = (void*)GetProcAddress(x, "XInputGetState");
    if (!g_getStateAddr) {
        log("input: XInputGetState not found");
        return false;
    }
    if (MH_CreateHook(g_getStateAddr, (void*)&hk_XInputGetState, (void**)&g_origGetState) != MH_OK ||
        MH_EnableHook(g_getStateAddr) != MH_OK) {
        log("input: cannot hook XInputGetState");
        return false;
    }
    g_getCapsAddr = (void*)GetProcAddress(x, "XInputGetCapabilities");
    if (g_getCapsAddr && (MH_CreateHook(g_getCapsAddr, (void*)&hk_XInputGetCapabilities, (void**)&g_origGetCaps) != MH_OK ||
                          MH_EnableHook(g_getCapsAddr) != MH_OK))
        log("input: cannot hook XInputGetCapabilities (pad 0 may not look connected)");
    log("input: XInputGetState hooked (virtual controller ready)");
    return true;
}

bool input_pad_active() { return g_padActive != 0; }

}  // namespace mb
