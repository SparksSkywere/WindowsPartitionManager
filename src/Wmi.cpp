#include "Wmi.hpp"

#include <comdef.h>

namespace pm {
namespace {

std::wstring variantString(const VARIANT& v) {
    if (v.vt == VT_BSTR && v.bstrVal) return v.bstrVal;
    if (v.vt == VT_NULL || v.vt == VT_EMPTY) return {};
    _variant_t conv(v);
    conv.ChangeType(VT_BSTR);
    return conv.bstrVal ? conv.bstrVal : L"";
}

} // namespace

std::wstring WmiObject::getString(const wchar_t* name) const {
    if (!raw) return {};
    VARIANT v;
    VariantInit(&v);
    if (FAILED(raw->Get(name, 0, &v, nullptr, nullptr))) return {};
    auto s = variantString(v);
    VariantClear(&v);
    return s;
}

bool WmiObject::getBool(const wchar_t* name) const {
    if (!raw) return false;
    VARIANT v;
    VariantInit(&v);
    if (FAILED(raw->Get(name, 0, &v, nullptr, nullptr))) return false;
    bool value = false;
    if (v.vt == VT_BOOL) value = v.boolVal != VARIANT_FALSE;
    else if (v.vt == VT_I4 || v.vt == VT_UI4) value = v.lVal != 0;
    VariantClear(&v);
    return value;
}

int WmiObject::getInt(const wchar_t* name) const {
    return static_cast<int>(getU64(name));
}

std::uint16_t WmiObject::getU16(const wchar_t* name) const {
    return static_cast<std::uint16_t>(getU64(name));
}

std::uint32_t WmiObject::getU32(const wchar_t* name) const {
    return static_cast<std::uint32_t>(getU64(name));
}

std::uint64_t WmiObject::getU64(const wchar_t* name) const {
    if (!raw) return 0;
    VARIANT v;
    VariantInit(&v);
    if (FAILED(raw->Get(name, 0, &v, nullptr, nullptr))) return 0;
    std::uint64_t value = 0;
    switch (v.vt) {
    case VT_I2: value = static_cast<std::uint64_t>(v.iVal); break;
    case VT_UI2: value = v.uiVal; break;
    case VT_I4: value = static_cast<std::uint64_t>(v.lVal); break;
    case VT_UI4: value = v.ulVal; break;
    case VT_I8: value = static_cast<std::uint64_t>(v.llVal); break;
    case VT_UI8: value = v.ullVal; break;
    case VT_BSTR:
        if (v.bstrVal) value = _wcstoui64(v.bstrVal, nullptr, 10);
        break;
    default:
        break;
    }
    VariantClear(&v);
    return value;
}

wchar_t WmiObject::getLetter(const wchar_t* name) const {
    if (!raw) return 0;
    VARIANT v;
    VariantInit(&v);
    if (FAILED(raw->Get(name, 0, &v, nullptr, nullptr))) return 0;
    wchar_t c = 0;
    if (v.vt == VT_I2 || v.vt == VT_UI2) {
        const auto n = v.uiVal;
        if (n >= L'A' && n <= L'Z') c = static_cast<wchar_t>(n);
        else if (n >= L'a' && n <= L'z') c = static_cast<wchar_t>(n - 32);
    } else if (v.vt == VT_BSTR && v.bstrVal && v.bstrVal[0]) {
        c = towupper(v.bstrVal[0]);
        if (c < L'A' || c > L'Z') c = 0;
    }
    VariantClear(&v);
    return c;
}

std::vector<std::wstring> WmiObject::getStrings(const wchar_t* name) const {
    std::vector<std::wstring> out;
    if (!raw) return out;
    VARIANT v;
    VariantInit(&v);
    if (FAILED(raw->Get(name, 0, &v, nullptr, nullptr))) return out;
    if (v.vt == (VT_ARRAY | VT_BSTR) && v.parray) {
        LONG lo = 0, hi = -1;
        SafeArrayGetLBound(v.parray, 1, &lo);
        SafeArrayGetUBound(v.parray, 1, &hi);
        for (LONG i = lo; i <= hi; ++i) {
            BSTR b = nullptr;
            if (SUCCEEDED(SafeArrayGetElement(v.parray, &i, &b)) && b) {
                out.emplace_back(b);
                SysFreeString(b);
            }
        }
    }
    VariantClear(&v);
    return out;
}

std::wstring WmiObject::path() const {
    return getString(L"__PATH");
}

Wmi::~Wmi() {
    if (svc_) svc_->Release();
}

bool Wmi::connect(const wchar_t* ns, std::wstring* error) {
    if (svc_) {
        svc_->Release();
        svc_ = nullptr;
    }
    IWbemLocator* loc = nullptr;
    HRESULT hr = CoCreateInstance(CLSID_WbemLocator, nullptr, CLSCTX_INPROC_SERVER, IID_IWbemLocator,
                                  reinterpret_cast<void**>(&loc));
    if (FAILED(hr)) {
        if (error) *error = L"WMI locator failed.";
        return false;
    }
    hr = loc->ConnectServer(_bstr_t(ns), nullptr, nullptr, nullptr, 0, nullptr, nullptr, &svc_);
    loc->Release();
    if (FAILED(hr) || !svc_) {
        if (error) *error = L"WMI connect failed.";
        return false;
    }
    hr = CoSetProxyBlanket(svc_, RPC_C_AUTHN_WINNT, RPC_C_AUTHZ_NONE, nullptr, RPC_C_AUTHN_LEVEL_CALL,
                           RPC_C_IMP_LEVEL_IMPERSONATE, nullptr, EOAC_NONE);
    if (FAILED(hr)) {
        if (error) *error = L"WMI proxy failed.";
        return false;
    }
    return true;
}

std::vector<WmiObject> Wmi::query(const wchar_t* wql) {
    std::vector<WmiObject> out;
    if (!svc_) return out;
    IEnumWbemClassObject* en = nullptr;
    HRESULT hr = svc_->ExecQuery(_bstr_t(L"WQL"), _bstr_t(wql),
                                 WBEM_FLAG_FORWARD_ONLY | WBEM_FLAG_RETURN_IMMEDIATELY, nullptr, &en);
    if (FAILED(hr) || !en) return out;
    while (true) {
        IWbemClassObject* obj = nullptr;
        ULONG got = 0;
        hr = en->Next(WBEM_INFINITE, 1, &obj, &got);
        if (FAILED(hr) || got == 0 || !obj) break;
        out.emplace_back(obj);
    }
    en->Release();
    return out;
}

WmiObject Wmi::first(const wchar_t* wql) {
    auto rows = query(wql);
    if (rows.empty()) return {};
    WmiObject first = std::move(rows.front());
    return first;
}

bool Wmi::invoke(const WmiObject& obj, const wchar_t* method,
                 const std::function<void(IWbemClassObject*)>& fill,
                 std::uint32_t* returnCode, std::wstring* error) {
    if (!svc_ || !obj.valid()) {
        if (error) *error = L"WMI object missing.";
        return false;
    }
    VARIANT className;
    VariantInit(&className);
    obj.raw->Get(L"__CLASS", 0, &className, nullptr, nullptr);
    IWbemClassObject* cls = nullptr;
    HRESULT hr = svc_->GetObject(className.bstrVal, 0, nullptr, &cls, nullptr);
    VariantClear(&className);
    if (FAILED(hr) || !cls) {
        if (error) *error = L"WMI class missing.";
        return false;
    }
    IWbemClassObject* inSig = nullptr;
    cls->GetMethod(method, 0, &inSig, nullptr);
    cls->Release();
    IWbemClassObject* inParams = nullptr;
    if (inSig) {
        inSig->SpawnInstance(0, &inParams);
        inSig->Release();
    }
    if (fill && inParams) fill(inParams);
    IWbemClassObject* out = nullptr;
    const auto path = obj.path();
    hr = svc_->ExecMethod(_bstr_t(path.c_str()), _bstr_t(method), 0, nullptr, inParams, &out, nullptr);
    if (inParams) inParams->Release();
    if (FAILED(hr)) {
        if (error) *error = L"WMI method failed.";
        if (out) out->Release();
        return false;
    }
    std::uint32_t code = 0;
    if (out) {
        VARIANT v;
        VariantInit(&v);
        if (SUCCEEDED(out->Get(L"ReturnValue", 0, &v, nullptr, nullptr))) {
            if (v.vt == VT_I4 || v.vt == VT_UI4) code = v.ulVal;
            else if (v.vt == VT_I2 || v.vt == VT_UI2) code = v.uiVal;
        }
        VariantClear(&v);
        out->Release();
    }
    if (returnCode) *returnCode = code;
    if (code != 0) {
        if (error) *error = L"Storage error " + std::to_wstring(code) + L".";
        return false;
    }
    return true;
}

bool Wmi::putInstance(const WmiObject& obj, std::wstring* error) {
    if (!svc_ || !obj.valid()) {
        if (error) *error = L"WMI object missing.";
        return false;
    }
    const HRESULT hr = svc_->PutInstance(obj.raw, WBEM_FLAG_UPDATE_ONLY, nullptr, nullptr);
    if (FAILED(hr)) {
        if (error) *error = L"WMI put failed.";
        return false;
    }
    return true;
}

} // namespace pm
