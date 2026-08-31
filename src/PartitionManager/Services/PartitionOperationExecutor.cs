using System.Management;
using PartitionManager.Helpers;
using PartitionManager.Models;

namespace PartitionManager.Services;

/// <summary>Executes pending operations against the live Windows Storage API.</summary>
public sealed class PartitionOperationExecutor
{
    private readonly LogService _log;

    public PartitionOperationExecutor(LogService log)
    {
        _log = log;
    }

    public async Task<OperationResult> ExecuteAsync(PendingOperation op, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _log.Info("Apply: " + op.Description);
        try
        {
            return op.Kind switch
            {
                OperationKind.CreatePartition => await Task.Run(() => CreatePartition(op), cancellationToken).ConfigureAwait(false),
                OperationKind.DeletePartition => await Task.Run(() => DeletePartition(op), cancellationToken).ConfigureAwait(false),
                OperationKind.FormatPartition => await Task.Run(() => FormatPartition(op), cancellationToken).ConfigureAwait(false),
                OperationKind.ResizePartition => await Task.Run(() => ResizePartition(op), cancellationToken).ConfigureAwait(false),
                OperationKind.ChangeDriveLetter => await Task.Run(() => ChangeDriveLetter(op), cancellationToken).ConfigureAwait(false),
                OperationKind.ChangeLabel => await Task.Run(() => ChangeLabel(op), cancellationToken).ConfigureAwait(false),
                OperationKind.HidePartition => await Task.Run(() => SetHidden(op, true), cancellationToken).ConfigureAwait(false),
                OperationKind.UnhidePartition => await Task.Run(() => SetHidden(op, false), cancellationToken).ConfigureAwait(false),
                OperationKind.SetActive => await Task.Run(() => SetActive(op), cancellationToken).ConfigureAwait(false),
                OperationKind.InitializeDisk => await Task.Run(() => InitializeDisk(op), cancellationToken).ConfigureAwait(false),
                OperationKind.ConvertPartitionStyle => await Task.Run(() => ConvertStyle(op), cancellationToken).ConfigureAwait(false),
                OperationKind.DeleteAllPartitions => await Task.Run(() => ClearDisk(op), cancellationToken).ConfigureAwait(false),
                OperationKind.OfflineDisk => await Task.Run(() => SetDiskOnline(op, false), cancellationToken).ConfigureAwait(false),
                OperationKind.OnlineDisk => await Task.Run(() => SetDiskOnline(op, true), cancellationToken).ConfigureAwait(false),
                _ => Fail("Unsupported operation.")
            };
        }
        catch (Exception ex)
        {
            _log.Error(op.Description + " failed: " + ex.Message);
            return Fail(ex.Message);
        }
    }

    public async Task<OperationResult> CheckPartitionAsync(char driveLetter, bool fix, CancellationToken cancellationToken)
    {
        var args = new List<string> { $"{driveLetter}:", "/scan" };
        if (fix)
        {
            args.Clear();
            args.Add($"{driveLetter}:");
            args.Add("/f");
            args.Add("/x");
        }

        _log.Info($"Running chkdsk {string.Join(' ', args)}");
        var result = await ProcessRunner.RunAsync("chkdsk.exe", args, cancellationToken, timeoutSeconds: 3600)
            .ConfigureAwait(false);
        var output = result.CombinedOutput;
        if (!string.IsNullOrWhiteSpace(output))
            _log.Info(output.Length > 2000 ? output[..2000] + "…" : output);

        // chkdsk returns 0 = no issues, 1 = fixed, 2 = dirty, 3 = not fixed
        if (result.ExitCode is 0 or 1)
            return new OperationResult { Success = true, Message = result.StdOut, ReturnCode = (uint)result.ExitCode };

        return Fail(string.IsNullOrWhiteSpace(output) ? $"chkdsk exited {result.ExitCode}" : output);
    }

