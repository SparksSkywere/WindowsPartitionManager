#pragma once

#include "Models.hpp"

#include <windows.h>
#include <optional>
#include <string>
#include <vector>

namespace pm {

struct CreateResult {
    std::uint64_t size = 0;
    wchar_t letter = 0;
    std::wstring label;
    std::wstring fileSystem = L"NTFS";
    bool formatAfter = true;
    bool quick = true;
};

struct FormatResult {
    std::wstring fileSystem = L"NTFS";
    std::wstring label;
    bool quick = true;
};

struct CloneDiskResult {
    int source = -1;
    int dest = -1;
    CloneCopyMode mode = CloneCopyMode::UsedData;
    bool align = true;
    bool expand = false;
};

struct ClonePartResult {
    int destDisk = -1;
    std::uint64_t destOffset = 0;
    std::uint64_t destRegion = 0;
    CloneCopyMode mode = CloneCopyMode::UsedData;
    bool align = true;
    bool fill = false;
};

std::optional<CreateResult> promptCreate(HWND owner, const Segment& gap, const std::vector<wchar_t>& letters);
std::optional<FormatResult> promptFormat(HWND owner, const Segment& seg);
std::optional<std::uint64_t> promptResize(HWND owner, const Segment& seg, std::uint64_t maxSize);
std::optional<wchar_t> promptLetter(HWND owner, wchar_t current, const std::vector<wchar_t>& letters);
std::optional<std::wstring> promptLabel(HWND owner, const std::wstring& current);
std::optional<PartitionStyle> promptInitialize(HWND owner, const Disk& disk);
std::optional<CloneDiskResult> promptCloneDisk(HWND owner, const Disk& source, const DiskLayout& layout);
std::optional<ClonePartResult> promptClonePart(HWND owner, const Segment& source, const DiskLayout& layout);
bool promptSettings(HWND owner, AppConfig& config);
bool promptApply(HWND owner, const std::vector<PendingOperation>& ops);
void showProperties(HWND owner, const Disk* disk, const Segment* seg);
void showAbout(HWND owner);

} // namespace pm
