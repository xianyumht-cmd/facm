/*
 * ggman_hud.dll — in-client HUD overlay for the League of Legends LCU client.
 *
 * Injected into LeagueClientUxRender.exe (CEF 108 renderer). Creates a
 * click-through, non-activating, always-on-top layered child of the desktop that
 * tracks the League client window rect, and paints a panel with text pushed from
 * an external process through WM_COPYDATA.
 *
 * Why this needs injection: the panel is owned by the client's own renderer
 * process, survives nothing but the renderer, can be extended with in-process
 * hooks (WndProc, CEF/Chromium internals, input interception) that an external
 * overlay cannot reach.
 *
 * IPC (external -> DLL):
 *   HWND ipc = FindWindowW(L"GGmanHudIPC", NULL);
 *   COPYDATASTRUCT cds = { kind, sizeof(payload), &payload };
 *   SendMessageW(ipc, WM_COPYDATA, 0, (LPARAM)&cds);
 *
 *   payload = struct GGmanHudCommand { DWORD kind; DWORD flags; WCHAR text[2048]; }
 *   kind 1 = replace text lines (lines separated by '\n')
 *   kind 2 = show / hide (flags 0 = hide, 1 = show, 2 = toggle)
 *   kind 3 = reset (clear text)
 *   kind 4 = unload request (removes window + hotkey; DLL stays mapped)
 *
 * Hotkey: F8 toggles visibility.
 */

#define WIN32_LEAN_AND_MEAN
#define UNICODE
#define _UNICODE
#include <windows.h>
#include <stdio.h>
#include <stdarg.h>

#define HUD_CLASS   L"GGmanHudOverlay"
#define IPC_CLASS   L"GGmanHudIPC"
#define HUD_W       520
#define HUD_H       214
#define HUD_MARGIN  18
#define MAX_LINES   8
#define LINE_LEN    200
#define TEXT_LEN    2048

#define COLOR_KEY   RGB(1, 2, 3)
#define COL_PANEL   RGB(16, 18, 26)
#define COL_BORDER  RGB(58, 160, 200)
#define COL_TITLE   RGB(120, 220, 255)
#define COL_BODY    RGB(222, 230, 240)
#define COL_DIM     RGB(130, 145, 165)

typedef struct GGmanHudCommand {
    DWORD kind;
    DWORD flags;
    WCHAR text[TEXT_LEN];
} GGmanHudCommand;

static HINSTANCE g_inst;
static HWND g_ipc;
static HWND g_hud;
static HWND g_host;
static HFONT g_font_title;
static HFONT g_font_body;
static WCHAR g_lines[MAX_LINES][LINE_LEN];
static int g_line_count;
static int g_visible = 1;
static DWORD g_start_tick;
static int g_hotkey_ok;
static WCHAR g_hotkey_name[32] = L"F8";
static char g_log_path[MAX_PATH];

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

static void init_log_path(void)
{
    lstrcpynA(g_log_path, "D:\\project2\\ggman\\dll\\build\\hud.log", MAX_PATH);
}

/* pick the League client window: class RCLIENT (Ux process) */
static BOOL CALLBACK find_host_proc(HWND h, LPARAM lp)
{
    WCHAR cls[128];
    if (!IsWindowVisible(h)) return TRUE;
    GetClassNameW(h, cls, 128);
    if (lstrcmpiW(cls, L"RCLIENT") == 0) {
        RECT rc;
        GetWindowRect(h, &rc);
        if (rc.right - rc.left > 600) {
            *(HWND *)lp = h;
            return FALSE;
        }
    }
    return TRUE;
}

static HWND find_host(void)
{
    HWND found = NULL;
    EnumWindows(find_host_proc, (LPARAM)&found);
    return found;
}

