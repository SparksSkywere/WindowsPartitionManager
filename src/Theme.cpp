#include "Theme.hpp"
#include "Util.hpp"

#include <commctrl.h>
#include <dwmapi.h>
#include <uxtheme.h>

namespace pm {
namespace {

constexpr COLORREF rgb(int r, int g, int b) {
    return RGB(r, g, b);
}

Theme make(const wchar_t* id, const wchar_t* name, const wchar_t* era,
           COLORREF face, COLORREF window, COLORREF parchment, COLORREF ink,
           COLORREF highlight, COLORREF highlightText, COLORREF shadow, COLORREF darkShadow,
           COLORREF light, COLORREF accent, COLORREF accentBorder, COLORREF sidebar,
           COLORREF disabledText, COLORREF menuBar, COLORREF menuBarText, COLORREF hover,
           COLORREF tabInactive, COLORREF primaryButton, COLORREF border, COLORREF statusBar,
           COLORREF placeholder, COLORREF metaText, bool dark) {
    Theme t;
    t.id = id;
    t.name = name;
    t.era = era;
    t.face = face;
    t.window = window;
    t.parchment = parchment;
    t.ink = ink;
    t.highlight = highlight;
    t.highlightText = highlightText;
    t.shadow = shadow;
    t.darkShadow = darkShadow;
    t.light = light;
    t.accent = accent;
    t.accentBorder = accentBorder;
    t.sidebar = sidebar;
    t.disabledText = disabledText;
    t.menuBar = menuBar;
    t.menuBarText = menuBarText;
    t.hover = hover;
    t.tabInactive = tabInactive;
    t.primaryButton = primaryButton;
    t.border = border;
    t.statusBar = statusBar;
    t.placeholder = placeholder;
    t.metaText = metaText;
    t.dark = dark;
    return t;
}

bool systemAppsDark() {
    DWORD value = 1;
    DWORD size = sizeof(value);
    if (RegGetValueW(HKEY_CURRENT_USER,
                     L"Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize",
                     L"AppsUseLightTheme", RRF_RT_REG_DWORD, nullptr, &value, &size) == ERROR_SUCCESS)
        return value == 0;
    return false;
}

std::vector<Theme> buildCatalog() {
    return {
        make(L"win95", L"Windows 95", L"1995",
             rgb(192, 192, 192), rgb(255, 255, 255), rgb(255, 255, 255), rgb(0, 0, 0),
             rgb(0, 0, 128), rgb(255, 255, 255), rgb(128, 128, 128), rgb(0, 0, 0),
             rgb(255, 255, 255), rgb(192, 192, 192), rgb(128, 128, 128), rgb(192, 192, 192),
             rgb(128, 128, 128), rgb(192, 192, 192), rgb(0, 0, 0), rgb(224, 224, 224),
             rgb(168, 168, 168), rgb(192, 192, 192), rgb(0, 0, 0), rgb(192, 192, 192),
             rgb(128, 128, 128), rgb(64, 64, 64), false),
        make(L"win98", L"Windows 98", L"1998",
             rgb(192, 192, 192), rgb(255, 255, 255), rgb(255, 252, 240), rgb(0, 0, 0),
             rgb(0, 0, 128), rgb(255, 255, 255), rgb(128, 128, 128), rgb(64, 64, 64),
             rgb(255, 255, 255), rgb(212, 208, 200), rgb(128, 128, 128), rgb(212, 208, 200),
             rgb(128, 128, 128), rgb(192, 192, 192), rgb(0, 0, 0), rgb(224, 224, 224),
             rgb(176, 176, 176), rgb(212, 208, 200), rgb(0, 0, 0), rgb(192, 192, 192),
             rgb(128, 128, 128), rgb(80, 80, 80), false),
        make(L"win2000", L"Windows 2000", L"2000",
             rgb(212, 208, 200), rgb(255, 255, 255), rgb(255, 248, 231), rgb(0, 0, 0),
             rgb(10, 36, 106), rgb(255, 255, 255), rgb(128, 128, 128), rgb(64, 64, 64),
             rgb(255, 255, 255), rgb(245, 230, 200), rgb(168, 152, 120), rgb(212, 208, 200),
             rgb(128, 128, 128), rgb(212, 208, 200), rgb(0, 0, 0), rgb(232, 228, 216),
             rgb(180, 176, 168), rgb(212, 196, 168), rgb(64, 64, 64), rgb(212, 208, 200),
             rgb(128, 128, 128), rgb(85, 85, 85), false),
        make(L"winxp", L"Windows XP", L"2001",
             rgb(236, 233, 216), rgb(255, 255, 255), rgb(255, 255, 245), rgb(0, 0, 0),
             rgb(49, 106, 197), rgb(255, 255, 255), rgb(172, 168, 153), rgb(113, 111, 100),
             rgb(255, 255, 255), rgb(255, 255, 225), rgb(0, 60, 116), rgb(127, 157, 185),
             rgb(161, 161, 146), rgb(236, 233, 216), rgb(0, 0, 0), rgb(196, 216, 240),
             rgb(200, 198, 182), rgb(236, 233, 216), rgb(0, 60, 116), rgb(236, 233, 216),
             rgb(128, 128, 128), rgb(70, 70, 90), false),
        make(L"winvista", L"Windows Vista", L"2007",
             rgb(235, 240, 245), rgb(255, 255, 255), rgb(252, 252, 255), rgb(30, 30, 30),
             rgb(51, 153, 255), rgb(255, 255, 255), rgb(160, 175, 190), rgb(100, 120, 140),
             rgb(255, 255, 255), rgb(230, 240, 250), rgb(120, 160, 200), rgb(220, 230, 240),
             rgb(140, 150, 160), rgb(235, 240, 245), rgb(30, 30, 30), rgb(190, 220, 250),
             rgb(210, 220, 230), rgb(200, 220, 240), rgb(140, 160, 180), rgb(235, 240, 245),
             rgb(140, 150, 160), rgb(80, 100, 120), false),
        make(L"win7", L"Windows 7", L"2009",
             rgb(240, 240, 240), rgb(255, 255, 255), rgb(255, 253, 245), rgb(0, 0, 0),
             rgb(51, 153, 255), rgb(255, 255, 255), rgb(160, 160, 160), rgb(105, 105, 105),
             rgb(255, 255, 255), rgb(255, 250, 235), rgb(180, 170, 140), rgb(230, 235, 240),
             rgb(131, 131, 131), rgb(240, 240, 240), rgb(0, 0, 0), rgb(229, 241, 251),
             rgb(220, 220, 220), rgb(225, 235, 245), rgb(112, 112, 112), rgb(240, 240, 240),
             rgb(128, 128, 128), rgb(80, 80, 80), false),
        make(L"win8", L"Windows 8", L"2012",
             rgb(240, 240, 240), rgb(255, 255, 255), rgb(255, 255, 255), rgb(0, 0, 0),
             rgb(0, 114, 198), rgb(255, 255, 255), rgb(171, 171, 171), rgb(85, 85, 85),
             rgb(255, 255, 255), rgb(240, 240, 240), rgb(0, 114, 198), rgb(245, 245, 245),
             rgb(153, 153, 153), rgb(255, 255, 255), rgb(0, 0, 0), rgb(222, 236, 249),
             rgb(230, 230, 230), rgb(0, 114, 198), rgb(171, 171, 171), rgb(240, 240, 240),
             rgb(153, 153, 153), rgb(102, 102, 102), false),
        make(L"win10", L"Windows 10", L"2015",
             rgb(243, 243, 243), rgb(255, 255, 255), rgb(255, 255, 255), rgb(32, 32, 32),
             rgb(0, 120, 215), rgb(255, 255, 255), rgb(200, 200, 200), rgb(96, 96, 96),
             rgb(255, 255, 255), rgb(243, 243, 243), rgb(0, 120, 215), rgb(249, 249, 249),
             rgb(150, 150, 150), rgb(243, 243, 243), rgb(32, 32, 32), rgb(230, 240, 250),
             rgb(230, 230, 230), rgb(0, 120, 215), rgb(200, 200, 200), rgb(243, 243, 243),
             rgb(150, 150, 150), rgb(96, 96, 96), false),
        make(L"win11", L"Windows 11", L"2021",
             rgb(243, 243, 243), rgb(255, 255, 255), rgb(252, 252, 252), rgb(28, 28, 28),
             rgb(0, 103, 192), rgb(255, 255, 255), rgb(229, 229, 229), rgb(80, 80, 80),
             rgb(255, 255, 255), rgb(243, 243, 243), rgb(0, 103, 192), rgb(249, 249, 249),
             rgb(150, 150, 150), rgb(243, 243, 243), rgb(28, 28, 28), rgb(237, 237, 237),
             rgb(237, 237, 237), rgb(0, 103, 192), rgb(229, 229, 229), rgb(249, 249, 249),
             rgb(150, 150, 150), rgb(96, 96, 96), false),
        make(L"win11-dark", L"Windows 11 Dark", L"2021",
             rgb(32, 32, 32), rgb(40, 40, 40), rgb(45, 45, 45), rgb(255, 255, 255),
             rgb(96, 205, 255), rgb(0, 0, 0), rgb(20, 20, 20), rgb(0, 0, 0),
             rgb(60, 60, 60), rgb(50, 50, 50), rgb(96, 205, 255), rgb(28, 28, 28),
             rgb(160, 160, 160), rgb(32, 32, 32), rgb(255, 255, 255), rgb(55, 55, 55),
             rgb(45, 45, 45), rgb(96, 205, 255), rgb(70, 70, 70), rgb(28, 28, 28),
             rgb(140, 140, 140), rgb(200, 200, 200), true),
    };
}

std::wstring g_id = L"system";
ThemeBrushes g_brushes;
const std::vector<Theme> g_catalog = buildCatalog();
bool g_darkApisReady = false;

using fnAllowDarkModeForWindow = BOOL(WINAPI*)(HWND, BOOL);
using fnSetPreferredAppMode = int(WINAPI*)(int);
using fnFlushMenuThemes = void(WINAPI*)();
fnAllowDarkModeForWindow g_allowDarkForWindow = nullptr;
fnSetPreferredAppMode g_setPreferredAppMode = nullptr;
fnFlushMenuThemes g_flushMenuThemes = nullptr;

void ensureDarkApis() {
    if (g_darkApisReady) return;
    g_darkApisReady = true;
    HMODULE uxtheme = LoadLibraryExW(L"uxtheme.dll", nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
    if (!uxtheme) return;
    // Ordinals used by Windows 10 1809+ for unofficial dark-mode Win32 theming.
    g_allowDarkForWindow = reinterpret_cast<fnAllowDarkModeForWindow>(GetProcAddress(uxtheme, MAKEINTRESOURCEA(133)));
    g_setPreferredAppMode = reinterpret_cast<fnSetPreferredAppMode>(GetProcAddress(uxtheme, MAKEINTRESOURCEA(135)));
    g_flushMenuThemes = reinterpret_cast<fnFlushMenuThemes>(GetProcAddress(uxtheme, MAKEINTRESOURCEA(136)));
    if (g_setPreferredAppMode)
        g_setPreferredAppMode(1); // AllowDark
}

void allowDark(HWND hwnd, bool dark) {
    ensureDarkApis();
    if (g_allowDarkForWindow)
        g_allowDarkForWindow(hwnd, dark ? TRUE : FALSE);
}

const Theme* findId(const wchar_t* id) {
    for (const auto& t : g_catalog)
        if (iequals(t.id, std::wstring(id))) return &t;
    return nullptr;
}

bool isPushButton(HWND ctl) {
    wchar_t cls[64]{};
    GetClassNameW(ctl, cls, 64);
    if (!iequals(cls, L"Button")) return false;
    const auto style = GetWindowLongW(ctl, GWL_STYLE);
    const auto type = style & 0x0000000F; // BS_TYPEMASK
    return type == BS_PUSHBUTTON || type == BS_DEFPUSHBUTTON;
}

LRESULT CALLBACK dialogButtonProc(HWND wnd, UINT msg, WPARAM w, LPARAM l, UINT_PTR, DWORD_PTR) {
    if (msg == WM_PAINT) {
        PAINTSTRUCT ps;
        HDC hdc = BeginPaint(wnd, &ps);
        RECT rc{};
        GetClientRect(wnd, &rc);
        const auto& t = currentTheme();
        const bool enabled = IsWindowEnabled(wnd) != FALSE;
        const bool hot = (GetWindowLongPtrW(wnd, GWLP_USERDATA) & 1) != 0;
        const bool pressed = (GetKeyState(VK_LBUTTON) < 0) && hot;
        const bool def = (GetWindowLongW(wnd, GWL_STYLE) & BS_DEFPUSHBUTTON) == BS_DEFPUSHBUTTON;

        COLORREF bg = t.dark ? t.light : t.face;
        if (def) bg = t.highlight;
        if (hot) bg = def ? blend(t.highlight, t.darkShadow, 0.15) : t.hover;
        if (pressed) bg = def ? blend(t.highlight, t.darkShadow, 0.3) : blend(t.hover, t.shadow, 0.35);
        if (!enabled) bg = blend(t.face, t.window, 0.5);

        HBRUSH br = CreateSolidBrush(bg);
        FillRect(hdc, &rc, br);
        DeleteObject(br);
        HPEN pen = CreatePen(PS_SOLID, 1, def ? t.highlight : t.border);
        auto oldPen = SelectObject(hdc, pen);
        auto oldBr = SelectObject(hdc, GetStockObject(NULL_BRUSH));
        Rectangle(hdc, rc.left, rc.top, rc.right, rc.bottom);
        SelectObject(hdc, oldPen);
        SelectObject(hdc, oldBr);
        DeleteObject(pen);

        wchar_t text[128]{};
        GetWindowTextW(wnd, text, 128);
        SetBkMode(hdc, TRANSPARENT);
        SetTextColor(hdc, !enabled ? t.disabledText : (def ? t.highlightText : t.ink));
        DrawTextW(hdc, text, -1, &rc, DT_CENTER | DT_VCENTER | DT_SINGLELINE);
        EndPaint(wnd, &ps);
        return 0;
    }
    if (msg == WM_MOUSEMOVE) {
        TRACKMOUSEEVENT tme{sizeof(tme), TME_LEAVE, wnd, 0};
        TrackMouseEvent(&tme);
        SetWindowLongPtrW(wnd, GWLP_USERDATA, 1);
        InvalidateRect(wnd, nullptr, FALSE);
    }
    if (msg == WM_MOUSELEAVE) {
        SetWindowLongPtrW(wnd, GWLP_USERDATA, 0);
        InvalidateRect(wnd, nullptr, FALSE);
    }
    if (msg == WM_ENABLE || msg == WM_LBUTTONDOWN || msg == WM_LBUTTONUP)
        InvalidateRect(wnd, nullptr, FALSE);
    if (msg == WM_NCDESTROY)
        RemoveWindowSubclass(wnd, dialogButtonProc, 2);
    return DefSubclassProc(wnd, msg, w, l);
}

void themeDialogButton(HWND btn) {
    if (!isPushButton(btn)) return;
    SetWindowSubclass(btn, dialogButtonProc, 2, 0);
    applyControlTheme(btn);
}

LRESULT CALLBACK dialogSubclass(HWND dlg, UINT msg, WPARAM w, LPARAM l, UINT_PTR, DWORD_PTR) {
    if (msg == WM_ERASEBKGND) {
        RECT rc{};
        GetClientRect(dlg, &rc);
        FillRect(reinterpret_cast<HDC>(w), &rc, themeBrushes().face);
        return 1;
    }
    if (msg == WM_CTLCOLORDLG || msg == WM_CTLCOLORSTATIC || msg == WM_CTLCOLORBTN ||
        msg == WM_CTLCOLOREDIT || msg == WM_CTLCOLORLISTBOX || msg == WM_CTLCOLORSCROLLBAR) {
        if (auto br = dialogCtlColor(dlg, msg, reinterpret_cast<HDC>(w), reinterpret_cast<HWND>(l)))
            return reinterpret_cast<LRESULT>(br);
    }
    if (msg == WM_NCDESTROY)
        RemoveWindowSubclass(dlg, dialogSubclass, 1);
    return DefSubclassProc(dlg, msg, w, l);
}

} // namespace

void ThemeBrushes::destroy() {
    auto drop = [](HBRUSH& b) {
        if (b) {
            DeleteObject(b);
            b = nullptr;
        }
    };
    drop(face);
    drop(window);
    drop(parchment);
    drop(sidebar);
    drop(status);
    drop(menu);
    drop(hover);
    drop(accent);
    drop(border);
    drop(highlight);
}

void ThemeBrushes::reset(const Theme& t) {
    destroy();
    face = CreateSolidBrush(t.face);
    window = CreateSolidBrush(t.window);
    parchment = CreateSolidBrush(t.parchment);
    sidebar = CreateSolidBrush(t.sidebar);
    status = CreateSolidBrush(t.statusBar);
    menu = CreateSolidBrush(t.menuBar);
    hover = CreateSolidBrush(t.hover);
    accent = CreateSolidBrush(t.highlight);
    border = CreateSolidBrush(t.border);
    highlight = CreateSolidBrush(t.highlight);
}

const std::vector<Theme>& allThemes() { return g_catalog; }

std::wstring normalizeThemeId(const std::wstring& id) {
    if (id.empty() || iequals(id, L"system")) return L"system";
    if (iequals(id, L"light")) return L"win10";
    if (iequals(id, L"dark")) return L"win11-dark";
    if (findId(id.c_str())) return id;
    return L"system";
}

const Theme& themeById(const std::wstring& id) {
    auto n = normalizeThemeId(id);
    if (iequals(n, L"system"))
        return systemAppsDark() ? *findId(L"win11-dark") : *findId(L"win10");
    if (auto* t = findId(n.c_str())) return *t;
    return *findId(L"win10");
}

const Theme& currentTheme() { return themeById(g_id); }

ThemeBrushes& themeBrushes() { return g_brushes; }

void setCurrentTheme(const std::wstring& id) {
    g_id = normalizeThemeId(id);
    ensureDarkApis();
    if (g_setPreferredAppMode)
        g_setPreferredAppMode(currentTheme().dark ? 2 : 1); // ForceDark / AllowDark
    if (g_flushMenuThemes)
        g_flushMenuThemes();
    g_brushes.reset(currentTheme());
}

COLORREF blend(COLORREF a, COLORREF b, double amountB) {
    amountB = amountB < 0 ? 0 : (amountB > 1 ? 1 : amountB);
    auto mix = [&](int ca, int cb) { return static_cast<int>(ca + (cb - ca) * amountB); };
    return RGB(mix(GetRValue(a), GetRValue(b)), mix(GetGValue(a), GetGValue(b)), mix(GetBValue(a), GetBValue(b)));
}

COLORREF listAltRow() {
    const auto& t = currentTheme();
    return blend(t.window, t.face, t.dark ? 0.45 : 0.35);
}

COLORREF listSelection() {
    const auto& t = currentTheme();
    return t.dark ? blend(t.highlight, t.face, 0.55) : blend(t.highlight, rgb(255, 255, 255), 0.75);
}

void applyWindowChrome(HWND wnd) {
    const auto& t = currentTheme();
    allowDark(wnd, t.dark);
    BOOL dark = t.dark ? TRUE : FALSE;
    DwmSetWindowAttribute(wnd, 20, &dark, sizeof(dark));
    DwmSetWindowAttribute(wnd, 19, &dark, sizeof(dark));
    auto colorrefToDwm = [](COLORREF c) {
        return static_cast<int>(GetRValue(c) | (GetGValue(c) << 8) | (GetBValue(c) << 16));
    };
    int caption = colorrefToDwm(t.face);
    int border = colorrefToDwm(t.border);
    int text = colorrefToDwm(t.ink);
    DwmSetWindowAttribute(wnd, 35, &caption, sizeof(caption));
    DwmSetWindowAttribute(wnd, 34, &border, sizeof(border));
    DwmSetWindowAttribute(wnd, 36, &text, sizeof(text));
}

void stripClientEdge(HWND hwnd) {
    if (!hwnd) return;
    auto ex = GetWindowLongW(hwnd, GWL_EXSTYLE);
    if (ex & (WS_EX_CLIENTEDGE | WS_EX_STATICEDGE | WS_EX_WINDOWEDGE)) {
        SetWindowLongW(hwnd, GWL_EXSTYLE, ex & ~(WS_EX_CLIENTEDGE | WS_EX_STATICEDGE | WS_EX_WINDOWEDGE));
        SetWindowPos(hwnd, nullptr, 0, 0, 0, 0,
                     SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);
    }
}

void paintPanelFrame(HDC hdc, const RECT& rc) {
    HPEN pen = CreatePen(PS_SOLID, 1, currentTheme().border);
    auto old = SelectObject(hdc, pen);
    auto oldBr = SelectObject(hdc, GetStockObject(NULL_BRUSH));
    Rectangle(hdc, rc.left, rc.top, rc.right, rc.bottom);
    SelectObject(hdc, old);
    SelectObject(hdc, oldBr);
    DeleteObject(pen);
}

void paintPanelFill(HDC hdc, const RECT& rc, HBRUSH fill) {
    FillRect(hdc, &rc, fill);
    paintPanelFrame(hdc, rc);
}

void applyControlTheme(HWND hwnd) {
    if (!hwnd) return;
    const auto& t = currentTheme();
    allowDark(hwnd, t.dark);
    stripClientEdge(hwnd);

    wchar_t cls[64]{};
    GetClassNameW(hwnd, cls, 64);
    if (iequals(cls, L"Edit") || iequals(cls, L"ComboBox") || iequals(cls, L"ComboBoxEx32")) {
        SetWindowTheme(hwnd, t.dark ? L"DarkMode_CFD" : L"CFD", nullptr);
    } else if (iequals(cls, L"Button")) {
        SetWindowTheme(hwnd, t.dark ? L"DarkMode_Explorer" : L"Explorer", nullptr);
    } else if (iequals(cls, L"ListBox") || iequals(cls, L"SysListView32") || iequals(cls, L"SysTreeView32") ||
               iequals(cls, L"msctls_trackbar32") || iequals(cls, L"msctls_progress32") ||
               iequals(cls, L"msctls_statusbar32") || iequals(cls, L"ScrollBar") ||
               iequals(cls, L"SysHeader32")) {
        SetWindowTheme(hwnd, t.dark ? L"DarkMode_Explorer" : L"Explorer", nullptr);
    } else {
        SetWindowTheme(hwnd, t.dark ? L"DarkMode_Explorer" : L"Explorer", nullptr);
    }
    InvalidateRect(hwnd, nullptr, TRUE);
}

void applyTreeTheme(HWND root) {
    if (!root) return;
    applyControlTheme(root);
    EnumChildWindows(
        root,
        [](HWND c, LPARAM) {
            applyControlTheme(c);
            return TRUE;
        },
        0);
}

void attachDialogTheme(HWND dlg) {
    SetWindowSubclass(dlg, dialogSubclass, 1, 0);
    applyWindowChrome(dlg);
    applyTreeTheme(dlg);
    EnumChildWindows(
        dlg,
        [](HWND c, LPARAM) {
            if (isPushButton(c))
                themeDialogButton(c);
            return TRUE;
        },
        0);
    InvalidateRect(dlg, nullptr, TRUE);
}

HBRUSH dialogCtlColor(HWND, UINT msg, HDC hdc, HWND ctl) {
    const auto& t = currentTheme();
    auto& b = themeBrushes();

    if (msg == WM_CTLCOLOREDIT || msg == WM_CTLCOLORLISTBOX) {
        SetTextColor(hdc, t.ink);
        SetBkColor(hdc, t.window);
        SetBkMode(hdc, OPAQUE);
        return b.window;
    }

    if (msg == WM_CTLCOLORBTN) {
        // Radio / checkbox labels — keep text readable on dialog face.
        SetTextColor(hdc, t.ink);
        SetBkColor(hdc, t.face);
        SetBkMode(hdc, TRANSPARENT);
        return b.face;
    }

    if (msg == WM_CTLCOLORSTATIC) {
        wchar_t cls[64]{};
        if (ctl) GetClassNameW(ctl, cls, 64);
        SetTextColor(hdc, t.ink);
        SetBkColor(hdc, t.face);
        SetBkMode(hdc, TRANSPARENT);
        // Static with SS_SUNKEN / edge still gets face fill behind text.
        return b.face;
    }

    SetTextColor(hdc, t.ink);
    SetBkColor(hdc, t.face);
    SetBkMode(hdc, TRANSPARENT);
    return b.face;
}

} // namespace pm
