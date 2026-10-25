#pragma once

#include <cstdint>
#include <string>
#include <vector>

namespace pm {

std::wstring formatBytes(std::uint64_t bytes, int decimals = 2);
std::uint64_t alignDown(std::uint64_t value, std::uint64_t alignment);
std::uint64_t alignUp(std::uint64_t value, std::uint64_t alignment);
std::wstring toWide(const std::string& s);
std::string toUtf8(const std::wstring& s);
std::wstring lower(std::wstring s);
bool iequals(const std::wstring& a, const std::wstring& b);
std::wstring trim(std::wstring s);
std::wstring join(const std::vector<std::wstring>& parts, const wchar_t* sep);
bool isElevated();
bool tryRelaunchElevated(int argc, wchar_t** argv);
std::wstring appDataDir();
void ensureDir(const std::wstring& path);
void openUrl(const wchar_t* url);
void openFolder(const std::wstring& path);
const wchar_t* kindText(int kind);
std::wstring driveText(wchar_t letter, bool unallocated);
std::wstring segmentDisplayName(wchar_t letter, const std::wstring& label, int kind, bool unallocated,
                                const std::wstring& sizeText);

} // namespace pm