static void draw_panel(HWND hwnd)
{
    PAINTSTRUCT ps;
    HDC dc = BeginPaint(hwnd, &ps);
    if (!dc) return;

    RECT rc;
    GetClientRect(hwnd, &rc);
    int w = rc.right, h = rc.bottom;

    HDC mem = CreateCompatibleDC(dc);
    HBITMAP bmp = CreateCompatibleBitmap(dc, w, h);
    HGDIOBJ old_bmp = SelectObject(mem, bmp);

    HBRUSH key = CreateSolidBrush(COLOR_KEY);
    FillRect(mem, &rc, key);
    DeleteObject(key);

    HBRUSH panel = CreateSolidBrush(COL_PANEL);
    HPEN border = CreatePen(PS_SOLID, 1, COL_BORDER);
    HGDIOBJ old_brush = SelectObject(mem, panel);
    HGDIOBJ old_pen = SelectObject(mem, border);
    RoundRect(mem, 1, 1, w - 1, h - 1, 14, 14);
    SelectObject(mem, old_brush);
    SelectObject(mem, old_pen);
    DeleteObject(panel);
    DeleteObject(border);

    SetBkMode(mem, TRANSPARENT);

    /* title */
    HGDIOBJ old_font = SelectObject(mem, g_font_title);
    SetTextColor(mem, COL_TITLE);
    RECT tr = { 16, 12, w - 16, 38 };
    DrawTextW(mem, L"GGman HUD  ·  injected into LeagueClientUxRender.exe", -1, &tr,
              DT_LEFT | DT_SINGLELINE | DT_VCENTER | DT_NOPREFIX);

    /* status line */
    SelectObject(mem, g_font_body);
    SetTextColor(mem, COL_DIM);
    WCHAR status[256];
    DWORD up = (GetTickCount() - g_start_tick) / 1000;
    wsprintfW(status, L"pid %lu   ·   已注入   ·   up %lu s   ·   热键 %s",
              (unsigned long)GetCurrentProcessId(), (unsigned long)up, g_hotkey_name);
    RECT sr = { 16, 40, w - 16, 62 };
    DrawTextW(mem, status, -1, &sr, DT_LEFT | DT_SINGLELINE | DT_VCENTER | DT_NOPREFIX | DT_END_ELLIPSIS);

    /* separator */
    HPEN sep = CreatePen(PS_SOLID, 1, RGB(40, 48, 62));
    old_pen = SelectObject(mem, sep);
    MoveToEx(mem, 16, 66, NULL);
    LineTo(mem, w - 16, 66);
    SelectObject(mem, old_pen);
    DeleteObject(sep);

    /* body lines */
    SetTextColor(mem, COL_BODY);
    int y = 74;
    if (g_line_count == 0) {
        SetTextColor(mem, COL_DIM);
        RECT er = { 16, y, w - 16, h - 12 };
        DrawTextW(mem, L"等待外部数据…（tools/hud_send.py 推送）", -1, &er,
                  DT_LEFT | DT_TOP | DT_NOPREFIX | DT_WORDBREAK);
    } else {
        for (int i = 0; i < g_line_count && y < h - 18; i++) {
            RECT lr = { 16, y, w - 16, y + 20 };
            DrawTextW(mem, g_lines[i], -1, &lr, DT_LEFT | DT_SINGLELINE | DT_NOPREFIX | DT_END_ELLIPSIS);
            y += 20;
        }
    }

    SelectObject(mem, old_font);
    BitBlt(dc, 0, 0, w, h, mem, 0, 0, SRCCOPY);
    SelectObject(mem, old_bmp);
    DeleteObject(bmp);
    DeleteDC(mem);
    EndPaint(hwnd, &ps);
}

static LRESULT CALLBACK hud_proc(HWND h, UINT msg, WPARAM wp, LPARAM lp)
{
    switch (msg) {
    case WM_PAINT:
        draw_panel(h);
        return 0;
    case WM_ERASEBKGND:
        return 1;
    case WM_NCHITTEST:
        return HTTRANSPARENT;
    }
    return DefWindowProcW(h, msg, wp, lp);
}

