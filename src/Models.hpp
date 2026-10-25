#pragma once

#include <cstdint>
#include <string>
#include <vector>

namespace pm {

enum class PartitionStyle { Unknown, Mbr, Gpt };

enum class SegmentKind {
    Unallocated,
    Primary,
    Logical,
    Extended,
    Efi,
    Recovery,
    MicrosoftReserved,
    Oem,
    Unknown
};

enum class OperationKind {
    CreatePartition,
    DeletePartition,
    FormatPartition,
    ResizePartition,
    ChangeDriveLetter,
    ChangeLabel,
    HidePartition,
    UnhidePartition,
    SetActive,
    InitializeDisk,
    ConvertPartitionStyle,
    DeleteAllPartitions,
    OfflineDisk,
    OnlineDisk,
    CloneDisk,
    ClonePartition
};

enum class CloneCopyMode { UsedData, AllSectors };

struct Segment {
    int diskNumber = 0;
    int partitionNumber = 0;
    bool unallocated = false;
    std::uint64_t offset = 0;
    std::uint64_t size = 0;
    wchar_t letter = 0;
    std::wstring label;
    std::wstring fileSystem;
    std::uint64_t sizeRemaining = 0;
    SegmentKind kind = SegmentKind::Unknown;
    bool boot = false;
    bool system = false;
    bool active = false;
    bool hidden = false;
    bool readOnly = false;
    bool offline = false;
    bool pending = false;
    std::wstring status;
    std::wstring gptType;
    std::uint16_t mbrType = 0;
    std::wstring volumePath;
    std::wstring accessPaths;

    std::uint64_t used() const { return size >= sizeRemaining ? size - sizeRemaining : 0; }
    bool protectedVolume() const { return boot || system; }
};

struct Disk {
    int number = 0;
    std::wstring friendlyName;
    std::wstring model;
    std::wstring serial;
    std::wstring busType;
    std::uint64_t size = 0;
    std::uint64_t allocated = 0;
    PartitionStyle style = PartitionStyle::Unknown;
    bool boot = false;
    bool system = false;
    bool offline = false;
    bool readOnly = false;
    bool removable = false;
    bool virtualDisk = false;
    bool optical = false;
    std::wstring health = L"Healthy";
    std::wstring status = L"Online";
    std::uint32_t logicalSector = 512;
    std::uint32_t physicalSector = 512;
    std::wstring location;
    std::wstring uniqueId;
    std::vector<Segment> segments;

    bool initialized() const { return style != PartitionStyle::Unknown; }
    const wchar_t* styleText() const {
        if (style == PartitionStyle::Mbr) return L"MBR";
        if (style == PartitionStyle::Gpt) return L"GPT";
        return L"Uninitialized";
    }
};

struct DiskLayout {
    std::vector<Disk> disks;

    Disk* findDisk(int number) {
        for (auto& d : disks)
            if (d.number == number) return &d;
        return nullptr;
    }
    const Disk* findDisk(int number) const {
        for (const auto& d : disks)
            if (d.number == number) return &d;
        return nullptr;
    }
};

struct CloneSlice {
    std::uint64_t offset = 0;
    std::uint64_t size = 0;
    std::wstring gptType;
    std::uint16_t mbrType = 0;
    SegmentKind kind = SegmentKind::Unknown;
    bool active = false;
    bool hidden = false;
    wchar_t letter = 0;
    std::wstring fileSystem;
    bool unallocated = false;
};

struct CloneDiskParams {
    int sourceDisk = -1;
    int destDisk = -1;
    CloneCopyMode mode = CloneCopyMode::UsedData;
    bool alignToMegabyte = true;
    bool expandLast = false;
    bool sourceIsBoot = false;
    PartitionStyle style = PartitionStyle::Unknown;
    std::uint64_t destSize = 0;
    std::vector<CloneSlice> sourceSlices;
};

struct ClonePartitionParams {
    int sourceDisk = -1;
    int destDisk = -1;
    CloneSlice source;
    std::uint64_t destOffset = 0;
    std::uint64_t destRegionSize = 0;
    CloneCopyMode mode = CloneCopyMode::UsedData;
    bool alignToMegabyte = true;
    bool fillRegion = false;
};

struct CreateParams {
    std::uint64_t offset = 0;
    std::uint64_t size = 0;
    wchar_t letter = 0;
    std::wstring label;
    std::wstring fileSystem = L"NTFS";
    bool formatAfter = true;
    bool quick = true;
};

struct FormatParams {
    std::wstring fileSystem = L"NTFS";
    std::wstring label;
    bool quick = true;
};

struct PendingOperation {
    int id = 0;
    OperationKind kind = OperationKind::CreatePartition;
    int diskNumber = 0;
    std::uint64_t offset = 0;
    std::uint64_t size = 0;
    std::wstring description;
    bool destructive = false;
    wchar_t letter = 0;
    std::wstring label;
    PartitionStyle targetStyle = PartitionStyle::Unknown;
    CreateParams create;
    FormatParams format;
    std::uint64_t newSize = 0;
    CloneDiskParams cloneDisk;
    ClonePartitionParams clonePart;
};

struct OperationResult {
    bool success = false;
    std::wstring message;
    std::uint32_t code = 0;
};

struct DisplaySettings {
    bool showRemovable = true;
    bool showVirtual = false;
    bool showUnallocated = true;
    bool showSystemReserved = true;
};

struct AppConfig {
    std::wstring theme = L"system";
    bool refreshOnLaunch = true;
    DisplaySettings display;
    bool confirmDestructive = true;
    bool protectSystem = true;
};

struct PlannedSlice {
    CloneSlice source;
    std::uint64_t destOffset = 0;
    std::uint64_t destSize = 0;
};

inline CloneSlice toSlice(const Segment& s) {
    CloneSlice c;
    c.offset = s.offset;
    c.size = s.size;
    c.gptType = s.gptType;
    c.mbrType = s.mbrType;
    c.kind = s.kind;
    c.active = s.active;
    c.hidden = s.hidden;
    c.letter = s.letter;
    c.fileSystem = s.fileSystem;
    c.unallocated = s.unallocated;
    return c;
}

} // namespace pm