    private OperationResult CreatePartition(PendingOperation op)
    {
        var p = op.Create ?? throw new InvalidOperationException("Create parameters missing.");
        using var disk = GetDisk(op.DiskNumber);
        if (disk is null)
            return Fail($"Disk {op.DiskNumber} not found.");

        var inParams = disk.GetMethodParameters("CreatePartition");
        inParams["Size"] = p.Size;
        inParams["UseMaximumSize"] = false;
        inParams["Offset"] = p.Offset;
        inParams["Alignment"] = 1024u * 1024u;
        if (p.DriveLetter is char letter)
        {
            inParams["AssignDriveLetter"] = true;
            inParams["DriveLetter"] = letter;
        }
        else
        {
            inParams["AssignDriveLetter"] = false;
        }

        var style = DiskInventoryService.GetUInt16(disk, "PartitionStyle");
        if (style == 1)
            inParams["MbrType"] = (ushort)7; // IFS / NTFS
        else
            inParams["GptType"] = "{ebd0a0a2-b9e5-4433-87c0-68b6b72699c7}";

        var created = Invoke(disk, "CreatePartition", inParams);
        if (!created.Success)
            return created;

        if (!p.FormatAfterCreate || string.IsNullOrWhiteSpace(p.FileSystem))
            return created;

        // Newly created partitions need a moment before the volume object exists.
        Thread.Sleep(800);
        var formatOp = new PendingOperation
        {
            Kind = OperationKind.FormatPartition,
            DiskNumber = op.DiskNumber,
            Offset = p.Offset,
            Size = p.Size,
            DriveLetter = p.DriveLetter,
            Format = new FormatPartitionParams
            {
                FileSystem = p.FileSystem,
                Label = p.Label,
                QuickFormat = p.QuickFormat,
                ClusterSize = p.ClusterSize
            }
        };
        return FormatPartition(formatOp);
    }

    private OperationResult DeletePartition(PendingOperation op)
    {
        using var part = GetPartition(op.DiskNumber, op.Offset);
        if (part is null)
            return Fail("Partition not found.");
        return Invoke(part, "DeleteObject", part.GetMethodParameters("DeleteObject"));
    }

    private OperationResult FormatPartition(PendingOperation op)
    {
        var f = op.Format ?? throw new InvalidOperationException("Format parameters missing.");
        using var volume = GetVolume(op);
        if (volume is null)
            return Fail("Volume not found. The partition may not have a file system yet — create it first.");

        var inParams = volume.GetMethodParameters("Format");
        inParams["FileSystem"] = f.FileSystem;
        inParams["QuickFormat"] = f.QuickFormat;
        if (!string.IsNullOrWhiteSpace(f.Label))
            inParams["FileSystemLabel"] = f.Label;
        if (f.ClusterSize > 0)
            inParams["ClusterSize"] = f.ClusterSize;
        inParams["Force"] = true;
        return Invoke(volume, "Format", inParams);
    }

    private OperationResult ResizePartition(PendingOperation op)
    {
        var r = op.Resize ?? throw new InvalidOperationException("Resize parameters missing.");
        using var part = GetPartition(op.DiskNumber, op.Offset);
        if (part is null)
            return Fail("Partition not found.");
        var inParams = part.GetMethodParameters("Resize");
        inParams["Size"] = r.NewSize;
        return Invoke(part, "Resize", inParams);
    }

    private OperationResult ChangeDriveLetter(PendingOperation op)
    {
        using var part = GetPartition(op.DiskNumber, op.Offset);
        if (part is null)
            return Fail("Partition not found.");

        var existing = DiskInventoryService.GetLetter(part["DriveLetter"]);
        if (existing is char oldLetter)
        {
            var remove = part.GetMethodParameters("RemoveAccessPath");
            remove["AccessPath"] = $"{oldLetter}:";
            var removed = Invoke(part, "RemoveAccessPath", remove);
            if (!removed.Success)
                return removed;
        }

        if (op.DriveLetter is not char letter)
            return Ok("Drive letter removed.");

        var add = part.GetMethodParameters("AddAccessPath");
        add["AccessPath"] = $"{letter}:";
        add["AssignDriveLetter"] = true;
        return Invoke(part, "AddAccessPath", add);
    }

