cmake_minimum_required(VERSION 3.20)
project(PartitionManager VERSION 1.0.0 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

if(NOT CMAKE_SYSTEM_NAME STREQUAL "Windows")
    message(FATAL_ERROR "Partition Manager is a Windows-only project.")
endif()

set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
set(PM_VERSION_MAJOR 1)
set(PM_VERSION_MINOR 0)
set(PM_VERSION_PATCH 0)
set(PM_VERSION "${PM_VERSION_MAJOR}.${PM_VERSION_MINOR}.${PM_VERSION_PATCH}")
set(PM_COMPANY "Skywere Industries")

set(CMAKE_RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/bin")
set(CMAKE_RC_FLAGS "${CMAKE_RC_FLAGS} /I\"${CMAKE_CURRENT_SOURCE_DIR}\" /I\"${CMAKE_CURRENT_SOURCE_DIR}/src\"")

add_executable(PartitionManager WIN32
    src/main.cpp
    src/Util.cpp
    src/Log.cpp
    src/Config.cpp
    src/Wmi.cpp
    src/Inventory.cpp
    src/Preview.cpp
    src/Executor.cpp
    src/Clone.cpp
    src/Queue.cpp
    src/Ui.cpp
    src/Theme.cpp
    src/Dialogs.cpp
    src/app.rc
)

target_include_directories(PartitionManager PRIVATE "${CMAKE_CURRENT_SOURCE_DIR}/src")
target_compile_definitions(PartitionManager PRIVATE
    UNICODE
    _UNICODE
    NOMINMAX
    WIN32_LEAN_AND_MEAN
    PM_VERSION_MAJOR=${PM_VERSION_MAJOR}
    PM_VERSION_MINOR=${PM_VERSION_MINOR}
    PM_VERSION_PATCH=${PM_VERSION_PATCH}
    PM_VERSION_STRING=L"${PM_VERSION}"
)
target_link_libraries(PartitionManager PRIVATE
    kernel32 user32 gdi32 comctl32 comdlg32 shell32
    ole32 oleaut32 uuid advapi32 shlwapi dwmapi uxtheme
    wbemuuid comsuppw
)

if(MSVC)
    target_compile_options(PartitionManager PRIVATE /W4 /permissive- /utf-8)
    target_link_options(PartitionManager PRIVATE /MANIFEST:NO)
endif()
