#include "Dialogs.hpp"
#include "AppInfo.hpp"
#include "Preview.hpp"
#include "Theme.hpp"
#include "Util.hpp"
#include "resource.h"

#include <commctrl.h>
#include <windowsx.h>

#include <algorithm>

namespace pm {
namespace {

void addFs(HWND combo, const wchar_t* current) {
    const wchar_t* items[] = {L"NTFS", L"FAT32", L"exFAT", L"ReFS"};
    int sel = 0;
    for (int i = 0; i < 4; ++i) {
        ComboBox_AddString(combo, items[i]);
        if (current && iequals(current, items[i])) sel = i;
    }
    ComboBox_SetCurSel(combo, sel);
}

std::wstring comboText(HWND combo) {
    const int i = ComboBox_GetCurSel(combo);
    if (i < 0) return {};
    wchar_t buf[128]{};
    ComboBox_GetLBText(combo, i, buf);
    return buf;
}

struct CreateState {
    const Segment* gap = nullptr;
    const std::vector<wchar_t>* letters = nullptr;
    CreateResult result;
};

INT_PTR CALLBACK createProc(HWND dlg, UINT msg, WPARAM w, LPARAM l) {
    auto* st = reinterpret_cast<CreateState*>(GetWindowLongPtrW(dlg, GWLP_USERDATA));
    if (msg == WM_INITDIALOG) {
        attachDialogTheme(dlg);
        st = reinterpret_cast<CreateState*>(l);
        SetWindowLongPtrW(dlg, GWLP_USERDATA, l);
        const auto mb = static_cast<int>(st->gap->size / (1024 * 1024));
        SetDlgItemTextW(dlg, IDC_SUMMARY, (L"Create a partition in " + formatBytes(st->gap->size) + L" unallocated space.").c_str());
        SendDlgItemMessageW(dlg, IDC_SIZE_SLIDER, TBM_SETRANGE, TRUE, MAKELPARAM(1, (std::max)(1, mb)));
        SendDlgItemMessageW(dlg, IDC_SIZE_SLIDER, TBM_SETPOS, TRUE, mb);
        SetDlgItemInt(dlg, IDC_SIZE_EDIT, mb, FALSE);
        auto letters = GetDlgItem(dlg, IDC_LETTER);
        ComboBox_AddString(letters, L"(none)");
        ComboBox_SetCurSel(letters, 0);
        for (auto c : *st->letters) {
            const wchar_t t[] = {c, L':', 0};
            ComboBox_AddString(letters, t);
        }
        addFs(GetDlgItem(dlg, IDC_FILESYSTEM), L"NTFS");
        CheckDlgButton(dlg, IDC_FORMAT, BST_CHECKED);
        CheckDlgButton(dlg, IDC_QUICK, BST_CHECKED);
        return TRUE;
    }
    if (msg == WM_HSCROLL && reinterpret_cast<HWND>(l) == GetDlgItem(dlg, IDC_SIZE_SLIDER)) {
        const int pos = static_cast<int>(SendDlgItemMessageW(dlg, IDC_SIZE_SLIDER, TBM_GETPOS, 0, 0));
        SetDlgItemInt(dlg, IDC_SIZE_EDIT, pos, FALSE);
        return TRUE;
    }
    if (msg == WM_COMMAND && LOWORD(w) == IDOK) {
        BOOL ok = FALSE;
        const int mb = GetDlgItemInt(dlg, IDC_SIZE_EDIT, &ok, FALSE);
        st->result.size = static_cast<std::uint64_t>(mb) * 1024ULL * 1024ULL;
        const int li = ComboBox_GetCurSel(GetDlgItem(dlg, IDC_LETTER));
        st->result.letter = li > 0 ? (*st->letters)[static_cast<size_t>(li - 1)] : 0;
        st->result.fileSystem = comboText(GetDlgItem(dlg, IDC_FILESYSTEM));
        wchar_t label[64]{};
        GetDlgItemTextW(dlg, IDC_LABEL, label, 64);
        st->result.label = label;
        st->result.formatAfter = IsDlgButtonChecked(dlg, IDC_FORMAT) == BST_CHECKED;
        st->result.quick = IsDlgButtonChecked(dlg, IDC_QUICK) == BST_CHECKED;
        EndDialog(dlg, IDOK);
        return TRUE;
    }
    if (msg == WM_COMMAND && LOWORD(w) == IDCANCEL) {
        EndDialog(dlg, IDCANCEL);
        return TRUE;
    }
    return FALSE;
}

struct FormatState {
    const Segment* seg = nullptr;
    FormatResult result;
};

INT_PTR CALLBACK formatProc(HWND dlg, UINT msg, WPARAM w, LPARAM l) {
    auto* st = reinterpret_cast<FormatState*>(GetWindowLongPtrW(dlg, GWLP_USERDATA));
    if (msg == WM_INITDIALOG) {
        attachDialogTheme(dlg);
        st = reinterpret_cast<FormatState*>(l);
        SetWindowLongPtrW(dlg, GWLP_USERDATA, l);
        SetDlgItemTextW(dlg, IDC_SUMMARY, (L"Format " + formatBytes(st->seg->size) + L".").c_str());
        addFs(GetDlgItem(dlg, IDC_FILESYSTEM), st->seg->fileSystem.c_str());
        SetDlgItemTextW(dlg, IDC_LABEL, st->seg->label.c_str());
        CheckDlgButton(dlg, IDC_QUICK, BST_CHECKED);
        return TRUE;
    }
    if (msg == WM_COMMAND && LOWORD(w) == IDOK) {
        st->result.fileSystem = comboText(GetDlgItem(dlg, IDC_FILESYSTEM));
        wchar_t label[64]{};
        GetDlgItemTextW(dlg, IDC_LABEL, label, 64);
        st->result.label = label;
        st->result.quick = IsDlgButtonChecked(dlg, IDC_QUICK) == BST_CHECKED;
        EndDialog(dlg, IDOK);
        return TRUE;
    }
    if (msg == WM_COMMAND && LOWORD(w) == IDCANCEL) {
        EndDialog(dlg, IDCANCEL);
        return TRUE;
    }
    return FALSE;
}

struct ResizeState {
    const Segment* seg = nullptr;
    std::uint64_t maxSize = 0;
    std::uint64_t result = 0;
};

INT_PTR CALLBACK resizeProc(HWND dlg, UINT msg, WPARAM w, LPARAM l) {
    auto* st = reinterpret_cast<ResizeState*>(GetWindowLongPtrW(dlg, GWLP_USERDATA));
    if (msg == WM_INITDIALOG) {
        attachDialogTheme(dlg);
        st = reinterpret_cast<ResizeState*>(l);
        SetWindowLongPtrW(dlg, GWLP_USERDATA, l);
        const int cur = static_cast<int>(st->seg->size / (1024 * 1024));
        const int maxMb = static_cast<int>((std::max)(st->seg->size, st->maxSize) / (1024 * 1024));
        SetDlgItemTextW(dlg, IDC_SUMMARY, (L"Resize from " + formatBytes(st->seg->size) + L".").c_str());
        SendDlgItemMessageW(dlg, IDC_SIZE_SLIDER, TBM_SETRANGE, TRUE, MAKELPARAM(1, (std::max)(1, maxMb)));
        SendDlgItemMessageW(dlg, IDC_SIZE_SLIDER, TBM_SETPOS, TRUE, cur);
        SetDlgItemInt(dlg, IDC_SIZE_EDIT, cur, FALSE);
        SetDlgItemTextW(dlg, IDC_SIZE_HINT, (L"Maximum " + formatBytes(st->maxSize) + L".").c_str());
        return TRUE;
    }
    if (msg == WM_HSCROLL && reinterpret_cast<HWND>(l) == GetDlgItem(dlg, IDC_SIZE_SLIDER)) {
        SetDlgItemInt(dlg, IDC_SIZE_EDIT, static_cast<UINT>(SendDlgItemMessageW(dlg, IDC_SIZE_SLIDER, TBM_GETPOS, 0, 0)), FALSE);
        return TRUE;
    }
    if (msg == WM_COMMAND && LOWORD(w) == IDOK) {
        BOOL ok = FALSE;
        st->result = static_cast<std::uint64_t>(GetDlgItemInt(dlg, IDC_SIZE_EDIT, &ok, FALSE)) * 1024ULL * 1024ULL;
        EndDialog(dlg, IDOK);
        return TRUE;
    }
    if (msg == WM_COMMAND && LOWORD(w) == IDCANCEL) {
        EndDialog(dlg, IDCANCEL);
        return TRUE;
    }
    return FALSE;
}

struct LetterState {
    wchar_t current = 0;
    const std::vector<wchar_t>* letters = nullptr;
    wchar_t result = 0;
};

INT_PTR CALLBACK letterProc(HWND dlg, UINT msg, WPARAM w, LPARAM l) {
    auto* st = reinterpret_cast<LetterState*>(GetWindowLongPtrW(dlg, GWLP_USERDATA));
    if (msg == WM_INITDIALOG) {
        attachDialogTheme(dlg);
        st = reinterpret_cast<LetterState*>(l);
        SetWindowLongPtrW(dlg, GWLP_USERDATA, l);
        SetDlgItemTextW(dlg, IDC_SUMMARY, L"Choose a drive letter, or none.");
        auto box = GetDlgItem(dlg, IDC_LETTER);
        ComboBox_AddString(box, L"(none)");
        ComboBox_SetCurSel(box, 0);
        for (size_t i = 0; i < st->letters->size(); ++i) {
            const wchar_t t[] = {(*st->letters)[i], L':', 0};
            ComboBox_AddString(box, t);
            if ((*st->letters)[i] == st->current) ComboBox_SetCurSel(box, static_cast<int>(i + 1));
        }
        return TRUE;
    }
    if (msg == WM_COMMAND && LOWORD(w) == IDOK) {
        const int i = ComboBox_GetCurSel(GetDlgItem(dlg, IDC_LETTER));
        st->result = i > 0 ? (*st->letters)[static_cast<size_t>(i - 1)] : 0;
        EndDialog(dlg, IDOK);
        return TRUE;
    }
    if (msg == WM_COMMAND && LOWORD(w) == IDCANCEL) {
        EndDialog(dlg, IDCANCEL);
        return TRUE;
    }
    return FALSE;
}

struct LabelState {
    std::wstring current;
    std::wstring result;
};

INT_PTR CALLBACK labelProc(HWND dlg, UINT msg, WPARAM w, LPARAM l) {
    auto* st = reinterpret_cast<LabelState*>(GetWindowLongPtrW(dlg, GWLP_USERDATA));
    if (msg == WM_INITDIALOG) {
        attachDialogTheme(dlg);
        st = reinterpret_cast<LabelState*>(l);
        SetWindowLongPtrW(dlg, GWLP_USERDATA, l);
        SetDlgItemTextW(dlg, IDC_SUMMARY, L"Volume label");
        SetDlgItemTextW(dlg, IDC_LABEL, st->current.c_str());
        return TRUE;
    }
    if (msg == WM_COMMAND && LOWORD(w) == IDOK) {
        wchar_t buf[64]{};
        GetDlgItemTextW(dlg, IDC_LABEL, buf, 64);
        st->result = buf;
        EndDialog(dlg, IDOK);
        return TRUE;
    }
    if (msg == WM_COMMAND && LOWORD(w) == IDCANCEL) {
        EndDialog(dlg, IDCANCEL);
        return TRUE;
    }
    return FALSE;
}

struct InitState {
    const Disk* disk = nullptr;
    PartitionStyle result = PartitionStyle::Gpt;
};

INT_PTR CALLBACK initProc(HWND dlg, UINT msg, WPARAM w, LPARAM l) {
    auto* st = reinterpret_cast<InitState*>(GetWindowLongPtrW(dlg, GWLP_USERDATA));
    if (msg == WM_INITDIALOG) {
        attachDialogTheme(dlg);
        st = reinterpret_cast<InitState*>(l);
        SetWindowLongPtrW(dlg, GWLP_USERDATA, l);
        SetDlgItemTextW(dlg, IDC_SUMMARY,
                        (L"Initialize Disk " + std::to_wstring(st->disk->number) + L" before creating partitions.").c_str());
        CheckDlgButton(dlg, IDC_GPT, BST_CHECKED);
        if (st->disk->size > 2ULL * 1024 * 1024 * 1024 * 1024) EnableWindow(GetDlgItem(dlg, IDC_MBR), FALSE);
        return TRUE;
    }
    if (msg == WM_COMMAND && LOWORD(w) == IDOK) {
        st->result = IsDlgButtonChecked(dlg, IDC_GPT) == BST_CHECKED ? PartitionStyle::Gpt : PartitionStyle::Mbr;
        EndDialog(dlg, IDOK);
        return TRUE;
    }
    if (msg == WM_COMMAND && LOWORD(w) == IDCANCEL) {
        EndDialog(dlg, IDCANCEL);
        return TRUE;
    }
    return FALSE;
}

struct CloneDiskState {
    const Disk* source = nullptr;
    const DiskLayout* layout = nullptr;
    std::vector<int> sourceNums;
    std::vector<int> destNums;
    CloneDiskResult result;
};

INT_PTR CALLBACK cloneDiskProc(HWND dlg, UINT msg, WPARAM w, LPARAM l) {
    auto* st = reinterpret_cast<CloneDiskState*>(GetWindowLongPtrW(dlg, GWLP_USERDATA));
    if (msg == WM_INITDIALOG) {
        attachDialogTheme(dlg);
        st = reinterpret_cast<CloneDiskState*>(l);
        SetWindowLongPtrW(dlg, GWLP_USERDATA, l);
        SetDlgItemTextW(dlg, IDC_SUMMARY, L"Copy every partition from the source disk onto the destination. The destination is overwritten when you Apply.");
        auto src = GetDlgItem(dlg, IDC_SOURCE);
        auto dest = GetDlgItem(dlg, IDC_DEST);
        int srcSel = 0;
        for (const auto& d : st->layout->disks) {
            if (d.optical || !d.initialized()) continue;
            bool hasPart = false;
            for (const auto& s : d.segments)
                if (!s.unallocated) hasPart = true;
            if (!hasPart) continue;
            const auto text = L"Disk " + std::to_wstring(d.number) + L"  —  " + d.model + L"  " + formatBytes(d.size);
            ComboBox_AddString(src, text.c_str());
            st->sourceNums.push_back(d.number);
            if (d.number == st->source->number) srcSel = static_cast<int>(st->sourceNums.size()) - 1;
        }
        ComboBox_SetCurSel(src, srcSel);
        for (const auto& d : st->layout->disks) {
            if (d.optical || d.boot || d.system || d.readOnly) continue;
            const auto text = L"Disk " + std::to_wstring(d.number) + L"  —  " + d.model + L"  " + formatBytes(d.size);
            ComboBox_AddString(dest, text.c_str());
            st->destNums.push_back(d.number);
        }
        if (!st->destNums.empty()) ComboBox_SetCurSel(dest, 0);
        CheckDlgButton(dlg, IDC_USED, BST_CHECKED);
        CheckDlgButton(dlg, IDC_ALIGN, BST_CHECKED);
        SetDlgItemTextW(dlg, IDC_WARN, L"All partitions on the destination will be erased.");
        return TRUE;
    }
    if (msg == WM_COMMAND && LOWORD(w) == IDOK) {
        const int si = ComboBox_GetCurSel(GetDlgItem(dlg, IDC_SOURCE));
        const int di = ComboBox_GetCurSel(GetDlgItem(dlg, IDC_DEST));
        if (si < 0 || di < 0) {
            MessageBoxW(dlg, L"Choose a source and a destination disk.", kProductName, MB_OK | MB_ICONINFORMATION);
            return TRUE;
        }
        st->result.source = st->sourceNums[static_cast<size_t>(si)];
        st->result.dest = st->destNums[static_cast<size_t>(di)];
        if (st->result.source == st->result.dest) {
            MessageBoxW(dlg, L"Source and destination must be different disks.", kProductName, MB_OK | MB_ICONWARNING);
            return TRUE;
        }
        st->result.mode = IsDlgButtonChecked(dlg, IDC_USED) == BST_CHECKED ? CloneCopyMode::UsedData : CloneCopyMode::AllSectors;
        st->result.align = IsDlgButtonChecked(dlg, IDC_ALIGN) == BST_CHECKED;
        st->result.expand = IsDlgButtonChecked(dlg, IDC_EXPAND) == BST_CHECKED;
        EndDialog(dlg, IDOK);
        return TRUE;
    }
    if (msg == WM_COMMAND && LOWORD(w) == IDCANCEL) {
        EndDialog(dlg, IDCANCEL);
        return TRUE;
    }
    return FALSE;
}

struct Region {
    int disk = 0;
    std::uint64_t offset = 0;
    std::uint64_t size = 0;
};

struct ClonePartState {
    const Segment* source = nullptr;
    const DiskLayout* layout = nullptr;
    std::vector<Region> regions;
    ClonePartResult result;
};

INT_PTR CALLBACK clonePartProc(HWND dlg, UINT msg, WPARAM w, LPARAM l) {
    auto* st = reinterpret_cast<ClonePartState*>(GetWindowLongPtrW(dlg, GWLP_USERDATA));
    if (msg == WM_INITDIALOG) {
        attachDialogTheme(dlg);
        st = reinterpret_cast<ClonePartState*>(l);
        SetWindowLongPtrW(dlg, GWLP_USERDATA, l);
        SetDlgItemTextW(dlg, IDC_SUMMARY, L"Copy the selected partition into unallocated space.");
        auto box = GetDlgItem(dlg, IDC_DEST);
        for (const auto& d : st->layout->disks) {
            if (d.optical || !d.initialized() || d.readOnly || d.offline) continue;
            for (const auto& g : d.segments) {
                if (!g.unallocated || g.size < st->source->size) continue;
                const bool overlap = g.diskNumber == st->source->diskNumber &&
                                     g.offset < st->source->offset + st->source->size &&
                                     st->source->offset < g.offset + g.size;
                if (overlap) continue;
                const auto text = L"Disk " + std::to_wstring(d.number) + L"  —  " + formatBytes(g.size) +
                                  L" free at " + formatBytes(g.offset);
                ComboBox_AddString(box, text.c_str());
                st->regions.push_back({d.number, g.offset, g.size});
            }
        }
        if (!st->regions.empty()) ComboBox_SetCurSel(box, 0);
        CheckDlgButton(dlg, IDC_USED, BST_CHECKED);
        CheckDlgButton(dlg, IDC_ALIGN, BST_CHECKED);
        SetDlgItemTextW(dlg, IDC_WARN, st->regions.empty()
                                           ? L"No unallocated region is large enough."
                                           : L"Existing partitions on the destination disk are left in place.");
        return TRUE;
    }
    if (msg == WM_COMMAND && LOWORD(w) == IDOK) {
        const int i = ComboBox_GetCurSel(GetDlgItem(dlg, IDC_DEST));
        if (i < 0) {
            MessageBoxW(dlg, L"Choose a destination region.", kProductName, MB_OK | MB_ICONINFORMATION);
            return TRUE;
        }
        st->result.destDisk = st->regions[static_cast<size_t>(i)].disk;
        st->result.destOffset = st->regions[static_cast<size_t>(i)].offset;
        st->result.destRegion = st->regions[static_cast<size_t>(i)].size;
        st->result.mode = IsDlgButtonChecked(dlg, IDC_USED) == BST_CHECKED ? CloneCopyMode::UsedData : CloneCopyMode::AllSectors;
        st->result.align = IsDlgButtonChecked(dlg, IDC_ALIGN) == BST_CHECKED;
        st->result.fill = IsDlgButtonChecked(dlg, IDC_FILL) == BST_CHECKED;
        EndDialog(dlg, IDOK);
        return TRUE;
    }
    if (msg == WM_COMMAND && LOWORD(w) == IDCANCEL) {
        EndDialog(dlg, IDCANCEL);
        return TRUE;
    }
    return FALSE;
}

INT_PTR CALLBACK settingsProc(HWND dlg, UINT msg, WPARAM w, LPARAM l) {
    auto* cfg = reinterpret_cast<AppConfig*>(GetWindowLongPtrW(dlg, GWLP_USERDATA));
    if (msg == WM_INITDIALOG) {
        attachDialogTheme(dlg);
        cfg = reinterpret_cast<AppConfig*>(l);
        SetWindowLongPtrW(dlg, GWLP_USERDATA, l);
        auto theme = GetDlgItem(dlg, IDC_THEME);
        ComboBox_AddString(theme, L"System (match Windows)");
        ComboBox_SetItemData(theme, 0, reinterpret_cast<LPARAM>(L"system"));
        int sel = iequals(cfg->theme, L"system") || cfg->theme.empty() ? 0 : -1;
        int index = 1;
        for (const auto& t : allThemes()) {
            std::wstring label = t.name;
            if (t.era && t.era[0])
                label += std::wstring(L"  (") + t.era + L")";
            ComboBox_AddString(theme, label.c_str());
            ComboBox_SetItemData(theme, index, reinterpret_cast<LPARAM>(t.id));
            if (iequals(cfg->theme, t.id)) sel = index;
            ++index;
        }
        ComboBox_SetCurSel(theme, sel < 0 ? 0 : sel);
        CheckDlgButton(dlg, IDC_REFRESH_LAUNCH, cfg->refreshOnLaunch ? BST_CHECKED : BST_UNCHECKED);
        CheckDlgButton(dlg, IDC_SHOW_REMOVABLE, cfg->display.showRemovable ? BST_CHECKED : BST_UNCHECKED);
        CheckDlgButton(dlg, IDC_SHOW_VIRTUAL, cfg->display.showVirtual ? BST_CHECKED : BST_UNCHECKED);
        CheckDlgButton(dlg, IDC_SHOW_UNALLOC, cfg->display.showUnallocated ? BST_CHECKED : BST_UNCHECKED);
        CheckDlgButton(dlg, IDC_SHOW_RESERVED, cfg->display.showSystemReserved ? BST_CHECKED : BST_UNCHECKED);
        CheckDlgButton(dlg, IDC_CONFIRM, cfg->confirmDestructive ? BST_CHECKED : BST_UNCHECKED);
        CheckDlgButton(dlg, IDC_PROTECT, cfg->protectSystem ? BST_CHECKED : BST_UNCHECKED);
        return TRUE;
    }
    if (msg == WM_COMMAND && LOWORD(w) == IDOK) {
        {
            const int ti = ComboBox_GetCurSel(GetDlgItem(dlg, IDC_THEME));
            auto* id = reinterpret_cast<const wchar_t*>(ComboBox_GetItemData(GetDlgItem(dlg, IDC_THEME), ti));
            cfg->theme = normalizeThemeId(id ? id : L"system");
        }
        cfg->refreshOnLaunch = IsDlgButtonChecked(dlg, IDC_REFRESH_LAUNCH) == BST_CHECKED;
        cfg->display.showRemovable = IsDlgButtonChecked(dlg, IDC_SHOW_REMOVABLE) == BST_CHECKED;
        cfg->display.showVirtual = IsDlgButtonChecked(dlg, IDC_SHOW_VIRTUAL) == BST_CHECKED;
        cfg->display.showUnallocated = IsDlgButtonChecked(dlg, IDC_SHOW_UNALLOC) == BST_CHECKED;
        cfg->display.showSystemReserved = IsDlgButtonChecked(dlg, IDC_SHOW_RESERVED) == BST_CHECKED;
        cfg->confirmDestructive = IsDlgButtonChecked(dlg, IDC_CONFIRM) == BST_CHECKED;
        cfg->protectSystem = IsDlgButtonChecked(dlg, IDC_PROTECT) == BST_CHECKED;
        EndDialog(dlg, IDOK);
        return TRUE;
    }
    if (msg == WM_COMMAND && LOWORD(w) == IDCANCEL) {
        EndDialog(dlg, IDCANCEL);
        return TRUE;
    }
    return FALSE;
}

INT_PTR CALLBACK applyProc(HWND dlg, UINT msg, WPARAM w, LPARAM l) {
    if (msg == WM_INITDIALOG) {
        attachDialogTheme(dlg);
        const auto* ops = reinterpret_cast<const std::vector<PendingOperation>*>(l);
        SetWindowLongPtrW(dlg, GWLP_USERDATA, l);
        int destructive = 0;
        for (const auto& op : *ops) {
            std::wstring line = (op.destructive ? L"⚠ " : L"") + op.description;
            SendDlgItemMessageW(dlg, IDC_OPLIST, LB_ADDSTRING, 0, reinterpret_cast<LPARAM>(line.c_str()));
            if (op.destructive) ++destructive;
        }
        SetDlgItemTextW(dlg, IDC_WARN, destructive
                                           ? (std::to_wstring(destructive) + L" destructive operation(s) will erase or convert data.").c_str()
                                           : L"No destructive operations in this batch.");
        return TRUE;
    }
    if (msg == WM_COMMAND && (LOWORD(w) == IDOK || LOWORD(w) == IDCANCEL)) {
        EndDialog(dlg, LOWORD(w));
        return TRUE;
    }
    return FALSE;
}

INT_PTR CALLBACK propsProc(HWND dlg, UINT msg, WPARAM w, LPARAM l) {
    if (msg == WM_INITDIALOG) {
        attachDialogTheme(dlg);
        const auto* lines = reinterpret_cast<const std::vector<std::wstring>*>(l);
        for (const auto& line : *lines)
            SendDlgItemMessageW(dlg, IDC_PROPLIST, LB_ADDSTRING, 0, reinterpret_cast<LPARAM>(line.c_str()));
        return TRUE;
    }
    if (msg == WM_COMMAND && (LOWORD(w) == IDOK || LOWORD(w) == IDCANCEL)) {
        EndDialog(dlg, IDOK);
        return TRUE;
    }
    return FALSE;
}

INT_PTR CALLBACK aboutProc(HWND dlg, UINT msg, WPARAM w, LPARAM) {
    if (msg == WM_INITDIALOG) {
        attachDialogTheme(dlg);
        std::wstring text = std::wstring(kProductName) + L"\nVersion " + kVersion + L"\n\n" +
                            kDescription + L"\n\n" + kCopyright + L"\nCreated by " + kCompany;
        SetDlgItemTextW(dlg, IDC_SUMMARY, text.c_str());
        return TRUE;
    }
    if (msg == WM_COMMAND && (LOWORD(w) == IDOK || LOWORD(w) == IDCANCEL)) {
        EndDialog(dlg, IDOK);
        return TRUE;
    }
    return FALSE;
}

} // namespace

std::optional<CreateResult> promptCreate(HWND owner, const Segment& gap, const std::vector<wchar_t>& letters) {
    CreateState st{&gap, &letters, {}};
    if (DialogBoxParamW(GetModuleHandleW(nullptr), MAKEINTRESOURCEW(IDD_CREATE), owner, createProc,
                        reinterpret_cast<LPARAM>(&st)) != IDOK)
        return std::nullopt;
    return st.result;
}

std::optional<FormatResult> promptFormat(HWND owner, const Segment& seg) {
    FormatState st{&seg, {}};
    if (DialogBoxParamW(GetModuleHandleW(nullptr), MAKEINTRESOURCEW(IDD_FORMAT), owner, formatProc,
                        reinterpret_cast<LPARAM>(&st)) != IDOK)
        return std::nullopt;
    return st.result;
}

std::optional<std::uint64_t> promptResize(HWND owner, const Segment& seg, std::uint64_t maxSize) {
    ResizeState st{&seg, maxSize, seg.size};
    if (DialogBoxParamW(GetModuleHandleW(nullptr), MAKEINTRESOURCEW(IDD_RESIZE), owner, resizeProc,
                        reinterpret_cast<LPARAM>(&st)) != IDOK)
        return std::nullopt;
    return st.result;
}

std::optional<wchar_t> promptLetter(HWND owner, wchar_t current, const std::vector<wchar_t>& letters) {
    LetterState st{current, &letters, current};
    if (DialogBoxParamW(GetModuleHandleW(nullptr), MAKEINTRESOURCEW(IDD_LETTER), owner, letterProc,
                        reinterpret_cast<LPARAM>(&st)) != IDOK)
        return std::nullopt;
    return st.result;
}

std::optional<std::wstring> promptLabel(HWND owner, const std::wstring& current) {
    LabelState st{current, {}};
    if (DialogBoxParamW(GetModuleHandleW(nullptr), MAKEINTRESOURCEW(IDD_LABEL), owner, labelProc,
                        reinterpret_cast<LPARAM>(&st)) != IDOK)
        return std::nullopt;
    return st.result;
}

std::optional<PartitionStyle> promptInitialize(HWND owner, const Disk& disk) {
    InitState st{&disk, PartitionStyle::Gpt};
    if (DialogBoxParamW(GetModuleHandleW(nullptr), MAKEINTRESOURCEW(IDD_INIT), owner, initProc,
                        reinterpret_cast<LPARAM>(&st)) != IDOK)
        return std::nullopt;
    return st.result;
}

std::optional<CloneDiskResult> promptCloneDisk(HWND owner, const Disk& source, const DiskLayout& layout) {
    CloneDiskState st{&source, &layout, {}, {}, {}};
    if (DialogBoxParamW(GetModuleHandleW(nullptr), MAKEINTRESOURCEW(IDD_CLONE_DISK), owner, cloneDiskProc,
                        reinterpret_cast<LPARAM>(&st)) != IDOK)
        return std::nullopt;
    return st.result;
}

std::optional<ClonePartResult> promptClonePart(HWND owner, const Segment& source, const DiskLayout& layout) {
    ClonePartState st{&source, &layout, {}, {}};
    if (DialogBoxParamW(GetModuleHandleW(nullptr), MAKEINTRESOURCEW(IDD_CLONE_PART), owner, clonePartProc,
                        reinterpret_cast<LPARAM>(&st)) != IDOK)
        return std::nullopt;
    return st.result;
}

bool promptSettings(HWND owner, AppConfig& config) {
    return DialogBoxParamW(GetModuleHandleW(nullptr), MAKEINTRESOURCEW(IDD_SETTINGS), owner, settingsProc,
                           reinterpret_cast<LPARAM>(&config)) == IDOK;
}

bool promptApply(HWND owner, const std::vector<PendingOperation>& ops) {
    return DialogBoxParamW(GetModuleHandleW(nullptr), MAKEINTRESOURCEW(IDD_APPLY), owner, applyProc,
                           reinterpret_cast<LPARAM>(&ops)) == IDOK;
}

void showProperties(HWND owner, const Disk* disk, const Segment* seg) {
    std::vector<std::wstring> lines;
    if (disk) {
        lines.push_back(L"Disk " + std::to_wstring(disk->number));
        lines.push_back(L"Model: " + disk->model);
        lines.push_back(L"Size: " + formatBytes(disk->size));
        lines.push_back(L"Style: " + std::wstring(disk->styleText()));
        lines.push_back(L"Bus: " + disk->busType);
        lines.push_back(L"Status: " + disk->status);
        if (!disk->serial.empty()) lines.push_back(L"Serial: " + disk->serial);
    }
    if (seg) {
        lines.push_back(L"");
        lines.push_back(L"Partition");
        lines.push_back(L"Type: " + std::wstring(kindText(static_cast<int>(seg->kind))));
        lines.push_back(L"Size: " + formatBytes(seg->size));
        lines.push_back(L"Offset: " + formatBytes(seg->offset));
        if (seg->letter) lines.push_back(L"Letter: " + std::wstring(1, seg->letter) + L":");
        if (!seg->label.empty()) lines.push_back(L"Label: " + seg->label);
        if (!seg->fileSystem.empty()) lines.push_back(L"File system: " + seg->fileSystem);
        if (!seg->gptType.empty()) lines.push_back(L"GPT type: " + seg->gptType);
    }
    DialogBoxParamW(GetModuleHandleW(nullptr), MAKEINTRESOURCEW(IDD_PROPS), owner, propsProc,
                    reinterpret_cast<LPARAM>(&lines));
}

void showAbout(HWND owner) {
    DialogBoxParamW(GetModuleHandleW(nullptr), MAKEINTRESOURCEW(IDD_ABOUT), owner, aboutProc, 0);
}

} // namespace pm
