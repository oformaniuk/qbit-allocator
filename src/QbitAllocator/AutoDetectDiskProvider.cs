namespace QbitAllocator;

public interface IAutoDetectDiskProvider
{
    IReadOnlyList<DiskOptions> Discover(AutoDetectOptions options);
}

public sealed class AutoDetectDiskProvider : IAutoDetectDiskProvider
{
    public IReadOnlyList<DiskOptions> Discover(AutoDetectOptions options)
    {
        var disks = new List<DiskOptions>();
        foreach (var root in options.RootPaths.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.Ordinal))
        {
            var normalizedRoot = root.TrimEnd('/');
            if (!Directory.Exists(normalizedRoot))
            {
                continue;
            }

            if (options.IncludeRootPathsAsDisks)
            {
                disks.Add(CreateDisk(normalizedRoot, options.MinFreeBytes));
            }

            foreach (var child in Directory.EnumerateDirectories(normalizedRoot).OrderBy(p => p, StringComparer.Ordinal))
            {
                disks.Add(CreateDisk(child.TrimEnd('/'), options.MinFreeBytes));
            }
        }

        return disks
            .GroupBy(d => d.Path, StringComparer.Ordinal)
            .Select(g => g.First())
            .ToList();
    }

    private static DiskOptions CreateDisk(string path, long minFreeBytes)
    {
        return new DiskOptions
        {
            Label = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            Path = path,
            Enabled = true,
            MinFreeBytes = minFreeBytes
        };
    }
}