static void reposition(void)
{
    if (!g_hud) return;
    if (!g_host || !IsWindow(g_host) || !IsWindowVisible(g_host)) {
        ShowWindow(g_hud, SW_HIDE);
        return;
    }
    RECT rc;
    GetClientRect(g_host, &rc);
    POINT pt = { rc.left, rc.top };
    ClientToScreen(g_host, &pt);
    int cw = rc.right - rc.left, ch = rc.bottom - rc.top;
    if (cw < 400 || ch < 300) {
        ShowWindow(g_hud, SW_HIDE);
        return;
    }
    int x = pt.x + HUD_MARGIN;
    int y = pt.y + ch - HUD_H - HUD_MARGIN;
    SetWindowPos(g_hud, HWND_TOPMOST, x, y, HUD_W, HUD_H,
                 SWP_NOACTIVATE | (g_visible ? SWP_SHOWWINDOW : SWP_NOACTIVATE));
    if (g_visible) {
        ShowWindow(g_hud, SW_SHOWNOACTIVATE);
        InvalidateRect(g_hud, NULL, FALSE);
    } else {
        ShowWindow(g_hud, SW_HIDE);
    }
}

static void apply_text(const WCHAR *text)
{
    g_line_count = 0;
    int idx = 0, n = 0;
    for (const WCHAR *p = text; ; p++) {
        if (*p == 0 || *p == L'\n' || n >= LINE_LEN - 1) {
            if (n > 0 && idx < MAX_LINES) {
                g_lines[idx][n] = 0;
                idx++;
            }
            n = 0;
            if (*p == 0) break;
            continue;
        }
        if (idx >= MAX_LINES) break;
        g_lines[idx][n++] = *p;
    }
    g_line_count = idx;
    if (g_hud) InvalidateRect(g_hud, NULL, FALSE);
}

static LRESULT CALLBACK ipc_proc(HWND h, UINT msg, WPARAM wp, LPARAM lp)
{
    switch (msg) {
    case WM_COPYDATA: {
        COPYDATASTRUCT *cds = (COPYDATASTRUCT *)lp;
        if (!cds || !cds->lpData || cds->cbData < sizeof(DWORD) * 2) return 0;
        GGmanHudCommand *cmd = (GGmanHudCommand *)cds->lpData;
        DWORD kind = cmd->kind;
        if (kind == 1) {
            cmd->text[TEXT_LEN - 1] = 0;
            apply_text(cmd->text);
            logf_("ipc text: %d lines", g_line_count);
        } else if (kind == 2) {
            if (cmd->flags == 2) g_visible = !g_visible;
            else g_visible = cmd->flags ? 1 : 0;
            reposition();
            logf_("ipc visible=%d", g_visible);
        } else if (kind == 3) {
            apply_text(L"");
        }
        return 1;
    }
    case WM_TIMER:
        if (wp == 2) {
            if (!g_host || !IsWindow(g_host)) g_host = find_host();
            reposition();
            if (g_hud && g_visible) InvalidateRect(g_hud, NULL, FALSE);
        }
        return 0;
    case WM_HOTKEY:
        if (wp == 1) {
            g_visible = !g_visible;
            reposition();
            logf_("hotkey F8 -> visible=%d", g_visible);
        }
        return 0;
    case WM_CLOSE:
        logf_("WM_CLOSE received, tearing down");
        if (g_hud) { DestroyWindow(g_hud); g_hud = NULL; }
        DestroyWindow(h);
        return 0;
    case WM_DESTROY:
        PostQuitMessage(0);
        return 0;
    }
    return DefWindowProcW(h, msg, wp, lp);
}