    private OperationResult ChangeLabel(PendingOperation op)
    {
        using var volume = GetVolume(op);
        if (volume is null)
            return Fail("Volume not found.");

        try
        {
            volume["FileSystemLabel"] = op.Label ?? "";
            volume.Put();
            return Ok("Label updated.");
        }
        catch (Exception ex)
        {
            return Fail(ex.Message);
        }
    }

    private OperationResult SetHidden(PendingOperation op, bool hidden)
    {
        using var part = GetPartition(op.DiskNumber, op.Offset);
        if (part is null)
            return Fail("Partition not found.");

        var inParams = part.GetMethodParameters("SetAttributes");
        inParams["IsHidden"] = hidden;
        var result = Invoke(part, "SetAttributes", inParams);
        if (!result.Success)
            return result;

        if (hidden)
        {
            var letter = DiskInventoryService.GetLetter(part["DriveLetter"]);
            if (letter is char c)
            {
                var remove = part.GetMethodParameters("RemoveAccessPath");
                remove["AccessPath"] = $"{c}:";
                Invoke(part, "RemoveAccessPath", remove);
            }
        }

        return result;
    }

    private OperationResult SetActive(PendingOperation op)
    {
        using var part = GetPartition(op.DiskNumber, op.Offset);
        if (part is null)
            return Fail("Partition not found.");
        var inParams = part.GetMethodParameters("SetAttributes");
        inParams["IsActive"] = true;
        return Invoke(part, "SetAttributes", inParams);
    }

    private OperationResult InitializeDisk(PendingOperation op)
    {
        using var disk = GetDisk(op.DiskNumber);
        if (disk is null)
            return Fail($"Disk {op.DiskNumber} not found.");
        var style = op.TargetStyle == PartitionStyleKind.Mbr ? (ushort)1 : (ushort)2;
        var inParams = disk.GetMethodParameters("Initialize");
        inParams["PartitionStyle"] = style;
        return Invoke(disk, "Initialize", inParams);
    }

    private OperationResult ConvertStyle(PendingOperation op)
    {
        using var disk = GetDisk(op.DiskNumber);
        if (disk is null)
            return Fail($"Disk {op.DiskNumber} not found.");
        var style = op.TargetStyle == PartitionStyleKind.Mbr ? (ushort)1 : (ushort)2;
        var inParams = disk.GetMethodParameters("ConvertStyle");
        inParams["PartitionStyle"] = style;
        return Invoke(disk, "ConvertStyle", inParams);
    }

    private OperationResult ClearDisk(PendingOperation op)
    {
        using var disk = GetDisk(op.DiskNumber);
        if (disk is null)
            return Fail($"Disk {op.DiskNumber} not found.");
        var inParams = disk.GetMethodParameters("Clear");
        inParams["RemoveData"] = true;
        inParams["RemoveOEM"] = true;
        inParams["ZeroOutEntireDisk"] = false;
        return Invoke(disk, "Clear", inParams);
    }

    private OperationResult SetDiskOnline(PendingOperation op, bool online)
    {
        using var disk = GetDisk(op.DiskNumber);
        if (disk is null)
            return Fail($"Disk {op.DiskNumber} not found.");
        var method = online ? "Online" : "Offline";
        return Invoke(disk, method, disk.GetMethodParameters(method));
    }

    private static ManagementObject? GetDisk(int number)
    {
        var scope = Connect();
        using var searcher = new ManagementObjectSearcher(
            scope,
            new ObjectQuery($"SELECT * FROM MSFT_Disk WHERE Number = {number}"));
        foreach (ManagementObject mo in searcher.Get())
            return mo;
        return null;
    }

