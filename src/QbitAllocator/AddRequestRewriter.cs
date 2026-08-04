using System.Net.Http.Headers;

namespace QbitAllocator;

public sealed class AddRequestRewriter
{
    private readonly AllocationService _allocator;
    private readonly AllocatorStatusStore _status;

    public AddRequestRewriter(AllocationService allocator, AllocatorStatusStore status)
    {
        _allocator = allocator;
        _status = status;
    }

    public async Task<RewrittenAddRequest> RewriteAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (request.HasFormContentType)
        {
            var form = await request.ReadFormAsync(cancellationToken);
            var category = form["category"].FirstOrDefault();
            var tags = form["tags"].SelectMany(SplitTags).ToArray();
            var size = TryParseSize(form["size"].FirstOrDefault());
            var allocation = _allocator.Allocate(category, size, tags);
            if (!allocation.Assigned)
            {
                if (IsAllocationFailureMode(AllocationFailureModes.Passthrough))
                {
                    _status.AddSkipped("", null, $"Allocation failed; forwarding add request unchanged: {allocation.Reason}");
                    return RewrittenAddRequest.Success(CopyFormContent(form));
                }

                if (IsAllocationFailureMode(AllocationFailureModes.PauseAndPassthrough))
                {
                    _status.AddSkipped("", null, $"Allocation failed; forwarding add request paused: {allocation.Reason}");
                    return RewrittenAddRequest.Success(CopyFormContent(form, forcePaused: true));
                }

                return RewrittenAddRequest.Failed(allocation.Reason!);
            }

            var content = new MultipartFormDataContent();
            foreach (var pair in form)
            {
                if (string.Equals(pair.Key, "savepath", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(pair.Key, "autoTMM", StringComparison.OrdinalIgnoreCase) ||
                    (_allocator.Options.TagOnProxyAdd && string.Equals(pair.Key, "tags", StringComparison.OrdinalIgnoreCase)) ||
                    (_allocator.Options.ForcePausedOnAdd is not null && string.Equals(pair.Key, "paused", StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                foreach (var value in pair.Value)
                {
                    content.Add(new StringContent(value ?? ""), pair.Key);
                }
            }

            foreach (var file in form.Files)
            {
                var fileContent = new StreamContent(file.OpenReadStream());
                if (!string.IsNullOrWhiteSpace(file.ContentType))
                {
                    fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(file.ContentType);
                }
                content.Add(fileContent, file.Name, file.FileName);
            }

            content.Add(new StringContent(allocation.SavePath!), "savepath");
            content.Add(new StringContent("false"), "autoTMM");
            if (_allocator.Options.TagOnProxyAdd)
            {
                content.Add(new StringContent(MergeTags(tags, _allocator.Options, allocation.DiskLabel!)), "tags");
            }
            if (_allocator.Options.ForcePausedOnAdd is bool paused)
            {
                content.Add(new StringContent(paused ? "true" : "false"), "paused");
            }

            _status.AddDecision("", null, allocation.DiskLabel!, allocation.SavePath!, "proxy");
            return RewrittenAddRequest.Success(content);
        }

        return RewrittenAddRequest.Failed("Only form-based torrents/add requests can be assigned.");
    }

    private static IReadOnlyCollection<string> SplitTags(string? tags)
    {
        return string.IsNullOrWhiteSpace(tags)
            ? []
            : tags.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }

    private static string MergeTags(IEnumerable<string> incomingTags, AllocatorOptions options, string diskLabel)
    {
        if (options.RemoveStaleDiskTagsOnProxyAdd)
        {
            incomingTags = incomingTags.Where(tag => !AllocatorTags.IsDiskTag(tag, options.DiskTagPrefix));
        }

        return string.Join(",", incomingTags
            .Concat([options.AssignmentTag, AllocatorTags.BuildDiskTag(options.DiskTagPrefix, diskLabel)])
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static MultipartFormDataContent CopyFormContent(IFormCollection form, bool? forcePaused = null)
    {
        var content = new MultipartFormDataContent();
        foreach (var pair in form)
        {
            if (forcePaused is not null && string.Equals(pair.Key, "paused", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var value in pair.Value)
            {
                content.Add(new StringContent(value ?? ""), pair.Key);
            }
        }

        foreach (var file in form.Files)
        {
            var fileContent = new StreamContent(file.OpenReadStream());
            if (!string.IsNullOrWhiteSpace(file.ContentType))
            {
                fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(file.ContentType);
            }
            content.Add(fileContent, file.Name, file.FileName);
        }

        if (forcePaused is bool paused)
        {
            content.Add(new StringContent(paused ? "true" : "false"), "paused");
        }

        return content;
    }

    private bool IsAllocationFailureMode(string mode)
    {
        return string.Equals(_allocator.Options.OnAllocationFailure, mode, StringComparison.OrdinalIgnoreCase);
    }

    private static long? TryParseSize(string? value)
    {
        return long.TryParse(value, out var parsed) ? parsed : null;
    }
}

public sealed record RewrittenAddRequest(bool Assigned, HttpContent? Content, string? Reason)
{
    public static RewrittenAddRequest Success(HttpContent content) => new(true, content, null);
    public static RewrittenAddRequest Failed(string reason) => new(false, null, reason);
}
