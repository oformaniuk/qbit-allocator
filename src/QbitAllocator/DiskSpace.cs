using System.IO;

namespace QbitAllocator;

public interface IDiskSpaceProvider
{
    DiskSpace GetSpace(string path);
}

public sealed record DiskSpace(long TotalBytes, long FreeBytes);

public sealed class DriveInfoDiskSpaceProvider : IDiskSpaceProvider
{
    public DiskSpace GetSpace(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var match = DriveInfo.GetDrives()
            .Where(d => d.IsReady)
            .Select(d => new { Drive = d, Root = Path.GetFullPath(d.RootDirectory.FullName) })
            .Where(d => fullPath.StartsWith(d.Root, StringComparison.Ordinal))
            .OrderByDescending(d => d.Root.Length)
            .FirstOrDefault();

        if (match is null)
        {
            var root = Path.GetPathRoot(fullPath) ?? "/";
            var drive = new DriveInfo(root);
            return new DiskSpace(drive.TotalSize, drive.AvailableFreeSpace);
        }

        return new DiskSpace(match.Drive.TotalSize, match.Drive.AvailableFreeSpace);
    }
}
