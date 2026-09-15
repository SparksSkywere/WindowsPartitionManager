# Changelog

## 1.1.0

- Disk clone: copy a whole disk to another disk (used data or all sectors), with optional 1 MB alignment and expand of the last data partition when the destination is larger.
- Partition clone: copy a partition into unallocated space on the same or another disk.
- Queued clones show on the live disk map before Apply.
- Raw aligned I/O for clone copies.
- Installer script renamed from `build-installer.ps1` to `build.ps1`.
- Repo `nuget.config` pins nuget.org so restore works on machines without a global package source.

## 1.0.0

- WiX MSI + Burn Setup packaging via `build.ps1` (`dist\PartitionManager.msi`, `dist\PartitionManager-Setup.exe`).
- Initial release: WPF partition manager with Windows Patch Manager chrome (themes, menus, status bar, options).
- Disk map, partition list, pending-operation queue, and Apply / Undo / Discard.
- Create, delete, format, resize/extend, drive letter, label, hide, set active, initialize, convert MBR/GPT (empty disks), delete all, online/offline, chkdsk.
- CLI `--list --no-ui`.
