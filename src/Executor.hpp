#pragma once

#include "Log.hpp"
#include "Models.hpp"

#include <atomic>
#include <functional>

struct IWbemClassObject;

namespace pm {

class Storage {
public:
    explicit Storage(Log& log) : log_(log) {}
    OperationResult execute(const PendingOperation& op, std::atomic<bool>* cancel,
                            const std::function<void(int)>& progress);
    OperationResult checkVolume(wchar_t letter, bool fix);
    OperationResult setOnline(int diskNumber, bool online);
    OperationResult clearDisk(int diskNumber);
    OperationResult initializeDisk(int diskNumber, PartitionStyle style);
    OperationResult resizePartition(int diskNumber, std::uint64_t offset, std::uint64_t newSize);
    OperationResult setActive(int diskNumber, std::uint64_t offset);
    OperationResult setHidden(int diskNumber, std::uint64_t offset, bool hidden);
    OperationResult createPartitionRaw(int diskNumber, std::uint64_t offset, std::uint64_t size,
                                       const std::wstring& gptType, std::uint16_t mbrType);

private:
    static void putU64(IWbemClassObject* obj, const wchar_t* name, std::uint64_t value);
    static void putU32(IWbemClassObject* obj, const wchar_t* name, std::uint32_t value);
    static void putU16(IWbemClassObject* obj, const wchar_t* name, std::uint16_t value);
    static void putBool(IWbemClassObject* obj, const wchar_t* name, bool value);
    static void putStr(IWbemClassObject* obj, const wchar_t* name, const std::wstring& value);
    OperationResult ok(const std::wstring& message);
    OperationResult fail(const std::wstring& message);
    Log& log_;
};

} // namespace pm
