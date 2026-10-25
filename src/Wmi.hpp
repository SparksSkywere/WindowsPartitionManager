#pragma once

#include <windows.h>
#include <wbemidl.h>
#include <string>
#include <vector>
#include <functional>

namespace pm {

class WmiObject {
public:
    IWbemClassObject* raw = nullptr;
    WmiObject() = default;
    explicit WmiObject(IWbemClassObject* obj) : raw(obj) {}
    WmiObject(const WmiObject&) = delete;
    WmiObject& operator=(const WmiObject&) = delete;
    WmiObject(WmiObject&& other) noexcept : raw(other.raw) { other.raw = nullptr; }
    WmiObject& operator=(WmiObject&& other) noexcept {
        if (this != &other) {
            reset();
            raw = other.raw;
            other.raw = nullptr;
        }
        return *this;
    }
    ~WmiObject() { reset(); }
    void reset() {
        if (raw) {
            raw->Release();
            raw = nullptr;
        }
    }
    bool valid() const { return raw != nullptr; }

    std::wstring getString(const wchar_t* name) const;
    bool getBool(const wchar_t* name) const;
    int getInt(const wchar_t* name) const;
    std::uint16_t getU16(const wchar_t* name) const;
    std::uint32_t getU32(const wchar_t* name) const;
    std::uint64_t getU64(const wchar_t* name) const;
    wchar_t getLetter(const wchar_t* name) const;
    std::vector<std::wstring> getStrings(const wchar_t* name) const;
    std::wstring path() const;
};

class Wmi {
public:
    Wmi() = default;
    ~Wmi();
    bool connect(const wchar_t* ns, std::wstring* error = nullptr);
    std::vector<WmiObject> query(const wchar_t* wql);
    WmiObject first(const wchar_t* wql);
    bool invoke(const WmiObject& obj, const wchar_t* method,
                const std::function<void(IWbemClassObject*)>& fill,
                std::uint32_t* returnCode, std::wstring* error);
    bool putInstance(const WmiObject& obj, std::wstring* error);

private:
    IWbemServices* svc_ = nullptr;
};

} // namespace pm
