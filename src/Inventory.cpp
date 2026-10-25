#include "Inventory.hpp"
#include "Util.hpp"
#include "Wmi.hpp"

#include <algorithm>
#include <map>

namespace pm {
namespace {

const wchar_t* busName(std::uint16_t bus) {
    static const wchar_t* names[] = {
        L"Unknown", L"SCSI", L"ATAPI", L"ATA", L"IEEE 1394", L"SSA", L"Fibre Channel",
        L"USB", L"RAID", L"iSCSI", L"SAS", L"SATA", L"SD", L"MMC", L"Virtual",
        L"File-backed virtual", L"Storage Spaces", L"NVMe"
    };
    return bus < 18 ? names[bus] : L"Unknown";
}

SegmentKind classifyGpt(const std::wstring& gpt, std::uint16_t mbr) {
    std::wstring key = lower(gpt);
    if (key == L"{c12a7328-f81f-11d2-ba4b-00a0c93ec93b}") return SegmentKind::Efi;
    if (key == L"{e3c9e316-0b5c-4db8-817d-f92df00215ae}") return SegmentKind::MicrosoftReserved;
    if (key == L"{de94bba4-06d1-4d40-a16a-bfd50179d6ac}") return SegmentKind::Recovery;
    if (key == L"{21686148-6449-6e6f-744e-656564454649}") return SegmentKind::Oem;
    if (!gpt.empty()) return SegmentKind::Primary;
    if (mbr == 5 || mbr == 15) return SegmentKind::Extended;
    return SegmentKind::Primary;
}

Disk readDisk(const WmiObject& mo) {
    Disk d;
    const auto bus = mo.getU16(L"BusType");
    const auto style = mo.getU16(L"PartitionStyle");
    d.number = mo.getInt(L"Number");
    d.friendlyName = mo.getString(L"FriendlyName");
    d.model = mo.getString(L"Model");
    if (d.model.empty()) d.model = d.friendlyName;
    if (d.friendlyName.empty()) d.friendlyName = L"Disk " + std::to_wstring(d.number);
    d.serial = mo.getString(L"SerialNumber");
    d.busType = busName(bus);
    d.size = mo.getU64(L"Size");
    d.allocated = mo.getU64(L"AllocatedSize");
    d.style = style == 1 ? PartitionStyle::Mbr : style == 2 ? PartitionStyle::Gpt : PartitionStyle::Unknown;
    d.boot = mo.getBool(L"IsBoot");
    d.system = mo.getBool(L"IsSystem");
    d.offline = mo.getBool(L"IsOffline");
    d.readOnly = mo.getBool(L"IsReadOnly");
    d.removable = bus == 7 || bus == 12 || bus == 13;
    d.virtualDisk = bus == 14 || bus == 15;
    d.status = d.offline ? L"Offline" : L"Online";
    d.logicalSector = mo.getU32(L"LogicalSectorSize");
    if (d.logicalSector == 0) d.logicalSector = 512;
    d.physicalSector = mo.getU32(L"PhysicalSectorSize");
    d.location = mo.getString(L"Location");
    d.uniqueId = mo.getString(L"UniqueId");
    return d;
}

Segment readPartition(const WmiObject& mo) {
    Segment s;
    s.diskNumber = mo.getInt(L"DiskNumber");
    s.partitionNumber = mo.getInt(L"PartitionNumber");
    s.offset = mo.getU64(L"Offset");
    s.size = mo.getU64(L"Size");
    s.letter = mo.getLetter(L"DriveLetter");
    s.gptType = mo.getString(L"GptType");
    s.mbrType = mo.getU16(L"MbrType");
    s.kind = classifyGpt(s.gptType, s.mbrType);
    s.boot = mo.getBool(L"IsBoot");
    s.system = mo.getBool(L"IsSystem");
    s.active = mo.getBool(L"IsActive");
    s.hidden = mo.getBool(L"IsHidden");
    s.readOnly = mo.getBool(L"IsReadOnly");
    s.offline = mo.getBool(L"IsOffline");
    s.status = L"Healthy";
    if (s.hidden) s.status = L"Hidden";
    if (s.offline) s.status = L"Offline";
    if (s.boot) s.status = L"Boot";
    if (s.system && !s.boot) s.status = L"System";
    const auto access = mo.getStrings(L"AccessPaths");
    s.accessPaths = join(access, L"; ");
    for (const auto& a : access) {
        if (a.rfind(L"\\\\?\\Volume{", 0) == 0) {
            s.volumePath = a;
            break;
        }
    }
    return s;
}

struct VolumeInfo {
    wchar_t letter = 0;
    std::wstring path;
    std::wstring label;
    std::wstring fileSystem;
    std::uint64_t sizeRemaining = 0;
};

void attachVolumes(std::vector<Segment>& parts, const std::vector<VolumeInfo>& volumes) {
    for (auto& p : parts) {
        for (const auto& v : volumes) {
            const bool letterMatch = v.letter && v.letter == p.letter;
            const bool pathMatch = !v.path.empty() && !p.volumePath.empty() &&
                                   _wcsnicmp(v.path.c_str(), p.volumePath.c_str(), v.path.size()) == 0;
            if (letterMatch || pathMatch) {
                p.label = v.label;
                p.fileSystem = v.fileSystem;
                p.sizeRemaining = v.sizeRemaining;
                if (!p.letter) p.letter = v.letter;
                break;
            }
        }
    }
}

bool isLogicalMbr(const std::vector<Segment>& parts, const Segment& candidate) {
    for (const auto& ext : parts) {
        if (ext.mbrType != 5 && ext.mbrType != 15) continue;
        if (candidate.offset > ext.offset && candidate.offset + candidate.size <= ext.offset + ext.size)
            return true;
    }
    return false;
}

void classifyMbr(Disk& disk, std::vector<Segment>& parts) {
    if (disk.style != PartitionStyle::Mbr) return;
    for (auto& p : parts) {
        if (p.kind == SegmentKind::Efi || p.kind == SegmentKind::Recovery ||
            p.kind == SegmentKind::MicrosoftReserved)
            continue;
        if (p.mbrType == 5 || p.mbrType == 15) p.kind = SegmentKind::Extended;
        else p.kind = isLogicalMbr(parts, p) ? SegmentKind::Logical : SegmentKind::Primary;
    }
}

Segment unallocated(int diskNumber, std::uint64_t offset, std::uint64_t size) {
    Segment s;
    s.diskNumber = diskNumber;
    s.unallocated = true;
    s.offset = offset;
    s.size = size;
    s.kind = SegmentKind::Unallocated;
    s.status = L"Unallocated";
    s.sizeRemaining = size;
    return s;
}

std::vector<Segment> buildSegments(const Disk& disk, std::vector<Segment> parts, const DisplaySettings& display) {
    std::sort(parts.begin(), parts.end(), [](const Segment& a, const Segment& b) { return a.offset < b.offset; });
    std::vector<Segment> result;
    std::uint64_t cursor = 0;
    for (auto& part : parts) {
        if (!display.showSystemReserved &&
            (part.kind == SegmentKind::MicrosoftReserved || part.kind == SegmentKind::Efi))
            continue;
        if (part.offset > cursor + kMinUnallocated && display.showUnallocated)
            result.push_back(unallocated(disk.number, cursor, part.offset - cursor));
        result.push_back(part);
        const auto end = part.offset + part.size;
        if (end > cursor) cursor = end;
    }
    if (display.showUnallocated && disk.size > cursor + kMinUnallocated)
        result.push_back(unallocated(disk.number, cursor, disk.size - cursor));
    if (result.empty() && display.showUnallocated && disk.size > 0 && disk.style == PartitionStyle::Unknown)
        result.push_back(unallocated(disk.number, 0, disk.size));
    return result;
}

DiskLayout queryWin32(const DisplaySettings& display, Log& log) {
    DiskLayout layout;
    Wmi wmi;
    std::wstring err;
    if (!wmi.connect(L"ROOT\\CIMV2", &err)) {
        log.error(err);
        return layout;
    }
    for (auto& mo : wmi.query(L"SELECT Index, Caption, Size, InterfaceType, SerialNumber, Status FROM Win32_DiskDrive")) {
        Disk d;
        d.number = mo.getInt(L"Index");
        d.friendlyName = mo.getString(L"Caption");
        d.model = d.friendlyName;
        d.serial = mo.getString(L"SerialNumber");
        d.busType = mo.getString(L"InterfaceType");
        d.size = mo.getU64(L"Size");
        d.status = mo.getString(L"Status");
        d.removable = lower(d.busType).find(L"usb") != std::wstring::npos;
        if (!display.showRemovable && d.removable) continue;
        layout.disks.push_back(std::move(d));
    }
    std::vector<Segment> parts;
    for (auto& mo : wmi.query(L"SELECT DiskIndex, Index, Size, StartingOffset, Type, Bootable, PrimaryPartition FROM Win32_DiskPartition")) {
        Segment s;
        s.diskNumber = mo.getInt(L"DiskIndex");
        s.partitionNumber = mo.getInt(L"Index");
        s.size = mo.getU64(L"Size");
        s.offset = mo.getU64(L"StartingOffset");
        s.kind = mo.getBool(L"PrimaryPartition") ? SegmentKind::Primary : SegmentKind::Logical;
        s.active = mo.getBool(L"Bootable");
        s.status = mo.getString(L"Type");
        parts.push_back(std::move(s));
    }
    for (auto& disk : layout.disks) {
        std::vector<Segment> diskParts;
        for (const auto& p : parts)
            if (p.diskNumber == disk.number) diskParts.push_back(p);
        disk.segments = buildSegments(disk, diskParts, display);
    }
    return layout;
}

} // namespace

DiskLayout queryInventory(const DisplaySettings& display, Log& log) {
    Wmi wmi;
    std::wstring err;
    if (!wmi.connect(kStorageNs, &err)) {
        log.warn(L"Storage namespace query failed: " + err + L" — falling back to Win32.");
        return queryWin32(display, log);
    }

    DiskLayout layout;
    try {
        std::vector<Disk> disks;
        for (auto& mo : wmi.query(L"SELECT * FROM MSFT_Disk")) {
            auto disk = readDisk(mo);
            if (!display.showRemovable && disk.removable) continue;
            if (!display.showVirtual && disk.virtualDisk) continue;
            disks.push_back(std::move(disk));
        }
        std::vector<Segment> parts;
        for (auto& mo : wmi.query(L"SELECT * FROM MSFT_Partition"))
            parts.push_back(readPartition(mo));

        std::vector<VolumeInfo> volumes;
        for (auto& mo : wmi.query(L"SELECT * FROM MSFT_Volume")) {
            VolumeInfo v;
            v.letter = mo.getLetter(L"DriveLetter");
            v.path = mo.getString(L"Path");
            v.label = mo.getString(L"FileSystemLabel");
            v.fileSystem = mo.getString(L"FileSystem");
            v.sizeRemaining = mo.getU64(L"SizeRemaining");
            volumes.push_back(std::move(v));
        }
        attachVolumes(parts, volumes);

        for (auto& disk : disks) {
            std::vector<Segment> diskParts;
            for (const auto& p : parts)
                if (p.diskNumber == disk.number) diskParts.push_back(p);
            classifyMbr(disk, diskParts);
            disk.segments = buildSegments(disk, diskParts, display);
        }
        std::sort(disks.begin(), disks.end(), [](const Disk& a, const Disk& b) {
            if (a.optical != b.optical) return !a.optical && b.optical;
            return a.number < b.number;
        });
        layout.disks = std::move(disks);
        int partsCount = 0;
        for (const auto& d : layout.disks)
            for (const auto& s : d.segments)
                if (!s.unallocated) ++partsCount;
        log.info(L"Inventory: " + std::to_wstring(layout.disks.size()) + L" disk(s), " +
                 std::to_wstring(partsCount) + L" partition(s).");
        return layout;
    } catch (...) {
        log.warn(L"Storage namespace query failed — falling back to Win32.");
        return queryWin32(display, log);
    }
}

std::vector<wchar_t> availableLetters(const DiskLayout& layout) {
    bool used[26]{};
    for (const auto& d : layout.disks)
        for (const auto& s : d.segments)
            if (s.letter >= L'A' && s.letter <= L'Z')
                used[s.letter - L'A'] = true;
    std::vector<wchar_t> out;
    for (wchar_t c = L'D'; c <= L'Z'; ++c)
        if (!used[c - L'A']) out.push_back(c);
    return out;
}

} // namespace pm