    private static ManagementObject? GetPartition(int diskNumber, ulong offset)
    {
        var scope = Connect();
        using var searcher = new ManagementObjectSearcher(
            scope,
            new ObjectQuery($"SELECT * FROM MSFT_Partition WHERE DiskNumber = {diskNumber}"));
        ManagementObject? best = null;
        ulong bestDelta = ulong.MaxValue;
        foreach (ManagementObject mo in searcher.Get())
        {
            var off = DiskInventoryService.GetUInt64(mo, "Offset");
            var delta = off > offset ? off - offset : offset - off;
            if (delta < bestDelta)
            {
                best?.Dispose();
                best = mo;
                bestDelta = delta;
            }
            else
            {
                mo.Dispose();
            }
        }

        // Offsets can shift slightly after alignment; require a reasonably close match.
        if (best is not null && bestDelta > 16UL * 1024UL * 1024UL)
        {
            best.Dispose();
            return null;
        }

        return best;
    }

    private static ManagementObject? GetVolume(PendingOperation op)
    {
        var scope = Connect();
        if (op.DriveLetter is char letter)
        {
            using var byLetter = new ManagementObjectSearcher(
                scope,
                new ObjectQuery("SELECT * FROM MSFT_Volume"));
            foreach (ManagementObject mo in byLetter.Get())
            {
                if (DiskInventoryService.GetLetter(mo["DriveLetter"]) == char.ToUpperInvariant(letter))
                    return mo;
                mo.Dispose();
            }
        }

        using var part = GetPartition(op.DiskNumber, op.Offset);
        if (part is null)
            return null;

        var paths = DiskInventoryService.GetStringArray(part, "AccessPaths");
        var volumePath = paths.FirstOrDefault(p => p.StartsWith(@"\\?\Volume{", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(volumePath))
        {
            // Try associators
            try
            {
                foreach (ManagementObject vol in part.GetRelated("MSFT_Volume"))
                    return vol;
            }
            catch
            {
                // ignore
            }

            return null;
        }

        using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM MSFT_Volume"));
        foreach (ManagementObject mo in searcher.Get())
        {
            var path = DiskInventoryService.GetString(mo, "Path");
            if (!string.IsNullOrWhiteSpace(path) &&
                volumePath.StartsWith(path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                return mo;
            mo.Dispose();
        }

        return null;
    }

    private static ManagementScope Connect()
    {
        var scope = new ManagementScope(DiskInventoryService.StorageNamespace);
        scope.Connect();
        return scope;
    }

    private OperationResult Invoke(ManagementObject mo, string method, ManagementBaseObject inParams)
    {
        ManagementBaseObject? output = null;
        try
        {
            output = mo.InvokeMethod(method, inParams, null);
            var code = output is null ? 0u : Convert.ToUInt32(output["ReturnValue"] ?? 0);
            var extended = output?["ExtendedStatus"]?.ToString();
            if (code == 0)
            {
                _log.Success($"{method} succeeded.");
                return Ok($"{method} succeeded.");
            }

            var message = Describe(code);
            if (!string.IsNullOrWhiteSpace(extended))
                message += " " + extended;
            _log.Error($"{method} failed ({code}): {message}");
            return new OperationResult { Success = false, Message = message, ReturnCode = code };
        }
        catch (Exception ex)
        {
            _log.Error($"{method} threw: {ex.Message}");
            return Fail(ex.Message);
        }
        finally
        {
            output?.Dispose();
            inParams.Dispose();
        }
    }

    private static string Describe(uint code) => code switch
    {
        1 => "Not supported.",
        2 => "Unspecified error.",
        3 => "Timeout.",
        4 => "Failed.",
        5 => "Invalid parameter.",
        6 => "Access denied. Run Partition Manager as administrator.",
        4097 => "Access denied. Run Partition Manager as administrator.",
        40000 => "Not supported by this disk.",
        40001 => "Unknown error from the storage provider.",
        40004 => "The object cannot be found.",
        42002 => "The requested size is not supported.",
        42006 => "There is not enough usable space.",
        42008 => "The partition is in use and cannot be modified.",
        42009 => "Access path already in use.",
        42010 => "The volume cannot be formatted with that file system.",
        _ => $"Storage error {code}."
    };

    private static OperationResult Ok(string message) => new() { Success = true, Message = message };
    private static OperationResult Fail(string message) => new() { Success = false, Message = message };
}
