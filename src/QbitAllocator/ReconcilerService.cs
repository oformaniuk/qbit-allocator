namespace QbitAllocator;

public sealed class ReconcilerService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<ReconcilerService> _logger;

    public ReconcilerService(IServiceProvider services, ILogger<ReconcilerService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = _services.CreateScope();
            var options = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<AllocatorOptions>>().Value;
            if (options.ReconcilerEnabled)
            {
                await RunOnceAsync(scope.ServiceProvider, stoppingToken);
            }
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, options.ReconcileIntervalSeconds)), stoppingToken);
        }
    }

    public static async Task RunOnceAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var qbit = services.GetRequiredService<QbitClient>();
        var allocator = services.GetRequiredService<AllocationService>();
        var status = services.GetRequiredService<AllocatorStatusStore>();
        var logger = services.GetService<ILogger<ReconcilerService>>();

        try
        {
            var torrents = await qbit.GetTorrentsAsync(cancellationToken);
            var reservations = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            foreach (var torrent in torrents)
            {
                var tags = SplitTags(torrent.Tags);
                if (HasTag(tags, allocator.Options.RelocateTag))
                {
                    await RelocateTaggedTorrentAsync(qbit, allocator, status, torrent, tags, reservations, cancellationToken);
                    continue;
                }

                if (!IsAssignableCandidate(torrent, allocator.Options.AssignmentTag))
                {
                    if (IsPausedIncompleteUnassigned(torrent, allocator.Options.AssignmentTag))
                    {
                        status.AddSkipped(torrent.Hash, torrent.Name, "Paused torrent appears already started; leaving location unchanged.");
                    }
                    continue;
                }

                var allocation = allocator.Allocate(torrent.Category, torrent.Size, tags, reservations);
                if (!allocation.Assigned)
                {
                    status.AddSkipped(torrent.Hash, torrent.Name, allocation.Reason!);
                    continue;
                }

                await qbit.SetLocationAsync(torrent.Hash, allocation.SavePath!, cancellationToken);
                await ReplaceDiskTagsAsync(qbit, torrent.Hash, tags, allocator.Options.AssignmentTag, allocator.Options.DiskTagPrefix, allocation.DiskLabel!);
                if (allocator.Options.StartAfterReconcile)
                {
                    await qbit.StartAsync(torrent.Hash, cancellationToken);
                }
                Reserve(reservations, allocation.DiskLabel!, allocator.GetReservationBytes(torrent.Size));
                status.AddDecision(torrent.Hash, torrent.Name, allocation.DiskLabel!, allocation.SavePath!, "reconciler");
            }

            status.MarkReconcileSuccess();
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "qBittorrent reconciliation failed");
            status.MarkReconcileFailure(ex);
        }
    }

    private static async Task RelocateTaggedTorrentAsync(
        QbitClient qbit,
        AllocationService allocator,
        AllocatorStatusStore status,
        QbitTorrent torrent,
        IReadOnlyCollection<string> tags,
        Dictionary<string, long> reservations,
        CancellationToken cancellationToken)
    {
        var allocation = allocator.Allocate(torrent.Category, torrent.Size, tags, reservations);
        if (!allocation.Assigned)
        {
            status.AddSkipped(torrent.Hash, torrent.Name, allocation.Reason!);
            return;
        }

        await qbit.SetLocationAsync(torrent.Hash, allocation.SavePath!, cancellationToken);
        await ReplaceDiskTagsAsync(qbit, torrent.Hash, tags, allocator.Options.AssignmentTag, allocator.Options.DiskTagPrefix, allocation.DiskLabel!);
        await qbit.RemoveTagsAsync(torrent.Hash, allocator.Options.RelocateTag);
        if (allocator.Options.StartAfterRelocate)
        {
            await qbit.StartAsync(torrent.Hash, cancellationToken);
        }
        Reserve(reservations, allocation.DiskLabel!, allocator.GetReservationBytes(torrent.Size));
        status.AddDecision(torrent.Hash, torrent.Name, allocation.DiskLabel!, allocation.SavePath!, "relocate-tag");
    }

    private static async Task ReplaceDiskTagsAsync(
        QbitClient qbit,
        string hash,
        IReadOnlyCollection<string> currentTags,
        string assignmentTag,
        string diskTagPrefix,
        string diskLabel)
    {
        var staleDiskTags = currentTags
            .Where(tag => AllocatorTags.IsDiskTag(tag, diskTagPrefix))
            .ToArray();
        if (staleDiskTags.Length > 0)
        {
            await qbit.RemoveTagsAsync(hash, staleDiskTags);
        }

        await qbit.AddTagsAsync(hash, assignmentTag, AllocatorTags.BuildDiskTag(diskTagPrefix, diskLabel) ?? "");
    }

    private static void Reserve(Dictionary<string, long> reservations, string diskLabel, long sizeBytes)
    {
        reservations[diskLabel] = reservations.GetValueOrDefault(diskLabel) + Math.Max(0, sizeBytes);
    }

    public static bool IsAssignableCandidate(QbitTorrent torrent, string assignmentTag)
    {
        return IsPausedIncompleteUnassigned(torrent, assignmentTag) &&
            torrent.Progress <= 0 &&
            torrent.Downloaded <= 0 &&
            torrent.LastActivity <= 0;
    }

    private static bool IsPausedIncompleteUnassigned(QbitTorrent torrent, string assignmentTag)
    {
        var tags = SplitTags(torrent.Tags);
        return torrent.Progress < 1 &&
            torrent.State.Contains("paused", StringComparison.OrdinalIgnoreCase) &&
            !tags.Contains(assignmentTag, StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyCollection<string> SplitTags(string? tags)
    {
        return string.IsNullOrWhiteSpace(tags)
            ? []
            : tags.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }

    private static bool HasTag(IReadOnlyCollection<string> tags, string tag)
    {
        return !string.IsNullOrWhiteSpace(tag) &&
            tags.Contains(tag, StringComparer.OrdinalIgnoreCase);
    }
}
