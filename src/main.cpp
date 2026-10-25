#include "Ui.hpp"
#include "Util.hpp"

#include <windows.h>
#include <objbase.h>
#include <shellapi.h>

int WINAPI wWinMain(HINSTANCE instance, HINSTANCE, PWSTR, int cmdShow) {
    int argc = 0;
    auto* argv = CommandLineToArgvW(GetCommandLineW(), &argc);
    if (pm::tryRelaunchElevated(argc, argv)) {
        LocalFree(argv);
        return 0;
    }
    LocalFree(argv);

    HRESULT hr = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    if (FAILED(hr)) return 1;
    const int code = pm::runUi(instance, cmdShow);
    CoUninitialize();
    return code;
}
