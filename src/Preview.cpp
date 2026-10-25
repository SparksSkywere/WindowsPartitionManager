#include "Preview.hpp"
#include "Inventory.hpp"
#include "Util.hpp"

#include <algorithm>

namespace pm {
namespace {

Segment makeUnallocated(int diskNumber, std::uint64_t offset, std::uint64_t size) {
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

void mergeUnallocated(Disk& disk) {
    std::sort(disk.segments.begin(), disk.segments.end(),
              [](const Segment& a, const Segment& b) { return a.offset < b.offset; });
    std::vector<Segment> merged;
    for (auto& seg : disk.segments) {
        if (!merged.empty() && merged.back().unallocated && seg.unallocated) {
            merged.back().size += seg.size;
            merged.back().sizeRemaining = merged.back().size;
            continue;
        }
        merged.push_back(seg);
    }
    disk.segments = std::move(merged);
}

Segment* findTarget(Disk& disk, const PendingOperation& op) {
    for (auto& s : disk.segments)
        if (!s.unallocated && s.offset == op.offset) return &s;
    for (auto& s : disk.segments)
        if (s.offset == op.offset) return &s;
    return nullptr;
}

Segment pendingFromSlice(int diskNumber, const PlannedSlice& slice) {
    Segment s;
    s.diskNumber = diskNumber;
    s.offset = slice.destOffset;
    s.size = slice.destSize;
    s.fileSystem = slice.source.fileSystem;
    s.sizeRemaining = slice.destSize;
    s.kind = slice.source.kind;
    s.active = slice.source.active;
    s.hidden = slice.source.hidden;
    s.status = L"Pending clone";
    s.pending = true;
    s.gptType = slice.source.gptType;
    s.mbrType = slice.source.mbrType;
    return s;
}

void fillGaps(Disk& disk) {
    std::sort(disk.segments.begin(), disk.segments.end(),
              [](const Segment& a, const Segment& b) { return a.offset < b.offset; });
    std::vector<Segment> filled;
    std::uint64_t cursor = 0;
    for (auto& seg : disk.segments) {
        if (seg.offset > cursor + kMinUnallocated) {
            Segment gap;
            gap.diskNumber = disk.number;
            gap.unallocated = true;
            gap.offset = cursor;
            gap.size = seg.offset - cursor;
            gap.kind = SegmentKind::Unallocated;
            gap.status = L"Unallocated";
            gap.sizeRemaining = gap.size;
            gap.pending = true;
            filled.push_back(gap);
        }
        filled.push_back(seg);
        cursor = seg.offset + seg.size;
    }
    if (disk.size > cursor + kMinUnallocated) {
        Segment gap;
        gap.diskNumber = disk.number;
        gap.unallocated = true;
        gap.offset = cursor;
        gap.size = disk.size - cursor;
        gap.kind = SegmentKind::Unallocated;
        gap.status = L"Unallocated";
        gap.sizeRemaining = gap.size;
        gap.pending = true;
        filled.push_back(gap);
    }
    disk.segments = std::move(filled);
}

int lastDataIndex(const std::vector<CloneSlice>& parts) {
    for (int i = static_cast<int>(parts.size()) - 1; i >= 0; --i)
        if (parts[i].kind == SegmentKind::Primary || parts[i].kind == SegmentKind::Logical)
            return i;
    return -1;
}

void applyCreate(DiskLayout& layout, const PendingOperation& op) {
    auto* disk = layout.findDisk(op.diskNumber);
    if (!disk) return;
    Segment* gap = nullptr;
    for (auto& s : disk->segments) {
        if (s.unallocated && s.offset <= op.create.offset &&
            s.offset + s.size >= op.create.offset + op.create.size) {
            gap = &s;
            break;
        }
    }
    if (!gap) return;
    Segment created;
    created.diskNumber = disk->number;
    created.offset = gap->offset;
    created.size = alignDown(op.create.size, kAlign);
    if (created.size < kAlign) return;
    created.letter = op.create.letter;
    created.label = op.create.label;
    created.fileSystem = op.create.formatAfter ? op.create.fileSystem : L"";
    created.sizeRemaining = op.create.formatAfter ? created.size : 0;
    created.kind = SegmentKind::Primary;
    created.status = L"Pending create";
    created.pending = true;
    const auto leftoverOff = created.offset + created.size;
    const auto leftover = (gap->offset + gap->size > leftoverOff) ? gap->offset + gap->size - leftoverOff : 0;
    std::vector<Segment> next;
    for (auto& s : disk->segments) {
        if (&s == gap) {
            next.push_back(created);
            if (leftover >= kMinUnallocated) {
                Segment rest;
                rest.diskNumber = disk->number;
                rest.unallocated = true;
                rest.offset = leftoverOff;
                rest.size = leftover;
                rest.kind = SegmentKind::Unallocated;
                rest.status = L"Unallocated";
                rest.sizeRemaining = leftover;
                next.push_back(rest);
            }
        } else {
            next.push_back(s);
        }
    }
    disk->segments = std::move(next);
    mergeUnallocated(*disk);
}

void applyDelete(DiskLayout& layout, const PendingOperation& op) {
    auto* disk = layout.findDisk(op.diskNumber);
    if (!disk) return;
    auto* seg = findTarget(*disk, op);
    if (!seg || seg->unallocated) return;
    seg->unallocated = true;
    seg->kind = SegmentKind::Unallocated;
    seg->letter = 0;
    seg->label.clear();
    seg->fileSystem.clear();
    seg->sizeRemaining = seg->size;
    seg->boot = seg->system = seg->active = seg->hidden = false;
    seg->status = L"Unallocated";
    seg->pending = true;
    mergeUnallocated(*disk);
}

void applyCloneDisk(DiskLayout& layout, const PendingOperation& op) {
    auto* dest = layout.findDisk(op.cloneDisk.destDisk);
    std::vector<PlannedSlice> plan;
    std::wstring error;
    if (!dest || !tryPlanDisk(op.cloneDisk, plan, error)) return;
    dest->style = op.cloneDisk.style;
    dest->status = L"Pending clone";
    dest->offline = false;
    dest->segments.clear();
    for (const auto& slice : plan)
        dest->segments.push_back(pendingFromSlice(dest->number, slice));
    fillGaps(*dest);
}

void applyClonePart(DiskLayout& layout, const PendingOperation& op) {
    auto* dest = layout.findDisk(op.clonePart.destDisk);
    PlannedSlice planned;
    std::wstring error;
    if (!dest || !tryPlanPartition(op.clonePart, planned, error)) return;
    Segment* gap = nullptr;
    for (auto& s : dest->segments) {
        if (s.unallocated && s.offset <= planned.destOffset &&
            s.offset + s.size >= planned.destOffset + planned.destSize) {
            gap = &s;
            break;
        }
    }
    if (!gap) return;
    auto created = pendingFromSlice(dest->number, planned);
    std::vector<Segment> next;
    for (auto& s : dest->segments) {
        if (&s == gap) {
            if (planned.destOffset > gap->offset && planned.destOffset - gap->offset >= kMinUnallocated) {
                Segment lead = *gap;
                lead.size = planned.destOffset - gap->offset;
                lead.sizeRemaining = lead.size;
                lead.pending = true;
                next.push_back(lead);
            }
            next.push_back(created);
            const auto leftoverOff = planned.destOffset + planned.destSize;
            const auto gapEnd = gap->offset + gap->size;
            if (gapEnd > leftoverOff && gapEnd - leftoverOff >= kMinUnallocated) {
                Segment rest = *gap;
                rest.offset = leftoverOff;
                rest.size = gapEnd - leftoverOff;
                rest.sizeRemaining = rest.size;
                rest.pending = true;
                next.push_back(rest);
            }
        } else {
            next.push_back(s);
        }
    }
    dest->segments = std::move(next);
    mergeUnallocated(*dest);
}

void applyOne(DiskLayout& layout, const PendingOperation& op) {
    switch (op.kind) {
    case OperationKind::CreatePartition:
        applyCreate(layout, op);
        break;
    case OperationKind::DeletePartition:
        applyDelete(layout, op);
        break;
    case OperationKind::FormatPartition:
        if (auto* disk = layout.findDisk(op.diskNumber))
            if (auto* seg = findTarget(*disk, op)) {
                seg->fileSystem = op.format.fileSystem;
                if (!op.format.label.empty()) seg->label = op.format.label;
                seg->status = L"Pending format";
                seg->pending = true;
            }
        break;
    case OperationKind::ResizePartition:
        if (auto* disk = layout.findDisk(op.diskNumber))
            if (auto* seg = findTarget(*disk, op)) {
                const auto newSize = alignDown(op.newSize, kAlign);
                if (newSize < seg->size) {
                    const auto freed = seg->size - newSize;
                    seg->size = newSize;
                    Segment gap;
                    gap.diskNumber = disk->number;
                    gap.unallocated = true;
                    gap.offset = seg->offset + seg->size;
                    gap.size = freed;
                    gap.kind = SegmentKind::Unallocated;
                    gap.status = L"Unallocated";
                    gap.sizeRemaining = freed;
                    gap.pending = true;
                    disk->segments.push_back(gap);
                } else if (newSize > seg->size) {
                    const auto extra = newSize - seg->size;
                    for (auto& after : disk->segments) {
                        if (after.unallocated && after.offset == seg->offset + seg->size && after.size >= extra) {
                            seg->size = newSize;
                            after.offset += extra;
                            after.size -= extra;
                            after.sizeRemaining = after.size;
                            break;
                        }
                    }
                }
                seg->status = L"Pending resize";
                seg->pending = true;
                mergeUnallocated(*disk);
            }
        break;
    case OperationKind::ChangeDriveLetter:
        if (auto* disk = layout.findDisk(op.diskNumber))
            if (auto* seg = findTarget(*disk, op)) {
                seg->letter = op.letter;
                seg->pending = true;
                seg->status = L"Pending letter";
            }
        break;
    case OperationKind::ChangeLabel:
        if (auto* disk = layout.findDisk(op.diskNumber))
            if (auto* seg = findTarget(*disk, op)) {
                seg->label = op.label;
                seg->pending = true;
                seg->status = L"Pending label";
            }
        break;
    case OperationKind::HidePartition:
    case OperationKind::UnhidePartition:
        if (auto* disk = layout.findDisk(op.diskNumber))
            if (auto* seg = findTarget(*disk, op)) {
                seg->hidden = op.kind == OperationKind::HidePartition;
                if (seg->hidden) seg->letter = 0;
                seg->status = seg->hidden ? L"Hidden" : L"Healthy";
                seg->pending = true;
            }
        break;
    case OperationKind::SetActive:
        if (auto* disk = layout.findDisk(op.diskNumber)) {
            for (auto& s : disk->segments) s.active = false;
            if (auto* seg = findTarget(*disk, op)) {
                seg->active = true;
                seg->pending = true;
                seg->status = L"Active";
            }
        }
        break;
    case OperationKind::InitializeDisk:
        if (auto* disk = layout.findDisk(op.diskNumber)) {
            disk->style = op.targetStyle;
            disk->status = L"Pending initialize";
        }
        break;
    case OperationKind::ConvertPartitionStyle:
        if (auto* disk = layout.findDisk(op.diskNumber)) {
            disk->style = op.targetStyle;
            disk->status = L"Pending convert";
        }
        break;
    case OperationKind::DeleteAllPartitions:
        if (auto* disk = layout.findDisk(op.diskNumber)) {
            disk->segments.clear();
            disk->segments.push_back(makeUnallocated(disk->number, 0, disk->size));
            disk->segments.back().pending = true;
        }
        break;
    case OperationKind::OfflineDisk:
    case OperationKind::OnlineDisk:
        if (auto* disk = layout.findDisk(op.diskNumber)) {
            disk->offline = op.kind == OperationKind::OfflineDisk;
            disk->status = disk->offline ? L"Offline" : L"Online";
        }
        break;
    case OperationKind::CloneDisk:
        applyCloneDisk(layout, op);
        break;
    case OperationKind::ClonePartition:
        applyClonePart(layout, op);
        break;
    }
}

} // namespace

bool tryPlanDisk(const CloneDiskParams& p, std::vector<PlannedSlice>& plan, std::wstring& error) {
    plan.clear();
    std::vector<CloneSlice> parts;
    for (const auto& s : p.sourceSlices)
        if (!s.unallocated && s.kind != SegmentKind::Extended) parts.push_back(s);
    std::sort(parts.begin(), parts.end(), [](const CloneSlice& a, const CloneSlice& b) { return a.offset < b.offset; });
    if (parts.empty()) {
        error = L"Source disk has no partitions to clone.";
        return false;
    }
    const auto reserve = p.style == PartitionStyle::Gpt ? kAlign : 0ULL;
    const auto usable = p.destSize > reserve ? p.destSize - reserve : p.destSize;
    const int expandIndex = p.expandLast ? lastDataIndex(parts) : -1;
    if (p.alignToMegabyte) {
        std::uint64_t cursor = kAlign;
        for (size_t i = 0; i < parts.size(); ++i) {
            const auto offset = alignUp(cursor, kAlign);
            auto size = parts[i].size;
            if (static_cast<int>(i) == expandIndex && usable > offset) {
                const auto grown = alignDown(usable - offset, kAlign);
                if (grown > size) size = grown;
            }
            if (offset + size > p.destSize) {
                error = L"Destination disk is too small for the source partitions.";
                return false;
            }
            plan.push_back({parts[i], offset, size});
            cursor = offset + size;
        }
    } else {
        for (const auto& part : parts) {
            if (part.offset + part.size > usable && expandIndex < 0) {
                error = L"Destination disk is too small for the source layout.";
                return false;
            }
            plan.push_back({part, part.offset, part.size});
        }
        if (expandIndex >= 0) {
            auto& last = plan[static_cast<size_t>(expandIndex)];
            if (usable > last.destOffset) {
                const auto grown = alignDown(usable - last.destOffset, kAlign);
                if (grown > last.source.size) last.destSize = grown;
            }
        }
    }
    return true;
}

bool tryPlanPartition(const ClonePartitionParams& p, PlannedSlice& plan, std::wstring& error) {
    if (p.source.unallocated || p.source.size == 0) {
        error = L"Source is not a partition.";
        return false;
    }
    auto offset = p.alignToMegabyte ? alignUp(p.destOffset, kAlign) : p.destOffset;
    if (offset < p.destOffset) offset = p.destOffset;
    const auto regionEnd = p.destOffset + p.destRegionSize;
    if (offset >= regionEnd) {
        error = L"Destination region is too small after alignment.";
        return false;
    }
    auto available = regionEnd - offset;
    if (p.alignToMegabyte) available = alignDown(available, kAlign);
    auto size = p.fillRegion ? available : p.source.size;
    if (p.alignToMegabyte) size = alignDown(size, kAlign);
    if (size < p.source.size || size == 0) {
        error = L"Destination region is smaller than the source partition.";
        return false;
    }
    plan = {p.source, offset, size};
    return true;
}

std::wstring gptTypeFor(const CloneSlice& s) {
    if (!s.gptType.empty()) return s.gptType;
    switch (s.kind) {
    case SegmentKind::Efi: return L"{c12a7328-f81f-11d2-ba4b-00a0c93ec93b}";
    case SegmentKind::MicrosoftReserved: return L"{e3c9e316-0b5c-4db8-817d-f92df00215ae}";
    case SegmentKind::Recovery: return L"{de94bba4-06d1-4d40-a16a-bfd50179d6ac}";
    default: return L"{ebd0a0a2-b9e5-4433-87c0-68b6b72699c7}";
    }
}

std::uint16_t mbrTypeFor(const CloneSlice& s) { return s.mbrType ? s.mbrType : 7; }

bool needsCopy(const CloneSlice& s) {
    return !s.unallocated && s.kind != SegmentKind::MicrosoftReserved && s.kind != SegmentKind::Extended;
}

std::uint64_t lastAllocatedEnd(const std::vector<CloneSlice>& slices) {
    std::uint64_t end = kAlign;
    for (const auto& s : slices)
        if (!s.unallocated) end = (std::max)(end, s.offset + s.size);
    return end;
}

DiskLayout applyPreview(const DiskLayout& live, const std::vector<PendingOperation>& ops) {
    auto working = live;
    for (const auto& op : ops) applyOne(working, op);
    return working;
}

} // namespace pm
