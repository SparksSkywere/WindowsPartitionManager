#pragma once

namespace pm {

inline constexpr wchar_t kProductName[] = L"Partition Manager";
inline constexpr wchar_t kCompany[] = L"Skywere Industries";
inline constexpr wchar_t kCopyright[] = L"Copyright © Skywere Industries";
inline constexpr wchar_t kVersion[] = PM_VERSION_STRING;
inline constexpr wchar_t kGitHubUrl[] = L"https://github.com/SparksSkywere/WindowsPartitionManager";
inline constexpr wchar_t kGitHubIssuesUrl[] = L"https://github.com/SparksSkywere/WindowsPartitionManager/issues";
inline constexpr wchar_t kDescription[] =
    L"Manage disks and partitions on Windows. Create, delete, format, resize, and clone "
    L"volumes, change drive letters and labels, initialize MBR or GPT disks, and queue "
    L"changes until you click Apply.";

} // namespace pm
