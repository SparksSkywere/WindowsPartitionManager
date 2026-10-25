#include "Config.hpp"
#include "Theme.hpp"
#include "Util.hpp"

#include <windows.h>

namespace pm {

static int readInt(const std::wstring& path, const wchar_t* section, const wchar_t* key, int fallback) {
    return GetPrivateProfileIntW(section, key, fallback, path.c_str());
}

static void writeInt(const std::wstring& path, const wchar_t* section, const wchar_t* key, int value) {
    WritePrivateProfileStringW(section, key, std::to_wstring(value).c_str(), path.c_str());
}

std::wstring ConfigStore::path() const {
    return appDataDir() + L"\\config.ini";
}

void ConfigStore::load() {
    const auto file = path();
    wchar_t theme[64]{};
    GetPrivateProfileStringW(L"general", L"theme", L"system", theme, 64, file.c_str());
    config.theme = normalizeThemeId(theme);
    config.refreshOnLaunch = readInt(file, L"general", L"refreshOnLaunch", 1) != 0;
    config.display.showRemovable = readInt(file, L"display", L"showRemovable", 1) != 0;
    config.display.showVirtual = readInt(file, L"display", L"showVirtual", 0) != 0;
    config.display.showUnallocated = readInt(file, L"display", L"showUnallocated", 1) != 0;
    config.display.showSystemReserved = readInt(file, L"display", L"showSystemReserved", 1) != 0;
    config.confirmDestructive = readInt(file, L"safety", L"confirmDestructive", 1) != 0;
    config.protectSystem = readInt(file, L"safety", L"protectSystemPartitions", 1) != 0;
}

void ConfigStore::save() const {
    ensureDir(appDataDir());
    const auto file = path();
    WritePrivateProfileStringW(L"general", L"theme", config.theme.c_str(), file.c_str());
    writeInt(file, L"general", L"refreshOnLaunch", config.refreshOnLaunch ? 1 : 0);
    writeInt(file, L"display", L"showRemovable", config.display.showRemovable ? 1 : 0);
    writeInt(file, L"display", L"showVirtual", config.display.showVirtual ? 1 : 0);
    writeInt(file, L"display", L"showUnallocated", config.display.showUnallocated ? 1 : 0);
    writeInt(file, L"display", L"showSystemReserved", config.display.showSystemReserved ? 1 : 0);
    writeInt(file, L"safety", L"confirmDestructive", config.confirmDestructive ? 1 : 0);
    writeInt(file, L"safety", L"protectSystemPartitions", config.protectSystem ? 1 : 0);
}

} // namespace pm
