using Microsoft.Extensions.Options;

namespace QbitAllocator;

public sealed class AllocationService
{
    private readonly IDiskSpaceProvider _spaceProvider;
    private readonly IAutoDetectDiskProvider? _autoDetectDiskProvider;
    private string? _fillCurrentLabel;

    public AllocationService(
        IOptions<AllocatorOptions> options,
        IDiskSpaceProvider spaceProvider,
        IAutoDetectDiskProvider? autoDetectDiskProvider = null)
    {
        Options = options.Value;
        _spaceProvider = spaceProvider;
        _autoDetectDiskProvider = autoDetectDiskProvider;
    }

    public AllocatorOptions Options { get; }

    public AllocationResult Allocate(string? category, long? sizeBytes, IReadOnlyCollection<string>? tags = null)
    {
        return Allocate(category, sizeBytes, tags, null);
    }

    public AllocationResult Allocate(
        string? category,
        long? sizeBytes,
        IReadOnlyCollection<string>? tags,
        IReadOnlyDictionary<string, long>? reservedBytesByDiskLabel)
    {
        var requestedBytes = sizeBytes.GetValueOrDefault(0) > 0 ? sizeBytes!.Value : Options.UnknownSizeBytes;
        var safeCategory = PathBuilder.SanitizeCategory(category, Options.DefaultCategory);
        var diskOptions = GetDisks();
        var categoryPinnedDisk = GetCategoryPinnedDiskLabel(category);
        var pinnedDisks = GetPinnedDiskLabels(tags);
        if (pinnedDisks.Length > 1)
        {
            return AllocationResult.Failed($"Multiple disk pin tags matched '{Options.DiskPinTagPrefix}': {string.Join(", ", pinnedDisks)}.");
        }

        var tagPinnedDisk = pinnedDisks.SingleOrDefault();
        if (categoryPinnedDisk is not null &&
            tagPinnedDisk is not null &&
            !string.Equals(categoryPinnedDisk, tagPinnedDisk, StringComparison.OrdinalIgnoreCase))
        {
            return AllocationResult.Failed($"Category pins disk '{categoryPinnedDisk}' but tag pins disk '{tagPinnedDisk}'.");
        }

        var pinnedDisk = categoryPinnedDisk ?? tagPinnedDisk;
        if (pinnedDisk is not null && !diskOptions.Any(d => string.Equals(d.Label, pinnedDisk, StringComparison.OrdinalIgnoreCase)))
        {
            return AllocationResult.Failed($"Pinned disk '{pinnedDisk}' is not configured.");
        }

        var eligible = GetDiskStatuses(diskOptions, category, tags, pinnedDisk, requestedBytes, reservedBytesByDiskLabel)
            .Where(d => d.Enabled && d.Fits)
            .ToList();

        if (eligible.Count == 0)
        {
            return AllocationResult.Failed($"No eligible disk has {requestedBytes} bytes available after reserve.");
        }

        DiskStatus selected = Options.Policy switch
        {
            AllocationPolicies.Fill => ChooseFillDisk(eligible),
            AllocationPolicies.BalancePercent => eligible.OrderBy(d => d.UsedPercent).ThenByDescending(d => d.AvailableAfterReserveBytes).First(),
            _ => eligible.OrderByDescending(d => d.AvailableAfterReserveBytes).First()
        };

        _fillCurrentLabel = selected.Label;
        var selectedOptions = diskOptions.First(d => string.Equals(d.Label, selected.Label, StringComparison.OrdinalIgnoreCase));
        var savePathTemplate = selectedOptions.SavePathTemplate ?? Options.SavePathTemplate;
        return AllocationResult.Success(selected.Label, PathBuilder.BuildSavePath(selected.Path, safeCategory, savePathTemplate));

        DiskStatus ChooseFillDisk(List<DiskStatus> disks)
        {
            var current = disks.FirstOrDefault(d => string.Equals(d.Label, _fillCurrentLabel, StringComparison.OrdinalIgnoreCase));
            return current ?? disks.OrderByDescending(d => d.AvailableAfterReserveBytes).First();
        }
    }

    public IReadOnlyList<DiskStatus> GetDiskStatuses(string? category = null, IReadOnlyCollection<string>? tags = null, long? requestedBytesOverride = null)
    {
        var requestedBytes = requestedBytesOverride.GetValueOrDefault(Options.UnknownSizeBytes);
        var pinnedDisks = GetPinnedDiskLabels(tags);
        var pinnedDisk = GetCategoryPinnedDiskLabel(category) ?? (pinnedDisks.Length == 1 ? pinnedDisks[0] : null);
        return GetDiskStatuses(GetDisks(), category, tags, pinnedDisk, requestedBytes, null);
    }

