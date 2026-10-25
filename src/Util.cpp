#include "Util.hpp"
#include "Models.hpp"

#include <windows.h>
#include <shellapi.h>
#include <shlobj.h>
#include <shlwapi.h>

#include <algorithm>
#include <cmath>
#include <sstream>

namespace pm {

std::wstring formatBytes(std::uint64_t bytes, int decimals) {
    static const wchar_t* units[] = {L"B", L"KB", L"MB", L"GB", L"TB", L"PB"};
    if (bytes < 1024) return std::to_wstring(bytes) + L" B";
    double value = static_cast<double>(bytes);
    int unit = 0;
    while (value >= 1024 && unit < 5) {
        value /= 1024;
        ++unit;
    }
    wchar_t buf[64];
    const int prec = unit <= 1 ? 0 : decimals;
    swprintf_s(buf, L"%.*f %s", prec, value, units[unit]);
    return buf;
}

std::uint64_t alignDown(std::uint64_t value, std::uint64_t alignment) {
    if (alignment == 0) return value;
    return value / alignment * alignment;
}

std::uint64_t alignUp(std::uint64_t value, std::uint64_t alignment) {
    if (alignment == 0) return value;
    const auto rem = value % alignment;
    return rem == 0 ? value : value + (alignment - rem);
}

std::wstring toWide(const std::string& s) {
    if (s.empty()) return {};
    const int n = MultiByteToWideChar(CP_UTF8, 0, s.c_str(), static_cast<int>(s.size()), nullptr, 0);
    std::wstring out(n, 0);
    MultiByteToWideChar(CP_UTF8, 0, s.c_str(), static_cast<int>(s.size()), out.data(), n);
    return out;
}

std::string toUtf8(const std::wstring& s) {
    if (s.empty()) return {};
    const int n = WideCharToMultiByte(CP_UTF8, 0, s.c_str(), static_cast<int>(s.size()), nullptr, 0, nullptr, nullptr);
    std::string out(n, 0);
    WideCharToMultiByte(CP_UTF8, 0, s.c_str(), static_cast<int>(s.size()), out.data(), n, nullptr, nullptr);
    return out;
}

std::wstring lower(std::wstring s) {
    std::transform(s.begin(), s.end(), s.begin(), [](wchar_t c) { return static_cast<wchar_t>(towlower(c)); });
    return s;
}

bool iequals(const std::wstring& a, const std::wstring& b) {
    return _wcsicmp(a.c_str(), b.c_str()) == 0;
}

std::wstring trim(std::wstring s) {
    const auto start = s.find_first_not_of(L" \t\r\n");
    if (start == std::wstring::npos) return {};
    const auto end = s.find_last_not_of(L" \t\r\n");
    return s.substr(start, end - start + 1);
}

std::wstring join(const std::vector<std::wstring>& parts, const wchar_t* sep) {
    std::wstring out;
    for (size_t i = 0; i < parts.size(); ++i) {
        if (parts[i].empty()) continue;
        if (!out.empty()) out += sep;
        out += parts[i];
    }
    return out;
}

bool isElevated() {
    BOOL admin = FALSE;
    PSID group = nullptr;
    SID_IDENTIFIER_AUTHORITY nt = SECURITY_NT_AUTHORITY;
    if (AllocateAndInitializeSid(&nt, 2, SECURITY_BUILTIN_DOMAIN_RID, DOMAIN_ALIAS_RID_ADMINS,
                                 0, 0, 0, 0, 0, 0, &group)) {
        CheckTokenMembership(nullptr, group, &admin);
        FreeSid(group);
    }
    return admin == TRUE;
}

bool tryRelaunchElevated(int argc, wchar_t** argv) {
    if (isElevated()) return false;
    for (int i = 1; i < argc; ++i) {
        if (_wcsicmp(argv[i], L"--no-elevate") == 0 || _wcsicmp(argv[i], L"--elevating") == 0)
            return false;
    }
    wchar_t exe[MAX_PATH]{};
    GetModuleFileNameW(nullptr, exe, MAX_PATH);
    std::wstring args;
    for (int i = 1; i < argc; ++i) {
        if (_wcsicmp(argv[i], L"--elevating") == 0) continue;
        if (!args.empty()) args += L' ';
        args += L'"';
        args += argv[i];
        args += L'"';
    }
    if (!args.empty()) args += L' ';
    args += L"--elevating";
    SHELLEXECUTEINFOW sei{sizeof(sei)};
    sei.fMask = SEE_MASK_NOASYNC;
    sei.lpVerb = L"runas";
    sei.lpFile = exe;
    sei.lpParameters = args.c_str();
    sei.nShow = SW_SHOWNORMAL;
    if (ShellExecuteExW(&sei)) return true;
    return false;
}

std::wstring appDataDir() {
    wchar_t* raw = nullptr;
    std::wstring dir;
    if (SUCCEEDED(SHGetKnownFolderPath(FOLDERID_RoamingAppData, 0, nullptr, &raw))) {
        dir = raw;
        CoTaskMemFree(raw);
    }
    if (dir.empty()) return {};
    dir += L"\\Skywere Industries\\PartitionManager";
    return dir;
}

void ensureDir(const std::wstring& path) {
    if (path.empty()) return;
    SHCreateDirectoryExW(nullptr, path.c_str(), nullptr);
}

void openUrl(const wchar_t* url) {
    ShellExecuteW(nullptr, L"open", url, nullptr, nullptr, SW_SHOWNORMAL);
}

void openFolder(const std::wstring& path) {
    ShellExecuteW(nullptr, L"open", path.c_str(), nullptr, nullptr, SW_SHOWNORMAL);
}

const wchar_t* kindText(int kind) {
    switch (static_cast<SegmentKind>(kind)) {
    case SegmentKind::Unallocated: return L"Unallocated";
    case SegmentKind::Primary: return L"Primary";
    case SegmentKind::Logical: return L"Logical";
    case SegmentKind::Extended: return L"Extended";
    case SegmentKind::Efi: return L"EFI System";
    case SegmentKind::Recovery: return L"Recovery";
    case SegmentKind::MicrosoftReserved: return L"MSR";
    case SegmentKind::Oem: return L"OEM";
    default: return L"Unknown";
    }
}

std::wstring driveText(wchar_t letter, bool unallocated) {
    if (unallocated) return L"*";
    if (letter) return std::wstring(1, letter) + L":";
    return L"—";
}

std::wstring segmentDisplayName(wchar_t letter, const std::wstring& label, int kind, bool unallocated,
                                const std::wstring& sizeText) {
    if (unallocated) return L"Unallocated (" + sizeText + L")";
    std::wstring name;
    if (letter) {
        name += letter;
        name += L": ";
    }
    if (!label.empty()) name += label;
    else name += kindText(kind);
    return trim(name);
}

} // namespace pm
