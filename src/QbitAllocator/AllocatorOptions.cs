namespace QbitAllocator;

public sealed class AllocatorOptions
{
    public string UpstreamUrl { get; set; } = "http://qbittorrent:8080";
    public string Policy { get; set; } = AllocationPolicies.MostFree;
    public long UnknownSizeBytes { get; set; } = 100L * 1024 * 1024 * 1024;
    public string DefaultCategory { get; set; } = "default";
    public string SavePathTemplate { get; set; } = "{disk}/{category}";
    public string AssignmentTag { get; set; } = "qbit-assigned";
    public string DiskTagPrefix { get; set; } = "disk:";
    public string RelocateTag { get; set; } = "qbit-relocate";
    public string DiskPinTagPrefix { get; set; } = "root:";
    public Dictionary<string, string> CategoryPins { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool TagOnProxyAdd { get; set; } = true;
    public bool RemoveStaleDiskTagsOnProxyAdd { get; set; }
    public string OnAllocationFailure { get; set; } = AllocationFailureModes.Reject;
    public bool ReconcilerEnabled { get; set; } = true;
    public int ReconcileIntervalSeconds { get; set; } = 30;
    public bool StartAfterReconcile { get; set; } = true;
    public bool StartAfterRelocate { get; set; }
    public int QbitClientTimeoutSeconds { get; set; } = 30;
    public int ProxyTimeoutSeconds { get; set; } = 300;
    public bool? ForcePausedOnAdd { get; set; }
    public AutoDetectOptions AutoDetect { get; set; } = new();
    public List<DiskOptions> Disks { get; set; } = [];
}

public sealed class AutoDetectOptions
{
    public bool Enabled { get; set; } = true;
    public string[] RootPaths { get; set; } = ["/data"];
    public bool IncludeRootPathsAsDisks { get; set; }
    public long MinFreeBytes { get; set; }
}

public sealed class DiskOptions
{
    public string Label { get; set; } = "";
    public string Path { get; set; } = "";
    public string? SavePathTemplate { get; set; }
    public bool Enabled { get; set; } = true;
    public long MinFreeBytes { get; set; }
    public string[]? Categories { get; set; }
    public string[]? Tags { get; set; }
    public int Weight { get; set; } = 1;
}

public static class AllocationPolicies
{
    public const string MostFree = "most_free";
    public const string Fill = "fill";
    public const string BalancePercent = "balance_percent";
}

public static class AllocationFailureModes
{
    public const string Reject = "reject";
    public const string Passthrough = "passthrough";
    public const string PauseAndPassthrough = "pause-and-passthrough";
}
