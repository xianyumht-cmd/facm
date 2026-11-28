/*
 * ggman_core.dll — in-client agent for the League of Legends LCU client.
 *
 * Injected into LeagueClientUxRender.exe. Unlike an external overlay it needs
 * nothing outside the client process: it finds the LCU port from its own command
 * line, reads the remoting token from the LeagueClientUx.exe command line (PEB),
 * drives the client over HTTPS/WinHTTP itself and paints an in-client HUD.
 *
 * Capabilities
 *   - self-service LCU session discovery (no lockfile, no external helper)
 *   - live gameflow state, summoner info, ready-check auto accept (armed by hotkey)
 *   - in-client HUD panel with state + action log
 *   - named-IPC (WM_COPYDATA) for text injection and remote control
 *
 * Hotkeys
 *   F8  HUD on/off
 *   F9  arm/disarm auto accept
 *   F10 accept ready check now
 *
 * IPC: window class "GGmanHudIPC"
 *   struct GGmanHudCommand { DWORD kind; DWORD flags; WCHAR text[2048]; }
 *   kind 1 text override, 2 visibility, 3 clear text,
 *   kind 5 set session (flags = port, text = token),
 *   kind 6 arm (flags 0/1/2=toggle), kind 7 accept now
 */

#define WIN32_LEAN_AND_MEAN
#define UNICODE
#define _UNICODE
#include <windows.h>
#include <winhttp.h>
#include <winsock2.h>
#include <ws2tcpip.h>
#include <tlhelp32.h>
#include <stdio.h>
#include <stdarg.h>

#define HUD_CLASS   L"GGmanHudOverlay"
#define IPC_CLASS   L"GGmanHudIPC"
#define HUD_W       600
#define HUD_H       372
#define HUD_MARGIN  18
#define MAX_LINES   6
#define LINE_LEN    220
#define TEXT_LEN    2048

#define COLOR_KEY   RGB(1, 2, 3)
/* League "hextech" palette so the overlay reads as part of the client */
#define COL_PANEL   RGB(1, 10, 19)      /* #010A13  deep navy */
#define COL_PANEL2  RGB(10, 20, 40)     /* #0A1428  panel body */
#define COL_BORDER  RGB(120, 90, 40)    /* #785A28  gold edge */
#define COL_GOLD    RGB(200, 170, 110)  /* #C8AA6E  gold text */
#define COL_TITLE   RGB(200, 170, 110)
#define COL_BODY    RGB(160, 155, 140)  /* #A09B8C  body text */
#define COL_DIM     RGB(91, 90, 86)     /* #5B5A56  muted */
#define COL_OK      RGB(10, 200, 185)   /* #0AC8B9  teal accent */
#define COL_WARN    RGB(230, 160, 70)
#define COL_BUTTON  RGB(30, 35, 40)     /* #1E2328 */
#define COL_BTNLINE RGB(60, 60, 65)     /* #3C3C41 */

#define ACCEPT_PATH L"/lol-matchmaking/v1/ready-check/accept"
#define PHASE_PATH  L"/lol-gameflow/v1/gameflow-phase"
#define SUMMONER_PATH L"/lol-summoner/v1/current-summoner"

typedef struct GGmanHudCommand {
    DWORD kind;
    DWORD flags;
    WCHAR text[TEXT_LEN];
} GGmanHudCommand;

/* ---------- state ---------- */
static HINSTANCE g_inst;
static HWND g_ipc;
static HWND g_hud;
static HWND g_host;
static HWND g_badge;
static HWINEVENTHOOK g_move_hook;
static DWORD g_hook_pid;
static DWORD g_last_move_tick;
static HFONT g_font_title;
static HFONT g_font_body;
static int g_visible = 1;
static DWORD g_start_tick;
static int g_hotkey_ok;
static WCHAR g_hotkey_name[32] = L"F8";
static char g_log_path[MAX_PATH];

static int g_port;
static char g_token[256];
static DWORD g_token_tick;
static WCHAR g_phase[32] = L"?";
static WCHAR g_name[64] = L"?";
static WCHAR g_avail[24] = L"?";
static WCHAR g_self_rank[48];
static int g_level;
static int g_armed;
static int g_accepted_for_phase;
static WCHAR g_last_action[96] = L"（无）";
static WCHAR g_override[MAX_LINES][LINE_LEN];
static int g_override_count;
static int g_pid_hud;
static int g_ban_next;
static int g_pending_action = -1;
static DWORD g_pending_at;
static int g_my_champ;
static int g_runes_applied_for;
static int g_spells_applied_for;

/* ---------- session intel: 10 players ---------- */
#define INTEL_MAX   10
#define INTEL_GAMES 20

typedef struct {
    int cell;
    int team;                 /* 1 = mine, 2 = theirs */
    int champion;
    char pos[16];
    char puuid[64];
    WCHAR name[48];
    WCHAR rank[28];
    int games, wins;
    int streak;               /* >0 win streak, <0 loss streak */
    int top[3];
    int topn[3];
    long long game_ids[INTEL_GAMES];   /* gameId is ~7e9, must be 64-bit */
    int game_id_n;
    int duo_score;      /* shared games with someone on the same team */
    int known;          /* in the local watch list */
    int known_count;
} IntelEntry;

static IntelEntry g_intel[INTEL_MAX];
static int g_intel_n;
static int g_intel_done;
static int g_intel_next;
static char g_my_pos[16];
static int g_quit_advice;
static int g_quit_sent;
static WCHAR g_quit_reason[80];
static int g_bench_done;
static int g_bench_pick;
static int g_bench_points;
static int g_panel_x = -99999, g_panel_y = -99999, g_panel_vis = -1;
static int g_badge_x = -99999, g_badge_y = -99999, g_badge_vis = -1;
static int g_dirty = 1;

/* ---------- config (reloaded every 10 s) ---------- */
typedef struct {
    int hud;
    int auto_accept;
    int auto_pick; int pick_champ;
    int auto_ban;  int ban_champ;
    int auto_honor;
    int auto_play_again;
    int auto_runes;
    char rune_page[64];
    int auto_spells;
    int spell1;
    int spell2;
    int agent_enabled;  /* master switch; 0 = do not create any window */
    int button;     /* show the launcher badge */
    int btn_dx;     /* -1 = auto anchor, else px from client left */
    int btn_dy;
    int mock_http;      /* testing: plain HTTP instead of TLS */
    int mock_port;      /* testing: override LCU port */
    char mock_token[128];
    /* --- batch 1 --- */
    int pick_by_pos;            /* 1 = choose champion by assigned position */
    int pos_champ[5];           /* top, jungle, middle, bottom, utility */
    char ban_list[128];         /* "157,99,238" */
    int ban_ids[8];
    int ban_count;
    int lockin_delay_s;         /* 0..15 seconds before locking in */
    char rune_map[256];         /* "103:法师页,22:辅助页" */
    char spell_map[256];        /* "103:4/14,22:3/4" */
    int rune_map_n; int rune_map_champ[8]; char rune_map_page[8][48];
    int spell_map_n; int spell_map_champ[8]; int spell_map_s1[8]; int spell_map_s2[8];
    /* --- batch B: dodge + swap --- */
    int quit_mode;          /* 0 off, 1 suggest only, 2 auto dodge */
    int quit_enemy_wr;      /* dodge if enemy avg winrate >= N% (0 = ignore) */
    int quit_ally_loss;     /* dodge if an ally is on >= N loss streak (0 = ignore) */
    int swap_policy;        /* 0 off, 1 accept all, 2 decline all */
    int auto_bench_swap;    /* ARAM: swap to the bench champion you have most mastery on */
    int bench_min_points;   /* skip when the best bench champion is below this mastery */
    int auto_ui_by_phase;   /* 1 = switch tab / hide panel according to gameflow phase */
    int http_port;          /* loopback console/API port, 0 = off */
    int ui_compact;         /* 1 = small card, 0 = full panel */
    int ui_dx;              /* offset from client client-area, -1 = auto */
    int ui_dy;
    int start_hidden;       /* 1 = only the badge shows until you click it */
} GgConfig;

static GgConfig g_cfg;
static char g_ini_path[MAX_PATH];

/* forward decls (defined further down) */
static void config_load(void);
static void config_save(void);
static void agent_shutdown(void);
static void reposition(void);
static void badge_reposition(void);
static BOOL anchor_rect(RECT *out, BOOL *is_fallback);
static void install_move_hook(void);
static int known_touch(const char *puuid, const char *name, const char *note);
static void bench_swap_step(const char *session, BOOL manual);
static void invite_online_friends(int max_invites);
static void invite_recent_teammates(int max_invites);
static void mute_all_probe(void);
static void badge_reposition(void);
static void champ_select_step(void);
static void honor_step(void);
static void reposition(void);
static void badge_reposition(void);


static void reposition(void);
static void config_save(void);
static void agent_shutdown(void);
static void reposition(void);
static void badge_reposition(void);
static void honor_step_once(void);
static void runes_step(void);
static void spells_step(void);
static int find_page_id(const char *json, const char *page_name);
static void allies_step(const char *session_json);
static void play_again_step(void);
static int champ_quit_route(WCHAR *path, DWORD cch);
static int cs_prefix_probe(WCHAR *out, DWORD cch);
static void cs_prefix_refresh(void);
static int g_cs_active;
static int g_cs_cell = -1;
static int g_ally_count;
static int g_ally_done;
static WCHAR g_ally[5][96];

