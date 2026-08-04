using System.Text;

namespace QbitAllocator;

public static class PathBuilder
{
    public static string SanitizeCategory(string? category, string defaultCategory)
    {
        var source = string.IsNullOrWhiteSpace(category) ? defaultCategory : category.Trim();
        var builder = new StringBuilder(source.Length);

        foreach (var ch in source)
        {
            if (char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.')
            {
                builder.Append(ch);
            }
            else if (char.IsWhiteSpace(ch))
            {
                builder.Append('_');
            }
        }

        var sanitized = builder.ToString().Trim('.', '_', '-');
        return string.IsNullOrWhiteSpace(sanitized) ? defaultCategory : sanitized;
    }

    public static string BuildSavePath(string diskPath, string category)
    {
        return BuildSavePath(diskPath, category, "{disk}/{category}");
    }

    public static string BuildSavePath(string diskPath, string category, string? template)
    {
        var normalizedDisk = diskPath.TrimEnd('/');
        var normalizedTemplate = string.IsNullOrWhiteSpace(template) ? "{disk}/{category}" : template.Trim();
        var path = normalizedTemplate
            .Replace("{disk}", normalizedDisk, StringComparison.OrdinalIgnoreCase)
            .Replace("{category}", category, StringComparison.OrdinalIgnoreCase)
            .Replace("{defaultCategory}", category, StringComparison.OrdinalIgnoreCase);

        if (!path.StartsWith('/'))
        {
            path = $"{normalizedDisk}/{path.TrimStart('/')}";
        }

        while (path.Contains("//", StringComparison.Ordinal))
        {
            path = path.Replace("//", "/", StringComparison.Ordinal);
        }

        return path.TrimEnd('/');
    }
}
