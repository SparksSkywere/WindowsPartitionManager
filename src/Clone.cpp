#include "Clone.hpp"
#include "Preview.hpp"
#include "Util.hpp"

#include <windows.h>
#include <winioctl.h>

#include <algorithm>
#include <stdexcept>
#include <vector>

namespace pm {
namespace {

constexpr int kBlock = 4 * 1024 * 1024;
constexpr int kIoAlign = 4096;

struct Handle {
    HANDLE h = INVALID_HANDLE_VALUE;
    explicit Handle(HANDLE value) : h(value) {}
    Handle(const Handle&) = delete;
    Handle& operator=(const Handle&) = delete;
    ~Handle() {
        if (h != INVALID_HANDLE_VALUE) CloseHandle(h);
    }
};

HANDLE openPhysical(int disk, bool write) {
    const auto path = L"\\\\.\\PhysicalDrive" + std::to_wstring(disk);
    return CreateFileW(path.c_str(), GENERIC_READ | (write ? GENERIC_WRITE : 0),
                       FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr, OPEN_EXISTING,
                       FILE_FLAG_NO_BUFFERING, nullptr);
}

HANDLE openVolume(wchar_t letter, bool write) {
    const wchar_t path[] = {L'\\', L'\\', L'.', L'\\', letter, L':', 0};
    return CreateFileW(path, GENERIC_READ | (write ? GENERIC_WRITE : 0),
                       FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr, OPEN_EXISTING, 0, nullptr);
}

bool readAt(HANDLE h, std::uint64_t offset, void* buf, DWORD bytes) {
    OVERLAPPED ov{};
    ov.Offset = static_cast<DWORD>(offset);
    ov.OffsetHigh = static_cast<DWORD>(offset >> 32);
    DWORD got = 0;
    return ReadFile(h, buf, bytes, &got, &ov) && got == bytes;
}

bool writeAt(HANDLE h, std::uint64_t offset, const void* buf, DWORD bytes) {
    OVERLAPPED ov{};
    ov.Offset = static_cast<DWORD>(offset);
    ov.OffsetHigh = static_cast<DWORD>(offset >> 32);
    DWORD put = 0;
    return WriteFile(h, buf, bytes, &put, &ov) && put == bytes;
}

std::uint64_t alignCopyLength(std::uint64_t count, std::uint64_t destSize) {
    auto aligned = (count + kIoAlign - 1) / kIoAlign * kIoAlign;
    if (aligned > destSize) aligned = destSize / kIoAlign * kIoAlign;
    return aligned;
}

OperationResult fail(const std::wstring& m) { return {false, m, 0}; }
OperationResult ok(const std::wstring& m) { return {true, m, 0}; }

void copyRange(HANDLE src, HANDLE dest, std::uint64_t srcOff, std::uint64_t destOff, std::uint64_t count,
               std::atomic<bool>* cancel, const std::function<void(int)>& progress,
               std::uint64_t already, std::uint64_t total) {
    const auto aligned = count / kIoAlign * kIoAlign;
    if (aligned == 0) return;
    void* a = VirtualAlloc(nullptr, kBlock, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
    if (!a) throw std::runtime_error("Out of memory");
    auto remaining = aligned;
    auto s = srcOff;
    auto d = destOff;
    while (remaining > 0) {
        if (cancel && cancel->load()) {
            VirtualFree(a, 0, MEM_RELEASE);
            throw std::runtime_error("Cancelled");
        }
        const auto n = static_cast<DWORD>((std::min<std::uint64_t>)(kBlock, remaining));
        if (!readAt(src, s, a, n) || !writeAt(dest, d, a, n)) {
            VirtualFree(a, 0, MEM_RELEASE);
            throw std::runtime_error("Disk copy failed");
        }
        remaining -= n;
        s += n;
        d += n;
        already += n;
        if (progress && total)
            progress(static_cast<int>((std::min)(99.0, already * 100.0 / total)));
    }
    VirtualFree(a, 0, MEM_RELEASE);
}

bool copyUsed(HANDLE src, HANDLE dest, const PlannedSlice& slice, wchar_t letter,
              std::atomic<bool>* cancel, const std::function<void(int)>& progress,
              std::uint64_t already, std::uint64_t total) {
    Handle vol(openVolume(letter, false));
    if (vol.h == INVALID_HANDLE_VALUE) return false;
    DWORD spc = 0, bps = 0, freeClusters = 0, totalClusters = 0;
    const wchar_t root[] = {letter, L':', L'\\', 0};
    if (!GetDiskFreeSpaceW(root, &spc, &bps, &freeClusters, &totalClusters) || spc == 0 || bps == 0)
        return false;
    const std::uint32_t cluster = spc * bps;

    STARTING_LCN_INPUT_BUFFER start{};
    std::vector<std::uint8_t> output(1024 * 1024);
    std::vector<std::uint8_t> bits;
    std::uint64_t bitCount = 0;
    std::uint64_t lcn = 0;
    while (true) {
        start.StartingLcn.QuadPart = static_cast<LONGLONG>(lcn);
        DWORD returned = 0;
        if (!DeviceIoControl(vol.h, FSCTL_GET_VOLUME_BITMAP, &start, sizeof(start),
                             output.data(), static_cast<DWORD>(output.size()), &returned, nullptr) ||
            returned < 16)
            break;
        auto* bmp = reinterpret_cast<VOLUME_BITMAP_BUFFER*>(output.data());
        const auto chunk = static_cast<std::uint64_t>(bmp->BitmapSize.QuadPart);
        if (chunk == 0) break;
        auto byteCount = static_cast<size_t>((chunk + 7) / 8);
        if (16 + byteCount > returned) byteCount = returned - 16;
        bits.insert(bits.end(), output.begin() + 16, output.begin() + 16 + static_cast<std::ptrdiff_t>(byteCount));
        bitCount += chunk;
        lcn = static_cast<std::uint64_t>(bmp->StartingLcn.QuadPart) + chunk;
    }
    if (bitCount == 0) return false;

    auto bitSet = [&](std::uint64_t i) {
        const auto bi = static_cast<size_t>(i >> 3);
        if (bi >= bits.size()) return false;
        return (bits[bi] & (1u << (i & 7))) != 0;
    };

    std::uint64_t i = 0;
    while (i < bitCount) {
        while (i < bitCount && !bitSet(i)) ++i;
        if (i >= bitCount) break;
        const auto run = i;
        while (i < bitCount && bitSet(i)) ++i;
        const auto rel = run * cluster;
        auto take = (i - run) * cluster;
        if (rel >= slice.source.size) break;
        if (rel + take > slice.source.size) take = slice.source.size - rel;
        if (take == 0) continue;
        copyRange(src, dest, slice.source.offset + rel, slice.destOffset + rel, take,
                  cancel, progress, already + rel, total);
    }
    return true;
}

void applyFlags(Storage& storage, int destDisk, const PlannedSlice& slice) {
    if (slice.source.active) storage.setActive(destDisk, slice.destOffset);
    if (slice.source.hidden) storage.setHidden(destDisk, slice.destOffset, true);
}

} // namespace

OperationResult cloneDisk(const CloneDiskParams& p, Storage& storage, Log& log,
                          std::atomic<bool>* cancel, const std::function<void(int)>& progress) {
    if (p.sourceDisk == p.destDisk) return fail(L"Source and destination must be different disks.");
    std::vector<PlannedSlice> plan;
    std::wstring error;
    if (!tryPlanDisk(p, plan, error)) return fail(error);

    const auto lastEnd = lastAllocatedEnd(p.sourceSlices);
    const bool rawWhole = p.mode == CloneCopyMode::AllSectors && !p.alignToMegabyte && p.destSize >= lastEnd;
    log.info(L"Clone disk " + std::to_wstring(p.sourceDisk) + L" → " + std::to_wstring(p.destDisk));
    auto online = storage.setOnline(p.destDisk, true);
    if (!online.success) return online;

    if (rawWhole) {
        auto offline = storage.setOnline(p.destDisk, false);
        if (!offline.success) log.info(L"Could not take the destination offline; writing while online.");
        try {
            Handle src(openPhysical(p.sourceDisk, false));
            Handle dest(openPhysical(p.destDisk, true));
            if (src.h == INVALID_HANDLE_VALUE || dest.h == INVALID_HANDLE_VALUE)
                throw std::runtime_error("Cannot open physical disk");
            const auto copyBytes = alignCopyLength(lastEnd, p.destSize);
            copyRange(src.h, dest.h, 0, 0, copyBytes, cancel, progress, 0, copyBytes);
        } catch (const std::exception& ex) {
            storage.setOnline(p.destDisk, true);
            return fail(toWide(ex.what()));
        }
        auto back = storage.setOnline(p.destDisk, true);
        if (!back.success) return back;
        for (const auto& slice : plan) {
            if (slice.destSize > slice.source.size)
                storage.resizePartition(p.destDisk, slice.destOffset, slice.destSize);
        }
        if (progress) progress(100);
        return ok(L"Disk clone finished.");
    }

    auto cleared = storage.clearDisk(p.destDisk);
    if (!cleared.success) log.info(L"Clear skipped: " + cleared.message);
    auto initialized = storage.initializeDisk(p.destDisk, p.style);
    if (!initialized.success) return initialized;
    for (const auto& slice : plan) {
        auto created = storage.createPartitionRaw(p.destDisk, slice.destOffset, slice.source.size,
                                                  gptTypeFor(slice.source), mbrTypeFor(slice.source));
        if (!created.success) return created;
    }
    Sleep(800);
    storage.setOnline(p.destDisk, false);

    std::uint64_t total = 0;
    for (const auto& slice : plan)
        if (needsCopy(slice.source)) total += slice.source.size;
    std::uint64_t done = 0;
    try {
        Handle src(openPhysical(p.sourceDisk, false));
        Handle dest(openPhysical(p.destDisk, true));
        if (src.h == INVALID_HANDLE_VALUE || dest.h == INVALID_HANDLE_VALUE)
            throw std::runtime_error("Cannot open physical disk");
        for (const auto& slice : plan) {
            if (!needsCopy(slice.source)) continue;
            bool used = false;
            if (p.mode == CloneCopyMode::UsedData && slice.source.letter)
                used = copyUsed(src.h, dest.h, slice, slice.source.letter, cancel, progress, done, total);
            if (!used)
                copyRange(src.h, dest.h, slice.source.offset, slice.destOffset, slice.source.size,
                          cancel, progress, done, total);
            done += slice.source.size;
        }
    } catch (const std::exception& ex) {
        storage.setOnline(p.destDisk, true);
        return fail(toWide(ex.what()));
    }

    auto back = storage.setOnline(p.destDisk, true);
    if (!back.success) return back;
    for (const auto& slice : plan) {
        if (slice.destSize > slice.source.size)
            storage.resizePartition(p.destDisk, slice.destOffset, slice.destSize);
        applyFlags(storage, p.destDisk, slice);
    }
    if (progress) progress(100);
    return ok(L"Disk clone finished.");
}

OperationResult clonePartition(const ClonePartitionParams& p, Storage& storage, Log& log,
                               std::atomic<bool>* cancel, const std::function<void(int)>& progress) {
    PlannedSlice planned;
    std::wstring error;
    if (!tryPlanPartition(p, planned, error)) return fail(error);
    log.info(L"Clone partition on disk " + std::to_wstring(p.sourceDisk) + L" → disk " +
             std::to_wstring(p.destDisk));
    auto created = storage.createPartitionRaw(p.destDisk, planned.destOffset, planned.source.size,
                                              gptTypeFor(planned.source), mbrTypeFor(planned.source));
    if (!created.success) return created;
    Sleep(800);
    storage.setOnline(p.destDisk, false);
    try {
        const bool same = p.sourceDisk == p.destDisk;
        Handle dest(openPhysical(p.destDisk, true));
        Handle src(same ? INVALID_HANDLE_VALUE : openPhysical(p.sourceDisk, false));
        HANDLE srcH = same ? dest.h : src.h;
        if (dest.h == INVALID_HANDLE_VALUE || srcH == INVALID_HANDLE_VALUE)
            throw std::runtime_error("Cannot open physical disk");
        bool used = false;
        if (p.mode == CloneCopyMode::UsedData && planned.source.letter)
            used = copyUsed(srcH, dest.h, planned, planned.source.letter, cancel, progress, 0, planned.source.size);
        if (!used)
            copyRange(srcH, dest.h, planned.source.offset, planned.destOffset, planned.source.size,
                      cancel, progress, 0, planned.source.size);
    } catch (const std::exception& ex) {
        storage.setOnline(p.destDisk, true);
        return fail(toWide(ex.what()));
    }
    auto online = storage.setOnline(p.destDisk, true);
    if (!online.success) return online;
    if (planned.destSize > planned.source.size)
        storage.resizePartition(p.destDisk, planned.destOffset, planned.destSize);
    applyFlags(storage, p.destDisk, planned);
    if (progress) progress(100);
    return ok(L"Partition clone finished.");
}

} // namespace pm
