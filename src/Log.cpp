#include "Log.hpp"
#include "Util.hpp"

#include <windows.h>

#include <fstream>

namespace pm {

void Log::add(const wchar_t* prefix, const std::wstring& line) {
    SYSTEMTIME st{};
    GetLocalTime(&st);
    wchar_t stamp[32];
    swprintf_s(stamp, L"%02u:%02u:%02u ", st.wHour, st.wMinute, st.wSecond);
    auto text = std::wstring(stamp) + prefix + line;
    lines_.push_back(text);
    if (lines_.size() > 500) lines_.erase(lines_.begin());
    if (onLine) onLine(text);

    const auto dir = appDataDir();
    if (dir.empty()) return;
    ensureDir(dir);
    std::ofstream out(toUtf8(dir + L"\\activity.log"), std::ios::app);
    out << toUtf8(text) << "\n";
}

void Log::info(const std::wstring& line) { add(L"", line); }
void Log::warn(const std::wstring& line) { add(L"Warning: ", line); }
void Log::error(const std::wstring& line) { add(L"Error: ", line); }
void Log::success(const std::wstring& line) { add(L"", line); }

} // namespace pm
