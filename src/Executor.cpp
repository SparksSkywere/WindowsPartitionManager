#include "Executor.hpp"
#include "Clone.hpp"
#include "Inventory.hpp"
#include "Util.hpp"
#include "Wmi.hpp"

#include <comdef.h>

namespace pm {
namespace {

WmiObject findDisk(Wmi& wmi, int number) {
    const auto q = L"SELECT * FROM MSFT_Disk WHERE Number = " + std::to_wstring(number);
    return wmi.first(q.c_str());
}

WmiObject findPartition(Wmi& wmi, int diskNumber, std::uint64_t offset) {
    const auto q = L"SELECT * FROM MSFT_Partition WHERE DiskNumber = " + std::to_wstring(diskNumber);
    WmiObject best;
    std::uint64_t bestDelta = ~0ULL;
    for (auto& mo : wmi.query(q.c_str())) {
        const auto off = mo.getU64(L"Offset");
        const auto delta = off > offset ? off - offset : offset - off;
        if (delta < bestDelta) {
            bestDelta = delta;
            best = std::move(mo);
        }
    }
    if (best.valid() && bestDelta > 16ULL * 1024ULL * 1024ULL) return {};
    return best;
}

WmiObject findVolume(Wmi& wmi, const PendingOperation& op) {
    if (op.letter) {
        for (auto& mo : wmi.query(L"SELECT * FROM MSFT_Volume")) {
            if (mo.getLetter(L"DriveLetter") == towupper(op.letter)) return std::move(mo);
        }
    }
    auto part = findPartition(wmi, op.diskNumber, op.offset);
    if (!part.valid()) return {};
    for (const auto& path : part.getStrings(L"AccessPaths")) {
        if (path.rfind(L"\\\\?\\Volume{", 0) == 0) {
            for (auto& mo : wmi.query(L"SELECT * FROM MSFT_Volume")) {
                const auto vpath = mo.getString(L"Path");
                if (!vpath.empty() && _wcsnicmp(path.c_str(), vpath.c_str(), vpath.size()) == 0)
                    return std::move(mo);
            }
        }
    }
    return {};
}

std::wstring describe(std::uint32_t code) {
    switch (code) {
    case 6:
    case 4097: return L"Access denied. Run Partition Manager as administrator.";
    case 42006: return L"There is not enough usable space.";
    case 42008: return L"The partition is in use and cannot be modified.";
    case 42010: return L"The volume cannot be formatted with that file system.";
    default: return L"Storage error " + std::to_wstring(code) + L".";
    }
}

} // namespace

void Storage::putU64(IWbemClassObject* obj, const wchar_t* name, std::uint64_t value) {
    VARIANT v;
    VariantInit(&v);
    v.vt = VT_UI8;
    v.ullVal = value;
    obj->Put(name, 0, &v, 0);
    VariantClear(&v);
}

void Storage::putU32(IWbemClassObject* obj, const wchar_t* name, std::uint32_t value) {
    VARIANT v;
    VariantInit(&v);
    v.vt = VT_UI4;
    v.ulVal = value;
    obj->Put(name, 0, &v, 0);
    VariantClear(&v);
}

void Storage::putU16(IWbemClassObject* obj, const wchar_t* name, std::uint16_t value) {
    VARIANT v;
    VariantInit(&v);
    v.vt = VT_UI2;
    v.uiVal = value;
    obj->Put(name, 0, &v, 0);
    VariantClear(&v);
}

void Storage::putBool(IWbemClassObject* obj, const wchar_t* name, bool value) {
    VARIANT v;
    VariantInit(&v);
    v.vt = VT_BOOL;
    v.boolVal = value ? VARIANT_TRUE : VARIANT_FALSE;
    obj->Put(name, 0, &v, 0);
    VariantClear(&v);
}

void Storage::putStr(IWbemClassObject* obj, const wchar_t* name, const std::wstring& value) {
    VARIANT v;
    VariantInit(&v);
    v.vt = VT_BSTR;
    v.bstrVal = SysAllocString(value.c_str());
    obj->Put(name, 0, &v, 0);
    VariantClear(&v);
}

OperationResult Storage::ok(const std::wstring& message) {
    return {true, message, 0};
}

OperationResult Storage::fail(const std::wstring& message) {
    log_.error(message);
    return {false, message, 0};
}

OperationResult Storage::setOnline(int diskNumber, bool online) {
    std::wstring err;
    Wmi wmi;
    if (!wmi.connect(kStorageNs, &err)) return fail(err);
    auto disk = findDisk(wmi, diskNumber);
    if (!disk.valid()) return fail(L"Disk " + std::to_wstring(diskNumber) + L" not found.");
    std::uint32_t code = 0;
    const wchar_t* method = online ? L"Online" : L"Offline";
    if (!wmi.invoke(disk, method, {}, &code, &err))
        return {false, err.empty() ? describe(code) : err, code};
    log_.success(std::wstring(method) + L" succeeded.");
    return ok(std::wstring(method) + L" succeeded.");
}

OperationResult Storage::clearDisk(int diskNumber) {
    std::wstring err;
    Wmi wmi;
    if (!wmi.connect(kStorageNs, &err)) return fail(err);
    auto disk = findDisk(wmi, diskNumber);
    if (!disk.valid()) return fail(L"Disk " + std::to_wstring(diskNumber) + L" not found.");
    std::uint32_t code = 0;
    const bool okCall = wmi.invoke(disk, L"Clear", [&](IWbemClassObject* in) {
        putBool(in, L"RemoveData", true);
        putBool(in, L"RemoveOEM", true);
        putBool(in, L"ZeroOutEntireDisk", false);
    }, &code, &err);
    if (!okCall) return {false, err.empty() ? describe(code) : err, code};
    return ok(L"Clear succeeded.");
}

OperationResult Storage::initializeDisk(int diskNumber, PartitionStyle style) {
    std::wstring err;
    Wmi wmi;
    if (!wmi.connect(kStorageNs, &err)) return fail(err);
    auto disk = findDisk(wmi, diskNumber);
    if (!disk.valid()) return fail(L"Disk " + std::to_wstring(diskNumber) + L" not found.");
    std::uint32_t code = 0;
    const bool okCall = wmi.invoke(disk, L"Initialize", [&](IWbemClassObject* in) {
        putU16(in, L"PartitionStyle", style == PartitionStyle::Mbr ? 1 : 2);
    }, &code, &err);
    if (!okCall) return {false, err.empty() ? describe(code) : err, code};
    return ok(L"Initialize succeeded.");
}

OperationResult Storage::resizePartition(int diskNumber, std::uint64_t offset, std::uint64_t newSize) {
    std::wstring err;
    Wmi wmi;
    if (!wmi.connect(kStorageNs, &err)) return fail(err);
    auto part = findPartition(wmi, diskNumber, offset);
    if (!part.valid()) return fail(L"Partition not found.");
    std::uint32_t code = 0;
    const bool okCall = wmi.invoke(part, L"Resize", [&](IWbemClassObject* in) {
        putU64(in, L"Size", newSize);
    }, &code, &err);
    if (!okCall) return {false, err.empty() ? describe(code) : err, code};
    return ok(L"Resize succeeded.");
}

OperationResult Storage::setActive(int diskNumber, std::uint64_t offset) {
    std::wstring err;
    Wmi wmi;
    if (!wmi.connect(kStorageNs, &err)) return fail(err);
    auto part = findPartition(wmi, diskNumber, offset);
    if (!part.valid()) return fail(L"Partition not found.");
    std::uint32_t code = 0;
    const bool okCall = wmi.invoke(part, L"SetAttributes", [&](IWbemClassObject* in) {
        putBool(in, L"IsActive", true);
    }, &code, &err);
    if (!okCall) return {false, err.empty() ? describe(code) : err, code};
    return ok(L"Set active succeeded.");
}

OperationResult Storage::setHidden(int diskNumber, std::uint64_t offset, bool hidden) {
    std::wstring err;
    Wmi wmi;
    if (!wmi.connect(kStorageNs, &err)) return fail(err);
    auto part = findPartition(wmi, diskNumber, offset);
    if (!part.valid()) return fail(L"Partition not found.");
    std::uint32_t code = 0;
    const bool okCall = wmi.invoke(part, L"SetAttributes", [&](IWbemClassObject* in) {
        putBool(in, L"IsHidden", hidden);
    }, &code, &err);
    if (!okCall) return {false, err.empty() ? describe(code) : err, code};
    if (hidden) {
        const auto letter = part.getLetter(L"DriveLetter");
        if (letter) {
            wmi.invoke(part, L"RemoveAccessPath", [&](IWbemClassObject* in) {
                putStr(in, L"AccessPath", std::wstring(1, letter) + L":");
            }, &code, &err);
        }
    }
    return ok(L"Set hidden succeeded.");
}

OperationResult Storage::createPartitionRaw(int diskNumber, std::uint64_t offset, std::uint64_t size,
                                           const std::wstring& gptType, std::uint16_t mbrType) {
    std::wstring err;
    Wmi wmi;
    if (!wmi.connect(kStorageNs, &err)) return fail(err);
    auto disk = findDisk(wmi, diskNumber);
    if (!disk.valid()) return fail(L"Disk " + std::to_wstring(diskNumber) + L" not found.");
    std::uint32_t code = 0;
    const auto style = disk.getU16(L"PartitionStyle");
    const bool okCall = wmi.invoke(disk, L"CreatePartition", [&](IWbemClassObject* in) {
        putU64(in, L"Size", size);
        putBool(in, L"UseMaximumSize", false);
        putU64(in, L"Offset", offset);
        putU32(in, L"Alignment", 1024 * 1024);
        putBool(in, L"AssignDriveLetter", false);
        if (style == 1) putU16(in, L"MbrType", mbrType ? mbrType : 7);
        else putStr(in, L"GptType", gptType.empty() ? L"{ebd0a0a2-b9e5-4433-87c0-68b6b72699c7}" : gptType);
    }, &code, &err);
    if (!okCall) return {false, err.empty() ? describe(code) : err, code};
    return ok(L"CreatePartition succeeded.");
}

OperationResult Storage::checkVolume(wchar_t letter, bool fix) {
    std::wstring cmd = L"chkdsk.exe ";
    cmd += letter;
    cmd += L": ";
    cmd += fix ? L"/f /x" : L"/scan";
    log_.info(L"Running " + cmd);
    STARTUPINFOW si{sizeof(si)};
    PROCESS_INFORMATION pi{};
    std::wstring mutableCmd = cmd;
    if (!CreateProcessW(nullptr, mutableCmd.data(), nullptr, nullptr, FALSE, CREATE_NO_WINDOW, nullptr, nullptr, &si, &pi))
        return fail(L"Could not start chkdsk.");
    WaitForSingleObject(pi.hProcess, INFINITE);
    DWORD code = 1;
    GetExitCodeProcess(pi.hProcess, &code);
    CloseHandle(pi.hThread);
    CloseHandle(pi.hProcess);
    if (code == 0 || code == 1) return ok(L"chkdsk finished.");
    return fail(L"chkdsk exited " + std::to_wstring(code));
}

OperationResult Storage::execute(const PendingOperation& op, std::atomic<bool>* cancel,
                                const std::function<void(int)>& progress) {
    if (cancel && cancel->load()) return fail(L"Cancelled.");
    log_.info(L"Apply: " + op.description);
    try {
        switch (op.kind) {
        case OperationKind::CloneDisk:
            return cloneDisk(op.cloneDisk, *this, log_, cancel, progress);
        case OperationKind::ClonePartition:
            return clonePartition(op.clonePart, *this, log_, cancel, progress);
        case OperationKind::OfflineDisk:
            return setOnline(op.diskNumber, false);
        case OperationKind::OnlineDisk:
            return setOnline(op.diskNumber, true);
        case OperationKind::DeleteAllPartitions:
            return clearDisk(op.diskNumber);
        case OperationKind::InitializeDisk:
            return initializeDisk(op.diskNumber, op.targetStyle);
        case OperationKind::ResizePartition:
            return resizePartition(op.diskNumber, op.offset, op.newSize);
        case OperationKind::SetActive:
            return setActive(op.diskNumber, op.offset);
        case OperationKind::HidePartition:
            return setHidden(op.diskNumber, op.offset, true);
        case OperationKind::UnhidePartition:
            return setHidden(op.diskNumber, op.offset, false);
        default:
            break;
        }

        std::wstring err;
        Wmi wmi;
        if (!wmi.connect(kStorageNs, &err)) return fail(err);

        if (op.kind == OperationKind::CreatePartition) {
            auto disk = findDisk(wmi, op.diskNumber);
            if (!disk.valid()) return fail(L"Disk not found.");
            std::uint32_t code = 0;
            const auto style = disk.getU16(L"PartitionStyle");
            const bool okCall = wmi.invoke(disk, L"CreatePartition", [&](IWbemClassObject* in) {
                putU64(in, L"Size", op.create.size);
                putBool(in, L"UseMaximumSize", false);
                putU64(in, L"Offset", op.create.offset);
                putU32(in, L"Alignment", 1024 * 1024);
                if (op.create.letter) {
                    putBool(in, L"AssignDriveLetter", true);
                    VARIANT v;
                    VariantInit(&v);
                    v.vt = VT_I2;
                    v.iVal = static_cast<SHORT>(op.create.letter);
                    in->Put(L"DriveLetter", 0, &v, 0);
                    VariantClear(&v);
                } else {
                    putBool(in, L"AssignDriveLetter", false);
                }
                if (style == 1) putU16(in, L"MbrType", 7);
                else putStr(in, L"GptType", L"{ebd0a0a2-b9e5-4433-87c0-68b6b72699c7}");
            }, &code, &err);
            if (!okCall) return {false, err.empty() ? describe(code) : err, code};
            if (!op.create.formatAfter) return ok(L"CreatePartition succeeded.");
            Sleep(800);
            PendingOperation formatOp = op;
            formatOp.kind = OperationKind::FormatPartition;
            formatOp.letter = op.create.letter;
            formatOp.format = {op.create.fileSystem, op.create.label, op.create.quick};
            return execute(formatOp, cancel, progress);
        }

        if (op.kind == OperationKind::DeletePartition) {
            auto part = findPartition(wmi, op.diskNumber, op.offset);
            if (!part.valid()) return fail(L"Partition not found.");
            std::uint32_t code = 0;
            if (!wmi.invoke(part, L"DeleteObject", {}, &code, &err))
                return {false, err.empty() ? describe(code) : err, code};
            return ok(L"DeleteObject succeeded.");
        }

        if (op.kind == OperationKind::FormatPartition) {
            auto volume = findVolume(wmi, op);
            if (!volume.valid()) return fail(L"Volume not found. The partition may not have a file system yet.");
            std::uint32_t code = 0;
            const bool okCall = wmi.invoke(volume, L"Format", [&](IWbemClassObject* in) {
                putStr(in, L"FileSystem", op.format.fileSystem);
                putBool(in, L"QuickFormat", op.format.quick);
                if (!op.format.label.empty()) putStr(in, L"FileSystemLabel", op.format.label);
                putBool(in, L"Force", true);
            }, &code, &err);
            if (!okCall) return {false, err.empty() ? describe(code) : err, code};
            return ok(L"Format succeeded.");
        }

        if (op.kind == OperationKind::ChangeDriveLetter) {
            auto part = findPartition(wmi, op.diskNumber, op.offset);
            if (!part.valid()) return fail(L"Partition not found.");
            const auto existing = part.getLetter(L"DriveLetter");
            std::uint32_t code = 0;
            if (existing) {
                if (!wmi.invoke(part, L"RemoveAccessPath", [&](IWbemClassObject* in) {
                    putStr(in, L"AccessPath", std::wstring(1, existing) + L":");
                }, &code, &err))
                    return {false, err.empty() ? describe(code) : err, code};
            }
            if (!op.letter) return ok(L"Drive letter removed.");
            const bool okCall = wmi.invoke(part, L"AddAccessPath", [&](IWbemClassObject* in) {
                putStr(in, L"AccessPath", std::wstring(1, op.letter) + L":");
                putBool(in, L"AssignDriveLetter", true);
            }, &code, &err);
            if (!okCall) return {false, err.empty() ? describe(code) : err, code};
            return ok(L"Drive letter updated.");
        }

        if (op.kind == OperationKind::ChangeLabel) {
            auto volume = findVolume(wmi, op);
            if (!volume.valid()) return fail(L"Volume not found.");
            VARIANT v;
            VariantInit(&v);
            v.vt = VT_BSTR;
            v.bstrVal = SysAllocString(op.label.c_str());
            const HRESULT hr = volume.raw->Put(L"FileSystemLabel", 0, &v, 0);
            VariantClear(&v);
            if (FAILED(hr) || !wmi.putInstance(volume, &err)) return fail(L"Label update failed.");
            return ok(L"Label updated.");
        }

        if (op.kind == OperationKind::ConvertPartitionStyle) {
            auto disk = findDisk(wmi, op.diskNumber);
            if (!disk.valid()) return fail(L"Disk not found.");
            std::uint32_t code = 0;
            const bool okCall = wmi.invoke(disk, L"ConvertStyle", [&](IWbemClassObject* in) {
                putU16(in, L"PartitionStyle", op.targetStyle == PartitionStyle::Mbr ? 1 : 2);
            }, &code, &err);
            if (!okCall) return {false, err.empty() ? describe(code) : err, code};
            return ok(L"ConvertStyle succeeded.");
        }

        return fail(L"Unsupported operation.");
    } catch (const std::exception& ex) {
        return fail(toWide(ex.what()));
    } catch (...) {
        return fail(L"Unexpected failure.");
    }
}

} // namespace pm
