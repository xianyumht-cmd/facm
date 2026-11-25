/* ---------- panel: tabs + rows ---------- */
#define TAB_COUNT 4
#define ROW_H     24
#define CONTENT_Y 66

static RECT g_tab_rect[TAB_COUNT];
static RECT g_row_rect[10];
static int  g_row_kind[10];   /* 0 info, 1 toggle, 2 action */
static int  g_row_id[10];     /* config slot or action id */
static int  g_row_count;
static int  g_tab;

static const WCHAR *TAB_NAMES[TAB_COUNT] = { L"状态", L"自动化", L"数据", L"工具" };

static void layout_panel(int w, int h)
{
    int tw = (w - 32) / TAB_COUNT;
    for (int i = 0; i < TAB_COUNT; i++) {
        g_tab_rect[i].left = 16 + i * tw;
        g_tab_rect[i].right = g_tab_rect[i].left + tw - 6;
        g_tab_rect[i].top = 36;
        g_tab_rect[i].bottom = 60;
    }
    (void)h;
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
    HGDIOBJ ob = SelectObject(mem, panel), op = SelectObject(mem, border);
    RoundRect(mem, 1, 1, w - 1, h - 1, 14, 14);
    SelectObject(mem, ob); SelectObject(mem, op);
    DeleteObject(panel); DeleteObject(border);
    SetBkMode(mem, TRANSPARENT);

    /* title */
    HGDIOBJ of = SelectObject(mem, g_font_title);
    SetTextColor(mem, COL_TITLE);
    RECT tr = { 16, 8, w - 16, 30 };
    DrawTextW(mem, L"GGman Agent", -1, &tr, DT_LEFT | DT_SINGLELINE | DT_VCENTER | DT_NOPREFIX);

    WCHAR right[160];
    wsprintfW(right, L"pid %lu   port %d   %ls", (unsigned long)g_pid_hud, g_port, g_hotkey_name);
    SetTextColor(mem, COL_DIM);
    DrawTextW(mem, right, -1, &tr, DT_RIGHT | DT_SINGLELINE | DT_VCENTER | DT_NOPREFIX);

    /* tabs */
    layout_panel(w, h);
    SelectObject(mem, g_font_body);
    for (int i = 0; i < TAB_COUNT; i++) {
        BOOL active = (i == g_tab);
        HBRUSH tb = CreateSolidBrush(active ? RGB(22, 46, 62) : RGB(20, 24, 32));
        HPEN tp = CreatePen(PS_SOLID, 1, active ? COL_BORDER : RGB(44, 52, 66));
        HGDIOBJ ob2 = SelectObject(mem, tb), op2 = SelectObject(mem, tp);
        RoundRect(mem, g_tab_rect[i].left, g_tab_rect[i].top, g_tab_rect[i].right,
                  g_tab_rect[i].bottom, 8, 8);
        SelectObject(mem, ob2); SelectObject(mem, op2);
        DeleteObject(tb); DeleteObject(tp);
        SetTextColor(mem, active ? COL_TITLE : COL_DIM);
        RECT trr = g_tab_rect[i];
        DrawTextW(mem, TAB_NAMES[i], -1, &trr, DT_CENTER | DT_SINGLELINE | DT_VCENTER | DT_NOPREFIX);
    }

    /* rows */
    g_row_count = 0;
    int y = CONTENT_Y;
    WCHAR line[LINE_LEN], tmp[48];

    if (g_tab == 0) {
        wsprintfW(line, L"召唤师  %ls   Lv.%d   状态 %ls", g_name, g_level, g_avail);
        g_row_kind[g_row_count] = 0; g_row_id[g_row_count++] = -1;
    } else if (g_tab == 1) {
        g_row_kind[g_row_count] = 1; g_row_id[g_row_count++] = 0;
        g_row_kind[g_row_count] = 1; g_row_id[g_row_count++] = 1;
        g_row_kind[g_row_count] = 1; g_row_id[g_row_count++] = 2;
        g_row_kind[g_row_count] = 1; g_row_id[g_row_count++] = 3;
        g_row_kind[g_row_count] = 1; g_row_id[g_row_count++] = 4;
        g_row_kind[g_row_count] = 1; g_row_id[g_row_count++] = 5;
        g_row_kind[g_row_count] = 1; g_row_id[g_row_count++] = 6;
    } else if (g_tab == 2) {
        wsprintfW(line, L"我的段位  %ls", g_self_rank[0] ? g_self_rank : L"未获取");
        g_row_kind[g_row_count] = 0; g_row_id[g_row_count++] = -1;
        for (int i = 0; i < g_ally_count && g_row_count < 10; i++) {
            lstrcpynW(line, g_ally[i], LINE_LEN);
            g_row_kind[g_row_count] = 0; g_row_id[g_row_count++] = -1;
        }
    } else {
        g_row_kind[g_row_count] = 2; g_row_id[g_row_count++] = 0;  /* accept */
        g_row_kind[g_row_count] = 2; g_row_id[g_row_count++] = 1;  /* honor */
        g_row_kind[g_row_count] = 2; g_row_id[g_row_count++] = 2;  /* play again */
        g_row_kind[g_row_count] = 2; g_row_id[g_row_count++] = 3;  /* reload config */
    }

    /* render rows for the active tab */
    int idx = 0;
    if (g_tab == 0) {
        const WCHAR *items[5];
        WCHAR b0[LINE_LEN], b1[LINE_LEN], b2[LINE_LEN];
        wsprintfW(b0, L"召唤师  %ls   Lv.%d   状态 %ls", g_name, g_level, g_avail);
        wsprintfW(b1, L"阶段  %ls    选人格 %d   %ls", g_phase, g_cs_cell, g_cs_active ? L"选人中" : L"空闲");
        wsprintfW(b2, L"最近动作  %ls", g_last_action);
        items[0] = b0; items[1] = b1; items[2] = b2;
        SetTextColor(mem, COL_BODY);
        for (int i = 0; i < 3; i++) {
            g_row_rect[i].left = 16; g_row_rect[i].right = w - 16;
            g_row_rect[i].top = y; g_row_rect[i].bottom = y + ROW_H;
            RECT rr = g_row_rect[i];
            DrawTextW(mem, items[i], -1, &rr, DT_LEFT | DT_SINGLELINE | DT_VCENTER | DT_NOPREFIX | DT_END_ELLIPSIS);
            y += ROW_H;
        }
        g_row_count = 3;
        for (int i = 0; i < 3; i++) { g_row_kind[i] = 0; g_row_id[i] = -1; }
    } else if (g_tab == 1) {
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
            g_row_rect[i].left = 16; g_row_rect[i].right = w - 16;
            g_row_rect[i].top = y; g_row_rect[i].bottom = y + ROW_H;
            RECT rr = g_row_rect[i];
            SetTextColor(mem, on ? COL_BODY : COL_DIM);
            DrawTextW(mem, labels[i], -1, &rr, DT_LEFT | DT_SINGLELINE | DT_VCENTER | DT_NOPREFIX);
            /* switch */
            RECT sr = { w - 92, y + 4, w - 20, y + ROW_H - 4 };
            HBRUSH sb = CreateSolidBrush(on ? RGB(26, 92, 64) : RGB(34, 40, 52));
            HPEN sp = CreatePen(PS_SOLID, 1, on ? COL_OK : RGB(60, 70, 84));
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
        SetTextColor(mem, COL_BODY);
        wsprintfW(line, L"我的段位  %ls", g_self_rank[0] ? g_self_rank : L"未获取");
        g_row_rect[0] = (RECT){ 16, y, w - 16, y + ROW_H };
        DrawTextW(mem, line, -1, &g_row_rect[0], DT_LEFT | DT_SINGLELINE | DT_VCENTER | DT_NOPREFIX);
        y += ROW_H;
        SetTextColor(mem, COL_DIM);
        wsprintfW(line, L"本局队友（%d）", g_ally_count);
        g_row_rect[1] = (RECT){ 16, y, w - 16, y + ROW_H };
        DrawTextW(mem, line, -1, &g_row_rect[1], DT_LEFT | DT_SINGLELINE | DT_VCENTER | DT_NOPREFIX);
        y += ROW_H;
        SetTextColor(mem, COL_BODY);
        for (int i = 0; i < g_ally_count && i < 8; i++) {
            g_row_rect[2 + i] = (RECT){ 24, y, w - 16, y + ROW_H };
            DrawTextW(mem, g_ally[i], -1, &g_row_rect[2 + i], DT_LEFT | DT_SINGLELINE | DT_VCENTER |
                      DT_NOPREFIX | DT_END_ELLIPSIS);
            y += ROW_H;
        }
        g_row_count = 2 + (g_ally_count < 8 ? g_ally_count : 8);
        for (int i = 0; i < g_row_count; i++) { g_row_kind[i] = 0; g_row_id[i] = -1; }
    } else {
        static const WCHAR *labels[4] = { L"立即接受对局", L"给队友点赞", L"结算后再来一局", L"重新读取配置" };
        for (int i = 0; i < 4; i++) {
            g_row_rect[i] = (RECT){ 16, y, w - 16, y + ROW_H + 2 };
            HBRUSH sb = CreateSolidBrush(RGB(24, 30, 42));
            HPEN sp = CreatePen(PS_SOLID, 1, RGB(58, 74, 96));
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
            y += ROW_H + 4;
        }
        g_row_count = 4;
    }

    /* footer */
    SetTextColor(mem, COL_DIM);
    WCHAR foot[160];
    wsprintfW(foot, L"F8 隐藏  ·  F9 自动接受  ·  点行切换  ·  已注入 pid %lu", (unsigned long)g_pid_hud);
    RECT fr = { 16, h - 26, w - 16, h - 8 };
    DrawTextW(mem, foot, -1, &fr, DT_LEFT | DT_SINGLELINE | DT_VCENTER | DT_NOPREFIX | DT_END_ELLIPSIS);
    (void)idx; (void)tmp;

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
    } else {
        config_load();
        set_action(L"配置已重载");
    }
}

