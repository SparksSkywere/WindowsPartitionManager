# Partition Manager

Disk and partition manager — inspect disks, queue changes on a live map, and apply them.

## Requirements

- Windows 10/11
- Administrator privileges for create / delete / format / resize / initialize
- For building: [.NET 8 SDK](https://dotnet.microsoft.com/download)

## Install (release packages)

Build installers:

```powershell
powershell -ExecutionPolicy Bypass -File .\build-installer.ps1
```

The installer creates **Start Menu** and **Desktop** shortcuts under Skywere Industries (both on by default; you can uncheck them).

### GUI

1. Launch **Partition Manager** (UAC is offered on startup)
2. Select a partition or unallocated region
3. Queue create, delete, format, resize, drive letter, and related operations
4. Review **Pending operations**, then **Apply** or **Undo** / **Discard**

## License
This repository is licensed under the MIT License as provided in the LICENSE file included with the project.
