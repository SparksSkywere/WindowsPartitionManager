#pragma once

#include <windows.h>
#include <string>
#include <vector>

namespace pm {

struct Theme {
    const wchar_t* id = L"system";
    const wchar_t* name = L"System";
    const wchar_t* era = L"";
    COLORREF face{};
    COLORREF window{};
    COLORREF parchment{};
    COLORREF ink{};
    COLORREF highlight{};
    COLORREF highlightText{};
    COLORREF shadow{};
    COLORREF darkShadow{};
    COLORREF light{};
    COLORREF accent{};
    COLORREF accentBorder{};
    COLORREF sidebar{};
    COLORREF disabledText{};
    COLORREF menuBar{};
    COLORREF menuBarText{};
    COLORREF hover{};
    COLORREF tabInactive{};
    COLORREF primaryButton{};
    COLORREF border{};
    COLORREF statusBar{};
    COLORREF placeholder{};
    COLORREF metaText{};
    bool dark = false;
};

struct ThemeBrushes {
    HBRUSH face = nullptr;
    HBRUSH window = nullptr;
    HBRUSH parchment = nullptr;
    HBRUSH sidebar = nullptr;
    HBRUSH status = nullptr;
    HBRUSH menu = nullptr;
    HBRUSH hover = nullptr;
    HBRUSH accent = nullptr;
    HBRUSH border = nullptr;
    HBRUSH highlight = nullptr;

    void reset(const Theme& t);
    void destroy();
};

const std::vector<Theme>& allThemes();
std::wstring normalizeThemeId(const std::wstring& id);
const Theme& themeById(const std::wstring& id);
const Theme& currentTheme();
ThemeBrushes& themeBrushes();
void setCurrentTheme(const std::wstring& id);

void applyWindowChrome(HWND wnd);
void applyControlTheme(HWND hwnd);
void applyTreeTheme(HWND root);
void attachDialogTheme(HWND dlg);
HBRUSH dialogCtlColor(HWND dlg, UINT msg, HDC hdc, HWND ctl);

void stripClientEdge(HWND hwnd);
void paintPanelFrame(HDC hdc, const RECT& rc);
void paintPanelFill(HDC hdc, const RECT& rc, HBRUSH fill);

COLORREF blend(COLORREF a, COLORREF b, double amountB);
COLORREF listAltRow();
COLORREF listSelection();

} // namespace pm