static DWORD WINAPI init_thread(LPVOID param)
{
    (void)param;
    init_log_path();
    logf_("=== ggman_hud initialising in pid %lu ===", (unsigned long)GetCurrentProcessId());

    g_start_tick = GetTickCount();

    WNDCLASSEXW wc = { 0 };
    wc.cbSize = sizeof(wc);
    wc.lpfnWndProc = ipc_proc;
    wc.hInstance = g_inst;
    wc.lpszClassName = IPC_CLASS;
    if (!RegisterClassExW(&wc)) {
        logf_("RegisterClassEx(ipc) failed %lu", (unsigned long)GetLastError());
        return 0;
    }
    g_ipc = CreateWindowExW(0, IPC_CLASS, L"ggman_hud_ipc", WS_POPUP,
                            0, 0, 0, 0, NULL, NULL, g_inst, NULL);
    if (!g_ipc) {
        logf_("CreateWindowEx(ipc) failed %lu", (unsigned long)GetLastError());
        return 0;
    }
    logf_("ipc window 0x%p", (void *)g_ipc);

    WNDCLASSEXW hc = { 0 };
    hc.cbSize = sizeof(hc);
    hc.lpfnWndProc = hud_proc;
    hc.hInstance = g_inst;
    hc.hCursor = LoadCursor(NULL, IDC_ARROW);
    hc.lpszClassName = HUD_CLASS;
    if (!RegisterClassExW(&hc)) {
        logf_("RegisterClassEx(hud) failed %lu", (unsigned long)GetLastError());
        return 0;
    }
    g_hud = CreateWindowExW(WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE |
                            WS_EX_TOOLWINDOW | WS_EX_TOPMOST,
                            HUD_CLASS, L"GGman HUD", WS_POPUP,
                            0, 0, HUD_W, HUD_H, NULL, NULL, g_inst, NULL);
    if (!g_hud) {
        logf_("CreateWindowEx(hud) failed %lu", (unsigned long)GetLastError());
        return 0;
    }
    SetLayeredWindowAttributes(g_hud, COLOR_KEY, 0, LWA_COLORKEY);
    logf_("hud window 0x%p", (void *)g_hud);

    g_font_title = CreateFontW(-15, 0, 0, 0, FW_SEMIBOLD, 0, 0, 0, DEFAULT_CHARSET,
                               OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS, CLEARTYPE_QUALITY,
                               DEFAULT_PITCH, L"Microsoft YaHei UI");
    g_font_body = CreateFontW(-14, 0, 0, 0, FW_NORMAL, 0, 0, 0, DEFAULT_CHARSET,
                              OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS, CLEARTYPE_QUALITY,
                              DEFAULT_PITCH, L"Microsoft YaHei UI");
    if (!g_font_title) g_font_title = (HFONT)GetStockObject(DEFAULT_GUI_FONT);
    if (!g_font_body) g_font_body = (HFONT)GetStockObject(DEFAULT_GUI_FONT);

    g_host = find_host();
    logf_("host window 0x%p", (void *)g_host);

    /* only one HUD instance may paint; wait for the current owner to die */
    static HANDLE owner_mutex;
    owner_mutex = CreateMutexW(NULL, TRUE, L"Local\\GGmanHudOwner");
    DWORD mtx_err = GetLastError();
    if (owner_mutex && mtx_err == ERROR_ALREADY_EXISTS) {
        logf_("another HUD instance owns the overlay; waiting (pid separate)");
        HANDLE existing = OpenMutexW(SYNCHRONIZE, FALSE, L"Local\\GGmanHudOwner");
        if (existing) {
            DWORD wait = WaitForSingleObject(existing, 60 * 60 * 1000);
            logf_("previous owner released mutex (wait=0x%lX); taking over", (unsigned long)wait);
            CloseHandle(existing);
        }
        WaitForSingleObject(owner_mutex, 0);
    }
    logf_("overlay ownership acquired");

    {
        static const UINT mods[4] = { 0, MOD_ALT, MOD_CONTROL | MOD_ALT, MOD_SHIFT | MOD_CONTROL };
        static const WCHAR *names[4] = { L"F8", L"Alt+F8", L"Ctrl+Alt+F8", L"Ctrl+Shift+F8" };
        int i;
        for (i = 0; i < 4; i++) {
            if (RegisterHotKey(g_ipc, 1, mods[i] | MOD_NOREPEAT, VK_F8)) {
                g_hotkey_ok = 1;
                lstrcpynW(g_hotkey_name, names[i], 32);
                logf_("hotkey registered: %ls", names[i]);
                break;
            }
            logf_("hotkey %ls failed %lu", names[i], (unsigned long)GetLastError());
        }
        if (!g_hotkey_ok) lstrcpynW(g_hotkey_name, L"不可用", 32);
    }

    SetTimer(g_ipc, 2, 250, NULL);
    reposition();

    MSG msg;
    while (GetMessageW(&msg, NULL, 0, 0) > 0) {
        TranslateMessage(&msg);
        DispatchMessageW(&msg);
    }
    logf_("message loop exited");
    return 0;
}

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



