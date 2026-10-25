# Partition Manager

Native Windows C++ port of Partition Manager. Inspect disks, queue changes on a live map, and apply them — including disk and partition clone.

## Requirements

- Windows 10/11
- Administrator privileges for create / delete / format / resize / initialize / clone
- For building: Visual Studio 2022 with C++ desktop tools, and CMake 3.20+

## Build

```powershell
powershell -ExecutionPolicy Bypass -File .\Build.ps1
```

Output:

- `build\bin\Release\PartitionManager.exe`
- `dist\PartitionManager.msi`
- `dist\PartitionManager-Setup.exe`

The Setup.exe wizard can choose the install folder and Start Menu / Desktop shortcuts. Use `-SkipPackage` to build only the EXE.

## Use

1. Launch **Partition Manager** (UAC is offered on startup)
2. Select a partition or unallocated region
3. Queue create, delete, format, resize, clone, drive letter, and related operations
4. Review pending operations, then **Apply** or **Undo** / **Discard**

## License

MIT License.