    private IReadOnlyList<DiskStatus> GetDiskStatuses(
        IReadOnlyList<DiskOptions> disks,
        string? category,
        IReadOnlyCollection<string>? tags,
        string? pinnedDiskLabel,
        long requestedBytes,
        IReadOnlyDictionary<string, long>? reservedBytesByDiskLabel)
    {
        return disks.Select(d =>
        {
            var space = SafeGetSpace(d.Path);
            var enabled = d.Enabled &&
                MatchesPinnedDisk(d, pinnedDiskLabel) &&
                MatchesFilters(d, category, tags);
            var plannedReserved = GetPlannedReservedBytes(reservedBytesByDiskLabel, d.Label);
            var effectiveFreeBytes = Math.Max(0, space.FreeBytes - plannedReserved);
            var afterReserve = Math.Max(0, effectiveFreeBytes - d.MinFreeBytes);
            return new DiskStatus(
                d.Label,
                d.Path,
                d.Enabled,
                space.TotalBytes,
                effectiveFreeBytes,
                d.MinFreeBytes,
                space.TotalBytes <= 0 ? 100 : Math.Round((double)(space.TotalBytes - effectiveFreeBytes) / space.TotalBytes * 100, 2),
                enabled,
                afterReserve,
                enabled && afterReserve >= requestedBytes);
        }).ToList();
    }

    public long GetReservationBytes(long? sizeBytes)
    {
        return sizeBytes.GetValueOrDefault(0) > 0 ? sizeBytes!.Value : Options.UnknownSizeBytes;
    }

    private static long GetPlannedReservedBytes(IReadOnlyDictionary<string, long>? reservations, string diskLabel)
    {
        return reservations is not null && reservations.TryGetValue(diskLabel, out var reserved)
            ? Math.Max(0, reserved)
            : 0;
    }

    private IReadOnlyList<DiskOptions> GetDisks()
    {
        if (Options.Disks.Count > 0 || !Options.AutoDetect.Enabled)
        {
            return Options.Disks;
        }

        return _autoDetectDiskProvider?.Discover(Options.AutoDetect) ?? [];
    }

    private DiskSpace SafeGetSpace(string path)
    {
        try
        {
            return _spaceProvider.GetSpace(path);
        }
        catch
        {
            return new DiskSpace(0, 0);
        }
    }

    private static bool MatchesFilters(DiskOptions disk, string? category, IReadOnlyCollection<string>? tags)
    {
        if (disk.Categories is { Length: > 0 } &&
            !disk.Categories.Any(c => string.Equals(c, category, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (disk.Tags is { Length: > 0 })
        {
            var requestedTags = tags ?? [];
            if (!disk.Tags.Any(t => requestedTags.Contains(t, StringComparer.OrdinalIgnoreCase)))
            {
                return false;
            }
        }

        return true;
    }

    private string? GetCategoryPinnedDiskLabel(string? category)
    {
        if (string.IsNullOrWhiteSpace(category) || Options.CategoryPins.Count == 0)
        {
            return null;
        }

        return Options.CategoryPins.TryGetValue(category.Trim(), out var diskLabel) && !string.IsNullOrWhiteSpace(diskLabel)
            ? diskLabel.Trim()
            : null;
    }

    private string[] GetPinnedDiskLabels(IReadOnlyCollection<string>? tags)
    {
        if (string.IsNullOrWhiteSpace(Options.DiskPinTagPrefix) || tags is null)
        {
            return [];
        }

        return tags
            .Where(tag => tag.StartsWith(Options.DiskPinTagPrefix, StringComparison.OrdinalIgnoreCase))
            .Select(tag => tag[Options.DiskPinTagPrefix.Length..].Trim())
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool MatchesPinnedDisk(DiskOptions disk, string? pinnedDiskLabel)
    {
        return pinnedDiskLabel is null ||
            string.Equals(disk.Label, pinnedDiskLabel, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record AllocationResult(bool Assigned, string? DiskLabel, string? SavePath, string? Reason)
{
    public static AllocationResult Success(string diskLabel, string savePath) => new(true, diskLabel, savePath, null);
    public static AllocationResult Failed(string reason) => new(false, null, null, reason);
}

public sealed record DiskStatus(
    string Label,
    string Path,
    bool ConfigEnabled,
    long TotalBytes,
    long FreeBytes,
    long ReserveBytes,
    double UsedPercent,
    bool Enabled,
    long AvailableAfterReserveBytes,
    bool Fits);