static void logf_(const char *fmt, ...)
{
    if (!g_log_path[0]) return;
    char buf[1024];
    va_list ap;
    va_start(ap, fmt);
    int n = wvsprintfA(buf, fmt, ap);
    va_end(ap);
    HANDLE f = CreateFileA(g_log_path, FILE_APPEND_DATA, FILE_SHARE_READ, NULL,
                           OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
    if (f == INVALID_HANDLE_VALUE) return;
    SetFilePointer(f, 0, NULL, FILE_END);
    DWORD wrote = 0;
    WriteFile(f, buf, (DWORD)n, &wrote, NULL);
    WriteFile(f, "\r\n", 2, &wrote, NULL);
    CloseHandle(f);
}

static void set_action(const WCHAR *fmt, ...)
{
    va_list ap;
    va_start(ap, fmt);
    wvsprintfW(g_last_action, fmt, ap);
    va_end(ap);
    WCHAR ts[16];
    SYSTEMTIME st;
    GetLocalTime(&st);
    wsprintfW(ts, L"%02d:%02d:%02d ", st.wHour, st.wMinute, st.wSecond);
    static WCHAR tmp[96];
    lstrcpynW(tmp, ts, 96);
    lstrcpynW(tmp + 9, g_last_action, 80);
    lstrcpynW(g_last_action, tmp, 96);
    logf_("action: %ls", g_last_action);
    g_dirty = 1;
    if (g_hud) InvalidateRect(g_hud, NULL, FALSE);
}

/* ---------- event-driven follow (client window moves/resizes) ---------- */
static volatile DWORD g_move_until = 0;   /* keep a fast follow timer alive until this tick */

static void CALLBACK move_event_proc(HWINEVENTHOOK hook, DWORD ev, HWND hwnd, LONG idObject,
                                     LONG idChild, DWORD thread, DWORD ms)
{
    (void)hook; (void)idChild; (void)thread; (void)ms;
    if (ev != EVENT_OBJECT_LOCATIONCHANGE) return;
    if (idObject != OBJID_WINDOW) return;
    if (hwnd != g_host) return;
    reposition();
    badge_reposition();
    /* keep a 60fps catch-up timer alive for 400ms so the panel tracks the drag smoothly */
    g_move_until = GetTickCount() + 400;
    if (g_ipc) SetTimer(g_ipc, 3, 16, NULL);
}

static void install_move_hook(void)
{
    DWORD pid = 0;
    if (!g_host || !IsWindow(g_host)) return;
    GetWindowThreadProcessId(g_host, &pid);
    if (g_move_hook && pid == g_hook_pid) return;
    if (g_move_hook) { UnhookWinEvent(g_move_hook); g_move_hook = NULL; }
    g_hook_pid = pid;
    g_move_hook = SetWinEventHook(EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE,
                                  NULL, move_event_proc, pid, 0,
                                  WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
    logf_("move hook installed for pid %lu -> %p", (unsigned long)pid, (void *)g_move_hook);
}

/* ---------- PEB command line reader (same user, same session) ---------- */
typedef LONG (NTAPI *PFN_NtQueryInformationProcess)(HANDLE, ULONG, PVOID, ULONG, PULONG);

static BOOL read_remote_command_line(DWORD pid, WCHAR *out, DWORD cch)
{
    HANDLE p = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_VM_READ, FALSE, pid);
    if (!p) { logf_("peb: OpenProcess(%lu) failed %lu", (unsigned long)pid, (unsigned long)GetLastError()); return FALSE; }

    HMODULE nt = GetModuleHandleW(L"ntdll.dll");
    PFN_NtQueryInformationProcess query =
        (PFN_NtQueryInformationProcess)(void *)GetProcAddress(nt, "NtQueryInformationProcess");
    struct { PVOID Reserved1; PVOID PebBaseAddress; PVOID Reserved2[2]; ULONG_PTR UniqueProcessId; PVOID Reserved3; } pbi;
    ULONG len = 0;
    BOOL ok = FALSE;
    long qst = query ? query(p, 0 /*ProcessBasicInformation*/, &pbi, sizeof(pbi), &len) : -1;
    logf_("peb: query=%s status=0x%lX peb=0x%p", query ? "ok" : "missing", (unsigned long)qst, pbi.PebBaseAddress);
    if (query && qst >= 0 && pbi.PebBaseAddress) {
        BYTE *peb = (BYTE *)pbi.PebBaseAddress;
        PVOID params = NULL;
        SIZE_T got = 0;
        /* PEB->ProcessParameters at 0x20 on x64 */
        BOOL r1 = ReadProcessMemory(p, peb + 0x20, &params, sizeof(params), &got);
        logf_("peb: params read=%d ptr=0x%p err=%lu", r1, params, (unsigned long)GetLastError());
        if (r1 && params) {
            /* RTL_USER_PROCESS_PARAMETERS->CommandLine at 0x70 (UNICODE_STRING) */
            USHORT slen = 0, smax = 0;
            PVOID buf = NULL;
            BYTE *base = (BYTE *)params + 0x70;
            logf_("peb: reading UNICODE_STRING at 0x%p", base);
            if (ReadProcessMemory(p, base, &slen, sizeof(slen), &got) &&
                ReadProcessMemory(p, base + 2, &smax, sizeof(smax), &got) &&
                ReadProcessMemory(p, base + 8, &buf, sizeof(buf), &got) && buf && slen > 0) {
                DWORD bytes = slen < (cch - 1) * 2 ? slen : (cch - 1) * 2;
                logf_("peb: cmdline len=%u buf=0x%p bytes=%lu", (unsigned)slen, buf, (unsigned long)bytes);
                if (ReadProcessMemory(p, buf, out, bytes, &got)) {
                    out[bytes / 2] = 0;
                    ok = TRUE;
                }
            }
        }
    }
    CloseHandle(p);
    return ok;
}

static BOOL extract_arg(const WCHAR *cmdline, const WCHAR *key, WCHAR *out, DWORD cch)
{
    if (!cmdline || !key || !out || !cch) return FALSE;
    size_t klen = lstrlenW(key);
    const WCHAR *p = cmdline;
    while (p && *p) {
        const WCHAR *hit = wcsstr(p, key);
        if (!hit) return FALSE;
        const WCHAR *v = hit + klen;
        if (*v == L'=') v++;
        else if (*v == L'"') { v++; if (*v == L'=') v++; }
        DWORD i = 0;
        while (*v && *v != L' ' && *v != L'"' && i < cch - 1) out[i++] = *v++;
        out[i] = 0;
        if (i > 0) return TRUE;
        p = hit + 1;   /* keep scanning: quoted args have no space before the value */
    }
    return FALSE;
}

static DWORD find_pid_by_name(const WCHAR *name)
{
    HANDLE snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
    if (snap == INVALID_HANDLE_VALUE) return 0;
    PROCESSENTRY32W pe;
    pe.dwSize = sizeof(pe);
    DWORD pid = 0;
    if (Process32FirstW(snap, &pe)) {
        do {
            if (lstrcmpiW(pe.szExeFile, name) == 0) { pid = pe.th32ProcessID; break; }
        } while (Process32NextW(snap, &pe));
    }
    CloseHandle(snap);
    return pid;
}

/* ---------- LCU session discovery ---------- */
static BOOL lcu_discover(void)
{
    WCHAR cmd[4096];
    WCHAR val[256];

    /* offline harness: agent.ini can pin the session */
    if (g_cfg.mock_port > 0) {
        g_port = g_cfg.mock_port;
        if (!g_token[0] && g_cfg.mock_token[0])
            lstrcpynA(g_token, g_cfg.mock_token, sizeof(g_token));
        return g_token[0] != 0;
    }

    if (!g_port) {
        lstrcpynW(cmd, GetCommandLineW(), 4096);
        if (extract_arg(cmd, L"--app-port", val, 256)) g_port = _wtoi(val);
    }
    if (!g_port) {
        DWORD pid = find_pid_by_name(L"LeagueClientUx.exe");
        if (pid && read_remote_command_line(pid, cmd, 4096) && extract_arg(cmd, L"--app-port", val, 256))
            g_port = _wtoi(val);
    }
    if (!g_token[0]) {
        DWORD pid = find_pid_by_name(L"LeagueClientUx.exe");
        logf_("discover: ux pid=%lu", (unsigned long)pid);
        if (pid && read_remote_command_line(pid, cmd, 4096)) {
            WCHAR tok[256];
            const WCHAR *hit = wcsstr(cmd, L"--remoting-auth-token");
            if (hit) {
                WCHAR sample[48];
                lstrcpynW(sample, hit, 44);
                logf_("discover: marker sample=%ls", sample);
            } else {
                logf_("discover: marker not found");
            }
            if (extract_arg(cmd, L"--remoting-auth-token", tok, 256)) {
                WideCharToMultiByte(CP_ACP, 0, tok, -1, g_token, sizeof(g_token), NULL, NULL);
                g_token_tick = GetTickCount();
                logf_("token acquired from Ux command line (len=%d)", (int)lstrlenW(tok));
            }
        }
    }
    return g_port > 0 && g_token[0];
}

/* ---------- WinHTTP ---------- */
static char *b64(const char *in)
{
    static const char *t = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
    static char out[512];
    int n = (int)lstrlenA(in), i, o = 0;
    for (i = 0; i + 2 < n; i += 3) {
        out[o++] = t[(in[i] >> 2) & 0x3F];
        out[o++] = t[((in[i] & 3) << 4) | ((in[i + 1] >> 4) & 0xF)];
        out[o++] = t[((in[i + 1] & 0xF) << 2) | ((in[i + 2] >> 6) & 3)];
        out[o++] = t[in[i + 2] & 0x3F];
    }
    if (i < n) {
        out[o++] = t[(in[i] >> 2) & 0x3F];
        if (i + 1 < n) {
            out[o++] = t[((in[i] & 3) << 4) | ((in[i + 1] >> 4) & 0xF)];
            out[o++] = t[((in[i + 1] & 0xF) << 2)];
        } else {
            out[o++] = t[((in[i] & 3) << 4)];
            out[o++] = '=';
        }
    }
    while (o & 3) out[o++] = '=';
    out[o] = 0;
    return out;
}

static int g_cs_use_tb = -1;   /* -1 未知 / 0 旧前缀 / 1 team-builder（PvP 选人） */

static int lcu_http(const WCHAR *verb, const WCHAR *path, const char *body, char *resp, DWORD resp_cch)
{
    int status = 0;
    if (!lcu_discover()) return -1;

    HINTERNET ses = WinHttpOpen(L"GGman/1.0", WINHTTP_ACCESS_TYPE_NO_PROXY,
                                WINHTTP_NO_PROXY_NAME, WINHTTP_NO_PROXY_BYPASS, 0);
    if (!ses) return -1;
    WinHttpSetTimeouts(ses, 800, 800, 1500, 1500);

    HINTERNET con = WinHttpConnect(ses, L"127.0.0.1", (INTERNET_PORT)(g_cfg.mock_port ? g_cfg.mock_port : g_port), 0);
    if (!con) { WinHttpCloseHandle(ses); return -1; }

    /* PvP 选人走 lol-lobby-team-builder 插件，旧前缀在该场景没有 delegate：
       只把 /lol-champ-select/v1/session* 重写成 team-builder 的等价路径。
       toggle-player-muted / muted-players 只有旧前缀有路由（2026-11-28 实测），
       所以重写范围必须收窄，否则静音会被转成 404。 */
    WCHAR rewritten[256];
    const WCHAR *use_path = path;
    if (g_cs_use_tb == 1 && wcsncmp(path, L"/lol-champ-select/v1/session", 28) == 0) {
        wsprintfW(rewritten, L"/lol-lobby-team-builder/champ-select/v1/session%s", path + 28);
        use_path = rewritten;
    }

    HINTERNET req = WinHttpOpenRequest(con, verb, use_path, NULL, WINHTTP_NO_REFERER,
                                       WINHTTP_DEFAULT_ACCEPT_TYPES,
                                       g_cfg.mock_http ? 0 : WINHTTP_FLAG_SECURE);
    if (!req) { WinHttpCloseHandle(con); WinHttpCloseHandle(ses); return -1; }

    DWORD sec = SECURITY_FLAG_IGNORE_UNKNOWN_CA | SECURITY_FLAG_IGNORE_CERT_CN_INVALID |
                SECURITY_FLAG_IGNORE_CERT_DATE_INVALID;
    WinHttpSetOption(req, WINHTTP_OPTION_SECURITY_FLAGS, &sec, sizeof(sec));

    char auth[512];
    char plain[300];
    wsprintfA(plain, "riot:%s", g_cfg.mock_token[0] ? g_cfg.mock_token : g_token);
    wsprintfA(auth, "Authorization: Basic %s", b64(plain));
    WCHAR wauth[512];
    MultiByteToWideChar(CP_ACP, 0, auth, -1, wauth, 512);
    WinHttpAddRequestHeaders(req, wauth, (DWORD)-1L,
                             WINHTTP_ADDREQ_FLAG_ADD | WINHTTP_ADDREQ_FLAG_REPLACE);

    BOOL sent;
    if (body) {
        WCHAR hdr[] = L"Content-Type: application/json";
        WinHttpAddRequestHeaders(req, hdr, (DWORD)-1L, WINHTTP_ADDREQ_FLAG_ADD);
        sent = WinHttpSendRequest(req, WINHTTP_NO_ADDITIONAL_HEADERS, 0,
                                  (LPVOID)body, (DWORD)lstrlenA(body), (DWORD)lstrlenA(body), 0);
    } else {
        sent = WinHttpSendRequest(req, WINHTTP_NO_ADDITIONAL_HEADERS, 0, NULL, 0, 0, 0);
    }
    if (sent && WinHttpReceiveResponse(req, NULL)) {
        DWORD code = 0, sz = sizeof(code);
        WinHttpQueryHeaders(req, WINHTTP_QUERY_STATUS_CODE | WINHTTP_QUERY_FLAG_NUMBER,
                            WINHTTP_HEADER_NAME_BY_INDEX, &code, &sz, WINHTTP_NO_HEADER_INDEX);
        status = (int)code;
        if (resp && resp_cch) {
            DWORD total = 0;
            for (;;) {
                DWORD avail = 0;
                if (!WinHttpQueryDataAvailable(req, &avail) || avail == 0) break;
                if (total + avail > resp_cch - 1) avail = resp_cch - 1 - total;
                if (!avail) break;
                DWORD got = 0;
                if (!WinHttpReadData(req, resp + total, avail, &got) || got == 0) break;
                total += got;
                if (total >= resp_cch - 1) break;
            }
            resp[total] = 0;
        }
    }
    WinHttpCloseHandle(req);
    WinHttpCloseHandle(con);
    WinHttpCloseHandle(ses);
    return status;
}

/* json helpers (single-value lookups are enough here) */
static BOOL json_str(const char *json, const char *key, WCHAR *out, DWORD cch)
{
    if (!json || !key || !out || !cch) return FALSE;
    char pat[64];
    wsprintfA(pat, "\"%s\"", key);
    const char *p = strstr(json, pat);
    if (!p) return FALSE;
    p += lstrlenA(pat);
    while (*p == ' ' || *p == '\t') p++;
    if (*p != ':') return FALSE;
    p++;
    while (*p == ' ' || *p == '\t') p++;
    if (*p != '"') return FALSE;
    p++;
    int i = 0;
    char raw[256];
    while (*p && *p != '"' && i < 255) raw[i++] = *p++;
    raw[i] = 0;
    MultiByteToWideChar(CP_UTF8, 0, raw, -1, out, cch);
    return TRUE;
}

static int json_int(const char *json, const char *key)
{
    char pat[64];
    wsprintfA(pat, "\"%s\":", key);
    const char *p = strstr(json, pat);
    if (!p) return 0;
    return atoi(p + lstrlenA(pat));
}

/* ---------- HUD ---------- */
static void init_log_path(void)
{
    lstrcpynA(g_log_path, "D:\\project2\\ggman\\dll\\build\\core.log", MAX_PATH);
}

static BOOL CALLBACK find_host_proc(HWND h, LPARAM lp)
{
    WCHAR cls[128];
    if (!IsWindowVisible(h)) return TRUE;
    GetClassNameW(h, cls, 128);
    if (lstrcmpiW(cls, L"RCLIENT") == 0) {
        RECT rc;
        GetWindowRect(h, &rc);
        if (rc.right - rc.left > 600) { *(HWND *)lp = h; return FALSE; }
    }
    return TRUE;
}

static HWND find_host(void)
{
    HWND found = NULL;
    EnumWindows(find_host_proc, (LPARAM)&found);
    return found;
}

/* ---------- panel: tabs + rows ---------- */
#define TAB_COUNT 4
#define ROW_H     24
#define CONTENT_Y 66

static RECT g_tab_rect[TAB_COUNT];
static RECT g_close_rect;
static int g_hud_w = HUD_W, g_hud_h = HUD_H;
static int g_queue_secs = -1;
static int g_cs_left = -1;
static volatile DWORD g_last_tick_done = 0;   /* watchdog: last completed refresh */

#define COMPACT_W 520
#define COMPACT_H 36
static int g_compact = 1;
static RECT g_expand_rect;
static RECT g_title_rect;
static int g_panel_drag, g_panel_moved;
static POINT g_panel_click, g_panel_origin;

static int panel_w(void) { return g_compact ? COMPACT_W : HUD_W; }
static int panel_h(void) { return g_compact ? COMPACT_H : HUD_H; }
static void toggle_compact(void);
#define LAY_TAB_Y   36
#define LAY_TAB_H   24
#define LAY_CONTENT 66
#define LAY_ROW_H   24
#define ROW_MAX 16
static RECT g_row_rect[ROW_MAX];
static int  g_row_kind[ROW_MAX];   /* 0 info, 1 toggle, 2 action */
static int  g_row_id[ROW_MAX];     /* config slot or action id */
static int  g_row_count;
static int  g_tab;

static const WCHAR *TAB_NAMES[TAB_COUNT] = { L"状态", L"自动化", L"情报", L"工具" };

static void layout_panel(int w, int h)
{
    int tw = (w - 32) / TAB_COUNT;
    for (int i = 0; i < TAB_COUNT; i++) {
        g_tab_rect[i].left = 16 + i * tw;
        g_tab_rect[i].right = g_tab_rect[i].left + tw - 6;
        g_tab_rect[i].top = 36;
        g_tab_rect[i].bottom = 60;
    }
    g_close_rect.left = w - 34;
    g_close_rect.right = w - 12;
    g_close_rect.top = 6;
    g_close_rect.bottom = 28;
    (void)h;
}

static void draw_close_button(HDC mem, int w)
{
    RECT r = g_close_rect;
    HBRUSH b = CreateSolidBrush(RGB(30, 22, 26));
    HPEN p = CreatePen(PS_SOLID, 1, RGB(96, 60, 66));
    HGDIOBJ ob = SelectObject(mem, b), op = SelectObject(mem, p);
    RoundRect(mem, r.left, r.top, r.right, r.bottom, 6, 6);
    SelectObject(mem, ob); SelectObject(mem, op);
    DeleteObject(b); DeleteObject(p);
    SetTextColor(mem, RGB(230, 150, 150));
    HGDIOBJ of = SelectObject(mem, g_font_body);
    DrawTextW(mem, L"×", -1, &r, DT_CENTER | DT_SINGLELINE | DT_VCENTER | DT_NOPREFIX);
    SelectObject(mem, of);
    (void)w;
}

static const WCHAR *cfg_value_w(int slot, WCHAR *buf, int cch)
{
    switch (slot) {
    case 0: return g_armed ? L"开" : L"关";
    case 1: lstrcpynW(buf, g_cfg.auto_pick ? (g_cfg.pick_champ ? L"开" : L"开(未设)") : L"关", cch); return buf;
    case 2: lstrcpynW(buf, g_cfg.auto_ban ? (g_cfg.ban_champ ? L"开" : L"开(未设)") : L"关", cch); return buf;
    case 3: return g_cfg.auto_runes ? (g_cfg.rune_page[0] ? L"开" : L"开(未设页)") : L"关";
    case 4: return g_cfg.auto_spells ? L"开" : L"关";
    case 5: return g_cfg.auto_honor ? L"开" : L"关";
    case 6: return g_cfg.auto_play_again ? L"开" : L"关";
    }
    return L"?";
}

static void toggle_slot(int slot)
{
    switch (slot) {
    case 0: g_armed = !g_armed; set_action(L"自动接受 %ls", g_armed ? L"已开启" : L"已关闭"); break;
    case 1: g_cfg.auto_pick = !g_cfg.auto_pick; set_action(L"自动选人 %ls", g_cfg.auto_pick ? L"开" : L"关"); break;
    case 2: g_cfg.auto_ban = !g_cfg.auto_ban; set_action(L"自动禁人 %ls", g_cfg.auto_ban ? L"开" : L"关"); break;
    case 3: g_cfg.auto_runes = !g_cfg.auto_runes; set_action(L"自动符文 %ls", g_cfg.auto_runes ? L"开" : L"关"); break;
    case 4: g_cfg.auto_spells = !g_cfg.auto_spells; set_action(L"自动技能 %ls", g_cfg.auto_spells ? L"开" : L"关"); break;
    case 5: g_cfg.auto_honor = !g_cfg.auto_honor; set_action(L"自动点赞 %ls", g_cfg.auto_honor ? L"开" : L"关"); break;
    case 6: g_cfg.auto_play_again = !g_cfg.auto_play_again; set_action(L"自动再来一局 %ls", g_cfg.auto_play_again ? L"开" : L"关"); break;
    }
    config_save();
}

/* single source of truth for every clickable rect (draw + hit test use this) */
static void compute_layout(int w, int h)
{
    if (g_compact) {
        g_close_rect.left = w - 24;
        g_close_rect.top = 6;
        g_close_rect.right = w - 4;
        g_close_rect.bottom = 30;
        g_expand_rect.left = w - 46;
        g_expand_rect.top = 6;
        g_expand_rect.right = w - 26;
        g_expand_rect.bottom = 30;
        g_title_rect = (RECT){ 0, 0, w - 48, 34 };
        g_row_rect[0] = (RECT){ 4, 2, w - 50, 34 };
        g_row_kind[0] = 0;
        g_row_id[0] = -1;
        g_row_count = 1;
        for (int i = 0; i < TAB_COUNT; i++) g_tab_rect[i] = (RECT){ 0, 0, 0, 0 };
        return;
    }
    int tw = (w - 32) / TAB_COUNT;
    for (int i = 0; i < TAB_COUNT; i++) {
        g_tab_rect[i].left = 16 + i * tw;
        g_tab_rect[i].top = LAY_TAB_Y;
        g_tab_rect[i].right = 16 + i * tw + tw - 6;
        g_tab_rect[i].bottom = LAY_TAB_Y + LAY_TAB_H;
    }
    g_close_rect.left = w - 30;
    g_close_rect.top = 6;
    g_close_rect.right = w - 8;
    g_close_rect.bottom = 28;
    g_expand_rect.left = w - 56;
    g_expand_rect.top = 6;
    g_expand_rect.right = w - 34;
    g_expand_rect.bottom = 28;
    g_title_rect = (RECT){ 0, 0, w, 32 };
    (void)h;

    int y = LAY_CONTENT;
    int n = 0;
    if (g_tab == 0) {
        for (int i = 0; i < 5 && n < ROW_MAX; i++, n++) {
            g_row_rect[n] = (RECT){ 16, y, w - 16, y + LAY_ROW_H };
            g_row_kind[n] = 0;
            g_row_id[n] = -1;
            y += LAY_ROW_H;
        }
    } else if (g_tab == 1) {
        for (int i = 0; i < 7 && n < ROW_MAX; i++, n++) {
            g_row_rect[n] = (RECT){ 16, y, w - 16, y + LAY_ROW_H };
            g_row_kind[n] = 1;
            g_row_id[n] = i;
            y += LAY_ROW_H;
        }
    } else if (g_tab == 2) {
        g_row_rect[n] = (RECT){ 16, y, w - 16, y + 20 };
        g_row_kind[n] = 0;
        g_row_id[n] = -1;
        y += 22;
        n++;
        int list = (g_intel_n == 0) ? 1 : (g_intel_n > 9 ? 9 : g_intel_n);
        for (int i = 0; i < list && n < ROW_MAX; i++, n++) {
            g_row_rect[n] = (RECT){ 16, y, w - 16, y + 22 };
            g_row_kind[n] = 0;
            g_row_id[n] = -1;
            y += 22;
        }
    } else {
        for (int i = 0; i < 11 && n < ROW_MAX; i++, n++) {
            g_row_rect[n] = (RECT){ 16, y, w - 16, y + LAY_ROW_H + 2 };
            g_row_kind[n] = 2;
            g_row_id[n] = i;
            y += LAY_ROW_H;
        }
    }
    g_row_count = n;
}

static void draw_panel(HWND hwnd)
{
    PAINTSTRUCT ps;
    HDC dc = BeginPaint(hwnd, &ps);
    if (!dc) return;
    RECT rc;
    GetClientRect(hwnd, &rc);
    int w = rc.right, h = rc.bottom;
    compute_layout(w, h);

    HDC mem = CreateCompatibleDC(dc);
    HBITMAP bmp = CreateCompatibleBitmap(dc, w, h);
    HGDIOBJ old_bmp = SelectObject(mem, bmp);
    HBRUSH key = CreateSolidBrush(COLOR_KEY);
    FillRect(mem, &rc, key);
    DeleteObject(key);

    {
        /* body: vertical two-tone like the client's panels */
        RECT top = { 2, 2, w - 2, (h * 2) / 3 };
        RECT bot = { 2, (h * 2) / 3, w - 2, h - 2 };
        HBRUSH b1 = CreateSolidBrush(COL_PANEL2), b2 = CreateSolidBrush(COL_PANEL);
        FillRect(mem, &top, b1);
        FillRect(mem, &bot, b2);
        DeleteObject(b1); DeleteObject(b2);
        HBRUSH border_b = CreateSolidBrush(COL_BORDER);
        FrameRect(mem, &rc, border_b);
        DeleteObject(border_b);
        /* inner hairline for the bevel feel */
        HPEN hi = CreatePen(PS_SOLID, 1, RGB(30, 40, 55));
        HGDIOBJ oh = SelectObject(mem, hi);
        MoveToEx(mem, 2, 2, NULL);
        LineTo(mem, w - 2, 2);
        SelectObject(mem, oh);
        DeleteObject(hi);
    }
    SetBkMode(mem, TRANSPARENT);

    /* title bar */
    HGDIOBJ of = SelectObject(mem, g_font_title);
    SetTextColor(mem, COL_TITLE);
    RECT tr = { 16, 8, w - 46, 30 };
    DrawTextW(mem, L"GGMAN", -1, &tr, DT_LEFT | DT_SINGLELINE | DT_VCENTER | DT_NOPREFIX);

    WCHAR right[160];
    wsprintfW(right, L"pid %lu   port %d   %ls", (unsigned long)g_pid_hud, g_port, g_hotkey_name);
    SetTextColor(mem, COL_DIM);
    DrawTextW(mem, right, -1, &tr, DT_RIGHT | DT_SINGLELINE | DT_VCENTER | DT_NOPREFIX);

    /* tabs */
    layout_panel(w, h);
    SelectObject(mem, g_font_body);
    for (int i = 0; i < TAB_COUNT; i++) {
        BOOL active = (i == g_tab);
        /* client-style nav: text only, gold underline when active */
        {
            RECT ul = { g_tab_rect[i].left + 10, g_tab_rect[i].bottom - 2,
                        g_tab_rect[i].right - 10, g_tab_rect[i].bottom };
            HBRUSH ub = CreateSolidBrush(active ? COL_GOLD : RGB(30, 40, 52));
            FillRect(mem, &ul, ub);
            DeleteObject(ub);
        }
        SetTextColor(mem, active ? COL_TITLE : COL_DIM);
        RECT trr = g_tab_rect[i];
        DrawTextW(mem, TAB_NAMES[i], -1, &trr, DT_CENTER | DT_SINGLELINE | DT_VCENTER | DT_NOPREFIX);
    }
    draw_close_button(mem, w);
    if (!g_compact) {
        SetTextColor(mem, COL_DIM);
        RECT xr = g_expand_rect;
        DrawTextW(mem, L"\x25A3", -1, &xr, DT_CENTER | DT_SINGLELINE | DT_VCENTER | DT_NOPREFIX);
    }

    g_row_count = 0;
    int y = CONTENT_Y;
    WCHAR line[LINE_LEN];
    RECT lr;

    if (g_tab == 0) {
        /* ---------- 状态 ---------- */
        WCHAR b0[LINE_LEN], b1[LINE_LEN], b2[LINE_LEN], b3[LINE_LEN], b4[LINE_LEN];
        wsprintfW(b0, L"召唤师  %ls   Lv.%d   状态 %ls", g_name, g_level, g_avail);
        if (g_cs_left >= 0)
            wsprintfW(b1, L"阶段 %ls   选人格 %d   剩余 %d 秒", g_phase, g_cs_cell, g_cs_left);
        else if (g_queue_secs >= 0)
            wsprintfW(b1, L"阶段 %ls   已排队 %d:%02d", g_phase, g_queue_secs / 60, g_queue_secs % 60);
        else
            wsprintfW(b1, L"阶段  %ls    选人格 %d   %ls", g_phase, g_cs_cell, g_cs_active ? L"选人中" : L"空闲");
        wsprintfW(b2, L"我的段位  %ls", g_self_rank[0] ? g_self_rank : L"未获取");
        wsprintfW(b3, L"最近动作  %ls", g_last_action);
        wsprintfW(b4, L"情报  %d/10 人已抓取   %ls", g_intel_n,
                  g_intel_done ? L"完成" : (g_intel_n ? L"抓取中…" : L"选人时自动开始"));
        const WCHAR *items[5] = { b0, b1, b2, b3, b4 };
        for (int i = 0; i < 5; i++) {
            g_row_rect[i] = (RECT){ 16, y, w - 16, y + ROW_H };
            SetTextColor(mem, i == 4 ? COL_DIM : COL_BODY);
            DrawTextW(mem, items[i], -1, &g_row_rect[i],
                      DT_LEFT | DT_SINGLELINE | DT_VCENTER | DT_NOPREFIX | DT_END_ELLIPSIS);
            g_row_kind[i] = 0;
            g_row_id[i] = -1;
            y += ROW_H;
        }
        g_row_count = 5;
    } else if (g_tab == 1) {
        /* ---------- 自动化 ---------- */
        static const WCHAR *labels[7] = {
            L"自动接受对局", L"自动选人", L"自动禁人", L"自动符文页",
            L"自动召唤师技能", L"自动点赞队友", L"结算后再来一局"
        };
        for (int i = 0; i < 7; i++) {
            BOOL on = FALSE;
            switch (i) {
            case 0: on = g_armed != 0; break;
            case 1: on = g_cfg.auto_pick != 0; break;
            case 2: on = g_cfg.auto_ban != 0; break;
            case 3: on = g_cfg.auto_runes != 0; break;
            case 4: on = g_cfg.auto_spells != 0; break;
            case 5: on = g_cfg.auto_honor != 0; break;
            case 6: on = g_cfg.auto_play_again != 0; break;
            }
            g_row_rect[i] = (RECT){ 16, y, w - 16, y + ROW_H };
            RECT rr = g_row_rect[i];
            SetTextColor(mem, on ? COL_BODY : COL_DIM);
            DrawTextW(mem, labels[i], -1, &rr, DT_LEFT | DT_SINGLELINE | DT_VCENTER | DT_NOPREFIX);
            RECT sr = { w - 92, y + 4, w - 20, y + ROW_H - 4 };
            HBRUSH sb = CreateSolidBrush(on ? RGB(14, 42, 44) : COL_BUTTON);
            HPEN sp = CreatePen(PS_SOLID, 1, on ? COL_OK : COL_BTNLINE);
            HGDIOBJ ob3 = SelectObject(mem, sb), op3 = SelectObject(mem, sp);
            RoundRect(mem, sr.left, sr.top, sr.right, sr.bottom, 10, 10);
            SelectObject(mem, ob3); SelectObject(mem, op3);
            DeleteObject(sb); DeleteObject(sp);
            SetTextColor(mem, on ? COL_OK : COL_DIM);
            DrawTextW(mem, on ? L"ON" : L"OFF", -1, &sr, DT_CENTER | DT_SINGLELINE | DT_VCENTER | DT_NOPREFIX);
            g_row_kind[i] = 1;
            g_row_id[i] = i;
            y += ROW_H;
        }
        g_row_count = 7;
    } else if (g_tab == 2) {
        /* ---------- 情报：全场 10 人 ---------- */
        if (g_quit_advice)
            wsprintfW(line, L"⚠ 建议秒退：%ls", g_quit_reason);
        else if (g_cs_left >= 0 && g_intel_n == 0)
            wsprintfW(line, L"剩余 %d 秒   抓取中…", g_cs_left);
        else
            wsprintfW(line, L"我的段位  %ls    抓取 %d/%d", g_self_rank[0] ? g_self_rank : L"未获取",
                      g_intel_n, INTEL_MAX);
        g_row_rect[0] = (RECT){ 16, y, w - 16, y + 20 };
        SetTextColor(mem, g_quit_advice ? COL_WARN : COL_DIM);
        DrawTextW(mem, line, -1, &g_row_rect[0], DT_LEFT | DT_SINGLELINE | DT_VCENTER | DT_NOPREFIX);
        g_row_kind[0] = 0; g_row_id[0] = -1;
        y += 22;

        int rows = 1;
        if (g_intel_n == 0) {
            SetTextColor(mem, COL_DIM);
            wsprintfW(line, L"选人开始时自动抓取（%d 人 × 段位 + 近 %d 场）", INTEL_MAX, INTEL_GAMES);
            g_row_rect[rows] = (RECT){ 24, y, w - 16, y + ROW_H };
            DrawTextW(mem, line, -1, &g_row_rect[rows],
                      DT_LEFT | DT_SINGLELINE | DT_VCENTER | DT_NOPREFIX | DT_END_ELLIPSIS);
            g_row_kind[rows] = 0; g_row_id[rows] = -1;
            rows++;
        } else {
            for (int i = 0; i < g_intel_n && rows < 10; i++) {
                IntelEntry *e = &g_intel[i];
                int pct = (e->games > 0) ? (e->wins * 100 / e->games) : -1;
                WCHAR champs[48] = L"";
                WCHAR wpos[16] = L"-";
                if (e->top[0]) wsprintfW(champs, L"%d/%d/%d", e->top[0], e->top[1], e->top[2]);
                else lstrcpynW(champs, L"-", 48);
                if (e->pos[0]) MultiByteToWideChar(CP_UTF8, 0, e->pos, -1, wpos, 16);
                WCHAR streak[16] = L"";
                if (e->streak > 0) wsprintfW(streak, L"%d连胜", e->streak);
                else if (e->streak < 0) wsprintfW(streak, L"%d连败", -e->streak);
                WCHAR mark[24] = L"";
                if (e->known) wsprintfW(mark, L"⚠");
                if (e->duo_score > 0) {
                    WCHAR d[16];
                    wsprintfW(d, L" 双排%d", e->duo_score);
                    lstrcatW(mark, d);
                }
                wsprintfW(line, L"%ls%ls %-5ls %-13ls %-11ls %3d%%(%d) %ls %ls",
                          e->team == 1 ? L"我" : L"敌", mark,
                          wpos,
                          e->name[0] ? e->name : L"?",
                          e->rank[0] ? e->rank : L"-",
                          pct, e->games,
                          streak,
                          champs[0] ? champs : L"-");
                BOOL is_matchup = (e->team == 2 && g_my_pos[0] && lstrcmpiA(e->pos, g_my_pos) == 0);
                SetTextColor(mem, is_matchup ? COL_WARN : (e->team == 1 ? COL_BODY : RGB(190, 170, 190)));
                g_row_rect[rows] = (RECT){ 16, y, w - 16, y + 22 };
                DrawTextW(mem, line, -1, &g_row_rect[rows],
                          DT_LEFT | DT_SINGLELINE | DT_VCENTER | DT_NOPREFIX | DT_END_ELLIPSIS);
                g_row_kind[rows] = 0; g_row_id[rows] = -1;
                y += 22;
                rows++;
            }
        }
        g_row_count = rows;
    } else {
        /* ---------- 工具 ---------- */
        static const WCHAR *labels[11] = { L"立即接受对局", L"给队友点赞", L"结算后再来一局",
                                          L"重新读取配置", L"秒退（退出当前选人）",
                                          L"标记对位为踩坑",
                                          L"换上熟练度最高的 bench 英雄",
                                          L"邀请在线好友（最多 4 人）",
                                          L"邀请最近队友（上一局）",
                                          L"尝试一键静音当前会话",
                                          L"关闭 agent（移除所有窗口）" };
        for (int i = 0; i < 11 && i < ROW_MAX; i++) {
            g_row_rect[i] = (RECT){ 16, y, w - 16, y + ROW_H + 2 };
            HBRUSH sb = CreateSolidBrush(COL_BUTTON);
            HPEN sp = CreatePen(PS_SOLID, 1, COL_BTNLINE);
            HGDIOBJ ob4 = SelectObject(mem, sb), op4 = SelectObject(mem, sp);
            RoundRect(mem, g_row_rect[i].left, g_row_rect[i].top, g_row_rect[i].right,
                      g_row_rect[i].bottom, 8, 8);
            SelectObject(mem, ob4); SelectObject(mem, op4);
            DeleteObject(sb); DeleteObject(sp);
            SetTextColor(mem, COL_BODY);
            RECT rr = g_row_rect[i];
            DrawTextW(mem, labels[i], -1, &rr, DT_CENTER | DT_SINGLELINE | DT_VCENTER | DT_NOPREFIX);
            g_row_kind[i] = 2;
            g_row_id[i] = i;
            y += ROW_H;
        }
        g_row_count = 11;
    }

    /* footer */
    SetTextColor(mem, COL_DIM);
    WCHAR foot[160];
    wsprintfW(foot, L"F8 隐藏  ·  F9 自动接受  ·  点行切换  ·  已注入 pid %lu", (unsigned long)g_pid_hud);
    lr = (RECT){ 16, h - 26, w - 16, h - 8 };
    DrawTextW(mem, foot, -1, &lr, DT_LEFT | DT_SINGLELINE | DT_VCENTER | DT_NOPREFIX | DT_END_ELLIPSIS);

    if (g_compact) {
        /* tab content above overwrote the row rects; restore the compact layout */
        compute_layout(w, h);
        /* repaint the whole window as a small card (covers whatever the tabs drew) */
        HBRUSH cb2 = CreateSolidBrush(COL_PANEL);
        HPEN bp2 = CreatePen(PS_SOLID, 1, COL_BORDER);
        HGDIOBJ ob5 = SelectObject(mem, cb2), op5 = SelectObject(mem, bp2);
        RoundRect(mem, 1, 1, w - 1, h - 1, 3, 3);
        SelectObject(mem, ob5); SelectObject(mem, op5);
        DeleteObject(cb2); DeleteObject(bp2);
        SetBkMode(mem, TRANSPARENT);

        SelectObject(mem, g_font_title);
        SetTextColor(mem, COL_TITLE);
        RECT tt = { 10, 0, 30, 34 };
        DrawTextW(mem, L"G", -1, &tt, DT_LEFT | DT_SINGLELINE | DT_VCENTER | DT_NOPREFIX);
        SelectObject(mem, g_font_body);
        SetTextColor(mem, COL_DIM);
        RECT tt2 = { 28, 0, w - 52, 34 };
        WCHAR head[96];
        if (g_cs_left >= 0) wsprintfW(head, L"%ls  ·  剩 %d 秒", g_phase, g_cs_left);
        else if (g_queue_secs >= 0)
            wsprintfW(head, L"%ls  ·  排队 %d:%02d", g_phase, g_queue_secs / 60, g_queue_secs % 60);
        else wsprintfW(head, L"%ls", g_phase);
        DrawTextW(mem, head, -1, &tt2, DT_LEFT | DT_SINGLELINE | DT_VCENTER | DT_NOPREFIX | DT_END_ELLIPSIS);
        SIZE head_sz = { 0, 0 };
        GetTextExtentPoint32W(mem, head, lstrlenW(head), &head_sz);
        int head_w = head_sz.cx;

        /* expand glyph */
        SetTextColor(mem, COL_OK);
        RECT er = g_expand_rect;
        DrawTextW(mem, L"\x25A3", -1, &er, DT_CENTER | DT_SINGLELINE | DT_VCENTER | DT_NOPREFIX);
        draw_close_button(mem, w);

        /* 1-line strip: G | phase | timer | name Lv | rank | intel | last action */
        SelectObject(mem, g_font_body);
        WCHAR l1[96], l2[96], l3[96], l4[96];
        wsprintfW(l1, L"%ls  Lv.%d", g_name, g_level);
        if (g_cs_left >= 0)
            wsprintfW(l2, L"选人剩余 %d 秒   %ls", g_cs_left, g_self_rank[0] ? g_self_rank : L"未定级");
        else if (g_queue_secs >= 0)
            wsprintfW(l2, L"排队 %d:%02d   %ls", g_queue_secs / 60, g_queue_secs % 60,
                      g_self_rank[0] ? g_self_rank : L"未定级");
        else
            wsprintfW(l2, L"%ls   情报 %d 人", g_self_rank[0] ? g_self_rank : L"未定级", g_intel_n);
        if (g_quit_advice) wsprintfW(l3, L"⚠ %ls", g_quit_reason);
        else lstrcpynW(l3, g_last_action, 96);
        l4[0] = 0;
        if (g_intel_n > 0) {
            int enemies = 0, wins = 0, games = 0;
            for (int i = 0; i < g_intel_n; i++) {
                if (g_intel[i].team == 2) { enemies++; wins += g_intel[i].wins; games += g_intel[i].games; }
            }
            if (games > 0)
                wsprintfW(l4, L"敌方 %d 人  近 %d 场 %d%%", enemies, games, wins * 100 / games);
        }
        SetTextColor(mem, COL_DIM);
        RECT sep = { 34 + head_w + 2, 0, 34 + head_w + 14, 34 };
        DrawTextW(mem, L"|", -1, &sep, DT_CENTER | DT_SINGLELINE | DT_VCENTER | DT_NOPREFIX);

        WCHAR one[256];
        WCHAR tail[128];
        if (g_quit_advice) lstrcpynW(tail, g_quit_reason, 128);
        else if (g_last_action[0]) lstrcpynW(tail, g_last_action, 128);
        else lstrcpynW(tail, L"-", 128);
        if (l2[0] && l1[0])
            wsprintfW(one, L"%ls  ·  %ls  ·  %ls", l1, l2, tail);
        else
            wsprintfW(one, L"%ls", tail);
        SetTextColor(mem, g_quit_advice ? COL_WARN : COL_BODY);
        RECT one_r = { 34 + head_w + 14, 0, w - 52, 34 };
        DrawTextW(mem, one, -1, &one_r,
                  DT_LEFT | DT_SINGLELINE | DT_VCENTER | DT_NOPREFIX | DT_END_ELLIPSIS);
    }

    SelectObject(mem, of);
    BitBlt(dc, 0, 0, w, h, mem, 0, 0, SRCCOPY);
    SelectObject(mem, old_bmp);
    DeleteObject(bmp);
    DeleteDC(mem);
    EndPaint(hwnd, &ps);
}

static void run_action(int id)
{
    if (id == 0) {
        int st = lcu_http(L"POST", ACCEPT_PATH, "{}", NULL, 0);
        set_action(L"点击接受 -> HTTP %d", st);
    } else if (id == 1) {
        honor_step_once();
    } else if (id == 2) {
        int st = lcu_http(L"POST", L"/lol-lobby/v2/play-again", "{}", NULL, 0);
        set_action(L"点击再来一局 -> HTTP %d", st);
    } else if (id == 3) {
        config_load();
        set_action(L"配置已重载");
    } else if (id == 4) {
        WCHAR qpath[128];
        int route = champ_quit_route(qpath, 128);
        if (route == 1) {
            int st = lcu_http(L"POST", qpath, "{}", NULL, 0);
            if (st == 404)   /* 旧客户端兜底 */
                st = lcu_http(L"POST", L"/lol-champ-select/v1/session/quit", "{}", NULL, 0);
            set_action(L"手动秒退 -> HTTP %d", st);
        } else if (route == 0) {
            set_action(L"本模式不允许退出选人（服务端 showQuitButton=false）");
        } else {
            set_action(L"不在选人阶段");
        }
    } else if (id == 5) {
        int idx = -1;
        for (int i = 0; i < g_intel_n && idx < 0; i++) {
            IntelEntry *e = &g_intel[i];
            if (e->team == 2 && g_my_pos[0] && lstrcmpiA(e->pos, g_my_pos) == 0) idx = i;
        }
        if (idx < 0) {
            for (int i = 0; i < g_intel_n && idx < 0; i++)
                if (g_intel[i].team == 2 && g_intel[i].puuid[0]) idx = i;
        }
        if (idx >= 0) {
            char nm[64];
            WideCharToMultiByte(CP_ACP, 0, g_intel[idx].name, -1, nm, sizeof(nm), NULL, NULL);
            known_touch(g_intel[idx].puuid, nm, "手动标记");
            g_intel[idx].known = 1;
            g_dirty = 1;
            set_action(L"已标记 %ls 为踩坑", g_intel[idx].name[0] ? g_intel[idx].name : L"对位");
        } else {
            set_action(L"没有可标记的目标（还没抓到情报？）");
        }
    } else if (id == 6) {
        static char sess[16384];
        if (lcu_http(L"GET", L"/lol-champ-select/v1/session", NULL, sess, sizeof(sess)) == 200)
            bench_swap_step(sess, TRUE);
        else
            set_action(L"不在选人阶段");
    } else if (id == 7) {
        invite_online_friends(4);
    } else if (id == 8) {
        mute_all_probe();
    } else if (id == 9) {
        invite_recent_teammates(4);
    } else {
        /* id == 10: shut the agent down for this process */
        agent_shutdown();
    }
}

static void agent_shutdown(void)
{
    logf_("agent shutdown requested");
    g_cfg.agent_enabled = 0;
    config_save();
    if (g_badge) { DestroyWindow(g_badge); g_badge = NULL; }
    if (g_hud) { DestroyWindow(g_hud); g_hud = NULL; }
    if (g_ipc) { DestroyWindow(g_ipc); g_ipc = NULL; }
    if (g_badge) InvalidateRect(g_badge, NULL, FALSE);
    /* message loop exits on WM_QUIT from WM_DESTROY */
}

static LRESULT CALLBACK hud_proc(HWND h, UINT msg, WPARAM wp, LPARAM lp)
{
    switch (msg) {
    case WM_PAINT: draw_panel(h); return 0;
    case WM_ERASEBKGND: return 1;
    case WM_MOUSEACTIVATE: return MA_NOACTIVATE;
    case WM_NCHITTEST: {
        if (g_last_tick_done && GetTickCount() - g_last_tick_done > 2000)
            return HTTRANSPARENT;   /* our loop is stalled: never swallow the user's clicks */
        POINT pt;
        pt.x = (short)LOWORD(lp);
        pt.y = (short)HIWORD(lp);
        ScreenToClient(h, &pt);
        compute_layout(g_hud_w > 0 ? g_hud_w : HUD_W, g_hud_h > 0 ? g_hud_h : HUD_H);
        if (PtInRect(&g_close_rect, pt)) return HTCLIENT;
        if (PtInRect(&g_expand_rect, pt)) return HTCLIENT;
        if (PtInRect(&g_title_rect, pt)) return HTCLIENT;   /* drag handle */
        for (int i = 0; i < TAB_COUNT; i++)
            if (PtInRect(&g_tab_rect[i], pt)) return HTCLIENT;
        for (int i = 0; i < g_row_count; i++)
            if (PtInRect(&g_row_rect[i], pt)) return HTCLIENT;
        return HTTRANSPARENT;   /* let the client receive clicks over the background */
    }
    case WM_LBUTTONDOWN: {
        POINT dp;
        dp.x = (short)LOWORD(lp);
        dp.y = (short)HIWORD(lp);
        compute_layout(g_hud_w, g_hud_h);
        BOOL on_btn = PtInRect(&g_close_rect, dp) || PtInRect(&g_expand_rect, dp);
        int strip = g_compact ? 26 : 32;
        if (!on_btn && dp.y <= strip) {
            g_panel_drag = 1;
            g_panel_moved = 0;
            GetCursorPos(&g_panel_click);
            g_panel_origin = g_panel_click;
            SetCapture(h);
        }
        return 0;
    }
    case WM_CAPTURECHANGED:
        g_panel_drag = 0;
        return 0;
    case WM_MOUSEMOVE:
        if (g_panel_drag) {
            if (!(GetKeyState(VK_LBUTTON) & 0x8000)) {
                g_panel_drag = 0;
                ReleaseCapture();
                return 0;
            }
            POINT cur;
            GetCursorPos(&cur);
            int dx = cur.x - g_panel_click.x, dy = cur.y - g_panel_click.y;
            if (abs(dx) > 3 || abs(dy) > 3) g_panel_moved = 1;
            if (g_panel_moved) {
                RECT ar;
                if (anchor_rect(&ar, NULL)) {
                    int nx = g_panel_origin.x + dx, ny = g_panel_origin.y + dy;
                    int pw = panel_w(), ph = panel_h();
                    if (nx < ar.left + 4) nx = ar.left + 4;
                    if (ny < ar.top + 4) ny = ar.top + 4;
                    if (nx > ar.right - pw - 4) nx = ar.right - pw - 4;
                    if (ny > ar.bottom - ph - 4) ny = ar.bottom - ph - 4;
                    SetWindowPos(h, HWND_TOPMOST, nx, ny, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
                    g_panel_x = nx;
                    g_panel_y = ny;
                }
            }
        }
        return 0;
    case WM_LBUTTONUP: {
        POINT pt;
        pt.x = (short)LOWORD(lp);
        pt.y = (short)HIWORD(lp);
        compute_layout(g_hud_w > 0 ? g_hud_w : HUD_W, g_hud_h > 0 ? g_hud_h : HUD_H);
        if (g_panel_drag) {
            g_panel_drag = 0;
            ReleaseCapture();
            if (g_panel_moved) {
                RECT ar;
                if (anchor_rect(&ar, NULL)) {
                    g_cfg.ui_dx = g_panel_x - ar.left;
                    g_cfg.ui_dy = g_panel_y - ar.top;
                    config_save();
                    logf_("panel pos saved dx=%d dy=%d", g_cfg.ui_dx, g_cfg.ui_dy);
                }
            } else if (g_compact) {
                toggle_compact();   /* a click (no drag) on the strip opens the panel */
                return 0;
            } else {
                return 0;
            }
        }
        if (PtInRect(&g_expand_rect, pt)) {
            toggle_compact();   /* marker for idempotent patches */
            return 0;
        }
        if (!g_compact && PtInRect(&g_close_rect, pt)) {
            g_visible = 0;
            reposition();
            if (g_badge) InvalidateRect(g_badge, NULL, FALSE);
            logf_("close button -> panel hidden");
            return 0;
        }
        for (int i = 0; i < TAB_COUNT; i++) {
            if (PtInRect(&g_tab_rect[i], pt)) {
                g_tab = i;
                g_dirty = 1;
                InvalidateRect(h, NULL, FALSE);
                return 0;
            }
        }
        if (g_compact && g_row_count > 0 && PtInRect(&g_row_rect[0], pt)) {
            toggle_compact();     /* click the strip: open the full panel */
            return 0;
        }
        for (int i = 0; i < g_row_count && i < ROW_MAX; i++) {
            if (!PtInRect(&g_row_rect[i], pt)) continue;
            if (g_row_kind[i] == 1) toggle_slot(g_row_id[i]);
            else if (g_row_kind[i] == 2) run_action(g_row_id[i]);
            g_dirty = 1;
            InvalidateRect(h, NULL, FALSE);
            return 0;
        }
        return 0;
    }
    }
    return DefWindowProcW(h, msg, wp, lp);
}

/* ---------- launcher badge (top bar, right of 商城) ---------- */
#define BTN_CLASS L"GGmanHudToggle"
#define BADGE_W   32
#define BADGE_H   32

static int  g_badge_drag;
static int  g_badge_moved;
static POINT g_badge_origin;
static POINT g_badge_click;

static void badge_save_pos(void)
{
    RECT wr, cr;
    POINT pt = { 0, 0 };
    if (!g_host) return;
    GetWindowRect(g_badge, &wr);
    GetClientRect(g_host, &cr);
    ClientToScreen(g_host, &pt);
    int dx = wr.left - pt.x, dy = wr.top - pt.y;
    int cw = cr.right - cr.left, ch = cr.bottom - cr.top;
    if (dx < 4) dx = 4;
    if (dy < 4) dy = 4;
    if (dx > cw - BADGE_W - 4) dx = cw - BADGE_W - 4;
    if (dy > ch - BADGE_H - 4) dy = ch - BADGE_H - 4;
    g_cfg.btn_dx = dx;
    g_cfg.btn_dy = dy;
    config_save();
    set_action(L"按钮位置已保存 %d,%d", g_cfg.btn_dx, g_cfg.btn_dy);
}

static void badge_reposition(void)
{
    if (!g_badge) return;
    if (g_badge_drag) return;   /* never fight an in-progress drag */
    if (!g_cfg.button || (g_visible && g_compact)) { if (g_badge_vis != 0) { ShowWindow(g_badge, SW_HIDE); g_badge_vis = 0; } return; }
    RECT ar;
    if (!anchor_rect(&ar, NULL)) {
        if (g_badge_vis != 0) { ShowWindow(g_badge, SW_HIDE); g_badge_vis = 0; }
        return;
    }
    RECT cr = ar;
    int cw = cr.right - cr.left, ch = cr.bottom - cr.top;
    if (cw < 400 || ch < 300) { if (g_badge_vis != 0) { ShowWindow(g_badge, SW_HIDE); g_badge_vis = 0; } return; }
    POINT pt = { ar.left, ar.top };
    int ax, ay;
    if (g_cfg.btn_dx >= 0 && g_cfg.btn_dy >= 0) {
        ax = g_cfg.btn_dx;
        ay = g_cfg.btn_dy;
    } else {
        /* top bar, in the empty gap between the tab strip and the icon row */
        ax = 404;
        ay = 10;
    }
    /* hard clamp: the badge may never leave the client area */
    if (ax > cw - BADGE_W - 4) ax = cw - BADGE_W - 4;
    if (ay > ch - BADGE_H - 4) ay = ch - BADGE_H - 4;
    if (ax < 4) ax = 4;
    if (ay < 4) ay = 4;
    int x = pt.x + ax, y = pt.y + ay;
    if (x != g_badge_x || y != g_badge_y) {
        SetWindowPos(g_badge, HWND_TOPMOST, x, y, BADGE_W, BADGE_H, SWP_NOACTIVATE);
        g_badge_x = x;
        g_badge_y = y;
    }
    SetWindowPos(g_badge, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    if (g_badge_vis != 1) {
        ShowWindow(g_badge, SW_SHOWNOACTIVATE);
        g_badge_vis = 1;
    }
}

static void badge_paint(HWND h)
{
    PAINTSTRUCT ps;
    HDC dc = BeginPaint(h, &ps);
    RECT rc;
    GetClientRect(h, &rc);
    HDC mem = CreateCompatibleDC(dc);
    HBITMAP bmp = CreateCompatibleBitmap(dc, rc.right, rc.bottom);
    HGDIOBJ ob = SelectObject(mem, bmp);
    HBRUSH key = CreateSolidBrush(COLOR_KEY);
    FillRect(mem, &rc, key);
    DeleteObject(key);

    HBRUSH fill = CreateSolidBrush(g_visible ? RGB(18, 52, 70) : RGB(20, 24, 32));
    HPEN pen = CreatePen(PS_SOLID, 2, g_visible ? COL_BORDER : RGB(90, 108, 128));
    HGDIOBJ of = SelectObject(mem, fill), op = SelectObject(mem, pen);
    Ellipse(mem, 2, 2, rc.right - 2, rc.bottom - 2);
    SelectObject(mem, of); SelectObject(mem, op);
    DeleteObject(fill); DeleteObject(pen);

    SetBkMode(mem, TRANSPARENT);
    SetTextColor(mem, g_visible ? COL_TITLE : COL_BODY);
    HGDIOBJ ofont = SelectObject(mem, g_font_title);
    DrawTextW(mem, L"G", -1, &rc, DT_CENTER | DT_VCENTER | DT_SINGLELINE | DT_NOPREFIX);
    SelectObject(mem, ofont);

    BitBlt(dc, 0, 0, rc.right, rc.bottom, mem, 0, 0, SRCCOPY);
    SelectObject(mem, ob);
    DeleteObject(bmp);
    DeleteDC(mem);
    EndPaint(h, &ps);
}

static LRESULT CALLBACK badge_proc(HWND h, UINT msg, WPARAM wp, LPARAM lp)
{
    switch (msg) {
    case WM_PAINT: badge_paint(h); return 0;
    case WM_ERASEBKGND: return 1;
    case WM_MOUSEACTIVATE: return MA_NOACTIVATE;
    case WM_LBUTTONDOWN:
        if (!(GetKeyState(VK_LBUTTON) & 0x8000)) return 0;
        g_badge_drag = 1;
        g_badge_moved = 0;
        GetCursorPos(&g_badge_click);
        g_badge_origin = g_badge_click;
        SetCapture(h);
        return 0;
    case WM_CAPTURECHANGED:
        g_badge_drag = 0;
        return 0;
    case WM_MOUSEMOVE:
        if (g_badge_drag) {
            if (!(GetKeyState(VK_LBUTTON) & 0x8000)) {   /* button no longer down */
                g_badge_drag = 0;
                ReleaseCapture();
                return 0;
            }
            POINT cur;
            GetCursorPos(&cur);
            int dx = cur.x - g_badge_click.x, dy = cur.y - g_badge_click.y;
            if (abs(dx) > 3 || abs(dy) > 3) g_badge_moved = 1;
            if (g_badge_moved) {
                RECT cr;
                POINT c0 = { 0, 0 };
                GetClientRect(g_host, &cr);
                ClientToScreen(g_host, &c0);
                int nx = g_badge_origin.x + dx, ny = g_badge_origin.y + dy;
                int mx = c0.x + 4, my = c0.y + 4;
                int mxx = c0.x + (cr.right - cr.left) - BADGE_W - 4;
                int myy = c0.y + (cr.bottom - cr.top) - BADGE_H - 4;
                if (nx < mx) nx = mx;
                if (ny < my) ny = my;
                if (nx > mxx) nx = mxx;
                if (ny > myy) ny = myy;
                SetWindowPos(h, HWND_TOPMOST, nx, ny, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
                g_badge_x = nx;
                g_badge_y = ny;
            }
        }
        return 0;
    case WM_LBUTTONUP:
        if (g_badge_drag) {
            ReleaseCapture();
            g_badge_drag = 0;
            if (g_badge_moved) {
                badge_save_pos();
            } else {
                g_visible = !g_visible;
                reposition();
                InvalidateRect(h, NULL, FALSE);
                logf_("badge click -> panel visible=%d", g_visible);
            }
        }
        return 0;
    }
    return DefWindowProcW(h, msg, wp, lp);
}
static void reposition(void);
static void badge_reposition(void);


/* client client-area in screen coords; when there is no client window (offline host),
   fall back to the desktop work area so the panel can still be used */
static BOOL anchor_rect(RECT *out, BOOL *is_fallback)
{
    if (g_host && IsWindow(g_host) && IsWindowVisible(g_host)) {
        RECT rc;
        POINT pt = { 0, 0 };
        GetClientRect(g_host, &rc);
        ClientToScreen(g_host, &pt);
        out->left = pt.x;
        out->top = pt.y;
        out->right = pt.x + (rc.right - rc.left);
        out->bottom = pt.y + (rc.bottom - rc.top);
        if (is_fallback) *is_fallback = FALSE;
        return TRUE;
    }
    if (g_host && IsWindow(g_host)) return FALSE;      /* client exists but hidden: stay hidden */
    RECT wa;
    if (!SystemParametersInfoW(SPI_GETWORKAREA, 0, &wa, 0)) return FALSE;
    *out = wa;
    if (is_fallback) *is_fallback = TRUE;
    return TRUE;
}

static void toggle_compact(void)
{
    g_compact = !g_compact;
    g_cfg.ui_compact = g_compact;
    config_save();
    g_dirty = 1;
    reposition();
    if (g_hud) InvalidateRect(g_hud, NULL, FALSE);
    logf_("panel mode -> %s", g_compact ? "compact" : "full");
}

static void reposition(void)
{
    if (!g_hud) return;
    RECT ar;
    BOOL fallback = FALSE;
    if (!anchor_rect(&ar, &fallback)) { ShowWindow(g_hud, SW_HIDE); g_panel_vis = 0; return; }
    POINT pt = { ar.left, ar.top };
    int cw = ar.right - ar.left, ch = ar.bottom - ar.top;
    if (cw < 400 || ch < 300) { ShowWindow(g_hud, SW_HIDE); return; }
    /* only draw over the client while the client is the foreground window */
    {
        HWND fg = GetForegroundWindow();
        BOOL on_client = (fg == g_host);
        if (!on_client && fg) {
            HWND root = GetAncestor(fg, GA_ROOT);
            on_client = (root == g_host);
        }
        if (!on_client) {
            if (g_panel_vis != 0) { ShowWindow(g_hud, SW_HIDE); g_panel_vis = 0; }
            return;
        }
    }
    int pw = panel_w(), ph = panel_h();
    if (ch < ph + 40 || cw < pw + 40) {
        if (g_panel_vis != 0) { ShowWindow(g_hud, SW_HIDE); g_panel_vis = 0; }
        return;
    }
    (void)fallback;
    int x, y;
    if (g_cfg.ui_dx >= 0 && g_cfg.ui_dy >= 0) {
        x = pt.x + g_cfg.ui_dx;
        y = pt.y + g_cfg.ui_dy;
    } else if (g_compact) {
        /* thin status strip: flush with the client's bottom-left edge, looks like part of the chrome */
        x = pt.x;
        y = pt.y + ch - ph;
    } else {
        x = pt.x + HUD_MARGIN;
        y = pt.y + ch - ph - HUD_MARGIN;
    }
    int edge = g_compact ? 0 : 8;         /* the strip sits flush with the client edge */
    if (x + pw > pt.x + cw - edge) x = pt.x + cw - pw - edge;
    if (y + ph > pt.y + ch - edge) y = pt.y + ch - ph - edge;
    if (x < pt.x + edge) x = pt.x + edge;
    if (y < pt.y + edge) y = pt.y + edge;
    if (x != g_panel_x || y != g_panel_y || pw != g_hud_w || ph != g_hud_h) {
        SetWindowPos(g_hud, HWND_TOPMOST, x, y, pw, ph, SWP_NOACTIVATE);
        g_panel_x = x;
        g_panel_y = y;
        g_hud_w = pw;
        g_hud_h = ph;
        g_dirty = 1;
    }
    SetWindowPos(g_hud, HWND_TOPMOST, 0, 0, 0, 0,
                 SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    int vis = g_visible ? 1 : 0;
    if (vis != g_panel_vis) {
        ShowWindow(g_hud, vis ? SW_SHOWNOACTIVATE : SW_HIDE);
        g_panel_vis = vis;
        g_dirty = 1;
    }
    if (vis && g_dirty) {
        InvalidateRect(g_hud, NULL, FALSE);
        g_dirty = 0;
    }
}

/* ---------- LCU refresh loop ---------- */

/* phase -> default tab / visibility */
static void apply_phase_ui(const WCHAR *phase)
{
    if (!g_cfg.auto_ui_by_phase) return;
    int tab = -1, vis = -1;
    if (lstrcmpW(phase, L"ChampSelect") == 0) { tab = 2; vis = 1; }
    else if (lstrcmpW(phase, L"EndOfGame") == 0 || lstrcmpW(phase, L"PreEndOfGame") == 0) { tab = 3; vis = 1; }
    else if (lstrcmpW(phase, L"Lobby") == 0 || lstrcmpW(phase, L"Matchmaking") == 0 ||
             lstrcmpW(phase, L"ReadyCheck") == 0) { tab = 0; vis = 1; }
    else if (lstrcmpW(phase, L"InProgress") == 0 || lstrcmpW(phase, L"GameStart") == 0 ||
             lstrcmpW(phase, L"Reconnect") == 0) { vis = 0; }
    else if (lstrcmpW(phase, L"None") == 0 || lstrcmpW(phase, L"TerminatedInError") == 0) { tab = 0; vis = 1; }

    if (g_cfg.start_hidden && vis == 1) vis = -1;   /* badge-only: never auto-show the panel */
    if (tab >= 0 && tab != g_tab) {
        g_tab = tab;
        g_dirty = 1;
        logf_("ui: phase %ls -> tab %d", phase, tab);
    }
    if (vis >= 0 && g_visible != vis) {
        g_visible = vis;
        logf_("ui: phase %ls -> visible %d", phase, vis);
    }
    reposition();
}

static void refresh(void)
{
    char resp[2048];
    int st = lcu_http(L"GET", PHASE_PATH, NULL, resp, sizeof(resp));
    if (st == 200) {
        WCHAR ph[32];
        if (json_str(resp, "gameflow-phase", ph, 32) ||
            (resp[0] == '"' && strlen(resp) < 30)) {
            if (resp[0] == '"') {
                int i = 0;
                char tmp[32] = { 0 };
                for (i = 1; resp[i] && resp[i] != '"' && i < 31; i++) tmp[i - 1] = resp[i];
                MultiByteToWideChar(CP_UTF8, 0, tmp, -1, ph, 32);
            }
            if (lstrcmpW(g_phase, ph) != 0) {
                lstrcpynW(g_phase, ph, 32);
                logf_("phase -> %ls", g_phase);
                if (lstrcmpW(g_phase, L"ChampSelect") == 0) cs_prefix_refresh();
                apply_phase_ui(g_phase);
            }
        }
    } else if (st > 0) {
        lstrcpynW(g_phase, L"?", 32);
    }

    if (lstrcmpW(g_phase, L"ReadyCheck") != 0) g_accepted_for_phase = 0;

    static DWORD last_cfg;
    if (GetTickCount() - last_cfg > 10000) {
        last_cfg = GetTickCount();
        config_load();
        g_armed = g_cfg.auto_accept ? 1 : 0;
        if (!g_cfg.hud && g_visible) { g_visible = 0; reposition(); }
        if (g_cfg.hud && !g_visible && !g_cfg.start_hidden) { g_visible = 1; reposition(); }
    }

    if (g_armed && lstrcmpW(g_phase, L"ReadyCheck") == 0 && !g_accepted_for_phase) {
        int code = lcu_http(L"POST", ACCEPT_PATH, "{}", resp, sizeof(resp));
        g_accepted_for_phase = 1;
        set_action(L"自动接受 -> HTTP %d", code);
    }

    static DWORD last_summoner;
    if (GetTickCount() - last_summoner > 6000) {
        last_summoner = GetTickCount();
        static char big[4096];
        if (lcu_http(L"GET", L"/lol-chat/v1/me", NULL, big, sizeof(big)) == 200) {
            WCHAR nm[64] = L"?", tag[32] = L"", av[24] = L"?";
            json_str(big, "gameName", nm, 64);
            json_str(big, "gameTag", tag, 32);
            json_str(big, "availability", av, 24);
            if (tag[0]) {
                WCHAR full[64];
                wsprintfW(full, L"%ls#%ls", nm, tag);
                lstrcpynW(g_name, full, 64);
            } else {
                lstrcpynW(g_name, nm, 64);
            }
            lstrcpynW(g_avail, av, 24);
        }
        {
            static char rb[4096];
            if (lcu_http(L"GET", L"/lol-ranked/v1/current-ranked-stats", NULL, rb, sizeof(rb)) == 200) {
                WCHAR tier[24] = L"", div[16] = L"";
                int lp = json_int(rb, "leaguePoints");
                json_str(rb, "tier", tier, 24);
                json_str(rb, "division", div, 16);
                if (tier[0])
                    wsprintfW(g_self_rank, L"%ls %ls %dLP", tier, div, lp);
                else
                    lstrcpynW(g_self_rank, L"未定级", 48);
            }
        }
        if (lcu_http(L"GET", SUMMONER_PATH, NULL, big, sizeof(big)) == 200) {
            g_level = json_int(big, "summonerLevel");
            WCHAR nm2[64];
            if (lstrcmpW(g_name, L"?") == 0 && json_str(big, "gameName", nm2, 64))
                lstrcpynW(g_name, nm2, 64);
        }
    }

    {
        static DWORD last_timer;
        if (GetTickCount() - last_timer > 500) {
            last_timer = GetTickCount();
            if (lstrcmpW(g_phase, L"Matchmaking") == 0) {
                static char qb[2048];
                if (lcu_http(L"GET", L"/lol-matchmaking/v1/search", NULL, qb, sizeof(qb)) == 200)
                    g_queue_secs = json_int(qb, "timeInQueue") / 1000;
                else
                    g_queue_secs = -1;
            } else {
                g_queue_secs = -1;
            }
            if (lstrcmpW(g_phase, L"ChampSelect") != 0) g_cs_left = -1;
            g_dirty = 1;
        }
    }
    champ_select_step();
    if (g_pending_action >= 0) {
        if (GetTickCount() >= g_pending_at) {
            WCHAR path[160];
            char apath[160];
            wsprintfA(apath, "/lol-champ-select/v1/session/actions/%d/complete", g_pending_action);
            MultiByteToWideChar(CP_ACP, 0, apath, -1, path, 160);
            int st = lcu_http(L"POST", path, "{}", NULL, 0);
            set_action(L"延迟锁定 #%d -> HTTP %d", g_pending_action, st);
            g_pending_action = -1;
        }
    }
    runes_step();
    spells_step();
    honor_step();
    play_again_step();
    g_last_tick_done = GetTickCount();
}


static void config_defaults(void)
{
    g_cfg.hud = 1;
    g_cfg.auto_accept = 0;
    g_cfg.auto_pick = 0; g_cfg.pick_champ = 0;
    g_cfg.auto_ban = 0;  g_cfg.ban_champ = 0;
    g_cfg.auto_honor = 0;
    g_cfg.auto_play_again = 0;
    g_cfg.auto_runes = 0;
    g_cfg.rune_page[0] = 0;
    g_cfg.auto_spells = 0;
    g_cfg.spell1 = 4;    /* flash */
    g_cfg.spell2 = 14;   /* ignite */
    g_cfg.agent_enabled = 1;
    g_cfg.button = 1;
    g_cfg.btn_dx = -1;
    g_cfg.btn_dy = -1;
    g_cfg.mock_http = 0;
    g_cfg.mock_port = 0;
    g_cfg.mock_token[0] = 0;
    g_cfg.pick_by_pos = 0;
    for (int i = 0; i < 5; i++) g_cfg.pos_champ[i] = 0;
    g_cfg.ban_list[0] = 0;
    g_cfg.ban_count = 0;
    g_cfg.lockin_delay_s = 0;
    g_cfg.rune_map[0] = 0;
    g_cfg.spell_map[0] = 0;
    g_cfg.rune_map_n = 0;
    g_cfg.spell_map_n = 0;
    g_cfg.quit_mode = 0;
    g_cfg.quit_enemy_wr = 0;
    g_cfg.quit_ally_loss = 0;
    g_cfg.swap_policy = 0;
    g_cfg.auto_bench_swap = 0;
    g_cfg.bench_min_points = 0;
    g_cfg.auto_ui_by_phase = 1;
    g_cfg.http_port = 27888;
    g_cfg.ui_compact = 1;
    g_cfg.ui_dx = -1;
    g_cfg.ui_dy = -1;
    g_cfg.start_hidden = 1;
}

/* "157,99,238" -> int array */
static void parse_int_list(const char *s, int *out, int max, int *count)
{
    *count = 0;
    if (!s || !*s) return;
    const char *p = s;
    while (*p && *count < max) {
        while (*p == ' ' || *p == ',' || *p == ';') p++;
        if (!*p) break;
        int v = atoi(p);
        if (v > 0) out[(*count)++] = v;
        while (*p && *p != ',' && *p != ';') p++;
    }
}

/* "103:page,22:page2" and "103:4/14,22:3/4" */
static void parse_maps(void)
{
    g_cfg.rune_map_n = 0;
    g_cfg.spell_map_n = 0;
    const char *p = g_cfg.rune_map;
    while (*p && g_cfg.rune_map_n < 8) {
        while (*p == ' ' || *p == ',' || *p == ';') p++;
        if (!*p) break;
        int champ = atoi(p);
        const char *c = strchr(p, ':');
        if (champ > 0 && c) {
            c++;
            int i = 0;
            g_cfg.rune_map_champ[g_cfg.rune_map_n] = champ;
            while (*c && *c != ',' && *c != ';' && i < 47) g_cfg.rune_map_page[g_cfg.rune_map_n][i++] = *c++;
            g_cfg.rune_map_page[g_cfg.rune_map_n][i] = 0;
            /* trim trailing spaces */
            while (i > 0 && g_cfg.rune_map_page[g_cfg.rune_map_n][i - 1] == ' ') g_cfg.rune_map_page[g_cfg.rune_map_n][--i] = 0;
            if (i > 0) g_cfg.rune_map_n++;
        }
        while (*p && *p != ',' && *p != ';') p++;
    }
    p = g_cfg.spell_map;
    while (*p && g_cfg.spell_map_n < 8) {
        while (*p == ' ' || *p == ',' || *p == ';') p++;
        if (!*p) break;
        int champ = atoi(p);
        const char *c = strchr(p, ':');
        if (champ > 0 && c) {
            c++;
            int s1 = atoi(c);
            const char *sl = strchr(c, '/');
            int s2 = sl ? atoi(sl + 1) : 0;
            if (s1 > 0 && s2 > 0) {
                g_cfg.spell_map_champ[g_cfg.spell_map_n] = champ;
                g_cfg.spell_map_s1[g_cfg.spell_map_n] = s1;
                g_cfg.spell_map_s2[g_cfg.spell_map_n] = s2;
                g_cfg.spell_map_n++;
            }
        }
        while (*p && *p != ',' && *p != ';') p++;
    }
}

static void config_write_defaults(void)
{
    HANDLE f = CreateFileA(g_ini_path, GENERIC_WRITE, FILE_SHARE_READ, NULL, CREATE_ALWAYS,
                           FILE_ATTRIBUTE_NORMAL, NULL);
    if (f == INVALID_HANDLE_VALUE) return;
    const char *t =
        "; GGman agent config (reloaded every 10s)\r\n"
        "hud=1\r\n"
        "auto_accept=0\r\n"
        "auto_pick=0\r\n"
        "pick_champ=0\r\n"
        "auto_ban=0\r\n"
        "ban_champ=0\r\n"
        "auto_honor=0\r\n"
        "auto_play_again=0\r\n"
        "auto_runes=0\r\n"
        "rune_page=\r\n"
        "auto_spells=0\r\n"
        "spell1=4\r\n"
        "spell2=14\r\n"
        "; ---- batch 1 ----\r\n"
        "pick_by_pos=0\r\n"
        "pos_top=0\r\n"
        "pos_jungle=0\r\n"
        "pos_middle=0\r\n"
        "pos_bottom=0\r\n"
        "pos_utility=0\r\n"
        "ban_list=\r\n"
        "lockin_delay_s=0\r\n"
        "rune_map=\r\n"
        "spell_map=\r\n"
        "; ---- batch B ----\r\n"
        "quit_mode=0\r\n"
        "quit_enemy_wr=0\r\n"
        "quit_ally_loss=0\r\n"
        "swap_policy=0\r\n"
        "auto_bench_swap=0\r\n"
        "bench_min_points=0\r\n"
        "auto_ui_by_phase=1\r\n"
        "http_port=27888\r\n"
        "ui_compact=1\r\n"
        "ui_dx=-1\r\n"
        "ui_dy=-1\r\n"
        "start_hidden=1\r\n"
        "agent_enabled=1\r\n"
        "button=1\r\n"
        "btn_dx=-1\r\n"
        "btn_dy=-1\r\n"
        "; mock_http=1 / mock_port / mock_token are for the offline harness only\r\n";
    DWORD w = 0;
    WriteFile(f, t, (DWORD)lstrlenA(t), &w, NULL);
    CloseHandle(f);
}

static void config_load(void)
{
    config_defaults();
    HANDLE f = CreateFileA(g_ini_path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, NULL,
                           OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, NULL);
    if (f == INVALID_HANDLE_VALUE) { config_write_defaults(); return; }
    char buf[2048];
    DWORD got = 0;
    ReadFile(f, buf, sizeof(buf) - 1, &got, NULL);
    CloseHandle(f);
    buf[got] = 0;
    char *line = buf;
    while (line && *line) {
        char *nl = strchr(line, '\n');
        if (nl) *nl = 0;
        int len = lstrlenA(line);
        if (len > 1 && line[len - 1] == '\r') line[len - 1] = 0;
        if (line[0] && line[0] != ';' && line[0] != '#') {
            char *eq = strchr(line, '=');
            if (eq) {
                *eq = 0;
                char *k = line, *v = eq + 1;
                while (*k == ' ' || *k == '\t') k++;
                while (*v == ' ' || *v == '\t') v++;
                if (lstrcmpiA(k, "hud") == 0) g_cfg.hud = atoi(v);
                else if (lstrcmpiA(k, "auto_accept") == 0) g_cfg.auto_accept = atoi(v);
                else if (lstrcmpiA(k, "auto_pick") == 0) g_cfg.auto_pick = atoi(v);
                else if (lstrcmpiA(k, "pick_champ") == 0) g_cfg.pick_champ = atoi(v);
                else if (lstrcmpiA(k, "auto_ban") == 0) g_cfg.auto_ban = atoi(v);
                else if (lstrcmpiA(k, "ban_champ") == 0) g_cfg.ban_champ = atoi(v);
                else if (lstrcmpiA(k, "auto_honor") == 0) g_cfg.auto_honor = atoi(v);
                else if (lstrcmpiA(k, "auto_play_again") == 0) g_cfg.auto_play_again = atoi(v);
                else if (lstrcmpiA(k, "auto_runes") == 0) g_cfg.auto_runes = atoi(v);
                else if (lstrcmpiA(k, "rune_page") == 0) lstrcpynA(g_cfg.rune_page, v, sizeof(g_cfg.rune_page));
                else if (lstrcmpiA(k, "auto_spells") == 0) g_cfg.auto_spells = atoi(v);
                else if (lstrcmpiA(k, "spell1") == 0) g_cfg.spell1 = atoi(v);
                else if (lstrcmpiA(k, "spell2") == 0) g_cfg.spell2 = atoi(v);
                else if (lstrcmpiA(k, "button") == 0) g_cfg.button = atoi(v);
                else if (lstrcmpiA(k, "agent_enabled") == 0) g_cfg.agent_enabled = atoi(v);
                else if (lstrcmpiA(k, "pick_by_pos") == 0) g_cfg.pick_by_pos = atoi(v);
                else if (lstrcmpiA(k, "pos_top") == 0) g_cfg.pos_champ[0] = atoi(v);
                else if (lstrcmpiA(k, "pos_jungle") == 0) g_cfg.pos_champ[1] = atoi(v);
                else if (lstrcmpiA(k, "pos_middle") == 0) g_cfg.pos_champ[2] = atoi(v);
                else if (lstrcmpiA(k, "pos_bottom") == 0) g_cfg.pos_champ[3] = atoi(v);
                else if (lstrcmpiA(k, "pos_utility") == 0) g_cfg.pos_champ[4] = atoi(v);
                else if (lstrcmpiA(k, "ban_list") == 0) lstrcpynA(g_cfg.ban_list, v, sizeof(g_cfg.ban_list));
                else if (lstrcmpiA(k, "lockin_delay_s") == 0) g_cfg.lockin_delay_s = atoi(v);
                else if (lstrcmpiA(k, "rune_map") == 0) lstrcpynA(g_cfg.rune_map, v, sizeof(g_cfg.rune_map));
                else if (lstrcmpiA(k, "spell_map") == 0) lstrcpynA(g_cfg.spell_map, v, sizeof(g_cfg.spell_map));
                else if (lstrcmpiA(k, "btn_dx") == 0) g_cfg.btn_dx = atoi(v);
                else if (lstrcmpiA(k, "btn_dy") == 0) g_cfg.btn_dy = atoi(v);
                else if (lstrcmpiA(k, "quit_mode") == 0) g_cfg.quit_mode = atoi(v);
                else if (lstrcmpiA(k, "quit_enemy_wr") == 0) g_cfg.quit_enemy_wr = atoi(v);
                else if (lstrcmpiA(k, "quit_ally_loss") == 0) g_cfg.quit_ally_loss = atoi(v);
                else if (lstrcmpiA(k, "swap_policy") == 0) g_cfg.swap_policy = atoi(v);
                else if (lstrcmpiA(k, "auto_bench_swap") == 0) g_cfg.auto_bench_swap = atoi(v);
                else if (lstrcmpiA(k, "bench_min_points") == 0) g_cfg.bench_min_points = atoi(v);
                else if (lstrcmpiA(k, "auto_ui_by_phase") == 0) g_cfg.auto_ui_by_phase = atoi(v);
                else if (lstrcmpiA(k, "http_port") == 0) g_cfg.http_port = atoi(v);
                else if (lstrcmpiA(k, "ui_compact") == 0) g_cfg.ui_compact = atoi(v);
                else if (lstrcmpiA(k, "ui_dx") == 0) g_cfg.ui_dx = atoi(v);
                else if (lstrcmpiA(k, "ui_dy") == 0) g_cfg.ui_dy = atoi(v);
                else if (lstrcmpiA(k, "start_hidden") == 0) g_cfg.start_hidden = atoi(v);
                else if (lstrcmpiA(k, "mock_http") == 0) g_cfg.mock_http = atoi(v);
                else if (lstrcmpiA(k, "mock_port") == 0) g_cfg.mock_port = atoi(v);
                else if (lstrcmpiA(k, "mock_token") == 0) lstrcpynA(g_cfg.mock_token, v, sizeof(g_cfg.mock_token));
            }
        }
        if (!nl) break;
        line = nl + 1;
    }
    parse_int_list(g_cfg.ban_list, g_cfg.ban_ids, 8, &g_cfg.ban_count);
    parse_maps();
    if (g_cfg.lockin_delay_s < 0) g_cfg.lockin_delay_s = 0;
    if (g_cfg.lockin_delay_s > 15) g_cfg.lockin_delay_s = 15;
}

static void config_save(void)
{
    HANDLE f = CreateFileA(g_ini_path, GENERIC_WRITE, FILE_SHARE_READ, NULL, CREATE_ALWAYS,
                           FILE_ATTRIBUTE_NORMAL, NULL);
    if (f == INVALID_HANDLE_VALUE) return;
    char buf[2048];
    int n = wsprintfA(buf,
        "; GGman agent config (auto-saved by panel)\r\n"
        "hud=%d\r\n"
        "auto_accept=%d\r\n"
        "auto_pick=%d\r\n"
        "pick_champ=%d\r\n"
        "auto_ban=%d\r\n"
        "ban_champ=%d\r\n"
        "auto_honor=%d\r\n"
        "auto_play_again=%d\r\n"
        "auto_runes=%d\r\n"
        "rune_page=%s\r\n"
        "auto_spells=%d\r\n"
        "spell1=%d\r\n"
        "spell2=%d\r\n"
        "pick_by_pos=%d\r\n"
        "pos_top=%d\r\n"
        "pos_jungle=%d\r\n"
        "pos_middle=%d\r\n"
        "pos_bottom=%d\r\n"
        "pos_utility=%d\r\n"
        "ban_list=%s\r\n"
        "lockin_delay_s=%d\r\n"
        "rune_map=%s\r\n"
        "spell_map=%s\r\n"
        "quit_mode=%d\r\n"
        "quit_enemy_wr=%d\r\n"
        "quit_ally_loss=%d\r\n"
        "swap_policy=%d\r\n"
        "auto_bench_swap=%d\r\n"
        "bench_min_points=%d\r\n"
        "auto_ui_by_phase=%d\r\n"
        "http_port=%d\r\n"
        "ui_compact=%d\r\n"
        "ui_dx=%d\r\n"
        "ui_dy=%d\r\n"
        "start_hidden=%d\r\n"
        "agent_enabled=%d\r\n"
        "button=%d\r\n"
        "btn_dx=%d\r\n"
        "btn_dy=%d\r\n",
        g_cfg.hud, g_armed ? 1 : 0, g_cfg.auto_pick, g_cfg.pick_champ,
        g_cfg.auto_ban, g_cfg.ban_champ, g_cfg.auto_honor, g_cfg.auto_play_again,
        g_cfg.auto_runes, g_cfg.rune_page, g_cfg.auto_spells, g_cfg.spell1, g_cfg.spell2,
        g_cfg.pick_by_pos, g_cfg.pos_champ[0], g_cfg.pos_champ[1], g_cfg.pos_champ[2],
        g_cfg.pos_champ[3], g_cfg.pos_champ[4], g_cfg.ban_list, g_cfg.lockin_delay_s,
        g_cfg.rune_map, g_cfg.spell_map,
        g_cfg.quit_mode, g_cfg.quit_enemy_wr, g_cfg.quit_ally_loss, g_cfg.swap_policy,
        g_cfg.auto_bench_swap, g_cfg.bench_min_points, g_cfg.auto_ui_by_phase, g_cfg.http_port,
        g_cfg.ui_compact, g_cfg.ui_dx, g_cfg.ui_dy, g_cfg.start_hidden,
        g_cfg.agent_enabled, g_cfg.button, g_cfg.btn_dx, g_cfg.btn_dy);
    DWORD wrote = 0;
    WriteFile(f, buf, (DWORD)n, &wrote, NULL);
    CloseHandle(f);
    logf_("config saved (accept=%d pick=%d ban=%d runes=%d spells=%d honor=%d again=%d)",
          g_armed, g_cfg.auto_pick, g_cfg.auto_ban, g_cfg.auto_runes, g_cfg.auto_spells,
          g_cfg.auto_honor, g_cfg.auto_play_again);
}

/* ---------- windowed JSON helpers (no full parser, enough for LCU payloads) ---------- */
static const char *win_find(const char *a, const char *b, const char *key)
{
    char pat[64];
    wsprintfA(pat, "\"%s\"", key);
    for (const char *p = a; p < b; p++) {
        const char *h = strstr(p, pat);
        if (!h || h + lstrlenA(pat) > b) return NULL;
        return h;
    }
    return NULL;
}

static int win_int(const char *a, const char *b, const char *key, int def)
{
    const char *h = win_find(a, b, key);
    if (!h) return def;
    h = strchr(h, ':');
    if (!h || h >= b) return def;
    return atoi(h + 1);
}

static BOOL win_str(const char *a, const char *b, const char *key, char *out, int cch)
{
    const char *h = win_find(a, b, key);
    if (!h) return FALSE;
    h = strchr(h, ':');
    if (!h || h >= b) return FALSE;
    h++;
    while (h < b && (*h == ' ' || *h == '\t')) h++;
    if (h >= b || *h != '"') return FALSE;
    h++;
    int i = 0;
    while (h < b && *h && *h != '"' && i < cch - 1) out[i++] = *h++;
    out[i] = 0;
    return TRUE;
}

/* ---------- champ select ---------- */
typedef struct { int action_id; int champion_id; int is_ban; } CsDecision;

static int cs_decide(const char *json, int my_cell, int auto_pick, int auto_ban,
                     CsDecision *out, int max_out)
{
    if (!json || !out || max_out <= 0) return 0;
    int count = 0;
    const char *end = json + lstrlenA(json);
    const char *p = json;
    for (;;) {
        p = strstr(p, "\"isInProgress\"");
        if (!p) break;
        const char *v = strchr(p, ':');
        BOOL in_progress = FALSE;
        if (v) {
            v++;
            while (*v == ' ' || *v == '\t') v++;
            in_progress = (strncmp(v, "true", 4) == 0);
        }
        if (!in_progress) { p++; continue; }
        /* find the enclosing JSON object of this flag (actions objects are flat) */
        const char *wa = p;
        while (wa > json && *wa != '{') wa--;
        const char *wb = p;
        while (wb < end && *wb && *wb != '}') wb++;
        if (*wb == '}') wb++;
        if (*wa != '{' || wb <= wa) { p++; continue; }
        int actor = win_int(wa, wb, "actorCellId", -1);
        int id = win_int(wa, wb, "id", -1);
        int champ = win_int(wa, wb, "championId", 0);
        char type[16] = { 0 };
        BOOL hasType = win_str(wa, wb, "type", type, sizeof(type));
        if (actor == my_cell && id >= 0 && champ == 0 && hasType && count < max_out) {
            if (lstrcmpA(type, "pick") == 0 && auto_pick) {
                out[count].action_id = id;
                out[count].champion_id = 0;
                out[count].is_ban = 0;
                count++;
            } else if (lstrcmpA(type, "ban") == 0 && auto_ban) {
                out[count].action_id = id;
                out[count].champion_id = 0;
                out[count].is_ban = 1;
                count++;
            }
        }
        p++;
    }
    return count;
}

static int g_cs_last_action = -1;

/* 队友（含自己）是否已经把某个英雄预选上来了 —— 用来避免自动 ban 误伤队友 */
static BOOL ally_wants_champion(const char *json, int champ)
{
    if (!json || champ <= 0) return FALSE;
    const char *mt = strstr(json, "\"myTeam\"");
    if (!mt) return FALSE;
    const char *tt = strstr(mt, "\"theirTeam\"");
    const char *end = tt ? tt : json + lstrlenA(json);
    const char *p = mt;
    for (;;) {
        p = strstr(p, "\"championPickIntent\"");
        if (!p || p >= end) break;
        const char *v = strchr(p, ':');
        if (v) {
            int id = atoi(v + 1);
            if (id == champ) return TRUE;
        }
        p++;
    }
    return FALSE;
}
#define HANDLED_MAX 8
static int g_handled[HANDLED_MAX];
static int g_handled_n;

static BOOL was_handled(int id)
{
    for (int i = 0; i < g_handled_n; i++) if (g_handled[i] == id) return TRUE;
    return FALSE;
}

static void mark_handled(int id)
{
    if (was_handled(id)) return;
    if (g_handled_n < HANDLED_MAX) g_handled[g_handled_n++] = id;
    else {
        for (int i = 1; i < HANDLED_MAX; i++) g_handled[i - 1] = g_handled[i];
        g_handled[HANDLED_MAX - 1] = id;
    }
}

/* scan myTeam[] for my cell: assignedPosition + championId */
static BOOL my_cell_info(const char *json, int cell, char *pos, int pos_cch, int *champ)
{
    if (champ) *champ = 0;
    if (pos && pos_cch) pos[0] = 0;
    const char *mt = strstr(json, "\"myTeam\"");
    if (!mt) return FALSE;
    const char *start = strchr(mt, '[');
    if (!start) return FALSE;
    const char *end = strstr(start, "\"theirTeam\"");
    if (!end) end = json + lstrlenA(json);
    const char *p = start;
    while (p < end) {
        const char *ob = strchr(p, '{');
        if (!ob || ob >= end) break;
        const char *oe = strchr(ob, '}');
        if (!oe || oe > end) break;
        int c = win_int(ob, oe + 1, "cellId", -1);
        if (c == cell) {
            if (pos && pos_cch) win_str(ob, oe + 1, "assignedPosition", pos, pos_cch);
            if (champ) *champ = win_int(ob, oe + 1, "championId", 0);
            return TRUE;
        }
        p = oe + 1;
    }
    return FALSE;
}

static int pos_champ_of(const char *pos)
{
    if (!pos || !*pos) return 0;
    if (lstrcmpiA(pos, "top") == 0) return g_cfg.pos_champ[0];
    if (lstrcmpiA(pos, "jungle") == 0) return g_cfg.pos_champ[1];
    if (lstrcmpiA(pos, "middle") == 0) return g_cfg.pos_champ[2];
    if (lstrcmpiA(pos, "bottom") == 0) return g_cfg.pos_champ[3];
    if (lstrcmpiA(pos, "utility") == 0) return g_cfg.pos_champ[4];
    return 0;
}

static int g_intel_stage;   /* 0 summoner, 1 ranked, 2 matchlist */

static int count_kv_true(const char *json, const char *key, int want_true)
{
    int n = 0;
    const char *p = json;
    for (;;) {
        p = strstr(p, key);
        if (!p) break;
        const char *v = strchr(p, ':');
        if (v) {
            v++;
            while (*v == ' ' || *v == '\t') v++;
            if (want_true && strncmp(v, "true", 4) == 0) n++;
            if (!want_true && strncmp(v, "false", 5) == 0) n++;
        }
        p++;
    }
    return n;
}

/* ---- 选人阶段：会话前缀 + 退出路由（2026-11-28 逆向结论） -------------------
 * PvP 选人走 lol-lobby-team-builder 插件，旧 /lol-champ-select 在 PvP 下没有 delegate。
 * 客户端退出按钮的源码就是：POST {prefix}/session/quit
 *   （观战+isLegacyChampSelect 时换成 /lol-lobby/v1/lobby/custom/cancel-champ-select）
 * 按钮显隐唯一取决于服务端下发的 session.showQuitButton —— 为 false 时直接告知用户，
 * 不硬试：硬试只会再吃一次 400，且可能被服务端记成一次秒退。
 */
static int champ_quit_route(WCHAR *path, DWORD cch)
{
    static char buf[8192];
    if (lcu_http(L"GET", L"/lol-lobby-team-builder/champ-select/v1/session", NULL, buf, sizeof(buf)) != 200)
        return -1;                                          /* 不在选人 */

    BOOL allow  = count_kv_true(buf, "showQuitButton", 1) > 0;
    BOOL spect  = count_kv_true(buf, "isSpectating", 1) > 0;
    BOOL legacy = count_kv_true(buf, "isLegacyChampSelect", 1) > 0;

    if (!allow && !spect) return 0;                          /* 服务端禁止退出 */

    if (legacy && spect)
        lstrcpynW(path, L"/lol-lobby/v1/lobby/custom/cancel-champ-select", cch);
    else
        lstrcpynW(path, L"/lol-lobby-team-builder/champ-select/v1/session/quit", cch);
    return 1;
}

/* 返回 1 = team-builder 前缀可用（PvP 选人），0 = 退回旧 /lol-champ-select */
static int cs_prefix_probe(WCHAR *out, DWORD cch)
{
    static char probe[512];
    g_cs_use_tb = (lcu_http(L"GET", L"/lol-lobby-team-builder/champ-select/v1/session",
                            NULL, probe, sizeof(probe)) == 200) ? 1 : 0;
    if (g_cs_use_tb) {
        lstrcpynW(out, L"/lol-lobby-team-builder/champ-select/v1", cch);
        return 1;
    }
    lstrcpynW(out, L"/lol-champ-select/v1", cch);
    return 0;
}

static void cs_prefix_refresh(void)
{
    WCHAR tmp[64];
    cs_prefix_probe(tmp, 64);
}

static void tally_champion(IntelEntry *e, int champ)
{
    for (int i = 0; i < 3; i++)
        if (e->top[i] == champ) { e->topn[i]++; return; }
    for (int i = 0; i < 3; i++)
        if (e->top[i] == 0) { e->top[i] = champ; e->topn[i] = 1; return; }
    /* replace the smallest */
    int min_i = 0;
    for (int i = 1; i < 3; i++) if (e->topn[i] < e->topn[min_i]) min_i = i;
    if (e->topn[min_i] < 1) { e->top[min_i] = champ; e->topn[min_i] = 1; }
}

static void parse_matchlist(const char *json, IntelEntry *e)
{
    e->games = count_kv_true(json, "\"win\"", 1) + count_kv_true(json, "\"win\"", 0);
    e->wins = count_kv_true(json, "\"win\"", 1);

    /* streak: walk the games in order and count the leading run of the same result */
    {
        int first = -1, run = 0;
        const char *p = json;
        for (;;) {
            p = strstr(p, "\"win\"");
            if (!p) break;
            const char *v = strchr(p, ':');
            if (v) {
                v++;
                while (*v == ' ' || *v == '\t') v++;
                int win = (strncmp(v, "true", 4) == 0) ? 1 : 0;
                if (first < 0) { first = win; run = 1; }
                else if (win == first) run++;
                else break;
            }
            p++;
        }
        if (first == 1) e->streak = run;
        else if (first == 0) e->streak = -run;
        else e->streak = 0;
    }

    /* champion tally + game ids */
    {
        const char *p = json;
        for (;;) {
            p = strstr(p, "\"championId\"");
            if (!p) break;
            const char *v = strchr(p, ':');
            if (v) {
                int c = atoi(v + 1);
                if (c > 0) tally_champion(e, c);
            }
            p++;
        }
    }
    {
        const char *p = json;
        for (;;) {
            p = strstr(p, "\"gameId\"");
            if (!p) break;
            const char *v = strchr(p, ':');
            if (v && e->game_id_n < INTEL_GAMES) {
                long long g = strtoll(v + 1, NULL, 10);
                if (g > 0) e->game_ids[e->game_id_n++] = g;
            }
            p++;
        }
    }
}

/* ---------- local watch list (踩坑名单) ---------- */
#define KNOWN_MAX 200
static char g_known_puuid[KNOWN_MAX][40];
static char g_known_name[KNOWN_MAX][64];
static char g_known_note[KNOWN_MAX][40];
static int  g_known_count[KNOWN_MAX];
static int  g_known_n;
static char g_known_path[MAX_PATH];

static void known_load(void)
{
    g_known_n = 0;
    lstrcpynA(g_known_path, "D:\\project2\\ggman\\dll\\build\\known_players.txt", MAX_PATH);
    HANDLE f = CreateFileA(g_known_path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, NULL,
                           OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, NULL);
    if (f == INVALID_HANDLE_VALUE) return;
    char buf[16384];
    DWORD got = 0;
    ReadFile(f, buf, sizeof(buf) - 1, &got, NULL);
    CloseHandle(f);
    buf[got] = 0;
    char *line = buf;
    while (line && *line && g_known_n < KNOWN_MAX) {
        char *nl = strchr(line, '\n');
        if (nl) *nl = 0;
        int len = lstrlenA(line);
        if (len > 0 && line[len - 1] == '\r') line[len - 1] = 0;
        if (line[0] && line[0] != '#') {
            char *f1 = strchr(line, '\t');
            if (f1) {
                *f1 = 0;
                char *f2 = strchr(f1 + 1, '\t');
                char *f3 = f2 ? strchr(f2 + 1, '\t') : NULL;
                lstrcpynA(g_known_puuid[g_known_n], line, 40);
                lstrcpynA(g_known_name[g_known_n], f1 + 1, 64);
                if (f2) { *f2 = 0; lstrcpynA(g_known_note[g_known_n], f2 + 1, 40); }
                if (f3) g_known_count[g_known_n] = atoi(f3 + 1);
                else g_known_count[g_known_n] = 1;
                if (g_known_puuid[g_known_n][0]) g_known_n++;
            }
        }
        if (!nl) break;
        line = nl + 1;
    }
    logf_("known list loaded: %d entries", g_known_n);
}

static void known_save(void)
{
    HANDLE f = CreateFileA(g_known_path, GENERIC_WRITE, FILE_SHARE_READ, NULL, CREATE_ALWAYS,
                           FILE_ATTRIBUTE_NORMAL, NULL);
    if (f == INVALID_HANDLE_VALUE) return;
    char buf[1024];
    int n = wsprintfA(buf, "# puuid\tname\tnote\tcount\r\n");
    DWORD wrote = 0;
    WriteFile(f, buf, (DWORD)n, &wrote, NULL);
    for (int i = 0; i < g_known_n; i++) {
        n = wsprintfA(buf, "%s\t%s\t%s\t%d\r\n", g_known_puuid[i], g_known_name[i],
                      g_known_note[i], g_known_count[i]);
        WriteFile(f, buf, (DWORD)n, &wrote, NULL);
    }
    CloseHandle(f);
    logf_("known list saved: %d entries", g_known_n);
}

static int known_find(const char *puuid)
{
    if (!puuid || !*puuid) return -1;
    for (int i = 0; i < g_known_n; i++)
        if (lstrcmpA(g_known_puuid[i], puuid) == 0) return i;
    return -1;
}

static int known_touch(const char *puuid, const char *name, const char *note)
{
    if (!puuid || !*puuid) return -1;
    int idx = known_find(puuid);
    if (idx < 0) {
        if (g_known_n >= KNOWN_MAX) return -1;
        idx = g_known_n++;
        lstrcpynA(g_known_puuid[idx], puuid, 40);
        lstrcpynA(g_known_name[idx], name ? name : "?", 64);
        lstrcpynA(g_known_note[idx], note ? note : "", 40);
        g_known_count[idx] = 1;
    } else {
        g_known_count[idx]++;
        if (note && *note) lstrcpynA(g_known_note[idx], note, 40);
    }
    known_save();
    return idx;
}

/* ---------- duo detection: shared gameIds inside the same team ---------- */
static void duo_eval(void)
{
    for (int i = 0; i < g_intel_n; i++) g_intel[i].duo_score = 0;
    for (int i = 0; i < g_intel_n; i++) {
        for (int j = i + 1; j < g_intel_n; j++) {
            if (g_intel[i].team != g_intel[j].team) continue;
            int shared = 0;
            for (int a = 0; a < g_intel[i].game_id_n; a++)
                for (int b = 0; b < g_intel[j].game_id_n; b++)
                    if (g_intel[i].game_ids[a] == g_intel[j].game_ids[b]) shared++;
            if (shared >= 2) {
                if (shared > g_intel[i].duo_score) g_intel[i].duo_score = shared;
                if (shared > g_intel[j].duo_score) g_intel[j].duo_score = shared;
            }
        }
    }
}

static void intel_reset(void)
{
    g_intel_n = 0;
    g_intel_done = 0;
    g_intel_next = 0;
    g_intel_stage = 0;
}

/* build the 10 entries from myTeam + theirTeam */
static void intel_collect(const char *session_json)
{
    const char *sections[2] = { "\"myTeam\"", "\"theirTeam\"" };
    int teams[2] = { 1, 2 };
    IntelEntry built[INTEL_MAX];
    int n = 0;
    for (int s = 0; s < 2; s++) {
        const char *mt = strstr(session_json, sections[s]);
        if (!mt) continue;
        const char *start = strchr(mt, '[');
        if (!start) continue;
        const char *end = (s == 0) ? strstr(start, "\"theirTeam\"")
                                   : session_json + lstrlenA(session_json);
        if (!end) end = session_json + lstrlenA(session_json);
        const char *p = start;
        while (p < end && n < INTEL_MAX) {
            const char *ob = strchr(p, '{');
            if (!ob || ob >= end) break;
            const char *oe = strchr(ob, '}');
            if (!oe || oe > end) break;
            IntelEntry e;
            memset(&e, 0, sizeof(e));
            e.cell = win_int(ob, oe + 1, "cellId", -1);
            e.champion = win_int(ob, oe + 1, "championId", 0);
            e.team = teams[s];
            win_str(ob, oe + 1, "assignedPosition", e.pos, sizeof(e.pos));
            win_str(ob, oe + 1, "puuid", e.puuid, sizeof(e.puuid));
            if (e.cell >= 0 && (e.puuid[0] || win_int(ob, oe + 1, "summonerId", 0) > 0)) {
                built[n++] = e;
            }
            p = oe + 1;
        }
    }
    memcpy(g_intel, built, sizeof(IntelEntry) * n);
    g_intel_n = n;
    g_intel_done = (n == 0);
    g_intel_next = 0;
    if (n) logf_("intel: %d players in session", n);
}

/* one player per tick, so a 10-player session costs ~20 requests spread over ~3s */
static void intel_fetch_step(void)
{
    if (g_intel_done || g_intel_next >= g_intel_n) { g_intel_done = 1; return; }
    IntelEntry *e = &g_intel[g_intel_next];
    static char body[65536];
    WCHAR path[320];

    /* one request per tick: a 10-player session now costs ~30 ticks (~20s) instead of 3s of burst */
    if (e->puuid[0] && g_intel_stage == 2) {
        char ap[320];
        wsprintfA(ap, "/lol-match-history/v3/matchlist/account/%s?begIndex=0&endIndex=%d",
                  e->puuid, INTEL_GAMES);
        MultiByteToWideChar(CP_ACP, 0, ap, -1, path, 320);
        int st = lcu_http(L"GET", path, NULL, body, sizeof(body));
        if (st == 200) parse_matchlist(body, e);
        else logf_("intel: matchlist cell %d -> HTTP %d", e->cell, st);
        g_intel_stage = 0;
        g_intel_next++;
        if (g_intel_next >= g_intel_n) goto intel_finished;
        return;
    }
    if (e->puuid[0] && g_intel_stage == 1) {
        char ap[320];
        wsprintfA(ap, "/lol-ranked/v1/ranked-stats/%s", e->puuid);
        MultiByteToWideChar(CP_ACP, 0, ap, -1, path, 320);
        if (lcu_http(L"GET", path, NULL, body, sizeof(body)) == 200) {
            WCHAR tier[16] = L"", div[8] = L"";
            json_str(body, "tier", tier, 16);
            json_str(body, "division", div, 8);
            int lp = json_int(body, "leaguePoints");
            if (tier[0]) wsprintfW(e->rank, L"%ls %ls %d", tier, div, lp);
            else lstrcpynW(e->rank, L"未定级", 28);
        }
        g_intel_stage = 2;
        g_dirty = 1;
        return;
    }
    if (e->puuid[0] && g_intel_stage == 0) {
        char ap[320];
        wsprintfA(ap, "/lol-summoner/v2/summoners/puuid/%s", e->puuid);
        MultiByteToWideChar(CP_ACP, 0, ap, -1, path, 320);
        if (lcu_http(L"GET", path, NULL, body, sizeof(body)) == 200) {
            WCHAR nm[48] = L"", tag[16] = L"";
            json_str(body, "gameName", nm, 48);
            json_str(body, "gameTag", tag, 16);
            if (nm[0]) {
                if (tag[0]) wsprintfW(e->name, L"%ls#%ls", nm, tag);
                else lstrcpynW(e->name, nm, 48);
            }
        }
        g_intel_stage = 1;
        g_dirty = 1;
        return;
    }
    /* no puuid: skip straight to the next player */
    g_intel_stage = 0;
    g_intel_next++;
    if (g_intel_next >= g_intel_n) goto intel_finished;
    return;

intel_finished:
    if (0) {
    }

    if (e->puuid[0]) {
        char ap[320];
        wsprintfA(ap, "/lol-summoner/v2/summoners/puuid/%s", e->puuid);
        MultiByteToWideChar(CP_ACP, 0, ap, -1, path, 320);
        if (lcu_http(L"GET", path, NULL, body, sizeof(body)) == 200) {
            WCHAR nm[48] = L"", tag[16] = L"";
            json_str(body, "gameName", nm, 48);
            json_str(body, "gameTag", tag, 16);
            if (nm[0]) {
                if (tag[0]) wsprintfW(e->name, L"%ls#%ls", nm, tag);
                else lstrcpynW(e->name, nm, 48);
            }
        }
        wsprintfA(ap, "/lol-ranked/v1/ranked-stats/%s", e->puuid);
        MultiByteToWideChar(CP_ACP, 0, ap, -1, path, 320);
        if (lcu_http(L"GET", path, NULL, body, sizeof(body)) == 200) {
            WCHAR tier[16] = L"", div[8] = L"";
            json_str(body, "tier", tier, 16);
            json_str(body, "division", div, 8);
            int lp = json_int(body, "leaguePoints");
            if (tier[0]) wsprintfW(e->rank, L"%ls %ls %d", tier, div, lp);
            else lstrcpynW(e->rank, L"未定级", 28);
        }
        wsprintfA(ap, "/lol-match-history/v3/matchlist/account/%s?begIndex=0&endIndex=%d",
                  e->puuid, INTEL_GAMES);
        MultiByteToWideChar(CP_ACP, 0, ap, -1, path, 320);
        int st = lcu_http(L"GET", path, NULL, body, sizeof(body));
        if (st == 200) parse_matchlist(body, e);
        else logf_("intel: matchlist for cell %d -> HTTP %d", e->cell, st);
    }
    g_intel_next++;
    if (g_intel_next >= g_intel_n) {
        g_intel_done = 1;
        duo_eval();
        for (int i = 0; i < g_intel_n; i++) {
            int k = known_find(g_intel[i].puuid);
            if (k >= 0) {
                g_intel[i].known = 1;
                g_intel[i].known_count = g_known_count[k];
                g_known_count[k]++;
            }
        }
        known_save();
        int duos = 0;
        for (int i = 0; i < g_intel_n; i++) if (g_intel[i].duo_score > 0) duos++;
        logf_("intel: done (%d players), duo-marked=%d, known=%d", g_intel_n, duos, g_known_n);
        g_dirty = 1;
    }
}

static void quit_reset(void)
{
    g_quit_advice = 0;
    g_quit_sent = 0;
    g_quit_reason[0] = 0;
}

static void dodge_eval(void)
{
    if (g_quit_sent || !g_intel_done || g_intel_n == 0) return;
    int enemy_games = 0, enemy_wins = 0, worst_ally_streak = 0;
    for (int i = 0; i < g_intel_n; i++) {
        IntelEntry *e = &g_intel[i];
        if (e->team == 2) { enemy_games += e->games; enemy_wins += e->wins; }
        if (e->team == 1 && e->streak < 0 && -e->streak > worst_ally_streak)
            worst_ally_streak = -e->streak;
    }
    int enemy_wr = enemy_games > 0 ? (enemy_wins * 100 / enemy_games) : -1;

    BOOL hit = FALSE;
    if (g_cfg.quit_enemy_wr > 0 && enemy_wr >= g_cfg.quit_enemy_wr) {
        wsprintfW(g_quit_reason, L"敌方近 %d 场胜率 %d%% ≥ %d%%", enemy_games, enemy_wr, g_cfg.quit_enemy_wr);
        hit = TRUE;
    }
    if (!hit && g_cfg.quit_ally_loss > 0 && worst_ally_streak >= g_cfg.quit_ally_loss) {
        wsprintfW(g_quit_reason, L"队友 %d 连败 ≥ %d", worst_ally_streak, g_cfg.quit_ally_loss);
        hit = TRUE;
    }
    if (!hit) return;

    g_quit_advice = 1;
    g_dirty = 1;
    if (g_cfg.quit_mode <= 0) return;
    if (g_cfg.quit_mode == 1) {
        set_action(L"建议秒退：%ls", g_quit_reason);
        return;
    }
    WCHAR qpath[128];
    int route = champ_quit_route(qpath, 128);
    if (route != 1) {
        set_action(L"建议秒退但本模式禁止（%ls）", g_quit_reason);
        return;
    }
    int st = lcu_http(L"POST", qpath, "{}", NULL, 0);
    if (st == 404)
        st = lcu_http(L"POST", L"/lol-champ-select/v1/session/quit", "{}", NULL, 0);
    g_quit_sent = 1;
    set_action(L"已秒退（%ls）-> HTTP %d", g_quit_reason, st);
}

static void swaps_step(void)
{
    if (g_cfg.swap_policy <= 0) return;
    static char buf[4096];
    if (lcu_http(L"GET", L"/lol-champ-select/v1/session/champion-swaps", NULL, buf, sizeof(buf)) != 200)
        return;
    const char *end = buf + lstrlenA(buf);
    const char *p = buf;
    while (p < end) {
        const char *ob = strchr(p, '{');
        if (!ob || ob >= end) break;
        const char *oe = strchr(ob, '}');
        if (!oe || oe > end) break;
        int id = win_int(ob, oe + 1, "id", -1);
        int state_id = win_int(ob, oe + 1, "stateId", -1);
        static int done_ids[8];
        static int done_n;
        BOOL already = FALSE;
        for (int hi = 0; hi < done_n; hi++) if (done_ids[hi] == id) already = TRUE;
        if (id >= 0 && state_id != 4 && !already) {
            if (done_n < 8) done_ids[done_n++] = id;
            else { for (int hi = 1; hi < 8; hi++) done_ids[hi - 1] = done_ids[hi]; done_ids[7] = id; }
            WCHAR path[200];
            char apath[200];
            wsprintfA(apath, "/lol-champ-select/v1/session/champion-swaps/%d/%s", id,
                      g_cfg.swap_policy == 1 ? "accept" : "decline");
            MultiByteToWideChar(CP_ACP, 0, apath, -1, path, 200);
            int st = lcu_http(L"POST", path, "{}", NULL, 0);
            set_action(L"换英雄 #%d %ls -> HTTP %d", id,
                       g_cfg.swap_policy == 1 ? L"接受" : L"拒绝", st);
        }
        p = oe + 1;
    }
}

/* mastery points for champion id from /lol-champion-mastery/v1/local-player/champion-mastery */
static int mastery_of(const char *json, int champion_id)
{
    const char *end = json + lstrlenA(json);
    const char *p = json;
    while (p < end) {
        const char *ob = strchr(p, '{');
        if (!ob || ob >= end) break;
        const char *oe = strchr(ob, '}');
        if (!oe || oe > end) break;
        if (win_int(ob, oe + 1, "championId", -1) == champion_id)
            return win_int(ob, oe + 1, "championPoints", 0);
        p = oe + 1;
    }
    return 0;
}

static void bench_swap_step(const char *session, BOOL manual)
{
    if (!manual) {
        if (g_bench_done) return;
        if (!g_cfg.auto_bench_swap) return;
    }
    int bench[10];
    int bench_n = 0;
    const char *b = strstr(session, "\"benchChampionIds\"");
    if (!b) return;
    const char *arr = strchr(b, '[');
    if (!arr) return;
    const char *p = arr + 1;
    while (*p && *p != ']' && bench_n < 10) {
        while (*p == ' ' || *p == ',') p++;
        if (*p < '0' || *p > '9') break;
        int v = atoi(p);
        if (v > 0) bench[bench_n++] = v;
        while (*p && *p != ',' && *p != ']') p++;
    }
    if (!bench_n) {
        if (manual) set_action(L"Bench 里没有英雄可换");
        g_bench_done = manual ? g_bench_done : 1;
        return;
    }

    static char body[65536];
    int best = 0, best_pts = -1;
    if (lcu_http(L"GET", L"/lol-champion-mastery/v1/local-player/champion-mastery",
                 NULL, body, sizeof(body)) == 200) {
        for (int i = 0; i < bench_n; i++) {
            int pts = mastery_of(body, bench[i]);
            if (pts > best_pts) { best_pts = pts; best = bench[i]; }
        }
    }
    if (best <= 0) {
        best = bench[0];
        best_pts = 0;
    }
    if (!manual && g_cfg.bench_min_points > 0 && best_pts < g_cfg.bench_min_points) {
        logf_("bench: best %d has %d pts < %d, skip", best, best_pts, g_cfg.bench_min_points);
        g_bench_done = 1;
        return;
    }
    WCHAR path[160];
    char apath[160];
    wsprintfA(apath, "/lol-champ-select/v1/session/bench/swap/%d", best);
    MultiByteToWideChar(CP_ACP, 0, apath, -1, path, 160);
    int st = lcu_http(L"POST", path, "{}", NULL, 0);
    g_bench_done = 1;
    g_bench_pick = best;
    g_bench_points = best_pts;
    set_action(L"换到 bench 英雄 %d（熟练度 %d）-> HTTP %d", best, best_pts, st);
}

/* invite up to N online friends into the current lobby */
static void invite_online_friends(int max_invites)
{
    static char body[65536];
    if (lcu_http(L"GET", L"/lol-chat/v1/friends", NULL, body, sizeof(body)) != 200) {
        set_action(L"邀请：拿不到好友列表");
        return;
    }
    int ids[8], n = 0;
    const char *end = body + lstrlenA(body);
    const char *p = body;
    while (p < end && n < 8) {
        const char *ob = strchr(p, '{');
        if (!ob || ob >= end) break;
        const char *oe = strchr(ob, '}');
        if (!oe || oe > end) break;
        int sid = win_int(ob, oe + 1, "summonerId", 0);
        char avail[24] = { 0 };
        win_str(ob, oe + 1, "availability", avail, sizeof(avail));
        BOOL online = (lstrcmpiA(avail, "offline") != 0) && (lstrcmpiA(avail, "mobile") != 0);
        if (sid > 0 && online) {
            BOOL dup = FALSE;
            for (int i = 0; i < n; i++) if (ids[i] == sid) dup = TRUE;
            if (!dup) ids[n++] = sid;
        }
        p = oe + 1;
    }
    if (n == 0) { set_action(L"邀请：没有在线好友"); return; }

    int sent = 0;
    for (int i = 0; i < n && sent < max_invites; i++) {
        char jb[128];
        wsprintfA(jb, "[{\"toSummonerId\":%d}]", ids[i]);
        int st = lcu_http(L"POST", L"/lol-lobby/v2/lobby/invitations", jb, NULL, 0);
        if (st == 200 || st == 201 || st == 204) sent++;
        logf_("invite %d -> HTTP %d", ids[i], st);
    }
    set_action(L"邀请在线好友 %d/%d", sent, n);
}

/* invite the summonerIds that showed up in my most recent game */
static void invite_recent_teammates(int max_invites)
{
    static char me[8192];
    if (lcu_http(L"GET", L"/lol-summoner/v1/current-summoner", NULL, me, sizeof(me)) != 200) {
        set_action(L"邀请：拿不到自己的信息");
        return;
    }
    char puuid[96] = { 0 };
    win_str(me, me + lstrlenA(me), "puuid", puuid, sizeof(puuid));
    if (!puuid[0]) { set_action(L"邀请：没有 puuid"); return; }

    static char ml[65536];
    WCHAR path[320];
    char ap[320];
    wsprintfA(ap, "/lol-match-history/v3/matchlist/account/%s?begIndex=0&endIndex=0", puuid);
    MultiByteToWideChar(CP_ACP, 0, ap, -1, path, 320);
    if (lcu_http(L"GET", path, NULL, ml, sizeof(ml)) != 200) {
        set_action(L"邀请：拿不到最近对局");
        return;
    }
    int my_sid = json_int(me, "summonerId");
    int ids[8], n = 0;
    /* participants objects nest a stats object, so walk the key directly */
    const char *q = ml;
    while ((q = strstr(q, "\"summonerId\"")) != NULL && n < 8) {
        const char *v = strchr(q, ':');
        if (v) {
            int sid = atoi(v + 1);
            if (sid > 0 && sid != my_sid) {
                BOOL dup = FALSE;
                for (int i = 0; i < n; i++) if (ids[i] == sid) dup = TRUE;
                if (!dup) ids[n++] = sid;
            }
        }
        q += 11;
    }
    if (!n) { set_action(L"邀请：最近对局里没有可邀请的人"); return; }

    int sent = 0;
    for (int i = 0; i < n && sent < max_invites; i++) {
        char jb[128];
        wsprintfA(jb, "[{\"toSummonerId\":%d}]", ids[i]);
        int st = lcu_http(L"POST", L"/lol-lobby/v2/lobby/invitations", jb, NULL, 0);
        if (st == 200 || st == 201 || st == 204) sent++;
        logf_("invite recent %d -> HTTP %d", ids[i], st);
    }
    set_action(L"邀请最近队友 %d/%d", sent, n);
}

/* best-effort mute: LCU's chat API is the only muting surface; probe and report */
static void mute_all_probe(void)
{
    static char body[8192];
    if (lcu_http(L"GET", L"/lol-chat/v1/conversations", NULL, body, sizeof(body)) != 200) {
        set_action(L"静音：拿不到会话列表");
        return;
    }
    const char *p = body;
    char cid[96] = { 0 };
    while ((p = strstr(p, "\"id\"")) != NULL) {
        if (win_str(p, body + lstrlenA(body), "id", cid, sizeof(cid)) && cid[0]) break;
        p++;
    }
    if (!cid[0]) { set_action(L"静音：没有会话"); return; }
    char apath[200];
    wsprintfA(apath, "/lol-chat/v1/conversations/%s/mute", cid);
    WCHAR path[220];
    MultiByteToWideChar(CP_ACP, 0, apath, -1, path, 220);
    int st = lcu_http(L"POST", path, "{\"muted\":true}", NULL, 0);
    logf_("mute probe %s -> HTTP %d", apath, st);
    if (st == 200 || st == 204) set_action(L"静音会话成功 (HTTP %d)", st);
    else set_action(L"静音端点不可用 (HTTP %d) — 记录跳过", st);
}

static void champ_select_step(void)
{
    if (lstrcmpW(g_phase, L"ChampSelect") != 0) {
        g_cs_last_action = -1;
        g_cs_active = 0;
        g_ally_done = 0;
        g_ally_count = 0;
        g_ban_next = 0;
        g_handled_n = 0;
        intel_reset();
        quit_reset();
        g_bench_done = 0;
        g_pending_action = -1;
        g_my_champ = 0;
        g_runes_applied_for = 0;
        g_spells_applied_for = 0;
        return;
    }
    static char buf[16384];
    if (lcu_http(L"GET", L"/lol-champ-select/v1/session", NULL, buf, sizeof(buf)) != 200) {
        g_cs_active = 0;
        return;
    }
    g_cs_active = 1;
    g_cs_cell = json_int(buf, "localPlayerCellId");
    {
        int ms = json_int(buf, "adjustedTimeLeftInPhase");
        if (ms <= 0) ms = json_int(buf, "timeLeftInPhase");
        g_cs_left = (ms > 0) ? ms / 1000 : -1;
    }

    char mypos[24] = { 0 };
    my_cell_info(buf, g_cs_cell, mypos, sizeof(mypos), &g_my_champ);
    if (mypos[0]) lstrcpynA(g_my_pos, mypos, sizeof(g_my_pos));
    if (g_intel_n == 0) intel_collect(buf);
    intel_fetch_step();
    dodge_eval();
    bench_swap_step(buf, FALSE);
    {
        static DWORD last_swap;
        if (GetTickCount() - last_swap > 3000) { last_swap = GetTickCount(); swaps_step(); }
    }

    /* champion to pick: position map first, then the global default */
    int pick_champ = g_cfg.pick_champ;
    if (g_cfg.pick_by_pos) {
        int m = pos_champ_of(mypos);
        if (m > 0) pick_champ = m;
    }
    /* champion to ban: rotate through ban_list, else the single ban_champ */
    int ban_champ = g_cfg.ban_champ;
    if (g_cfg.ban_count > 0) ban_champ = g_cfg.ban_ids[g_ban_next % g_cfg.ban_count];

    CsDecision d[4];
    int n = cs_decide(buf, g_cs_cell, g_cfg.auto_pick && pick_champ > 0, g_cfg.auto_ban, d, 4);
    for (int i = 0; i < n; i++) {
        if (was_handled(d[i].action_id)) continue;
        int champ;
        if (d[i].is_ban) {
            /* 跳过队友（含自己）已预选的英雄，避免自动 ban 误伤 */
            int cand = 0;
            int n2 = g_cfg.ban_count > 0 ? g_cfg.ban_count : 1;
            for (int t = 0; t < n2; t++) {
                int idx = (g_ban_next + t) % n2;
                int c2 = g_cfg.ban_count > 0 ? g_cfg.ban_ids[idx] : ban_champ;
                if (c2 > 0 && !ally_wants_champion(buf, c2)) { cand = c2; g_ban_next += t; break; }
            }
            if (cand <= 0) {   /* 全部被队友预选占满，只能按原顺序来 */
                cand = g_cfg.ban_count > 0 ? g_cfg.ban_ids[g_ban_next % n2] : ban_champ;
                set_action(L"队友预选占满 ban 列表，仍 ban %d", cand);
            }
            champ = cand;
        } else {
            champ = pick_champ;
        }
        if (champ <= 0) continue;
        d[i].champion_id = champ;
        WCHAR path[160];
        char apath[160], body[64];
        wsprintfA(apath, "/lol-champ-select/v1/session/actions/%d", d[i].action_id);
        MultiByteToWideChar(CP_ACP, 0, apath, -1, path, 160);
        wsprintfA(body, "{\"championId\":%d}", d[i].champion_id);
        int s1 = lcu_http(L"PATCH", path, body, NULL, 0);
        g_cs_last_action = d[i].action_id;
        mark_handled(d[i].action_id);
        if (d[i].is_ban) {
            wsprintfA(apath, "/lol-champ-select/v1/session/actions/%d/complete", d[i].action_id);
            MultiByteToWideChar(CP_ACP, 0, apath, -1, path, 160);
            int s2 = lcu_http(L"POST", path, "{}", NULL, 0);
            g_ban_next++;
            set_action(L"自动 ban %d -> %d/%d", d[i].champion_id, s1, s2);
        } else if (g_cfg.lockin_delay_s > 0) {
            g_pending_action = d[i].action_id;
            g_pending_at = GetTickCount() + (DWORD)g_cfg.lockin_delay_s * 1000;
            set_action(L"已选中 %d，%d 秒后锁定 (HTTP %d)", d[i].champion_id, g_cfg.lockin_delay_s, s1);
        } else {
            wsprintfA(apath, "/lol-champ-select/v1/session/actions/%d/complete", d[i].action_id);
            MultiByteToWideChar(CP_ACP, 0, apath, -1, path, 160);
            int s2 = lcu_http(L"POST", path, "{}", NULL, 0);
            set_action(L"自动选中 %d -> %d/%d", d[i].champion_id, s1, s2);
        }
    }

}

/* read myTeam[] from the champ-select payload and fetch name/level/rank per ally */
static void allies_step(const char *session_json)
{
    if (g_ally_done) return;
    const char *mt = strstr(session_json, "\"myTeam\"");
    if (!mt) return;
    const char *start = strchr(mt, '[');
    if (!start) return;
    const char *end = strstr(start, "\"theirTeam\"");
    if (!end) end = session_json + lstrlenA(session_json);

    g_ally_done = 1;
    g_ally_count = 0;
    const char *p = start;
    while (p < end && g_ally_count < 5) {
        const char *ob = strchr(p, '{');
        if (!ob || ob >= end) break;
        const char *oe = strchr(ob, '}');
        if (!oe || oe > end) break;
        int cell = win_int(ob, oe + 1, "cellId", -1);
        char puuid[128] = { 0 };
        int sid = win_int(ob, oe + 1, "summonerId", 0);
        win_str(ob, oe + 1, "puuid", puuid, sizeof(puuid));
        if (cell >= 0 && (puuid[0] || sid > 0)) {
            WCHAR path[256], nm[64] = L"?", tag[24] = L"", tier[24] = L"", div[16] = L"";
            static char body[8192];
            int lv = 0, lp = 0;
            if (puuid[0]) {
                char ap0[320];
                wsprintfA(ap0, "/lol-summoner/v2/summoners/puuid/%s", puuid);
                MultiByteToWideChar(CP_ACP, 0, ap0, -1, path, 256);
                if (lcu_http(L"GET", path, NULL, body, sizeof(body)) == 200) {
                    json_str(body, "gameName", nm, 64);
                    json_str(body, "gameTag", tag, 24);
                    lv = json_int(body, "summonerLevel");
                }
                {
                    char ap[320];
                    wsprintfA(ap, "/lol-ranked/v1/ranked-stats/%s", puuid);
                    MultiByteToWideChar(CP_ACP, 0, ap, -1, path, 256);
                    if (lcu_http(L"GET", path, NULL, body, sizeof(body)) == 200) {
                        json_str(body, "tier", tier, 24);
                        json_str(body, "division", div, 16);
                        lp = json_int(body, "leaguePoints");
                    }
                }
            } else {
                char ap[160];
                wsprintfA(ap, "/lol-summoner/v1/summoners/%d", sid);
                MultiByteToWideChar(CP_ACP, 0, ap, -1, path, 256);
                if (lcu_http(L"GET", path, NULL, body, sizeof(body)) == 200) {
                    json_str(body, "gameName", nm, 64);
                    lv = json_int(body, "summonerLevel");
                }
            }
            WCHAR line[96];
            if (tier[0])
                wsprintfW(line, L"队%d  %ls%ls  Lv%d  %ls %ls %dLP", cell,
                          nm, tag[0] ? L"" : L"", lv, tier, div, lp);
            else
                wsprintfW(line, L"队%d  %ls  Lv%d  （未定级）", cell, nm, lv);
            if (tag[0]) {
                WCHAR withtag[96];
                wsprintfW(withtag, L"队%d  %ls#%ls  Lv%d  %ls %ls %dLP", cell, nm, tag, lv,
                          tier[0] ? tier : L"-", div, lp);
                lstrcpynW(g_ally[g_ally_count], withtag, 96);
            } else {
                lstrcpynW(g_ally[g_ally_count], line, 96);
            }
            g_ally_count++;
        }
        p = oe + 1;
    }
    if (g_ally_count) {
        logf_("allies: %d fetched", g_ally_count);
        if (g_hud) InvalidateRect(g_hud, NULL, FALSE);
    }
}

/* ---------- runes + summoner spells ---------- */
static int g_runes_applied;
static int g_spells_applied;

static const char *find_name_value(const char *json, const char *name)
{
    int nlen = lstrlenA(name);
    const char *p = json;
    while ((p = strstr(p, "\"name\"")) != NULL) {
        const char *v = p + 6;
        while (*v == ' ' || *v == '\t') v++;
        if (*v == ':') {
            v++;
            while (*v == ' ' || *v == '\t') v++;
            if (*v == '"') {
                v++;
                if (strncmp(v, name, nlen) == 0 && v[nlen] == '"') return v;
            }
        }
        p += 6;
    }
    return NULL;
}

static int find_page_id(const char *json, const char *page_name)
{
    if (!json || !page_name || !*page_name) return -1;
    const char *hit = find_name_value(json, page_name);
    if (!hit) return -1;
    const char *end = json + lstrlenA(json);
    const char *a = hit;
    while (a > json && *a != '{') a--;
    const char *b = hit;
    while (b < end && *b && *b != '}') b++;
    if (*b == '}') b++;
    if (*a != '{') return -1;
    return win_int(a, b + 1, "id", -1);
}

static void runes_step(void)
{
    if (lstrcmpW(g_phase, L"ChampSelect") != 0) { g_runes_applied = 0; g_spells_applied = 0; return; }
    if (!g_cfg.auto_runes || g_runes_applied) return;
    if (g_runes_applied_for != g_my_champ) g_runes_applied = 0;   /* champion changed: re-apply */

    const char *page_name = g_cfg.rune_page;
    for (int i = 0; i < g_cfg.rune_map_n; i++)
        if (g_cfg.rune_map_champ[i] == g_my_champ) { page_name = g_cfg.rune_map_page[i]; break; }
    if (!page_name || !*page_name) return;

    static char pages[16384];
    if (lcu_http(L"GET", L"/lol-perks/v1/pages", NULL, pages, sizeof(pages)) != 200) return;
    int id = find_page_id(pages, page_name);
    if (id <= 0) {
        /* fall back to the currently active page */
        id = -1;
        const char *act = strstr(pages, "\"isActive\"");
        const char *end0 = pages + lstrlenA(pages);
        while (act) {
            const char *v = strchr(act, ':');
            if (v) {
                v++;
                while (*v == ' ' || *v == '\t') v++;
                if (strncmp(v, "true", 4) == 0) break;
            }
            act = strstr(act + 8, "\"isActive\"");
            if (act > end0) { act = NULL; break; }
        }
        if (act) {
            const char *a = act, *end = pages + lstrlenA(pages), *b = act;
            while (a > pages && *a != '{') a--;
            while (b < end && *b && *b != '}') b++;
            if (*a == '{') id = win_int(a, b + 1, "id", -1);
        }
    }
    if (id <= 0) return;
    char body[32];
    wsprintfA(body, "%d", id);
    int st = lcu_http(L"PUT", L"/lol-perks/v1/currentpage", body, NULL, 0);
    if (st == 200 || st == 204) {
        g_runes_applied = 1;
        g_runes_applied_for = g_my_champ;
        set_action(L"符文页 %d(%d) 已应用 (HTTP %d)", id, g_my_champ, st);
    } else {
        g_runes_applied = 1;   /* do not hammer the endpoint */
        set_action(L"符文页失败 (HTTP %d)", st);
    }
}

static void spells_step(void)
{
    if (lstrcmpW(g_phase, L"ChampSelect") != 0) { g_spells_applied = 0; return; }
    if (!g_cfg.auto_spells || g_spells_applied) return;
    if (g_spells_applied_for != g_my_champ) g_spells_applied = 0;

    int s1 = g_cfg.spell1, s2 = g_cfg.spell2;
    for (int i = 0; i < g_cfg.spell_map_n; i++)
        if (g_cfg.spell_map_champ[i] == g_my_champ) {
            s1 = g_cfg.spell_map_s1[i];
            s2 = g_cfg.spell_map_s2[i];
            break;
        }
    if (s1 <= 0 || s2 <= 0) return;
    char body[96];
    wsprintfA(body, "{\"spell1Id\":%d,\"spell2Id\":%d}", s1, s2);
    int st = lcu_http(L"PATCH", L"/lol-champ-select/v1/session/my-selection", body, NULL, 0);
    if (st == 200 || st == 204) {
        g_spells_applied = 1;
        g_spells_applied_for = g_my_champ;
        set_action(L"召唤师技能 %d/%d 已设置 (HTTP %d)", s1, s2, st);
    }
}

/* ---------- post game ---------- */
static int g_honored_game = -1;
static int g_played_again;

static void honor_step_once(void)
{
    static char b[8192];
    if (lcu_http(L"GET", L"/lol-honor-v2/v1/ballot", NULL, b, sizeof(b)) != 200) {
        set_action(L"点赞：无可用 ballot");
        return;
    }
    int sid = json_int(b, "summonerId");
    char puuid[128] = { 0 };
    if (sid <= 0 || !win_str(b, b + lstrlenA(b), "puuid", puuid, sizeof(puuid))) {
        set_action(L"点赞：没有可点赞的队友");
        return;
    }
    char body[300];
    wsprintfA(body, "{\"summonerId\":%d,\"puuid\":\"%s\",\"position\":\"NONE\"}", sid, puuid);
    int st = lcu_http(L"POST", L"/lol-honor-v2/v1/honor-player", body, NULL, 0);
    set_action(L"点击点赞 -> HTTP %d", st);
}

static void honor_step(void)
{
    if (!g_cfg.auto_honor) return;
    if (lstrcmpW(g_phase, L"EndOfGame") != 0 && lstrcmpW(g_phase, L"PreEndOfGame") != 0) return;
    static char buf[8192];
    if (lcu_http(L"GET", L"/lol-honor-v2/v1/ballot", NULL, buf, sizeof(buf)) != 200) return;
    int game = json_int(buf, "gameId");
    if (game != 0 && game == g_honored_game) return;
    int sid = json_int(buf, "summonerId");
    char puuid[128] = { 0 };
    if (sid <= 0 || !win_str(buf, buf + lstrlenA(buf), "puuid", puuid, sizeof(puuid))) return;
    char body[300];
    wsprintfA(body, "{\"summonerId\":%d,\"puuid\":\"%s\",\"position\":\"NONE\"}", sid, puuid);
    int st = lcu_http(L"POST", L"/lol-honor-v2/v1/honor-player", body, NULL, 0);
    g_honored_game = game;
    set_action(L"自动点赞 -> HTTP %d", st);
}

static void play_again_step(void)
{
    if (!g_cfg.auto_play_again) return;
    if (lstrcmpW(g_phase, L"EndOfGame") != 0) { g_played_again = 0; return; }
    if (g_played_again) return;
    int st = lcu_http(L"POST", L"/lol-lobby/v2/play-again", "{}", NULL, 0);
    g_played_again = 1;
    set_action(L"自动再来一局 -> HTTP %d", st);
}

/* ---------- IPC ---------- */
static void apply_text(const WCHAR *text)
{
    g_override_count = 0;
    int idx = 0, n = 0;
    for (const WCHAR *p = text;; p++) {
        if (*p == 0 || *p == L'\n' || n >= LINE_LEN - 1) {
            if (n > 0 && idx < MAX_LINES) { g_override[idx][n] = 0; idx++; }
            n = 0;
            if (*p == 0) break;
            continue;
        }
        if (idx >= MAX_LINES) break;
        g_override[idx][n++] = *p;
    }
    g_override_count = idx;
    g_dirty = 1;
    if (g_hud) InvalidateRect(g_hud, NULL, FALSE);
}

static LRESULT CALLBACK ipc_proc(HWND h, UINT msg, WPARAM wp, LPARAM lp)
{
    switch (msg) {
    case WM_COPYDATA: {
        COPYDATASTRUCT *cds = (COPYDATASTRUCT *)lp;
        if (!cds || !cds->lpData || cds->cbData < sizeof(DWORD) * 2) return 0;
        GGmanHudCommand *cmd = (GGmanHudCommand *)cds->lpData;
        cmd->text[TEXT_LEN - 1] = 0;
        switch (cmd->kind) {
        case 1: apply_text(cmd->text); logf_("ipc text %d lines", g_override_count); break;
        case 2:
            g_visible = (cmd->flags == 2) ? !g_visible : (cmd->flags ? 1 : 0);
            reposition();
            break;
        case 3: apply_text(L""); break;
        case 5:
            g_port = (int)cmd->flags;
            WideCharToMultiByte(CP_ACP, 0, cmd->text, -1, g_token, sizeof(g_token), NULL, NULL);
            logf_("ipc session port=%d tokenlen=%d", g_port, (int)lstrlenW(cmd->text));
            break;
        case 6:
            g_armed = (cmd->flags == 2) ? !g_armed : (cmd->flags ? 1 : 0);
            set_action(L"自动接受 %ls", g_armed ? L"已开启" : L"已关闭");
            break;
        case 8:
            run_action((int)cmd->flags);
            break;
        case 7: {
            int code = lcu_http(L"POST", ACCEPT_PATH, "{}", NULL, 0);
            set_action(L"手动接受 -> HTTP %d", code);
            break;
        }
        }
        return 1;
    }
    case WM_TIMER:
        if (wp == 3) {
            reposition();
            badge_reposition();
            if (GetTickCount() > g_move_until) KillTimer(g_ipc, 3);
            return 0;
        }
        if (wp == 2) {
            if (!g_host || !IsWindow(g_host)) { g_host = find_host(); install_move_hook(); }
            reposition();
            badge_reposition();
            refresh();
        }
        return 0;
    case WM_HOTKEY:
        if (wp == 1) { g_visible = !g_visible; reposition(); logf_("hotkey vis=%d", g_visible); }
        else if (wp == 2) { g_armed = !g_armed; set_action(L"自动接受 %ls", g_armed ? L"已开启" : L"已关闭"); }
        else if (wp == 3) { int c = lcu_http(L"POST", ACCEPT_PATH, "{}", NULL, 0); set_action(L"手动接受 -> HTTP %d", c); }
        else if (wp == 4) {
            g_compact = !g_compact;
            g_cfg.ui_compact = g_compact;
            config_save();
            g_dirty = 1;
            reposition();
            if (g_hud) InvalidateRect(g_hud, NULL, FALSE);
            logf_("hotkey F7 compact=%d", g_compact);
        }
        return 0;
    case WM_CLOSE:
        logf_("WM_CLOSE, tearing down");
        if (g_hud) { DestroyWindow(g_hud); g_hud = NULL; }
        DestroyWindow(h);
        return 0;
    case WM_DESTROY:
        PostQuitMessage(0);
        return 0;
    }
    return DefWindowProcW(h, msg, wp, lp);
}


/* ---------------- loopback console (HTTP/1.1, GET only + /api/action) ---------------- */
static void json_esc(char *dst, const char *src, int cch)
{
    int o = 0;
    for (const char *p = src; p && *p && o < cch - 2; p++) {
        unsigned char c = (unsigned char)*p;
        if (c == '"' || c == '\\') { dst[o++] = '\\'; dst[o++] = (char)c; }
        else if (c == '\n') { dst[o++] = ' '; }
        else if (c < 0x20) { dst[o++] = ' '; }
        else dst[o++] = (char)c;
    }
    dst[o] = 0;
}

static void json_wesc(char *dst, const WCHAR *src, int cch)
{
    char tmp[256];
    WideCharToMultiByte(CP_UTF8, 0, src, -1, tmp, sizeof(tmp), NULL, NULL);
    json_esc(dst, tmp, cch);
}

static int http_build_state(char *out, int cch)
{
    char name[256], rank[128], act[256], pos[64], reason[256];
    json_wesc(name, g_name, sizeof(name));
    json_wesc(rank, g_self_rank, sizeof(rank));
    json_wesc(act, g_last_action, sizeof(act));
    json_wesc(pos, L"", sizeof(pos));
    char mpos[64];
    json_esc(mpos, g_my_pos, sizeof(mpos));
    json_wesc(reason, g_quit_reason, sizeof(reason));

    int n = wsprintfA(out,
        "{\"phase\":\"%ls\",\"summoner\":\"%s\",\"level\":%d,\"rank\":\"%s\","
        "\"queue_secs\":%d,\"cs_left\":%d,\"cs_cell\":%d,\"my_pos\":\"%s\","
        "\"quit_advice\":%d,\"quit_reason\":\"%s\",\"last_action\":\"%s\","
        "\"intel_total\":%d,\"known_count\":%d,\"pid\":%d,\"port\":%d,\"intel\":[",
        g_phase, name, g_level, rank, g_queue_secs, g_cs_left, g_cs_cell, mpos,
        g_quit_advice, reason, act, g_intel_n, g_known_n, g_pid_hud, g_port);

    for (int i = 0; i < g_intel_n && n < cch - 320; i++) {
        IntelEntry *e = &g_intel[i];
        char nm[128], rk[64], ps[32];
        json_wesc(nm, e->name, sizeof(nm));
        json_wesc(rk, e->rank, sizeof(rk));
        json_esc(ps, e->pos, sizeof(ps));
        BOOL matchup = (e->team == 2 && g_my_pos[0] && lstrcmpiA(e->pos, g_my_pos) == 0);
        n += wsprintfA(out + n,
            "%s{\"team\":%d,\"pos\":\"%s\",\"name\":\"%s\",\"rank\":\"%s\",\"games\":%d,\"wins\":%d,"
            "\"streak\":%d,\"top\":[%d,%d,%d],\"duo\":%d,\"known\":%d,\"matchup\":%d}",
            i ? "," : "", e->team, ps, nm, rk, e->games, e->wins,
            e->streak, e->top[0], e->top[1], e->top[2],
            e->duo_score, e->known, matchup ? 1 : 0);
    }
    n += wsprintfA(out + n, "]}");
    return n;
}

static BOOL http_read_file(const char *path, char *out, int cch)
{
    HANDLE f = CreateFileA(path, GENERIC_READ, FILE_SHARE_READ, NULL, OPEN_EXISTING,
                           FILE_ATTRIBUTE_NORMAL, NULL);
    if (f == INVALID_HANDLE_VALUE) return FALSE;
    DWORD got = 0;
    BOOL ok = ReadFile(f, out, (DWORD)cch - 1, &got, NULL);
    CloseHandle(f);
    if (!ok) return FALSE;
    out[got] = 0;
    return TRUE;
}

static void http_reply(SOCKET c, const char *status, const char *ctype, const char *body, int len)
{
    char head[256];
    int n = wsprintfA(head, "HTTP/1.1 %s\r\nContent-Type: %s\r\nContent-Length: %d\r\n"
                            "Cache-Control: no-store\r\nConnection: close\r\n\r\n", status, ctype, len);
    send(c, head, n, 0);
    if (len > 0) send(c, body, len, 0);
}

static DWORD WINAPI gg_http_thread(LPVOID param)
{
    (void)param;
    WSADATA wsa;
    if (WSAStartup(MAKEWORD(2, 2), &wsa) != 0) { logf_("http: WSAStartup failed"); return 0; }
    SOCKET srv = socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
    if (srv == INVALID_SOCKET) { logf_("http: socket failed"); WSACleanup(); return 0; }
    int yes = 1;
    setsockopt(srv, SOL_SOCKET, SO_REUSEADDR, (const char *)&yes, sizeof(yes));
    struct sockaddr_in a;
    memset(&a, 0, sizeof(a));
    a.sin_family = AF_INET;
    a.sin_port = htons((u_short)g_cfg.http_port);
    a.sin_addr.s_addr = inet_addr("127.0.0.1");
    if (bind(srv, (struct sockaddr *)&a, sizeof(a)) != 0) {
        logf_("http: bind 127.0.0.1:%d failed (%d)", g_cfg.http_port, WSAGetLastError());
        closesocket(srv);
        WSACleanup();
        return 0;
    }
    listen(srv, 8);
    logf_("http: console on http://127.0.0.1:%d/", g_cfg.http_port);

    char *req = (char *)malloc(16384);
    char *body = (char *)malloc(65536);
    if (!req || !body) { closesocket(srv); WSACleanup(); return 0; }

    for (;;) {
        SOCKET c = accept(srv, NULL, NULL);
        if (c == INVALID_SOCKET) break;
        DWORD tv = 3000;
        setsockopt(c, SOL_SOCKET, SO_RCVTIMEO, (const char *)&tv, sizeof(tv));
        int got = recv(c, req, 16383, 0);
        if (got <= 0) { closesocket(c); continue; }
        req[got] = 0;

        if (strncmp(req, "GET /api/state", 14) == 0) {
            int n = http_build_state(body, 65535);
            http_reply(c, "200 OK", "application/json; charset=utf-8", body, n);
        } else if (strncmp(req, "GET /api/action", 15) == 0 ||
                   strncmp(req, "POST /api/action", 16) == 0) {
            const char *q = strstr(req, "id=");
            int id = q ? atoi(q + 3) : -1;
            if (id >= 0 && id <= 10) {
                run_action(id);
                http_reply(c, "200 OK", "application/json", "{\"ok\":true}", 11);
            } else {
                http_reply(c, "400 Bad Request", "application/json", "{\"ok\":false}", 12);
            }
        } else if (strncmp(req, "GET /console.css", 16) == 0) {
            if (http_read_file("D:\\project2\\ggman\\assets\\console.css", body, 65535))
                http_reply(c, "200 OK", "text/css; charset=utf-8", body, lstrlenA(body));
            else
                http_reply(c, "404 Not Found", "text/plain", "no css", 6);
        } else if (strncmp(req, "GET / ", 6) == 0 || strncmp(req, "GET /login", 10) == 0) {
            if (http_read_file("D:\\project2\\ggman\\assets\\console.html", body, 65535))
                http_reply(c, "200 OK", "text/html; charset=utf-8", body, lstrlenA(body));
            else
                http_reply(c, "200 OK", "text/html; charset=utf-8",
                           "<h1>GGman</h1><p>console.html missing</p>", 38);
        } else {
            http_reply(c, "404 Not Found", "text/plain", "not found", 9);
        }
        shutdown(c, SD_SEND);
        closesocket(c);
    }
    free(req);
    free(body);
    closesocket(srv);
    WSACleanup();
    return 0;
}

/* if the LCU loop ever stalls (slow request / dead host), take the overlay out of the way */
static DWORD WINAPI watchdog_thread(LPVOID param)
{
    (void)param;
    for (;;) {
        Sleep(1000);
        DWORD last = g_last_tick_done;
        if (!last) continue;
        DWORD idle = GetTickCount() - last;
        if (g_hud && g_panel_vis && idle > 4000) {
            ShowWindow(g_hud, SW_HIDE);
            g_panel_vis = 0;
            logf_("watchdog: hid panel, loop stalled %lu ms", (unsigned long)idle);
        }
    }
    return 0;
}

static DWORD WINAPI init_thread(LPVOID param)
{
    (void)param;
    init_log_path();
    lstrcpynA(g_ini_path, "D:\\project2\\ggman\\dll\\build\\agent.ini", MAX_PATH);
    config_load();
    g_compact = g_cfg.ui_compact ? 1 : 0;
    known_load();
    g_armed = g_cfg.auto_accept ? 1 : 0;
    logf_("config: hud=%d accept=%d pick=%d/%d ban=%d/%d honor=%d again=%d",
          g_cfg.hud, g_cfg.auto_accept, g_cfg.auto_pick, g_cfg.pick_champ,
          g_cfg.auto_ban, g_cfg.ban_champ, g_cfg.auto_honor, g_cfg.auto_play_again);
    g_pid_hud = (int)GetCurrentProcessId();
    g_start_tick = GetTickCount();
    logf_("=== ggman_core init pid=%d ===", g_pid_hud);

    /* master switch */
    if (!g_cfg.agent_enabled) {
        logf_("agent disabled by config (agent_enabled=0); no windows created");
        return 0;
    }
    /* hard guard: if any live ggman window exists (this or another process), stay passive */
    if (FindWindowW(BTN_CLASS, NULL) || FindWindowW(HUD_CLASS, NULL) || FindWindowW(IPC_CLASS, NULL)) {
        logf_("existing ggman windows found; this instance stays passive (no duplicate badge)");
        return 0;
    }
    /* secondary guard: named mutex, never steal ownership from a live instance */
    {
        HANDLE owner = CreateMutexW(NULL, TRUE, L"Local\\GGmanHudOwner");
        if (owner && GetLastError() == ERROR_ALREADY_EXISTS) {
            DWORD w = WaitForSingleObject(owner, 3000);
            if (w != WAIT_OBJECT_0) {
                logf_("mutex still held by a live instance; staying passive");
                return 0;
            }
            logf_("took over abandoned mutex");
        }
    }

    WNDCLASSEXW wc = { 0 };
    wc.cbSize = sizeof(wc);
    wc.lpfnWndProc = ipc_proc;
    wc.hInstance = g_inst;
    wc.lpszClassName = IPC_CLASS;
    if (!RegisterClassExW(&wc)) { logf_("ipc class failed %lu", GetLastError()); return 0; }
    g_ipc = CreateWindowExW(0, IPC_CLASS, L"ggman_core_ipc", WS_POPUP, 0, 0, 0, 0, NULL, NULL, g_inst, NULL);
    if (!g_ipc) { logf_("ipc window failed %lu", GetLastError()); return 0; }

    WNDCLASSEXW hc = { 0 };
    hc.cbSize = sizeof(hc);
    hc.lpfnWndProc = hud_proc;
    hc.hInstance = g_inst;
    hc.lpszClassName = HUD_CLASS;
    if (!RegisterClassExW(&hc)) { logf_("hud class failed %lu", GetLastError()); return 0; }
    g_hud = CreateWindowExW(WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST,
                            HUD_CLASS, L"GGman Agent", WS_POPUP, 0, 0, HUD_W, HUD_H, NULL, NULL, g_inst, NULL);
    if (!g_hud) { logf_("hud window failed %lu", GetLastError()); return 0; }
    SetLayeredWindowAttributes(g_hud, COLOR_KEY, 0, LWA_COLORKEY);

    WNDCLASSEXW bc = { 0 };
    bc.cbSize = sizeof(bc);
    bc.lpfnWndProc = badge_proc;
    bc.hInstance = g_inst;
    bc.hCursor = LoadCursor(NULL, IDC_HAND);
    bc.lpszClassName = BTN_CLASS;
    if (RegisterClassExW(&bc)) {
        g_badge = CreateWindowExW(WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST,
                                  BTN_CLASS, L"GGman", WS_POPUP, 0, 0, BADGE_W, BADGE_H,
                                  NULL, NULL, g_inst, NULL);
        if (g_badge) SetLayeredWindowAttributes(g_badge, COLOR_KEY, 0, LWA_COLORKEY);
    }
    logf_("badge window 0x%p button=%d", (void *)g_badge, g_cfg.button);

    g_font_title = CreateFontW(-15, 0, 0, 0, FW_MEDIUM, 0, 0, 0, DEFAULT_CHARSET,
                               OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS, CLEARTYPE_QUALITY,
                               DEFAULT_PITCH, L"Microsoft YaHei UI");
    g_font_body = CreateFontW(-14, 0, 0, 0, FW_NORMAL, 0, 0, 0, DEFAULT_CHARSET,
                              OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS, CLEARTYPE_QUALITY,
                              DEFAULT_PITCH, L"Microsoft YaHei UI");
    if (!g_font_title) g_font_title = (HFONT)GetStockObject(DEFAULT_GUI_FONT);
    if (!g_font_body) g_font_body = (HFONT)GetStockObject(DEFAULT_GUI_FONT);

    {
        HANDLE wd = CreateThread(NULL, 0, watchdog_thread, NULL, 0, NULL);
        if (wd) CloseHandle(wd);
    }
    if (g_cfg.start_hidden) g_visible = 0;

    g_host = find_host();
    logf_("host=0x%p start_hidden=%d", (void *)g_host, g_cfg.start_hidden);
    install_move_hook();



    {
        static const UINT mods[4] = { 0, MOD_ALT, MOD_CONTROL | MOD_ALT, MOD_SHIFT | MOD_CONTROL };
        static const WCHAR *names[4] = { L"F8", L"Alt+F8", L"Ctrl+Alt+F8", L"Ctrl+Shift+F8" };
        for (int i = 0; i < 4; i++) {
            if (RegisterHotKey(g_ipc, 1, mods[i] | MOD_NOREPEAT, VK_F8)) {
                g_hotkey_ok = 1;
                lstrcpynW(g_hotkey_name, names[i], 32);
                logf_("hud hotkey %ls", names[i]);
                break;
            }
        }
        if (!g_hotkey_ok) lstrcpynW(g_hotkey_name, L"无热键", 32);
        RegisterHotKey(g_ipc, 2, MOD_NOREPEAT, VK_F9);
        RegisterHotKey(g_ipc, 3, MOD_NOREPEAT, VK_F10);
        RegisterHotKey(g_ipc, 4, MOD_NOREPEAT, VK_F7);
    }

    lcu_discover();
    logf_("lcu port=%d token=%s", g_port, g_token[0] ? "ok" : "missing");

    if (g_cfg.http_port > 0) {
        HANDLE hh = CreateThread(NULL, 0, gg_http_thread, NULL, 0, NULL);
        if (hh) CloseHandle(hh);
    }

    g_last_tick_done = GetTickCount();
    SetTimer(g_ipc, 2, 700, NULL);
    reposition();
    refresh();

    MSG msg;
    while (GetMessageW(&msg, NULL, 0, 0) > 0) {
        TranslateMessage(&msg);
        DispatchMessageW(&msg);
    }
    logf_("message loop exited");
    return 0;
}

#ifndef GG_TEST
BOOL WINAPI DllMain(HINSTANCE inst, DWORD reason, LPVOID reserved)
{
    (void)reserved;
    if (reason == DLL_PROCESS_ATTACH) {
        g_inst = inst;
        DisableThreadLibraryCalls(inst);
        HANDLE th = CreateThread(NULL, 0, init_thread, NULL, 0, NULL);
        if (th) CloseHandle(th);
    }
    return TRUE;
}
#else
/* offline harness: feed a captured champ-select JSON and print the decision */
int main(int argc, char **argv)
{
    if (argc < 2) {
        printf("usage: test_cs.exe session.json [myCell] [pickChamp] [banChamp]\n");
        return 2;
    }
    if (lstrcmpA(argv[1], "--ini") == 0 && argc >= 3) {
        lstrcpynA(g_ini_path, argv[2], sizeof(g_ini_path));
        config_load();
        printf("hud=%d accept=%d pick=%d/%d ban=%d/%d honor=%d again=%d runes=%d page='%s' spells=%d s1=%d s2=%d mock=%d/%d\n",
               g_cfg.hud, g_cfg.auto_accept, g_cfg.auto_pick, g_cfg.pick_champ,
               g_cfg.auto_ban, g_cfg.ban_champ, g_cfg.auto_honor, g_cfg.auto_play_again,
               g_cfg.auto_runes, g_cfg.rune_page, g_cfg.auto_spells, g_cfg.spell1, g_cfg.spell2,
               g_cfg.mock_http, g_cfg.mock_port);
        return 0;
    }
    FILE *fp = fopen(argv[1], "rb");
    if (!fp) { printf("cannot open %s\n", argv[1]); return 3; }
    static char buf[65536];
    size_t n = fread(buf, 1, sizeof(buf) - 1, fp);
    fclose(fp);
    buf[n] = 0;
    if (argc >= 3 && lstrcmpA(argv[1], "--ini") == 0) {
        lstrcpynA(g_ini_path, argv[2], sizeof(g_ini_path));
        config_load();
        printf("hud=%d accept=%d pick=%d/%d ban=%d/%d honor=%d again=%d runes=%d page='%s' spells=%d s1=%d s2=%d mock=%d/%d\n",
               g_cfg.hud, g_cfg.auto_accept, g_cfg.auto_pick, g_cfg.pick_champ,
               g_cfg.auto_ban, g_cfg.ban_champ, g_cfg.auto_honor, g_cfg.auto_play_again,
               g_cfg.auto_runes, g_cfg.rune_page, g_cfg.auto_spells, g_cfg.spell1, g_cfg.spell2,
               g_cfg.mock_http, g_cfg.mock_port);
        return 0;
    }
    if (argc >= 3 && strstr(argv[1], "pages")) {
        int pid_page = find_page_id(buf, argv[2]);
        printf("page '%s' -> id=%d\n", argv[2], pid_page);
        return pid_page > 0 ? 0 : 1;
    }
    int cell = argc > 2 ? atoi(argv[2]) : 0;
    int pick = argc > 3 ? atoi(argv[3]) : 0;
    int ban = argc > 4 ? atoi(argv[4]) : 0;
    int parsed_cell = json_int(buf, "localPlayerCellId");
    printf("localPlayerCellId=%d (arg %d)\n", parsed_cell, cell);
    CsDecision d[8];
    int c = cs_decide(buf, cell, 1, 1, d, 8);
    for (int i = 0; i < c; i++)
        d[i].champion_id = d[i].is_ban ? ban : pick;
    printf("decisions=%d\n", c);
    for (int i = 0; i < c; i++)
        printf("  action_id=%d type=%s champion=%d\n", d[i].action_id,
               d[i].is_ban ? "ban" : "pick", d[i].champion_id);
    return c > 0 ? 0 : 1;
}
#endif















































