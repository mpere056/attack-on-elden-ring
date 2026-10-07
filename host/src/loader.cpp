// aoer_host.dll: the small, never-changing DLL that me3 loads into Elden Ring (a [[natives]]
// entry in me3/aoer.me3). It loads the actual bridge, aoer_core.dll from the same folder,
// from a private copy so the core can be rebuilt and hot-swapped while the game runs.
//
// Adapted from Minecraft-Ring's dinput8.dll proxy (see host/LICENSE-upstream.txt). Changes:
// no DirectInput exports (me3 loads us by path, nothing is put in the game folder), and no
// launcher environment marker. The anti-cheat check stays: if Easy Anti-Cheat is loaded in
// this process, the loader does nothing at all.
//
// Folders: this DLL lives in <project>\dist. Logs and runtime files go to <project>\runtime;
// the loader publishes that path to the core through the AOER_DIR environment variable of
// this process.
#include "common.h"
#include <string.h>
#include <ctype.h>
#include <stdio.h>
#include <tlhelp32.h>

typedef bool (*core_init_t)();
typedef void (*core_shutdown_t)();

static HMODULE g_self = nullptr;
static HMODULE g_core = nullptr;
static core_shutdown_t g_coreShutdown = nullptr;
static char g_distDir[MAX_PATH];

static bool is_game_process() {
    char exe[MAX_PATH];
    GetModuleFileNameA(nullptr, exe, MAX_PATH);
    for (char* p = exe; *p; ++p) *p = (char)tolower((unsigned char)*p);
    return strstr(exe, "eldenring.exe") != nullptr;
}

// Never run next to Easy Anti-Cheat. me3 starts the game without it; this is a backstop.
static bool anticheat_absent() {
    HANDLE snap = CreateToolhelp32Snapshot(TH32CS_SNAPMODULE, GetCurrentProcessId());
    if (snap == INVALID_HANDLE_VALUE) return true;
    MODULEENTRY32 me;
    me.dwSize = sizeof(me);
    bool eac = false;
    if (Module32First(snap, &me)) {
        do {
            char name[MAX_PATH];
            snprintf(name, sizeof(name), "%s", me.szModule);
            for (char* p = name; *p; ++p) *p = (char)tolower((unsigned char)*p);
            if (strstr(name, "easyanticheat") || strstr(name, "eosac")) eac = true;
        } while (!eac && Module32Next(snap, &me));
    }
    CloseHandle(snap);
    if (eac) mb::log("loader: Easy Anti-Cheat is loaded; refusing to run (offline launch only)");
    return !eac;
}

static bool game_window_ready() {
    struct Ctx {
        DWORD pid;
        bool found;
    } ctx = {GetCurrentProcessId(), false};
    EnumWindows(
        [](HWND hwnd, LPARAM lp) -> BOOL {
            Ctx* c = (Ctx*)lp;
            DWORD pid = 0;
            GetWindowThreadProcessId(hwnd, &pid);
            if (pid != c->pid || !IsWindowVisible(hwnd)) return TRUE;
            RECT r;
            GetClientRect(hwnd, &r);
            if (r.right - r.left >= 320 && r.bottom - r.top >= 200) {
                c->found = true;
                return FALSE;
            }
            return TRUE;
        },
        (LPARAM)&ctx);
    return ctx.found;
}

static void unload_core() {
    if (!g_core) return;
    if (g_coreShutdown) g_coreShutdown();
    FreeLibrary(g_core);
    g_core = nullptr;
    g_coreShutdown = nullptr;
    mb::shm_header()->coreStatus = 0;
    mb::log("loader: core unloaded");
}

static bool load_core() {
    ErmcHeader* h = mb::shm_header();
    uint32_t gen = h->coreGeneration + 1;
    char src[MAX_PATH], dir[MAX_PATH], dst[MAX_PATH];
    snprintf(src, sizeof(src), "%s\\aoer_core.dll", g_distDir);
    // Load a private copy so the build output can be overwritten while the game runs.
    snprintf(dir, sizeof(dir), "%s", mb::bridge_path("loaded"));
    CreateDirectoryA(dir, nullptr);
    snprintf(dst, sizeof(dst), "%s\\core_%lu_%u.dll", dir, GetCurrentProcessId(), gen);
    if (!CopyFileA(src, dst, FALSE)) {
        mb::log("loader: cannot copy %s (%lu)", src, GetLastError());
        h->coreStatus = -1;
        return false;
    }
    HMODULE m = LoadLibraryA(dst);
    if (!m) {
        mb::log("loader: LoadLibrary(%s) failed (%lu)", dst, GetLastError());
        h->coreStatus = -2;
        return false;
    }
    core_init_t init = (core_init_t)GetProcAddress(m, "erb_core_init");
    core_shutdown_t shutdown = (core_shutdown_t)GetProcAddress(m, "erb_core_shutdown");
    if (!init || !shutdown) {
        mb::log("loader: core is missing its entry points");
        FreeLibrary(m);
        h->coreStatus = -3;
        return false;
    }
    g_core = m;
    g_coreShutdown = shutdown;
    bool ok = init();
    h->coreGeneration = gen;
    h->coreStatus = ok ? 1 : -4;
    mb::log("loader: core generation %u loaded (%s)", gen, ok ? "ok" : "init reported errors");
    return ok;
}

// <project>\dist\aoer_host.dll -> g_distDir = <project>\dist, AOER_DIR = <project>\runtime
static void resolve_folders() {
    char self[MAX_PATH];
    GetModuleFileNameA(g_self, self, MAX_PATH);
    char* slash = strrchr(self, '\\');
    if (slash) *slash = 0;
    snprintf(g_distDir, sizeof(g_distDir), "%s", self);
    char runtime[MAX_PATH];
    snprintf(runtime, sizeof(runtime), "%s", self);
    slash = strrchr(runtime, '\\');
    if (slash) *slash = 0;
    strncat(runtime, "\\runtime", sizeof(runtime) - strlen(runtime) - 1);
    CreateDirectoryA(runtime, nullptr);
    SetEnvironmentVariableA("AOER_DIR", runtime);
}

static DWORD WINAPI loader_thread(LPVOID) {
    resolve_folders();
    mb::log("loader: attached to pid %lu (dist %s)", GetCurrentProcessId(), g_distDir);
    if (!anticheat_absent()) return 0;
    if (!mb::shm_open()) return 1;

    // The core hooks the renderer, so wait for the game's window and D3D12 to exist.
    uint64_t start = mb::now_ms();
    while (!(GetModuleHandleA("d3d12.dll") && GetModuleHandleA("dxgi.dll") && game_window_ready())) {
        Sleep(20);
        if (mb::now_ms() - start > 10 * 60 * 1000) {
            mb::log("loader: game window never appeared");
            return 1;
        }
    }
    Sleep(1500);  // let the game finish creating its device and swapchain

    ErmcHeader* h = mb::shm_header();
    h->coreReloadAck = h->coreReloadReq;
    load_core();
    for (;;) {
        Sleep(50);
        uint32_t req = h->coreReloadReq;
        if (req != h->coreReloadAck) {
            mb::log("loader: hot reload requested");
            unload_core();
            load_core();
            h->coreReloadAck = req;
        }
    }
    return 0;
}

BOOL WINAPI DllMain(HINSTANCE inst, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH) {
        g_self = inst;
        DisableThreadLibraryCalls(inst);
        if (is_game_process()) CreateThread(nullptr, 0, loader_thread, nullptr, 0, nullptr);
    }
    return TRUE;
}