static LRESULT CALLBACK hud_proc(HWND h, UINT msg, WPARAM wp, LPARAM lp)
{
    switch (msg) {
    case WM_PAINT: draw_panel(h); return 0;
    case WM_ERASEBKGND: return 1;
    case WM_MOUSEACTIVATE: return MA_NOACTIVATE;
    case WM_NCHITTEST: {
        POINT pt;
        pt.x = (short)LOWORD(lp);
        pt.y = (short)HIWORD(lp);
        ScreenToClient(h, &pt);
        for (int i = 0; i < TAB_COUNT; i++)
            if (PtInRect(&g_tab_rect[i], pt)) return HTCLIENT;
        for (int i = 0; i < g_row_count; i++)
            if (PtInRect(&g_row_rect[i], pt)) return HTCLIENT;
        return HTTRANSPARENT;
    }
    case WM_LBUTTONUP: {
        POINT pt;
        pt.x = (short)LOWORD(lp);
        pt.y = (short)HIWORD(lp);
        for (int i = 0; i < TAB_COUNT; i++) {
            if (PtInRect(&g_tab_rect[i], pt)) {
                g_tab = i;
                InvalidateRect(h, NULL, FALSE);
                return 0;
            }
        }
        for (int i = 0; i < g_row_count; i++) {
            if (!PtInRect(&g_row_rect[i], pt)) continue;
            if (g_row_kind[i] == 1) toggle_slot(g_row_id[i]);
            else if (g_row_kind[i] == 2) run_action(g_row_id[i]);
            InvalidateRect(h, NULL, FALSE);
            return 0;
        }
        return 0;
    }
    }
    return DefWindowProcW(h, msg, wp, lp);
}

