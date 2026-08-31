# Changelog

## 1.0.0

- WiX MSI + Burn Setup packaging via `build-installer.ps1` (`dist\PartitionManager.msi`, `dist\PartitionManager-Setup.exe`).
- Initial release: WPF partition manager with Windows Patch Manager chrome (themes, menus, status bar, options).
- Disk map, partition list, pending-operation queue, and Apply / Undo / Discard.
- Create, delete, format, resize/extend, drive letter, label, hide, set active, initialize, convert MBR/GPT (empty disks), delete all, online/offline, chkdsk.
- CLI `--list --no-ui`.
