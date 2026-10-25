#include "Ui.hpp"
#include "AppInfo.hpp"
#include "Config.hpp"
#include "Dialogs.hpp"
#include "Executor.hpp"
#include "Inventory.hpp"
#include "Preview.hpp"
#include "Queue.hpp"
#include "Theme.hpp"
#include "Util.hpp"
#include "resource.h"

#include <commctrl.h>
#include <dbt.h>
#include <dwmapi.h>
#include <objbase.h>
#include <shellapi.h>
#include <uxtheme.h>
#include <windowsx.h>

#include <algorithm>
#include <atomic>
#include <memory>
#include <thread>

namespace pm {
namespace {

constexpr wchar_t kClass[] = L"PartitionManagerMain";
constexpr UINT WM_REFRESH_DONE = WM_APP + 1;
constexpr UINT WM_APPLY_DONE = WM_APP + 2;
constexpr UINT WM_LOG_LINE = WM_APP + 3;
constexpr UINT WM_PROGRESS = WM_APP + 4;
constexpr UINT_PTR kDeviceTimer = 1;

constexpr int kSidebarW = 232;
constexpr int kToolH = 40;
constexpr int kMapH = 168;
constexpr int kBottomH = 130;
constexpr int kStatusH = 24;
constexpr int kDiskLabelW = 168;
constexpr int kMinSegW = 56;

struct RowRef {
    int disk = 0;
    int seg = 0;
};

struct MenuItemData {
    int id = 0;
    std::wstring text;
};

struct App {
    HWND wnd = nullptr;
    HWND filter = nullptr;
    HWND list = nullptr;
    HWND map = nullptr;
    HWND pending = nullptr;
    HWND logBox = nullptr;
    HWND status = nullptr;
    HWND progress = nullptr;
    HWND banner = nullptr;
    HWND sidebar = nullptr;
    HWND lblFilter = nullptr;
    HWND lblPending = nullptr;
    HWND lblLog = nullptr;
    HWND lblMap = nullptr;
    HWND lblSidePart = nullptr;
    HWND lblSideDisk = nullptr;
    HFONT font = nullptr;
    HFONT fontBold = nullptr;
    HFONT fontSmall = nullptr;
    HFONT fontMono = nullptr;
    HMENU menu = nullptr;
    std::vector<std::unique_ptr<MenuItemData>> menuItems;
    std::vector<HWND> toolButtons;
    std::vector<HWND> sideButtons;
    ConfigStore cfg;
    Log log;
    Storage storage{log};
    Queue queue{log, storage};
    std::vector<RowRef> rows;
    int selDisk = -1;
    int selSeg = -1;
    bool busy = false;
    std::atomic<bool> cancel{false};
    std::wstring filterText;
    std::wstring statusText = L"Ready";
    int progressValue = 0;
};

constexpr LONG_PTR kBtnHot = 0x100;

LONG_PTR btnKind(HWND h) { return GetWindowLongPtrW(h, GWLP_USERDATA) & 0xFF; }
bool btnHot(HWND h) { return (GetWindowLongPtrW(h, GWLP_USERDATA) & kBtnHot) != 0; }
void setBtnHot(HWND h, bool hot) {
    auto v = GetWindowLongPtrW(h, GWLP_USERDATA);
    SetWindowLongPtrW(h, GWLP_USERDATA, hot ? (v | kBtnHot) : (v & ~kBtnHot));
}

COLORREF kindColor(SegmentKind kind, bool unallocated) {
    if (unallocated) return RGB(200, 200, 200);
    switch (kind) {
    case SegmentKind::Logical: return RGB(92, 184, 92);
    case SegmentKind::Efi: return RGB(142, 107, 199);
    case SegmentKind::Recovery: return RGB(230, 162, 60);
    case SegmentKind::MicrosoftReserved: return RGB(140, 140, 140);
    case SegmentKind::Extended: return RGB(120, 170, 210);
    default: return RGB(74, 144, 217);
    }
}

void applyThemeToApp(App& a) {
    setCurrentTheme(a.cfg.config.theme);
    const auto& t = currentTheme();
    applyWindowChrome(a.wnd);
    if (a.font) DeleteObject(a.font);
    if (a.fontBold) DeleteObject(a.fontBold);
    if (a.fontSmall) DeleteObject(a.fontSmall);
    if (a.fontMono) DeleteObject(a.fontMono);
    a.font = CreateFontW(16, 0, 0, 0, FW_NORMAL, FALSE, FALSE, FALSE, DEFAULT_CHARSET,
                         OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS, CLEARTYPE_QUALITY,
                         DEFAULT_PITCH | FF_DONTCARE, L"Segoe UI");
    a.fontBold = CreateFontW(16, 0, 0, 0, FW_SEMIBOLD, FALSE, FALSE, FALSE, DEFAULT_CHARSET,
                             OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS, CLEARTYPE_QUALITY,
                             DEFAULT_PITCH | FF_DONTCARE, L"Segoe UI");
    a.fontSmall = CreateFontW(13, 0, 0, 0, FW_NORMAL, FALSE, FALSE, FALSE, DEFAULT_CHARSET,
                              OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS, CLEARTYPE_QUALITY,
                              DEFAULT_PITCH | FF_DONTCARE, L"Segoe UI");
    a.fontMono = CreateFontW(14, 0, 0, 0, FW_NORMAL, FALSE, FALSE, FALSE, DEFAULT_CHARSET,
                             OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS, CLEARTYPE_QUALITY,
                             DEFAULT_PITCH | FF_DONTCARE, L"Cascadia Mono");
    EnumChildWindows(
        a.wnd,
        [](HWND c, LPARAM font) {
            SendMessageW(c, WM_SETFONT, static_cast<WPARAM>(font), TRUE);
            return TRUE;
        },
        reinterpret_cast<LPARAM>(a.font));
    if (a.logBox) SendMessageW(a.logBox, WM_SETFONT, reinterpret_cast<WPARAM>(a.fontMono), TRUE);
    auto bold = [&](HWND h) {
        if (h) SendMessageW(h, WM_SETFONT, reinterpret_cast<WPARAM>(a.fontBold), TRUE);
    };
    bold(a.lblPending);
    bold(a.lblLog);
    bold(a.lblSidePart);
    bold(a.lblSideDisk);
    applyTreeTheme(a.wnd);
    if (a.list) {
        ListView_SetBkColor(a.list, t.window);
        ListView_SetTextBkColor(a.list, t.window);
        ListView_SetTextColor(a.list, t.ink);
        if (HWND header = ListView_GetHeader(a.list))
            applyControlTheme(header);
    }
    if (a.pending)
        SendMessageW(a.pending, LB_SETSEL, FALSE, -1);
    if (a.menu) {
        MENUINFO mi{sizeof(mi), MIM_BACKGROUND | MIM_APPLYTOSUBMENUS};
        mi.hbrBack = themeBrushes().menu;
        SetMenuInfo(a.menu, &mi);
        DrawMenuBar(a.wnd);
    }
    if (a.status) InvalidateRect(a.status, nullptr, TRUE);
    if (a.progress) InvalidateRect(a.progress, nullptr, TRUE);
    InvalidateRect(a.wnd, nullptr, TRUE);
}

HWND makeButton(HWND parent, int id, const wchar_t* text, LONG_PTR styleKind);

LRESULT CALLBACK themedButtonProc(HWND wnd, UINT msg, WPARAM w, LPARAM l, UINT_PTR, DWORD_PTR) {
    if (msg == WM_MOUSEMOVE) {
        TRACKMOUSEEVENT tme{sizeof(tme), TME_LEAVE, wnd, 0};
        TrackMouseEvent(&tme);
        if (!btnHot(wnd)) {
            setBtnHot(wnd, true);
            InvalidateRect(wnd, nullptr, FALSE);
        }
    } else if (msg == WM_MOUSELEAVE) {
        if (btnHot(wnd)) {
            setBtnHot(wnd, false);
            InvalidateRect(wnd, nullptr, FALSE);
        }
    } else if (msg == WM_ENABLE || msg == WM_LBUTTONDOWN || msg == WM_LBUTTONUP) {
        InvalidateRect(wnd, nullptr, FALSE);
    } else if (msg == WM_NCDESTROY) {
        RemoveWindowSubclass(wnd, themedButtonProc, 3);
    }
    return DefSubclassProc(wnd, msg, w, l);
}

LRESULT CALLBACK statusProc(HWND wnd, UINT msg, WPARAM w, LPARAM l, UINT_PTR, DWORD_PTR) {
    auto* a = reinterpret_cast<App*>(GetWindowLongPtrW(GetParent(wnd), GWLP_USERDATA));
    if (msg == WM_ERASEBKGND) {
        RECT rc{};
        GetClientRect(wnd, &rc);
        FillRect(reinterpret_cast<HDC>(w), &rc, themeBrushes().status);
        return 1;
    }
    if (msg == WM_PAINT) {
        PAINTSTRUCT ps;
        HDC hdc = BeginPaint(wnd, &ps);
        RECT rc{};
        GetClientRect(wnd, &rc);
        FillRect(hdc, &rc, themeBrushes().status);
        HPEN pen = CreatePen(PS_SOLID, 1, currentTheme().border);
        auto old = SelectObject(hdc, pen);
        MoveToEx(hdc, rc.left, rc.top, nullptr);
        LineTo(hdc, rc.right, rc.top);
        SelectObject(hdc, old);
        DeleteObject(pen);
        SetBkMode(hdc, TRANSPARENT);
        SetTextColor(hdc, currentTheme().ink);
        if (a && a->font) SelectObject(hdc, a->font);
        RECT tr = rc;
        tr.left += 10;
        DrawTextW(hdc, a ? a->statusText.c_str() : L"", -1, &tr, DT_LEFT | DT_VCENTER | DT_SINGLELINE | DT_END_ELLIPSIS);
        EndPaint(wnd, &ps);
        return 0;
    }
    if (msg == WM_NCDESTROY)
        RemoveWindowSubclass(wnd, statusProc, 4);
    return DefSubclassProc(wnd, msg, w, l);
}

LRESULT CALLBACK progressProc(HWND wnd, UINT msg, WPARAM w, LPARAM l, UINT_PTR, DWORD_PTR) {
    auto* a = reinterpret_cast<App*>(GetWindowLongPtrW(GetParent(wnd), GWLP_USERDATA));
    if (msg == WM_ERASEBKGND) return 1;
    if (msg == WM_PAINT) {
        PAINTSTRUCT ps;
        HDC hdc = BeginPaint(wnd, &ps);
        RECT rc{};
        GetClientRect(wnd, &rc);
        const auto& t = currentTheme();
        HBRUSH track = CreateSolidBrush(t.dark ? t.light : blend(t.face, t.shadow, 0.25));
        FillRect(hdc, &rc, track);
        DeleteObject(track);
        paintPanelFrame(hdc, rc);
        const int value = a ? a->progressValue : 0;
        if (value > 0) {
            RECT fill = rc;
            InflateRect(&fill, -1, -1);
            fill.right = fill.left + (std::max)(0, static_cast<int>(fill.right - fill.left) * (std::min)(100, value) / 100);
            HBRUSH br = CreateSolidBrush(t.highlight);
            FillRect(hdc, &fill, br);
            DeleteObject(br);
        }
        EndPaint(wnd, &ps);
        return 0;
    }
    if (msg == WM_NCDESTROY)
        RemoveWindowSubclass(wnd, progressProc, 5);
    return DefSubclassProc(wnd, msg, w, l);
}

HWND makeButton(HWND parent, int id, const wchar_t* text, LONG_PTR styleKind) {
    HWND b = CreateWindowW(L"BUTTON", text,
                           WS_CHILD | WS_VISIBLE | WS_TABSTOP | BS_OWNERDRAW,
                           0, 0, 0, 0, parent, reinterpret_cast<HMENU>(static_cast<INT_PTR>(id)), nullptr, nullptr);
    SetWindowLongPtrW(b, GWLP_USERDATA, styleKind);
    SetWindowSubclass(b, themedButtonProc, 3, 0);
    return b;
}

void appendOwned(App& a, HMENU menu, UINT flags, int id, const wchar_t* text) {
    if (flags & MF_SEPARATOR) {
        AppendMenuW(menu, MF_SEPARATOR, 0, nullptr);
        return;
    }
    auto item = std::make_unique<MenuItemData>();
    item->id = id;
    item->text = text;
    AppendMenuW(menu, flags | MF_OWNERDRAW, static_cast<UINT>(id), reinterpret_cast<LPCWSTR>(item.get()));
    a.menuItems.push_back(std::move(item));
}

const Disk* selectedDisk(const App& a) {
    return a.queue.working.findDisk(a.selDisk);
}

const Segment* selectedSeg(const App& a) {
    const auto* disk = selectedDisk(a);
    if (!disk || a.selSeg < 0 || a.selSeg >= static_cast<int>(disk->segments.size())) return nullptr;
    return &disk->segments[static_cast<size_t>(a.selSeg)];
}

bool confirm(App& a, const std::wstring& text) {
    if (!a.cfg.config.confirmDestructive) return true;
    return MessageBoxW(a.wnd, text.c_str(), kProductName, MB_YESNO | MB_ICONWARNING) == IDYES;
}

void setStatus(App& a, const std::wstring& text) {
    a.statusText = text;
    if (a.status) InvalidateRect(a.status, nullptr, FALSE);
}

void updateActions(App& a);

void rebuildList(App& a) {
    ListView_DeleteAllItems(a.list);
    a.rows.clear();
    int parts = 0, unalloc = 0;
    int index = 0;
    for (const auto& disk : a.queue.working.disks) {
        for (int i = 0; i < static_cast<int>(disk.segments.size()); ++i) {
            const auto& s = disk.segments[static_cast<size_t>(i)];
            const auto size = formatBytes(s.size);
            const auto name = segmentDisplayName(s.letter, s.label, static_cast<int>(s.kind), s.unallocated, size);
            if (!a.filterText.empty()) {
                auto hay = lower(name + s.fileSystem + disk.model);
                if (hay.find(lower(a.filterText)) == std::wstring::npos) continue;
            }
            LVITEMW item{};
            item.mask = LVIF_TEXT | LVIF_PARAM;
            item.iItem = index;
            auto drive = driveText(s.letter, s.unallocated);
            item.pszText = drive.data();
            item.lParam = static_cast<LPARAM>(a.rows.size());
            ListView_InsertItem(a.list, &item);
            ListView_SetItemText(a.list, index, 1, const_cast<LPWSTR>(name.c_str()));
            auto type = std::wstring(kindText(static_cast<int>(s.kind)));
            ListView_SetItemText(a.list, index, 2, type.data());
            auto fs = s.fileSystem;
            ListView_SetItemText(a.list, index, 3, fs.data());
            auto sz = size;
            ListView_SetItemText(a.list, index, 4, sz.data());
            auto st = s.status;
            ListView_SetItemText(a.list, index, 5, st.data());
            auto dn = L"Disk " + std::to_wstring(disk.number);
            ListView_SetItemText(a.list, index, 6, dn.data());
            a.rows.push_back({disk.number, i});
            if (s.unallocated) ++unalloc;
            else ++parts;
            if (disk.number == a.selDisk && i == a.selSeg)
                ListView_SetItemState(a.list, index, LVIS_SELECTED | LVIS_FOCUSED, LVIS_SELECTED | LVIS_FOCUSED);
            ++index;
        }
    }
    SendMessageW(a.pending, LB_RESETCONTENT, 0, 0);
    for (const auto& op : a.queue.ops)
        SendMessageW(a.pending, LB_ADDSTRING, 0, reinterpret_cast<LPARAM>(op.description.c_str()));
    const auto summary = std::to_wstring(a.queue.working.disks.size()) + L" disk(s)  ·  " +
                         std::to_wstring(parts) + L" partition(s)  ·  " + std::to_wstring(unalloc) +
                         L" unallocated" +
                         (a.queue.hasPending() ? L"  ·  " + std::to_wstring(a.queue.ops.size()) + L" pending" : L"");
    setStatus(a, summary);
    InvalidateRect(a.map, nullptr, TRUE);
    updateActions(a);
}

void selectFromList(App& a) {
    const int i = ListView_GetNextItem(a.list, -1, LVNI_SELECTED);
    if (i < 0 || i >= static_cast<int>(a.rows.size())) return;
    a.selDisk = a.rows[static_cast<size_t>(i)].disk;
    a.selSeg = a.rows[static_cast<size_t>(i)].seg;
    InvalidateRect(a.map, nullptr, TRUE);
    updateActions(a);
}

void enqueue(App& a, PendingOperation op) {
    a.queue.enqueue(std::move(op));
    rebuildList(a);
    setStatus(a, std::to_wstring(a.queue.ops.size()) + L" pending operation(s) — click Apply to commit");
}

void startRefresh(App& a, bool discardPending) {
    if (a.busy) return;
    if (a.queue.hasPending() && !discardPending) {
        if (MessageBoxW(a.wnd, L"Refreshing will discard pending operations. Continue?", kProductName,
                        MB_YESNO | MB_ICONQUESTION) != IDYES)
            return;
        a.queue.discardAll();
    }
    a.busy = true;
    updateActions(a);
    setStatus(a, L"Reading disks…");
    std::thread([&a] {
        CoInitializeEx(nullptr, COINIT_MULTITHREADED);
        a.queue.refresh(a.cfg.config.display);
        CoUninitialize();
        PostMessageW(a.wnd, WM_REFRESH_DONE, 0, 0);
    }).detach();
}

void startApply(App& a) {
    if (a.busy || !a.queue.hasPending()) return;
    if (!promptApply(a.wnd, a.queue.ops)) return;
    a.busy = true;
    a.cancel = false;
    updateActions(a);
    ShowWindow(a.progress, SW_SHOW);
    a.progressValue = 0;
    InvalidateRect(a.progress, nullptr, FALSE);
    setStatus(a, L"Applying operations…");
    std::thread([&a] {
        CoInitializeEx(nullptr, COINIT_MULTITHREADED);
        const auto result = a.queue.apply(&a.cancel, [&a](int p) { PostMessageW(a.wnd, WM_PROGRESS, p, 0); });
        a.queue.refresh(a.cfg.config.display);
        CoUninitialize();
        auto* heap = new OperationResult(result);
        PostMessageW(a.wnd, WM_APPLY_DONE, 0, reinterpret_cast<LPARAM>(heap));
    }).detach();
}

bool canMutate(const App& a, const Segment* s) {
    const auto* d = selectedDisk(a);
    return !a.busy && s && d && !d->optical && !d->offline && d->initialized();
}

void enableId(HWND wnd, int id, bool on) {
    if (auto h = GetDlgItem(wnd, id)) {
        EnableWindow(h, on ? TRUE : FALSE);
        InvalidateRect(h, nullptr, FALSE);
    }
    EnableMenuItem(GetMenu(wnd), static_cast<UINT>(id), MF_BYCOMMAND | (on ? MF_ENABLED : MF_GRAYED));
}

void updateActions(App& a) {
    if (!a.wnd) return;
    const auto* disk = selectedDisk(a);
    const auto* seg = selectedSeg(a);
    const bool idle = !a.busy;
    enableId(a.wnd, IDM_REFRESH, idle);
    enableId(a.wnd, IDC_BTN_REFRESH, idle);
    enableId(a.wnd, IDM_APPLY, idle && a.queue.hasPending());
    enableId(a.wnd, IDC_BTN_APPLY, idle && a.queue.hasPending());
    enableId(a.wnd, IDM_UNDO, idle && a.queue.hasPending());
    enableId(a.wnd, IDC_BTN_UNDO, idle && a.queue.hasPending());
    enableId(a.wnd, IDM_DISCARD, idle && a.queue.hasPending());
    const bool create = canMutate(a, seg) && seg && seg->unallocated;
    const bool mutatePart = canMutate(a, seg) && seg && !seg->unallocated;
    const bool protect = seg && a.cfg.config.protectSystem && seg->protectedVolume();
    enableId(a.wnd, IDM_CREATE, create);
    enableId(a.wnd, IDC_BTN_CREATE, create);
    enableId(a.wnd, IDM_DELETE, mutatePart && !protect);
    enableId(a.wnd, IDC_BTN_DELETE, mutatePart && !protect);
    enableId(a.wnd, IDM_FORMAT, mutatePart && !protect);
    enableId(a.wnd, IDC_BTN_FORMAT, mutatePart && !protect);
    enableId(a.wnd, IDM_RESIZE, mutatePart);
    enableId(a.wnd, IDC_BTN_RESIZE, mutatePart);
    enableId(a.wnd, IDM_LETTER, mutatePart);
    enableId(a.wnd, IDM_LABEL, mutatePart && seg && !seg->fileSystem.empty());
    enableId(a.wnd, IDM_HIDE, mutatePart && !protect);
    enableId(a.wnd, IDM_ACTIVE, mutatePart && disk && disk->style == PartitionStyle::Mbr &&
                                   seg && seg->kind == SegmentKind::Primary);
    enableId(a.wnd, IDM_CHECK, idle && seg && seg->letter);
    enableId(a.wnd, IDM_CLONE_PART, mutatePart);
    enableId(a.wnd, IDM_PART_PROPS, seg != nullptr);
    const bool diskOk = idle && disk && !disk->optical;
    enableId(a.wnd, IDM_INIT, diskOk && disk && !disk->initialized());
    bool empty = disk != nullptr;
    if (disk)
        for (const auto& s : disk->segments)
            if (!s.unallocated) empty = false;
    enableId(a.wnd, IDM_CONVERT, diskOk && disk && disk->initialized() && empty && !disk->boot && !disk->system);
    enableId(a.wnd, IDM_DELETE_ALL, diskOk && disk && !disk->boot && !disk->system && !empty);
    enableId(a.wnd, IDM_ONLINE, diskOk && disk && !disk->boot && !disk->system);
    enableId(a.wnd, IDM_CLONE_DISK, diskOk && disk && disk->initialized());
    enableId(a.wnd, IDC_BTN_CLONE, diskOk && disk && disk->initialized());
    enableId(a.wnd, IDM_DISK_PROPS, disk != nullptr);
    for (auto h : a.toolButtons) InvalidateRect(h, nullptr, TRUE);
    for (auto h : a.sideButtons) InvalidateRect(h, nullptr, TRUE);
}

void onCommand(App& a, int id) {
    auto* disk = selectedDisk(a);
    auto* seg = selectedSeg(a);
    switch (id) {
    case IDM_REFRESH:
    case IDC_BTN_REFRESH:
        startRefresh(a, false);
        break;
    case IDM_APPLY:
    case IDC_BTN_APPLY:
        startApply(a);
        break;
    case IDM_UNDO:
    case IDC_BTN_UNDO:
        a.queue.undoLast();
        rebuildList(a);
        break;
    case IDM_DISCARD:
        if (a.queue.hasPending() &&
            MessageBoxW(a.wnd, L"Discard all pending operations?", kProductName, MB_YESNO | MB_ICONQUESTION) == IDYES) {
            a.queue.discardAll();
            rebuildList(a);
        }
        break;
    case IDM_CREATE:
    case IDC_BTN_CREATE:
        if (canMutate(a, seg) && seg->unallocated) {
            if (auto r = promptCreate(a.wnd, *seg, availableLetters(a.queue.working))) {
                PendingOperation op;
                op.kind = OperationKind::CreatePartition;
                op.diskNumber = seg->diskNumber;
                op.offset = seg->offset;
                op.size = r->size;
                op.description = L"Create " + formatBytes(r->size) + L" " + r->fileSystem + L" partition on Disk " +
                                 std::to_wstring(seg->diskNumber);
                op.create = {seg->offset, r->size, r->letter, r->label, r->fileSystem, r->formatAfter, r->quick};
                enqueue(a, std::move(op));
            }
        }
        break;
    case IDM_DELETE:
    case IDC_BTN_DELETE:
        if (canMutate(a, seg) && !seg->unallocated &&
            !(a.cfg.config.protectSystem && seg->protectedVolume()) &&
            confirm(a, L"Delete this partition? The data will be unrecoverable after Apply.")) {
            PendingOperation op;
            op.kind = OperationKind::DeletePartition;
            op.diskNumber = seg->diskNumber;
            op.offset = seg->offset;
            op.size = seg->size;
            op.destructive = true;
            op.description = L"Delete partition on Disk " + std::to_wstring(seg->diskNumber);
            enqueue(a, std::move(op));
        }
        break;
    case IDM_FORMAT:
    case IDC_BTN_FORMAT:
        if (canMutate(a, seg) && !seg->unallocated &&
            !(a.cfg.config.protectSystem && seg->protectedVolume())) {
            if (auto r = promptFormat(a.wnd, *seg)) {
                if (!confirm(a, L"Format this partition? All files will be erased after Apply.")) break;
                PendingOperation op;
                op.kind = OperationKind::FormatPartition;
                op.diskNumber = seg->diskNumber;
                op.offset = seg->offset;
                op.size = seg->size;
                op.letter = seg->letter;
                op.destructive = true;
                op.description = L"Format partition as " + r->fileSystem;
                op.format = {r->fileSystem, r->label, r->quick};
                enqueue(a, std::move(op));
            }
        }
        break;
    case IDM_RESIZE:
    case IDC_BTN_RESIZE:
        if (canMutate(a, seg) && !seg->unallocated) {
            std::uint64_t max = seg->size;
            for (const auto& s : disk->segments)
                if (s.unallocated && s.offset == seg->offset + seg->size) max += s.size;
            if (auto r = promptResize(a.wnd, *seg, max)) {
                PendingOperation op;
                op.kind = OperationKind::ResizePartition;
                op.diskNumber = seg->diskNumber;
                op.offset = seg->offset;
                op.newSize = *r;
                op.description = (*r > seg->size ? L"Extend" : L"Shrink") + std::wstring(L" partition to ") + formatBytes(*r);
                enqueue(a, std::move(op));
            }
        }
        break;
    case IDM_LETTER:
        if (canMutate(a, seg) && !seg->unallocated) {
            auto letters = availableLetters(a.queue.working);
            if (seg->letter) letters.insert(letters.begin(), seg->letter);
            if (auto r = promptLetter(a.wnd, seg->letter, letters)) {
                PendingOperation op;
                op.kind = OperationKind::ChangeDriveLetter;
                op.diskNumber = seg->diskNumber;
                op.offset = seg->offset;
                op.letter = *r;
                op.description = *r ? L"Change drive letter to " + std::wstring(1, *r) + L":"
                                    : L"Remove drive letter";
                enqueue(a, std::move(op));
            }
        }
        break;
    case IDM_LABEL:
        if (canMutate(a, seg) && !seg->unallocated) {
            if (auto r = promptLabel(a.wnd, seg->label)) {
                PendingOperation op;
                op.kind = OperationKind::ChangeLabel;
                op.diskNumber = seg->diskNumber;
                op.offset = seg->offset;
                op.letter = seg->letter;
                op.label = *r;
                op.description = L"Set label to \"" + *r + L"\"";
                enqueue(a, std::move(op));
            }
        }
        break;
    case IDM_HIDE:
        if (canMutate(a, seg) && !seg->unallocated && !seg->protectedVolume()) {
            PendingOperation op;
            op.kind = seg->hidden ? OperationKind::UnhidePartition : OperationKind::HidePartition;
            op.diskNumber = seg->diskNumber;
            op.offset = seg->offset;
            op.description = (seg->hidden ? L"Unhide " : L"Hide ") + std::wstring(L"partition");
            enqueue(a, std::move(op));
        }
        break;
    case IDM_ACTIVE:
        if (canMutate(a, seg) && disk && disk->style == PartitionStyle::Mbr && seg->kind == SegmentKind::Primary) {
            PendingOperation op;
            op.kind = OperationKind::SetActive;
            op.diskNumber = seg->diskNumber;
            op.offset = seg->offset;
            op.description = L"Set partition active on Disk " + std::to_wstring(seg->diskNumber);
            enqueue(a, std::move(op));
        }
        break;
    case IDM_CHECK:
        if (!a.busy && seg && seg->letter) {
            const auto fix = MessageBoxW(a.wnd,
                                         L"Check this volume for file system errors?\n\nYes = scan only\nNo = scan and fix\nCancel = abort",
                                         kProductName, MB_YESNOCANCEL | MB_ICONQUESTION);
            if (fix == IDCANCEL) break;
            a.busy = true;
            const wchar_t letter = seg->letter;
            const bool doFix = fix == IDNO;
            std::thread([&a, letter, doFix] {
                CoInitializeEx(nullptr, COINIT_MULTITHREADED);
                const auto result = a.storage.checkVolume(letter, doFix);
                CoUninitialize();
                auto* heap = new OperationResult(result);
                PostMessageW(a.wnd, WM_APPLY_DONE, 1, reinterpret_cast<LPARAM>(heap));
            }).detach();
        }
        break;
    case IDM_INIT:
        if (!a.busy && disk && !disk->initialized() && !disk->optical) {
            if (auto r = promptInitialize(a.wnd, *disk)) {
                PendingOperation op;
                op.kind = OperationKind::InitializeDisk;
                op.diskNumber = disk->number;
                op.targetStyle = *r;
                op.description = L"Initialize Disk " + std::to_wstring(disk->number);
                enqueue(a, std::move(op));
            }
        }
        break;
    case IDM_CONVERT:
        if (!a.busy && disk && disk->initialized() && !disk->optical && !disk->boot && !disk->system) {
            bool empty = true;
            for (const auto& s : disk->segments)
                if (!s.unallocated) empty = false;
            if (!empty) break;
            const auto target = disk->style == PartitionStyle::Gpt ? PartitionStyle::Mbr : PartitionStyle::Gpt;
            if (!confirm(a, L"Convert this empty disk's partition style?")) break;
            PendingOperation op;
            op.kind = OperationKind::ConvertPartitionStyle;
            op.diskNumber = disk->number;
            op.targetStyle = target;
            op.destructive = true;
            op.description = L"Convert Disk " + std::to_wstring(disk->number);
            enqueue(a, std::move(op));
        }
        break;
    case IDM_DELETE_ALL:
        if (!a.busy && disk && !disk->optical && !disk->boot && !disk->system &&
            confirm(a, L"Delete ALL partitions on this disk? Every volume will be destroyed after Apply.")) {
            PendingOperation op;
            op.kind = OperationKind::DeleteAllPartitions;
            op.diskNumber = disk->number;
            op.destructive = true;
            op.description = L"Delete all partitions on Disk " + std::to_wstring(disk->number);
            enqueue(a, std::move(op));
        }
        break;
    case IDM_ONLINE:
        if (!a.busy && disk && !disk->optical && !disk->boot && !disk->system) {
            PendingOperation op;
            op.kind = disk->offline ? OperationKind::OnlineDisk : OperationKind::OfflineDisk;
            op.diskNumber = disk->number;
            op.description = (disk->offline ? L"Bring online " : L"Take offline ") + std::wstring(L"Disk ") +
                             std::to_wstring(disk->number);
            enqueue(a, std::move(op));
        }
        break;
    case IDM_CLONE_DISK:
    case IDC_BTN_CLONE:
        if (!a.busy && disk && disk->initialized() && !disk->optical) {
            if (auto r = promptCloneDisk(a.wnd, *disk, a.queue.working)) {
                const auto* src = a.queue.working.findDisk(r->source);
                const auto* dest = a.queue.working.findDisk(r->dest);
                if (!src || !dest) break;
                if (!confirm(a, L"Clone onto the destination disk? Every partition there will be destroyed after Apply."))
                    break;
                PendingOperation op;
                op.kind = OperationKind::CloneDisk;
                op.diskNumber = dest->number;
                op.destructive = true;
                op.description = L"Clone Disk " + std::to_wstring(src->number) + L" to Disk " +
                                 std::to_wstring(dest->number);
                op.cloneDisk.sourceDisk = src->number;
                op.cloneDisk.destDisk = dest->number;
                op.cloneDisk.mode = r->mode;
                op.cloneDisk.alignToMegabyte = r->align;
                op.cloneDisk.expandLast = r->expand;
                op.cloneDisk.sourceIsBoot = src->boot || src->system;
                op.cloneDisk.style = src->style;
                op.cloneDisk.destSize = dest->size;
                for (const auto& s : src->segments) op.cloneDisk.sourceSlices.push_back(toSlice(s));
                enqueue(a, std::move(op));
            }
        }
        break;
    case IDM_CLONE_PART:
        if (canMutate(a, seg) && !seg->unallocated) {
            if (auto r = promptClonePart(a.wnd, *seg, a.queue.working)) {
                if (!confirm(a, L"Clone this partition? The destination region will be overwritten after Apply."))
                    break;
                PendingOperation op;
                op.kind = OperationKind::ClonePartition;
                op.diskNumber = r->destDisk;
                op.offset = r->destOffset;
                op.destructive = true;
                op.description = L"Clone partition to Disk " + std::to_wstring(r->destDisk);
                op.clonePart.sourceDisk = seg->diskNumber;
                op.clonePart.destDisk = r->destDisk;
                op.clonePart.source = toSlice(*seg);
                op.clonePart.destOffset = r->destOffset;
                op.clonePart.destRegionSize = r->destRegion;
                op.clonePart.mode = r->mode;
                op.clonePart.alignToMegabyte = r->align;
                op.clonePart.fillRegion = r->fill;
                enqueue(a, std::move(op));
            }
        }
        break;
    case IDM_PART_PROPS:
        showProperties(a.wnd, disk, seg);
        break;
    case IDM_DISK_PROPS:
        showProperties(a.wnd, disk, nullptr);
        break;
    case IDM_CLEAR_LOG:
        a.log.clear();
        SendMessageW(a.logBox, LB_RESETCONTENT, 0, 0);
        break;
    case IDM_OPTIONS:
    case IDC_BTN_OPTIONS:
        if (promptSettings(a.wnd, a.cfg.config)) {
            a.cfg.save();
            applyThemeToApp(a);
            startRefresh(a, false);
        }
        break;
    case IDM_CONFIG_FOLDER:
        ensureDir(appDataDir());
        openFolder(appDataDir());
        break;
    case IDM_FEEDBACK:
        openUrl(kGitHubIssuesUrl);
        break;
    case IDM_GITHUB:
        openUrl(kGitHubUrl);
        break;
    case IDM_ABOUT:
        showAbout(a.wnd);
        break;
    case IDM_EXIT:
        PostMessageW(a.wnd, WM_CLOSE, 0, 0);
        break;
    default:
        break;
    }
}

void paintMap(App& a, HDC hdc, RECT rc) {
    const auto& t = currentTheme();
    FillRect(hdc, &rc, themeBrushes().window);
    SetBkMode(hdc, TRANSPARENT);
    SelectObject(hdc, a.fontSmall);
    SetTextColor(hdc, t.metaText);

    auto swatch = [&](int& x, COLORREF c, const wchar_t* label) {
        RECT box{x, rc.top + 6, x + 12, rc.top + 18};
        HBRUSH br = CreateSolidBrush(c);
        FillRect(hdc, &box, br);
        DeleteObject(br);
        FrameRect(hdc, &box, themeBrushes().border);
        x += 16;
        RECT tr{x, rc.top + 4, x + 70, rc.top + 20};
        DrawTextW(hdc, label, -1, &tr, DT_LEFT | DT_VCENTER | DT_SINGLELINE);
        x += 78;
    };
    int lx = rc.left + kDiskLabelW + 16;
    SetTextColor(hdc, t.ink);
    SelectObject(hdc, a.fontBold);
    RECT title{rc.left + 10, rc.top + 4, rc.left + kDiskLabelW, rc.top + 22};
    DrawTextW(hdc, L"Disk map", -1, &title, DT_LEFT | DT_VCENTER | DT_SINGLELINE);
    SelectObject(hdc, a.fontSmall);
    SetTextColor(hdc, t.metaText);
    swatch(lx, RGB(74, 144, 217), L"Primary");
    swatch(lx, RGB(92, 184, 92), L"Logical");
    swatch(lx, RGB(142, 107, 199), L"EFI");
    swatch(lx, RGB(230, 162, 60), L"Recovery");
    swatch(lx, RGB(200, 200, 200), L"Unallocated");

    const int rowH = 56;
    int y = 26;
    for (const auto& disk : a.queue.working.disks) {
        RECT label{rc.left + 6, y, rc.left + kDiskLabelW - 4, y + rowH};
        if (disk.number == a.selDisk) {
            FillRect(hdc, &label, themeBrushes().hover);
            FrameRect(hdc, &label, themeBrushes().accent);
        }
        SetTextColor(hdc, t.ink);
        SelectObject(hdc, a.fontBold);
        const auto header = L"Disk " + std::to_wstring(disk.number);
        RECT h1 = label;
        h1.bottom = y + 28;
        DrawTextW(hdc, header.c_str(), -1, &h1, DT_LEFT | DT_BOTTOM | DT_SINGLELINE | DT_END_ELLIPSIS);
        SelectObject(hdc, a.fontSmall);
        SetTextColor(hdc, t.metaText);
        auto size = formatBytes(disk.size) + L"  " + disk.styleText();
        RECT h2 = label;
        h2.top = y + 28;
        DrawTextW(hdc, size.c_str(), -1, &h2, DT_LEFT | DT_TOP | DT_SINGLELINE | DT_END_ELLIPSIS);

        RECT bar{rc.left + kDiskLabelW, y + 8, rc.right - 8, y + rowH - 8};
        FrameRect(hdc, &bar, themeBrushes().border);
        InflateRect(&bar, -1, -1);
        if (disk.size > 0 && !disk.segments.empty()) {
            const int n = static_cast<int>(disk.segments.size());
            const int inner = (std::max)(1, static_cast<int>(bar.right - bar.left));
            std::vector<int> ws(static_cast<size_t>(n));
            int used = 0;
            for (int i = 0; i < n; ++i) {
                int w = static_cast<int>(disk.segments[static_cast<size_t>(i)].size * static_cast<double>(inner) / disk.size);
                w = (std::max)(w, (std::min)(kMinSegW, inner / n));
                ws[static_cast<size_t>(i)] = w;
                used += w;
            }
            if (used > inner) {
                for (int i = 0; i < n; ++i)
                    ws[static_cast<size_t>(i)] = (std::max)(4, ws[static_cast<size_t>(i)] * inner / used);
            }
            int x = bar.left;
            for (int i = 0; i < n; ++i) {
                int w = ws[static_cast<size_t>(i)];
                if (i == n - 1) w = bar.right - x;
                RECT part{x, bar.top, x + w, bar.bottom};
                const auto& s = disk.segments[static_cast<size_t>(i)];
                HBRUSH br = CreateSolidBrush(kindColor(s.kind, s.unallocated));
                FillRect(hdc, &part, br);
                DeleteObject(br);
                if (w > 36) {
                    SetTextColor(hdc, RGB(255, 255, 255));
                    SelectObject(hdc, a.fontSmall);
                    auto cap = driveText(s.letter, s.unallocated);
                    if (cap == L"—" || cap.empty())
                        cap = s.unallocated ? L"Unallocated" : (s.label.empty() ? kindText(static_cast<int>(s.kind)) : s.label);
                    RECT tr = part;
                    tr.bottom -= (part.bottom - part.top) / 2;
                    DrawTextW(hdc, cap.c_str(), -1, &tr, DT_CENTER | DT_BOTTOM | DT_SINGLELINE | DT_END_ELLIPSIS);
                    auto sz = formatBytes(s.size);
                    RECT sr = part;
                    sr.top += (part.bottom - part.top) / 2;
                    DrawTextW(hdc, sz.c_str(), -1, &sr, DT_CENTER | DT_TOP | DT_SINGLELINE | DT_END_ELLIPSIS);
                }
                if (disk.number == a.selDisk && i == a.selSeg) {
                    HPEN pen = CreatePen(PS_SOLID, 2, t.highlight);
                    auto old = SelectObject(hdc, pen);
                    SelectObject(hdc, GetStockObject(NULL_BRUSH));
                    Rectangle(hdc, part.left + 1, part.top + 1, part.right - 1, part.bottom - 1);
                    SelectObject(hdc, old);
                    DeleteObject(pen);
                }
                x += w;
            }
        }
        y += rowH + 6;
    }
}

void hitMap(App& a, int px, int py, bool rightClick) {
    RECT rc{};
    GetClientRect(a.map, &rc);
    const int rowH = 56;
    int y = 26;
    for (const auto& disk : a.queue.working.disks) {
        if (py >= y && py < y + rowH) {
            a.selDisk = disk.number;
            a.selSeg = 0;
            RECT bar{rc.left + kDiskLabelW, y + 8, rc.right - 8, y + rowH - 8};
            if (disk.size > 0 && px >= bar.left && px <= bar.right) {
                const int n = static_cast<int>(disk.segments.size());
                const int inner = (std::max)(1, static_cast<int>(bar.right - bar.left));
                int x = bar.left;
                for (int i = 0; i < n; ++i) {
                    int w = static_cast<int>(disk.segments[static_cast<size_t>(i)].size * static_cast<double>(inner) / disk.size);
                    w = (std::max)(w, (std::min)(kMinSegW, inner / n));
                    if (i == n - 1) w = bar.right - x;
                    if (px >= x && px < x + w) {
                        a.selSeg = i;
                        break;
                    }
                    x += w;
                }
            } else if (px < bar.left && disk.segments.size()) {
                bool foundGap = false;
                for (int i = 0; i < static_cast<int>(disk.segments.size()); ++i) {
                    if (disk.segments[static_cast<size_t>(i)].unallocated) {
                        a.selSeg = i;
                        foundGap = true;
                        break;
                    }
                }
                if (!foundGap) a.selSeg = 0;
            }
            rebuildList(a);
            if (rightClick) {
                POINT pt{px, py};
                ClientToScreen(a.map, &pt);
                HMENU pop = GetSubMenu(a.menu, px < rc.left + kDiskLabelW ? 2 : 1);
                if (pop) TrackPopupMenu(pop, TPM_RIGHTBUTTON, pt.x, pt.y, 0, a.wnd, nullptr);
            }
            return;
        }
        y += rowH + 6;
    }
}

HMENU buildMenu(App& a) {
    a.menuItems.clear();
    auto menu = CreateMenu();
    auto file = CreatePopupMenu();
    appendOwned(a, file, MF_STRING, IDM_REFRESH, L"Refresh\tF5");
    appendOwned(a, file, MF_STRING, IDM_APPLY, L"Apply\tCtrl+Enter");
    appendOwned(a, file, MF_SEPARATOR, 0, nullptr);
    appendOwned(a, file, MF_STRING, IDM_CONFIG_FOLDER, L"Open config folder");
    appendOwned(a, file, MF_SEPARATOR, 0, nullptr);
    appendOwned(a, file, MF_STRING, IDM_EXIT, L"Exit");
    auto part = CreatePopupMenu();
    appendOwned(a, part, MF_STRING, IDM_RESIZE, L"Resize / extend");
    appendOwned(a, part, MF_STRING, IDM_CREATE, L"Create partition");
    appendOwned(a, part, MF_STRING, IDM_DELETE, L"Delete partition");
    appendOwned(a, part, MF_STRING, IDM_FORMAT, L"Format partition");
    appendOwned(a, part, MF_SEPARATOR, 0, nullptr);
    appendOwned(a, part, MF_STRING, IDM_LETTER, L"Change drive letter");
    appendOwned(a, part, MF_STRING, IDM_LABEL, L"Change label");
    appendOwned(a, part, MF_STRING, IDM_HIDE, L"Hide / unhide");
    appendOwned(a, part, MF_STRING, IDM_ACTIVE, L"Set active");
    appendOwned(a, part, MF_SEPARATOR, 0, nullptr);
    appendOwned(a, part, MF_STRING, IDM_CHECK, L"Check partition");
    appendOwned(a, part, MF_STRING, IDM_CLONE_PART, L"Clone partition");
    appendOwned(a, part, MF_STRING, IDM_PART_PROPS, L"Properties");
    auto disk = CreatePopupMenu();
    appendOwned(a, disk, MF_STRING, IDM_CLONE_DISK, L"Clone disk");
    appendOwned(a, disk, MF_STRING, IDM_INIT, L"Initialize disk");
    appendOwned(a, disk, MF_STRING, IDM_CONVERT, L"Convert MBR/GPT");
    appendOwned(a, disk, MF_STRING, IDM_DELETE_ALL, L"Delete all partitions");
    appendOwned(a, disk, MF_STRING, IDM_ONLINE, L"Online / offline");
    appendOwned(a, disk, MF_SEPARATOR, 0, nullptr);
    appendOwned(a, disk, MF_STRING, IDM_DISK_PROPS, L"Disk properties");
    auto view = CreatePopupMenu();
    appendOwned(a, view, MF_STRING, IDM_CLEAR_LOG, L"Clear log");
    auto tools = CreatePopupMenu();
    appendOwned(a, tools, MF_STRING, IDM_UNDO, L"Undo last pending\tCtrl+Z");
    appendOwned(a, tools, MF_STRING, IDM_DISCARD, L"Discard pending");
    appendOwned(a, tools, MF_SEPARATOR, 0, nullptr);
    appendOwned(a, tools, MF_STRING, IDM_OPTIONS, L"Options...");
    auto help = CreatePopupMenu();
    appendOwned(a, help, MF_STRING, IDM_FEEDBACK, L"Send feedback on GitHub…");
    appendOwned(a, help, MF_STRING, IDM_GITHUB, L"Open GitHub repository…");
    appendOwned(a, help, MF_SEPARATOR, 0, nullptr);
    appendOwned(a, help, MF_STRING, IDM_ABOUT, L"About Partition Manager");
    auto addTop = [&](HMENU pop, const wchar_t* title) {
        auto item = std::make_unique<MenuItemData>();
        item->id = 0;
        item->text = title;
        AppendMenuW(menu, MF_POPUP | MF_OWNERDRAW, reinterpret_cast<UINT_PTR>(pop),
                    reinterpret_cast<LPCWSTR>(item.get()));
        a.menuItems.push_back(std::move(item));
    };
    addTop(file, L"File");
    addTop(part, L"Partition");
    addTop(disk, L"Disk");
    addTop(view, L"View");
    addTop(tools, L"Tools");
    addTop(help, L"Help");
    return menu;
}

void layout(App& a) {
    RECT rc{};
    GetClientRect(a.wnd, &rc);
    const int bannerH = isElevated() ? 0 : 28;
    const int gap = 8;
    MoveWindow(a.banner, 0, 0, rc.right, bannerH, TRUE);
    const int toolY = bannerH + 6;
    int x = 8;
    auto placeTools = [&](const std::vector<HWND>& btns, int w) {
        for (auto b : btns) {
            MoveWindow(b, x, toolY, w, 28, TRUE);
            x += w + 4;
        }
    };
    placeTools(a.toolButtons, 84);
    MoveWindow(a.lblFilter, rc.right - 268, toolY + 4, 48, 20, TRUE);
    MoveWindow(a.filter, rc.right - 216, toolY + 1, 208, 24, TRUE);

    const int y = bannerH + kToolH;
    const int midH = (std::max)(80, static_cast<int>(rc.bottom) - y - kBottomH - kMapH - kStatusH - gap * 2);
    MoveWindow(a.sidebar, gap, y, kSidebarW, midH, TRUE);
    int sy = y + 28;
    MoveWindow(a.lblSidePart, gap + 10, y + 8, kSidebarW - 20, 18, TRUE);
    for (size_t i = 0; i < a.sideButtons.size(); ++i) {
        if (i == 11) {
            sy += 8;
            MoveWindow(a.lblSideDisk, gap + 10, sy, kSidebarW - 20, 18, TRUE);
            sy += 22;
        }
        MoveWindow(a.sideButtons[i], gap + 8, sy, kSidebarW - 16, 26, TRUE);
        sy += 28;
    }
    MoveWindow(a.list, gap + kSidebarW + gap, y, rc.right - kSidebarW - gap * 3, midH, TRUE);
    const int mapY = y + midH + gap;
    MoveWindow(a.lblMap, gap, mapY, 120, 0, TRUE); // painted inside map
    MoveWindow(a.map, gap, mapY, rc.right - gap * 2, kMapH, TRUE);
    const int by = rc.bottom - kStatusH - kBottomH;
    const int colW = (rc.right - gap * 3) / 2;
    MoveWindow(a.lblPending, gap, by, colW, 18, TRUE);
    MoveWindow(a.pending, gap, by + 20, colW, kBottomH - 28, TRUE);
    MoveWindow(a.lblLog, gap * 2 + colW, by, colW, 18, TRUE);
    MoveWindow(a.logBox, gap * 2 + colW, by + 20, colW, kBottomH - 28, TRUE);
    MoveWindow(a.status, 0, rc.bottom - kStatusH, rc.right - 160, kStatusH, TRUE);
    MoveWindow(a.progress, rc.right - 156, rc.bottom - kStatusH + 4, 148, 16, TRUE);
}

void showListContext(App& a) {
    POINT pt{};
    GetCursorPos(&pt);
    if (HMENU pop = GetSubMenu(a.menu, 1))
        TrackPopupMenu(pop, TPM_RIGHTBUTTON, pt.x, pt.y, 0, a.wnd, nullptr);
}

void drawButton(const DRAWITEMSTRUCT* di) {
    const auto& t = currentTheme();
    const LONG_PTR kind = btnKind(di->hwndItem);
    const bool enabled = IsWindowEnabled(di->hwndItem) != FALSE;
    const bool pressed = enabled && ((di->itemState & ODS_SELECTED) != 0);
    const bool hot = enabled && btnHot(di->hwndItem);
    const bool primary = kind == 3;

    COLORREF bg = primary ? t.highlight : (kind == 2 ? t.sidebar : t.face);
    if (hot && !pressed)
        bg = primary ? blend(t.highlight, t.darkShadow, 0.12) : t.hover;
    if (pressed)
        bg = primary ? blend(t.highlight, t.darkShadow, 0.28) : (t.dark ? t.light : blend(t.hover, t.shadow, 0.35));
    if (!enabled)
        bg = kind == 2 ? blend(t.sidebar, t.face, 0.35) : blend(t.face, t.window, 0.4);

    HBRUSH br = CreateSolidBrush(bg);
    FillRect(di->hDC, &di->rcItem, br);
    DeleteObject(br);

    const COLORREF edge = !enabled ? t.border
                                   : ((primary || hot || pressed) ? t.highlight : t.border);
    HPEN pen = CreatePen(PS_SOLID, 1, edge);
    auto oldPen = SelectObject(di->hDC, pen);
    auto oldBr = SelectObject(di->hDC, GetStockObject(NULL_BRUSH));
    Rectangle(di->hDC, di->rcItem.left, di->rcItem.top, di->rcItem.right, di->rcItem.bottom);
    SelectObject(di->hDC, oldPen);
    SelectObject(di->hDC, oldBr);
    DeleteObject(pen);

    wchar_t text[64]{};
    GetWindowTextW(di->hwndItem, text, 64);
    SetBkMode(di->hDC, TRANSPARENT);
    COLORREF fg = primary ? t.highlightText : t.ink;
    if (!enabled)
        fg = t.dark ? blend(t.ink, t.face, 0.55) : t.disabledText;
    SetTextColor(di->hDC, fg);
    RECT r = di->rcItem;
    if (kind == 2) r.left += 10;
    DrawTextW(di->hDC, text, -1, &r,
              (kind == 2 ? DT_LEFT : DT_CENTER) | DT_VCENTER | DT_SINGLELINE | DT_END_ELLIPSIS);
}

void drawMenuItem(const DRAWITEMSTRUCT* di) {
    const auto& t = currentTheme();
    auto* item = reinterpret_cast<MenuItemData*>(di->itemData);
    if (!item) return;
    const bool top = item->id == 0;
    const bool hot = (di->itemState & ODS_SELECTED) != 0;
    const bool enabled = (di->itemState & ODS_GRAYED) == 0 && (di->itemState & ODS_DISABLED) == 0;
    FillRect(di->hDC, &di->rcItem, hot && enabled ? themeBrushes().highlight : themeBrushes().menu);
    SetBkMode(di->hDC, TRANSPARENT);
    SetTextColor(di->hDC, !enabled ? t.disabledText : (hot && enabled ? t.highlightText : t.menuBarText));
    RECT r = di->rcItem;
    if (top) {
        DrawTextW(di->hDC, item->text.c_str(), -1, &r, DT_CENTER | DT_VCENTER | DT_SINGLELINE);
    } else {
        r.left += 12;
        r.right -= 12;
        DrawTextW(di->hDC, item->text.c_str(), -1, &r, DT_LEFT | DT_VCENTER | DT_SINGLELINE);
    }
}

LRESULT CALLBACK mapProc(HWND wnd, UINT msg, WPARAM w, LPARAM l) {
    auto* a = reinterpret_cast<App*>(GetWindowLongPtrW(GetParent(wnd), GWLP_USERDATA));
    if (msg == WM_PAINT) {
        PAINTSTRUCT ps;
        HDC hdc = BeginPaint(wnd, &ps);
        RECT rc;
        GetClientRect(wnd, &rc);
        if (a) paintMap(*a, hdc, rc);
        EndPaint(wnd, &ps);
        return 0;
    }
    if (msg == WM_ERASEBKGND) return 1;
    if ((msg == WM_LBUTTONDOWN || msg == WM_RBUTTONDOWN) && a) {
        hitMap(*a, GET_X_LPARAM(l), GET_Y_LPARAM(l), msg == WM_RBUTTONDOWN);
        return 0;
    }
    return DefWindowProcW(wnd, msg, w, l);
}

LRESULT CALLBACK wndProc(HWND wnd, UINT msg, WPARAM w, LPARAM l) {
    auto* a = reinterpret_cast<App*>(GetWindowLongPtrW(wnd, GWLP_USERDATA));
    switch (msg) {
    case WM_CREATE: {
        auto* cs = reinterpret_cast<CREATESTRUCTW*>(l);
        a = reinterpret_cast<App*>(cs->lpCreateParams);
        SetWindowLongPtrW(wnd, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(a));
        a->wnd = wnd;
        setCurrentTheme(a->cfg.config.theme);
        a->menu = buildMenu(*a);
        SetMenu(wnd, a->menu);
        auto mkStatic = [&](const wchar_t* text) {
            return CreateWindowW(L"STATIC", text, WS_CHILD | WS_VISIBLE, 0, 0, 0, 0, wnd,
                                 nullptr, nullptr, nullptr);
        };
        a->banner = CreateWindowW(L"STATIC",
                                  L"Not running as administrator — disk changes will fail. Restart elevated, or continue read-only.",
                                  WS_CHILD | (isElevated() ? 0 : WS_VISIBLE),
                                  0, 0, 0, 0, wnd, nullptr, nullptr, nullptr);
        a->lblFilter = mkStatic(L"Filter:");
        a->lblPending = mkStatic(L"Pending operations");
        a->lblLog = mkStatic(L"Activity log");
        a->lblSidePart = mkStatic(L"Partition operations");
        a->lblSideDisk = mkStatic(L"Disk operations");
        a->sidebar = mkStatic(L"");
        a->toolButtons = {
            makeButton(wnd, IDC_BTN_REFRESH, L"Refresh", 1),
            makeButton(wnd, IDC_BTN_APPLY, L"Apply", 3),
            makeButton(wnd, IDC_BTN_UNDO, L"Undo", 1),
            makeButton(wnd, IDC_BTN_RESIZE, L"Resize", 1),
            makeButton(wnd, IDC_BTN_CREATE, L"Create", 1),
            makeButton(wnd, IDC_BTN_DELETE, L"Delete", 1),
            makeButton(wnd, IDC_BTN_FORMAT, L"Format", 1),
            makeButton(wnd, IDC_BTN_CLONE, L"Clone", 1),
            makeButton(wnd, IDC_BTN_OPTIONS, L"Options", 1),
        };
        a->sideButtons = {
            makeButton(wnd, IDM_RESIZE, L"Resize / extend", 2),
            makeButton(wnd, IDM_CREATE, L"Create partition", 2),
            makeButton(wnd, IDM_DELETE, L"Delete partition", 2),
            makeButton(wnd, IDM_FORMAT, L"Format partition", 2),
            makeButton(wnd, IDM_LETTER, L"Change drive letter", 2),
            makeButton(wnd, IDM_LABEL, L"Change label", 2),
            makeButton(wnd, IDM_HIDE, L"Hide / unhide", 2),
            makeButton(wnd, IDM_ACTIVE, L"Set active", 2),
            makeButton(wnd, IDM_CHECK, L"Check partition", 2),
            makeButton(wnd, IDM_CLONE_PART, L"Clone partition", 2),
            makeButton(wnd, IDM_PART_PROPS, L"Properties", 2),
            makeButton(wnd, IDM_CLONE_DISK, L"Clone disk", 2),
            makeButton(wnd, IDM_INIT, L"Initialize disk", 2),
            makeButton(wnd, IDM_CONVERT, L"Convert MBR/GPT", 2),
            makeButton(wnd, IDM_DELETE_ALL, L"Delete all partitions", 2),
            makeButton(wnd, IDM_ONLINE, L"Online / offline", 2),
            makeButton(wnd, IDM_DISK_PROPS, L"Disk properties", 2),
        };
        a->filter = CreateWindowW(L"EDIT", L"", WS_CHILD | WS_VISIBLE | ES_AUTOHSCROLL,
                                  0, 0, 0, 0, wnd, reinterpret_cast<HMENU>(IDC_FILTER), nullptr, nullptr);
        a->list = CreateWindowW(WC_LISTVIEWW, L"",
                                WS_CHILD | WS_VISIBLE | LVS_REPORT | LVS_SINGLESEL | LVS_SHOWSELALWAYS,
                                0, 0, 0, 0, wnd, reinterpret_cast<HMENU>(IDC_PART_LIST), nullptr, nullptr);
        ListView_SetExtendedListViewStyle(a->list, LVS_EX_FULLROWSELECT | LVS_EX_DOUBLEBUFFER);
        const wchar_t* cols[] = {L"Drive", L"Label", L"Type", L"FS", L"Size", L"Status", L"Disk"};
        const int widths[] = {64, 180, 110, 80, 90, 110, 70};
        for (int i = 0; i < 7; ++i) {
            LVCOLUMNW c{};
            c.mask = LVCF_TEXT | LVCF_WIDTH;
            c.pszText = const_cast<LPWSTR>(cols[i]);
            c.cx = widths[i];
            ListView_InsertColumn(a->list, i, &c);
        }
        WNDCLASSW mapCls{};
        mapCls.lpfnWndProc = mapProc;
        mapCls.hInstance = GetModuleHandleW(nullptr);
        mapCls.hCursor = LoadCursor(nullptr, IDC_ARROW);
        mapCls.hbrBackground = nullptr;
        mapCls.lpszClassName = L"PmDiskMap";
        RegisterClassW(&mapCls);
        a->map = CreateWindowW(L"PmDiskMap", L"", WS_CHILD | WS_VISIBLE | WS_VSCROLL, 0, 0, 0, 0,
                               wnd, reinterpret_cast<HMENU>(IDC_DISK_MAP), nullptr, nullptr);
        a->pending = CreateWindowW(L"LISTBOX", L"",
                                   WS_CHILD | WS_VISIBLE | WS_VSCROLL | LBS_NOINTEGRALHEIGHT,
                                   0, 0, 0, 0, wnd, reinterpret_cast<HMENU>(IDC_PENDING), nullptr, nullptr);
        a->logBox = CreateWindowW(L"LISTBOX", L"",
                                  WS_CHILD | WS_VISIBLE | WS_VSCROLL | LBS_NOINTEGRALHEIGHT,
                                  0, 0, 0, 0, wnd, reinterpret_cast<HMENU>(IDC_LOG), nullptr, nullptr);
        a->status = CreateWindowW(L"STATIC", L"", WS_CHILD | WS_VISIBLE, 0, 0, 0, 0, wnd,
                                  reinterpret_cast<HMENU>(IDC_STATUS), nullptr, nullptr);
        SetWindowSubclass(a->status, statusProc, 4, 0);
        a->progress = CreateWindowW(L"STATIC", L"", WS_CHILD, 0, 0, 0, 0, wnd,
                                    reinterpret_cast<HMENU>(IDC_PROGRESS), nullptr, nullptr);
        SetWindowSubclass(a->progress, progressProc, 5, 0);
        a->log.onLine = [wnd](const std::wstring& line) {
            auto* heap = new std::wstring(line);
            PostMessageW(wnd, WM_LOG_LINE, 0, reinterpret_cast<LPARAM>(heap));
        };
        applyThemeToApp(*a);
        if (a->cfg.config.refreshOnLaunch) startRefresh(*a, true);
        return 0;
    }
    case WM_ERASEBKGND: {
        HDC hdc = reinterpret_cast<HDC>(w);
        RECT rc{};
        GetClientRect(wnd, &rc);
        FillRect(hdc, &rc, themeBrushes().face);
        if (a) {
            auto frameChild = [&](HWND child, HBRUSH fill) {
                if (!child || !IsWindowVisible(child)) return;
                RECT r{};
                GetWindowRect(child, &r);
                MapWindowPoints(nullptr, wnd, reinterpret_cast<LPPOINT>(&r), 2);
                InflateRect(&r, 1, 1);
                paintPanelFill(hdc, r, fill);
            };
            RECT side{};
            GetWindowRect(a->sidebar, &side);
            MapWindowPoints(nullptr, wnd, reinterpret_cast<LPPOINT>(&side), 2);
            paintPanelFill(hdc, side, themeBrushes().sidebar);
            frameChild(a->list, themeBrushes().window);
            frameChild(a->map, themeBrushes().window);
            frameChild(a->pending, themeBrushes().window);
            frameChild(a->logBox, themeBrushes().window);
            frameChild(a->filter, themeBrushes().window);
            if (a->status && IsWindowVisible(a->status)) {
                RECT sr{};
                GetWindowRect(a->status, &sr);
                MapWindowPoints(nullptr, wnd, reinterpret_cast<LPPOINT>(&sr), 2);
                FillRect(hdc, &sr, themeBrushes().status);
            }
        }
        return 1;
    }
    case WM_CTLCOLORSTATIC: {
        const auto& t = currentTheme();
        HDC hdc = reinterpret_cast<HDC>(w);
        SetBkMode(hdc, TRANSPARENT);
        HWND ctl = reinterpret_cast<HWND>(l);
        if (a && ctl == a->banner) {
            SetTextColor(hdc, t.highlightText);
            SetBkColor(hdc, t.highlight);
            return reinterpret_cast<LRESULT>(themeBrushes().accent);
        }
        SetTextColor(hdc, t.ink);
        if (a && (ctl == a->sidebar || ctl == a->lblSidePart || ctl == a->lblSideDisk)) {
            SetBkColor(hdc, t.sidebar);
            return reinterpret_cast<LRESULT>(themeBrushes().sidebar);
        }
        SetBkColor(hdc, t.face);
        return reinterpret_cast<LRESULT>(themeBrushes().face);
    }
    case WM_CTLCOLOREDIT:
    case WM_CTLCOLORLISTBOX: {
        const auto& t = currentTheme();
        SetTextColor(reinterpret_cast<HDC>(w), t.ink);
        SetBkColor(reinterpret_cast<HDC>(w), t.window);
        SetBkMode(reinterpret_cast<HDC>(w), OPAQUE);
        return reinterpret_cast<LRESULT>(themeBrushes().window);
    }
    case WM_MEASUREITEM: {
        auto* mi = reinterpret_cast<MEASUREITEMSTRUCT*>(l);
        if (mi->CtlType == ODT_MENU) {
            auto* item = reinterpret_cast<MenuItemData*>(mi->itemData);
            HDC hdc = GetDC(wnd);
            if (a && a->font) SelectObject(hdc, a->font);
            SIZE sz{};
            const wchar_t* text = item ? item->text.c_str() : L"Item";
            GetTextExtentPoint32W(hdc, text, static_cast<int>(item ? item->text.size() : 4), &sz);
            ReleaseDC(wnd, hdc);
            if (item && item->id == 0) {
                // Top-level File / Partition / Disk / ... — size to label + padding.
                mi->itemWidth = sz.cx + 24;
                mi->itemHeight = 26;
            } else {
                mi->itemWidth = (std::max)(180, static_cast<int>(sz.cx) + 40);
                mi->itemHeight = 24;
            }
        }
        return TRUE;
    }
    case WM_DRAWITEM: {
        auto* di = reinterpret_cast<DRAWITEMSTRUCT*>(l);
        if (di->CtlType == ODT_MENU) drawMenuItem(di);
        else if (di->CtlType == ODT_BUTTON) drawButton(di);
        return TRUE;
    }
    case WM_DEVICECHANGE:
        if (w == DBT_DEVICEARRIVAL || w == DBT_DEVICEREMOVECOMPLETE || w == DBT_DEVNODES_CHANGED)
            SetTimer(wnd, kDeviceTimer, 900, nullptr);
        return TRUE;
    case WM_TIMER:
        if (w == kDeviceTimer) {
            KillTimer(wnd, kDeviceTimer);
            if (a && !a->busy && !a->queue.hasPending())
                startRefresh(*a, true);
        }
        return 0;
    case WM_SIZE:
        if (a) layout(*a);
        return 0;
    case WM_COMMAND:
        if (a && HIWORD(w) == EN_CHANGE && LOWORD(w) == IDC_FILTER) {
            wchar_t buf[128]{};
            GetWindowTextW(a->filter, buf, 128);
            a->filterText = buf;
            rebuildList(*a);
            return 0;
        }
        if (a) onCommand(*a, LOWORD(w));
        return 0;
    case WM_NOTIFY: {
        auto* hdr = reinterpret_cast<LPNMHDR>(l);
        if (a && hdr->idFrom == IDC_PART_LIST && hdr->code == LVN_ITEMCHANGED)
            selectFromList(*a);
        if (a && hdr->idFrom == IDC_PART_LIST && hdr->code == NM_DBLCLK)
            onCommand(*a, IDM_PART_PROPS);
        if (a && hdr->idFrom == IDC_PART_LIST && hdr->code == NM_RCLICK) {
            selectFromList(*a);
            showListContext(*a);
        }
        if (a && hdr->hwndFrom == a->list && hdr->code == NM_CUSTOMDRAW) {
            auto* cd = reinterpret_cast<LPNMLVCUSTOMDRAW>(l);
            if (cd->nmcd.dwDrawStage == CDDS_PREPAINT)
                return CDRF_NOTIFYITEMDRAW;
            if (cd->nmcd.dwDrawStage == CDDS_ITEMPREPAINT) {
                const bool sel = ListView_GetItemState(a->list, static_cast<int>(cd->nmcd.dwItemSpec), LVIS_SELECTED) & LVIS_SELECTED;
                cd->clrText = currentTheme().ink;
                cd->clrTextBk = sel ? listSelection()
                                    : ((cd->nmcd.dwItemSpec % 2) ? listAltRow() : currentTheme().window);
                return CDRF_NEWFONT;
            }
        }
        if (a && a->list && hdr->hwndFrom == ListView_GetHeader(a->list) && hdr->code == NM_CUSTOMDRAW) {
            auto* cd = reinterpret_cast<LPNMCUSTOMDRAW>(l);
            if (cd->dwDrawStage == CDDS_PREPAINT)
                return CDRF_NOTIFYITEMDRAW;
            if (cd->dwDrawStage == CDDS_ITEMPREPAINT) {
                const auto& t = currentTheme();
                FillRect(cd->hdc, &cd->rc, themeBrushes().face);
                paintPanelFrame(cd->hdc, cd->rc);
                wchar_t text[64]{};
                HDITEMW item{};
                item.mask = HDI_TEXT;
                item.pszText = text;
                item.cchTextMax = 64;
                Header_GetItem(hdr->hwndFrom, static_cast<int>(cd->dwItemSpec), &item);
                SetBkMode(cd->hdc, TRANSPARENT);
                SetTextColor(cd->hdc, t.ink);
                RECT tr = cd->rc;
                tr.left += 8;
                DrawTextW(cd->hdc, text, -1, &tr, DT_LEFT | DT_VCENTER | DT_SINGLELINE);
                return CDRF_SKIPDEFAULT;
            }
        }
        return 0;
    }
    case WM_REFRESH_DONE:
        if (a) {
            a->busy = false;
            if (a->selDisk < 0 && !a->queue.working.disks.empty()) {
                a->selDisk = a->queue.working.disks.front().number;
                a->selSeg = 0;
            }
            rebuildList(*a);
            setStatus(*a, L"Ready");
        }
        return 0;
    case WM_APPLY_DONE: {
        auto* result = reinterpret_cast<OperationResult*>(l);
        if (a) {
            a->busy = false;
            ShowWindow(a->progress, SW_HIDE);
            a->progressValue = 0;
            rebuildList(*a);
            setStatus(*a, result->success ? result->message : L"Failed: " + result->message);
            if (!result->success)
                MessageBoxW(wnd, result->message.c_str(), kProductName, MB_OK | MB_ICONERROR);
        }
        delete result;
        return 0;
    }
    case WM_PROGRESS:
        if (a) {
            a->progressValue = static_cast<int>(w);
            if (a->progress) InvalidateRect(a->progress, nullptr, FALSE);
        }
        return 0;
    case WM_LOG_LINE: {
        auto* line = reinterpret_cast<std::wstring*>(l);
        if (a) {
            SendMessageW(a->logBox, LB_ADDSTRING, 0, reinterpret_cast<LPARAM>(line->c_str()));
            const auto count = SendMessageW(a->logBox, LB_GETCOUNT, 0, 0);
            SendMessageW(a->logBox, LB_SETTOPINDEX, count - 1, 0);
        }
        delete line;
        return 0;
    }
    case WM_DESTROY:
        if (a) {
            if (a->font) DeleteObject(a->font);
            if (a->fontBold) DeleteObject(a->fontBold);
            if (a->fontSmall) DeleteObject(a->fontSmall);
            if (a->fontMono) DeleteObject(a->fontMono);
            themeBrushes().destroy();
        }
        PostQuitMessage(0);
        return 0;
    default:
        return DefWindowProcW(wnd, msg, w, l);
    }
}

} // namespace

int runUi(HINSTANCE instance, int cmdShow) {
    INITCOMMONCONTROLSEX icc{sizeof(icc), ICC_LISTVIEW_CLASSES | ICC_BAR_CLASSES | ICC_PROGRESS_CLASS | ICC_STANDARD_CLASSES};
    InitCommonControlsEx(&icc);

    App app;
    app.cfg.load();
    setCurrentTheme(app.cfg.config.theme);
    app.log.info(isElevated() ? L"Running with administrator privileges."
                              : L"Running without administrator privileges. Partition changes require elevation.");

    WNDCLASSW wc{};
    wc.lpfnWndProc = wndProc;
    wc.hInstance = instance;
    wc.hIcon = LoadIconW(instance, MAKEINTRESOURCEW(IDI_APP));
    wc.hCursor = LoadCursor(nullptr, IDC_ARROW);
    wc.hbrBackground = nullptr;
    wc.lpszClassName = kClass;
    RegisterClassW(&wc);

    HWND wnd = CreateWindowExW(0, kClass, kProductName, WS_OVERLAPPEDWINDOW | WS_CLIPCHILDREN,
                               CW_USEDEFAULT, CW_USEDEFAULT, 1280, 780, nullptr, nullptr, instance, &app);
    if (!wnd) return 1;
    ShowWindow(wnd, cmdShow);
    UpdateWindow(wnd);

    ACCEL acc[] = {
        {FVIRTKEY, VK_F5, static_cast<WORD>(IDM_REFRESH)},
        {FVIRTKEY | FCONTROL, VK_RETURN, static_cast<WORD>(IDM_APPLY)},
        {FVIRTKEY | FCONTROL, 'Z', static_cast<WORD>(IDM_UNDO)},
    };
    HACCEL table = CreateAcceleratorTableW(acc, 3);
    MSG msg{};
    while (GetMessageW(&msg, nullptr, 0, 0)) {
        if (!TranslateAcceleratorW(wnd, table, &msg) && !IsDialogMessageW(wnd, &msg)) {
            TranslateMessage(&msg);
            DispatchMessageW(&msg);
        }
    }
    DestroyAcceleratorTable(table);
    return static_cast<int>(msg.wParam);
}

} // namespace pm
