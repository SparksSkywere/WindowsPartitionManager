#pragma once

#include "Log.hpp"
#include "Models.hpp"

namespace pm {

inline constexpr std::uint64_t kMinUnallocated = 8ULL * 1024ULL * 1024ULL;
inline constexpr wchar_t kStorageNs[] = L"ROOT\\Microsoft\\Windows\\Storage";

DiskLayout queryInventory(const DisplaySettings& display, Log& log);
std::vector<wchar_t> availableLetters(const DiskLayout& layout);

} // namespace pm
