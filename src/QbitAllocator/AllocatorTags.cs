namespace QbitAllocator;

public static class AllocatorTags
{
    public static string? BuildDiskTag(string? prefix, string? diskLabel)
    {
        return string.IsNullOrWhiteSpace(prefix) || string.IsNullOrWhiteSpace(diskLabel)
            ? null
            : $"{prefix.Trim()}{diskLabel.Trim()}";
    }

    public static bool IsDiskTag(string tag, string? prefix)
    {
        return !string.IsNullOrWhiteSpace(prefix) &&
            tag.StartsWith(prefix.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
