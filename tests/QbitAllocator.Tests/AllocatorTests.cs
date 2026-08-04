using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using QbitAllocator;

namespace QbitAllocator.Tests;

public sealed class AllocatorTests
{
    [Fact]
    public void MostFree_ChoosesDiskWithLargestFreeSpaceAfterReserve()
    {
        var allocator = CreateAllocator(policy: AllocationPolicies.MostFree, spaces: new()
        {
            ["/disk/a"] = new DiskSpace(1_000, 500),
            ["/disk/b"] = new DiskSpace(1_000, 900)
        });

        var result = allocator.Allocate("linux-isos", 100);

        Assert.True(result.Assigned);
        Assert.Equal("b", result.DiskLabel);
        Assert.Equal("/disk/b/linux-isos", result.SavePath);
    }

    [Fact]
    public void ReserveCheck_FailsWhenNoDiskFits()
    {
        var allocator = CreateAllocator(spaces: new()
        {
            ["/disk/a"] = new DiskSpace(1_000, 500),
            ["/disk/b"] = new DiskSpace(1_000, 450)
        }, reserve: 400);

        var result = allocator.Allocate("linux-isos", 200);

        Assert.False(result.Assigned);
        Assert.Contains("No eligible disk", result.Reason);
    }

    [Fact]
    public void Fill_KeepsCurrentDiskUntilItCannotFit()
    {
        var allocator = CreateAllocator(policy: AllocationPolicies.Fill, spaces: new()
        {
            ["/disk/a"] = new DiskSpace(1_000, 900),
            ["/disk/b"] = new DiskSpace(1_000, 800)
        });

        Assert.Equal("a", allocator.Allocate("datasets", 100).DiskLabel);
        Assert.Equal("a", allocator.Allocate("datasets", 850).DiskLabel);
    }

    [Fact]
    public void BalancePercent_ChoosesLowestUsedPercent()
    {
        var allocator = CreateAllocator(policy: AllocationPolicies.BalancePercent, spaces: new()
        {
            ["/disk/a"] = new DiskSpace(1_000, 700),
            ["/disk/b"] = new DiskSpace(2_000, 1_000)
        });

        var result = allocator.Allocate("datasets", 100);

        Assert.Equal("a", result.DiskLabel);
    }

    [Fact]
    public void DiskPinTag_ChoosesPinnedDiskInsteadOfMostFreeDisk()
    {
        var allocator = CreateAllocator(spaces: new()
        {
            ["/disk/a"] = new DiskSpace(1_000, 500),
            ["/disk/b"] = new DiskSpace(1_000, 900)
        });

        var result = allocator.Allocate("datasets", 100, ["root:a"]);

        Assert.True(result.Assigned);
        Assert.Equal("a", result.DiskLabel);
        Assert.Equal("/disk/a/datasets", result.SavePath);
    }

    [Fact]
    public void DiskPinTag_FailsWhenPinnedDiskIsUnknown()
    {
        var allocator = CreateAllocator(spaces: new()
        {
            ["/disk/a"] = new DiskSpace(1_000, 900)
        });

        var result = allocator.Allocate("datasets", 100, ["root:missing"]);

        Assert.False(result.Assigned);
        Assert.Contains("Pinned disk 'missing' is not configured", result.Reason);
    }

    [Fact]
    public void DiskPinTag_FailsWhenMultiplePinsConflict()
    {
        var allocator = CreateAllocator(spaces: new()
        {
            ["/disk/a"] = new DiskSpace(1_000, 900),
            ["/disk/b"] = new DiskSpace(1_000, 800)
        });

        var result = allocator.Allocate("datasets", 100, ["root:a", "root:b"]);

        Assert.False(result.Assigned);
        Assert.Contains("Multiple disk pin tags", result.Reason);
    }

    [Fact]
    public void CategoryPin_ChoosesPinnedDiskInsteadOfMostFreeDisk()
    {
        var options = CreateOptions();
        options.CategoryPins["datasets-disk-a"] = "a";
        var allocator = new AllocationService(
            Options.Create(options),
            new FakeDiskSpaceProvider(new()
            {
                ["/disk/a"] = new DiskSpace(1_000, 500),
                ["/disk/b"] = new DiskSpace(1_000, 900)
            }));

        var result = allocator.Allocate("datasets-disk-a", 100);

        Assert.True(result.Assigned);
        Assert.Equal("a", result.DiskLabel);
        Assert.Equal("/disk/a/datasets-disk-a", result.SavePath);
    }

    [Fact]
    public void CategoryPin_FailsWhenTagPinConflicts()
    {
        var options = CreateOptions();
        options.CategoryPins["datasets-disk-a"] = "a";
        var allocator = new AllocationService(
            Options.Create(options),
            new FakeDiskSpaceProvider(new()
            {
                ["/disk/a"] = new DiskSpace(1_000, 900),
                ["/disk/b"] = new DiskSpace(1_000, 800)
            }));

        var result = allocator.Allocate("datasets-disk-a", 100, ["root:b"]);

        Assert.False(result.Assigned);
        Assert.Contains("Category pins disk 'a' but tag pins disk 'b'", result.Reason);
    }

    [Theory]
    [InlineData(null, "default")]
    [InlineData("", "default")]
    [InlineData("Research Data", "Research_Data")]
    [InlineData("../bad/path", "badpath")]
    public void CategorySanitization_IsPathSafe(string? category, string expected)
    {
        Assert.Equal(expected, PathBuilder.SanitizeCategory(category, "default"));
    }

    [Fact]
    public void SavePathTemplate_CanUseFixedFolderInsteadOfCategory()
    {
        var options = CreateOptions();
        options.SavePathTemplate = "{disk}/torrents";
        var allocator = new AllocationService(
            Options.Create(options),
            new FakeDiskSpaceProvider(new()
            {
                ["/disk/a"] = new DiskSpace(1_000, 900),
                ["/disk/b"] = new DiskSpace(1_000, 800)
            }));

        var result = allocator.Allocate("linux-isos", 100);

        Assert.Equal("/disk/a/torrents", result.SavePath);
    }

    [Fact]
    public void DiskSavePathTemplate_OverridesGlobalTemplate()
    {
        var options = CreateOptions();
        options.SavePathTemplate = "{disk}/global";
        options.Disks[0].SavePathTemplate = "{disk}/torrents/{category}";
        var allocator = new AllocationService(
            Options.Create(options),
            new FakeDiskSpaceProvider(new()
            {
                ["/disk/a"] = new DiskSpace(1_000, 900),
                ["/disk/b"] = new DiskSpace(1_000, 800)
            }));

        var result = allocator.Allocate("Research Data", 100);

        Assert.Equal("/disk/a/torrents/Research_Data", result.SavePath);
    }

    [Fact]
    public void Allocation_AccountsForPlannedReservationsInSameRun()
    {
        var allocator = CreateAllocator(spaces: new()
        {
            ["/disk/a"] = new DiskSpace(1_000, 900),
            ["/disk/b"] = new DiskSpace(1_000, 800)
        });
        var reservations = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
        {
            ["a"] = 200
        };

        var result = allocator.Allocate("linux-isos", 750, null, reservations);

        Assert.Equal("b", result.DiskLabel);
    }

    [Fact]
    public async Task MultipartAddRewrite_InjectsSavePathAndAutoTmm_WithoutForcingPaused()
    {
        var allocator = CreateAllocator(spaces: new() { ["/disk/a"] = new DiskSpace(1_000_000, 900_000) });
        var rewriter = new AddRequestRewriter(allocator, new AllocatorStatusStore());
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/api/v2/torrents/add";
        await SetMultipartBodyAsync(context.Request, new Dictionary<string, string>
        {
            ["urls"] = "magnet:?xt=urn:btih:abc",
            ["category"] = "linux-isos",
            ["tags"] = "existing",
            ["paused"] = "false",
            ["autoTMM"] = "true",
            ["savepath"] = "/wrong"
        });

        var rewritten = await rewriter.RewriteAsync(context.Request, CancellationToken.None);
        var body = await rewritten.Content!.ReadAsStringAsync();

        Assert.True(rewritten.Assigned);
        Assert.Contains("name=savepath", body);
        Assert.Contains("/disk/a/linux-isos", body);
        Assert.Contains("name=autoTMM", body);
        Assert.Contains("false", body);
        Assert.Contains("name=paused", body);
        Assert.Contains("name=tags", body);
        Assert.Contains("existing,qbit-assigned,disk:a", body);
    }

    [Fact]
    public async Task MultipartAddRewrite_AddsAssignmentAndDiskTags_WhenIncomingTagsAreMissing()
    {
        var allocator = CreateAllocator(spaces: new() { ["/disk/a"] = new DiskSpace(1_000_000, 900_000) });
        var rewriter = new AddRequestRewriter(allocator, new AllocatorStatusStore());
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/api/v2/torrents/add";
        await SetMultipartBodyAsync(context.Request, new Dictionary<string, string>
        {
            ["urls"] = "magnet:?xt=urn:btih:abc",
            ["category"] = "linux-isos"
        });

        var rewritten = await rewriter.RewriteAsync(context.Request, CancellationToken.None);
        var body = await rewritten.Content!.ReadAsStringAsync();

        Assert.True(rewritten.Assigned);
        Assert.Contains("name=tags", body);
        Assert.Contains("qbit-assigned,disk:a", body);
    }

    [Fact]
    public async Task MultipartAddRewrite_DeduplicatesAssignmentAndDiskTags()
    {
        var allocator = CreateAllocator(spaces: new() { ["/disk/a"] = new DiskSpace(1_000_000, 900_000) });
        var rewriter = new AddRequestRewriter(allocator, new AllocatorStatusStore());
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/api/v2/torrents/add";
        await SetMultipartBodyAsync(context.Request, new Dictionary<string, string>
        {
            ["urls"] = "magnet:?xt=urn:btih:abc",
            ["category"] = "linux-isos",
            ["tags"] = "existing,qbit-assigned,disk:a"
        });

        var rewritten = await rewriter.RewriteAsync(context.Request, CancellationToken.None);
        var body = await rewritten.Content!.ReadAsStringAsync();

        Assert.True(rewritten.Assigned);
        Assert.Contains("name=tags", body);
        Assert.Equal(1, CountOccurrences(body, "existing"));
        Assert.Equal(1, CountOccurrences(body, "qbit-assigned"));
        Assert.Equal(1, CountOccurrences(body, "disk:a"));
    }

    [Fact]
    public async Task MultipartAddRewrite_UsesConfiguredDiskTagPrefix()
    {
        var options = CreateOptions();
        options.DiskTagPrefix = "qbit-disk:";
        var allocator = new AllocationService(Options.Create(options), new FakeDiskSpaceProvider(new()
        {
            ["/disk/a"] = new DiskSpace(1_000_000, 900_000)
        }));
        var rewriter = new AddRequestRewriter(allocator, new AllocatorStatusStore());
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/api/v2/torrents/add";
        await SetMultipartBodyAsync(context.Request, new Dictionary<string, string>
        {
            ["urls"] = "magnet:?xt=urn:btih:abc",
            ["category"] = "linux-isos",
            ["tags"] = "existing"
        });

        var rewritten = await rewriter.RewriteAsync(context.Request, CancellationToken.None);
        var body = await rewritten.Content!.ReadAsStringAsync();

        Assert.True(rewritten.Assigned);
        Assert.Contains("existing,qbit-assigned,qbit-disk:a", body);
    }

    [Fact]
    public async Task MultipartAddRewrite_CanRemoveStaleIncomingDiskTags()
    {
        var options = CreateOptions();
        options.RemoveStaleDiskTagsOnProxyAdd = true;
        var allocator = new AllocationService(Options.Create(options), new FakeDiskSpaceProvider(new()
        {
            ["/disk/a"] = new DiskSpace(1_000_000, 900_000)
        }));
        var rewriter = new AddRequestRewriter(allocator, new AllocatorStatusStore());
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/api/v2/torrents/add";
        await SetMultipartBodyAsync(context.Request, new Dictionary<string, string>
        {
            ["urls"] = "magnet:?xt=urn:btih:abc",
            ["category"] = "linux-isos",
            ["tags"] = "existing,disk:old"
        });

        var rewritten = await rewriter.RewriteAsync(context.Request, CancellationToken.None);
        var body = await rewritten.Content!.ReadAsStringAsync();

        Assert.True(rewritten.Assigned);
        Assert.DoesNotContain("disk:old", body);
        Assert.Contains("existing,qbit-assigned,disk:a", body);
    }

    [Fact]
    public async Task MultipartAddRewrite_CanPassthroughWhenAllocationFails()
    {
        var options = CreateOptions(reserve: 950);
        options.OnAllocationFailure = AllocationFailureModes.Passthrough;
        var allocator = new AllocationService(Options.Create(options), new FakeDiskSpaceProvider(new()
        {
            ["/disk/a"] = new DiskSpace(1_000, 900),
            ["/disk/b"] = new DiskSpace(1_000, 900)
        }));
        var rewriter = new AddRequestRewriter(allocator, new AllocatorStatusStore());
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/api/v2/torrents/add";
        await SetMultipartBodyAsync(context.Request, new Dictionary<string, string>
        {
            ["urls"] = "magnet:?xt=urn:btih:abc",
            ["category"] = "linux-isos",
            ["savepath"] = "/caller/path",
            ["autoTMM"] = "true"
        });

        var rewritten = await rewriter.RewriteAsync(context.Request, CancellationToken.None);
        var body = await rewritten.Content!.ReadAsStringAsync();

        Assert.True(rewritten.Assigned);
        Assert.Contains("/caller/path", body);
        Assert.Contains("true", body);
        Assert.DoesNotContain("/disk/a/linux-isos", body);
    }

    [Fact]
    public async Task NonAddProxyRequest_PassesThroughMethodPathHeadersAndBody()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Accepted)
        {
            Content = new StringContent("ok")
        });
        var services = BuildServices(handler, CreateOptions());
        var proxy = services.GetRequiredService<QbitProxyService>();
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/api/v2/torrents/setCategory";
        context.Request.QueryString = new QueryString("?hashes=abc");
        context.Request.Headers.Cookie = "SID=123";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("hashes=abc"));
        context.Request.ContentLength = context.Request.Body.Length;

        await proxy.ForwardAsync(context, CancellationToken.None);

        Assert.Equal(HttpStatusCode.Accepted, (HttpStatusCode)context.Response.StatusCode);
        Assert.Equal("/api/v2/torrents/setCategory?hashes=abc", handler.Requests.Single().RequestUri!.PathAndQuery);
        Assert.Equal("SID=123", handler.Requests.Single().Headers.GetValues("Cookie").Single());
    }

    [Fact]
    public async Task ProxyRequest_BuffersFormBodyWithContentLength()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var services = BuildServices(handler, CreateOptions());
        var proxy = services.GetRequiredService<QbitProxyService>();
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/api/v2/torrents/start";
        context.Request.ContentType = "application/x-www-form-urlencoded";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("hashes=all"));
        context.Request.ContentLength = context.Request.Body.Length;

        await proxy.ForwardAsync(context, CancellationToken.None);

        var content = handler.RequestBodies.Single();
        Assert.Equal("hashes=all", content);
        Assert.Equal(10, handler.Requests.Single().Content!.Headers.ContentLength);
        Assert.Equal("application/x-www-form-urlencoded", handler.Requests.Single().Content!.Headers.ContentType!.MediaType);
    }

    [Theory]
    [InlineData("/api/v2/torrents/pause", "/api/v2/torrents/stop")]
    [InlineData("/api/v2/torrents/resume", "/api/v2/torrents/start")]
    public async Task ProxyRequest_RewritesLegacyPauseResumeEndpointsForQbit5(string incomingPath, string expectedPath)
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var services = BuildServices(handler, CreateOptions());
        var proxy = services.GetRequiredService<QbitProxyService>();
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = incomingPath;
        context.Request.ContentType = "application/x-www-form-urlencoded";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("hashes=all"));
        context.Request.ContentLength = context.Request.Body.Length;

        await proxy.ForwardAsync(context, CancellationToken.None);

        Assert.Equal(expectedPath, handler.Requests.Single().RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task QbitClient_PostsSetLocationAddTagsAndStart()
    {
        var handler = new RecordingHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/info"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("[]", Encoding.UTF8, "application/json")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var client = new QbitClient(new HttpClient(handler) { BaseAddress = new Uri("http://qbit/") }, Options.Create(CreateOptions()));

        await client.SetLocationAsync("abc", "/disk/a/linux-isos", CancellationToken.None);
        await client.AddTagsAsync("abc", "qbit-assigned", "disk:a");
        await client.RemoveTagsAsync("abc", "qbit-relocate");
        await client.StartAsync("abc", CancellationToken.None);

        Assert.Equal(["/api/v2/torrents/setLocation", "/api/v2/torrents/addTags", "/api/v2/torrents/removeTags", "/api/v2/torrents/start"],
            handler.Requests.Select(r => r.RequestUri!.AbsolutePath).ToArray());
    }

    [Fact]
    public async Task Reconciler_MovesTagsAndStartsNeverStartedPausedTorrent_AndSkipsStartedPausedTorrent()
    {
        var torrents = """
[
  {"hash":"new","name":"new torrent","category":"linux-isos","tags":"disk:old","state":"pausedDL","progress":0,"downloaded":0,"size":100,"last_activity":0},
  {"hash":"old","name":"old torrent","category":"linux-isos","tags":"","state":"pausedDL","progress":0.25,"downloaded":25,"size":100,"last_activity":10}
]
""";
        var handler = new RecordingHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/info"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(torrents, Encoding.UTF8, "application/json")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var services = BuildServices(handler, CreateOptions(), new FakeDiskSpaceProvider(new()
        {
            ["/disk/a"] = new DiskSpace(1_000, 900)
        }));

        await ReconcilerService.RunOnceAsync(services, CancellationToken.None);

        var postPaths = handler.Requests.Where(r => r.Method == HttpMethod.Post).Select(r => r.RequestUri!.AbsolutePath).ToArray();
        Assert.Equal(["/api/v2/torrents/setLocation", "/api/v2/torrents/removeTags", "/api/v2/torrents/addTags", "/api/v2/torrents/start"], postPaths);
        Assert.Contains("tags=disk%3Aold", handler.RequestBodies[1]);
        Assert.Contains("tags=qbit-assigned%2Cdisk%3Aa", handler.RequestBodies[2]);
        var status = services.GetRequiredService<AllocatorStatusStore>();
        Assert.Single(status.GetDecisions());
        Assert.Single(status.GetSkipped());
    }

    [Fact]
    public async Task Reconciler_RelocatesTorrentWithRelocateTag_AndRemovesTriggerTagWithoutStarting()
    {
        var torrents = """
[
  {"hash":"move","name":"move torrent","category":"linux-isos","tags":"qbit-relocate,keep,disk:old,disk:older","state":"downloading","progress":0.5,"downloaded":50,"size":100,"last_activity":10}
]
""";
        var handler = new RecordingHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/info"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(torrents, Encoding.UTF8, "application/json")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var options = CreateOptions();
        options.SavePathTemplate = "{disk}/torrents";
        var services = BuildServices(handler, options, new FakeDiskSpaceProvider(new()
        {
            ["/disk/a"] = new DiskSpace(1_000, 900),
            ["/disk/b"] = new DiskSpace(1_000, 800)
        }));

        await ReconcilerService.RunOnceAsync(services, CancellationToken.None);

        var postPaths = handler.Requests.Where(r => r.Method == HttpMethod.Post).Select(r => r.RequestUri!.AbsolutePath).ToArray();
        Assert.Equal(["/api/v2/torrents/setLocation", "/api/v2/torrents/removeTags", "/api/v2/torrents/addTags", "/api/v2/torrents/removeTags"], postPaths);
        Assert.Contains("location=%2Fdisk%2Fa%2Ftorrents", handler.RequestBodies[0]);
        Assert.Contains("tags=disk%3Aold%2Cdisk%3Aolder", handler.RequestBodies[1]);
        Assert.Contains("tags=qbit-assigned%2Cdisk%3Aa", handler.RequestBodies[2]);
        Assert.Contains("tags=qbit-relocate", handler.RequestBodies[3]);
        var status = services.GetRequiredService<AllocatorStatusStore>();
        Assert.Single(status.GetDecisions());
        Assert.Empty(status.GetSkipped());
    }

    [Fact]
    public async Task Reconciler_RelocateTag_AccountsForEarlierMovesInSameRun()
    {
        var torrents = """
[
  {"hash":"move1","name":"move one","category":"linux-isos","tags":"qbit-relocate","state":"downloading","progress":0.5,"downloaded":50,"size":700,"last_activity":10},
  {"hash":"move2","name":"move two","category":"linux-isos","tags":"qbit-relocate","state":"downloading","progress":0.5,"downloaded":50,"size":700,"last_activity":10}
]
""";
        var handler = new RecordingHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/info"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(torrents, Encoding.UTF8, "application/json")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var options = CreateOptions();
        options.SavePathTemplate = "{disk}/torrents";
        var services = BuildServices(handler, options, new FakeDiskSpaceProvider(new()
        {
            ["/disk/a"] = new DiskSpace(1_000, 900),
            ["/disk/b"] = new DiskSpace(1_000, 800)
        }));

        await ReconcilerService.RunOnceAsync(services, CancellationToken.None);

        var setLocationBodies = handler.RequestBodyByPath
            .Where(pair => pair.Path == "/api/v2/torrents/setLocation")
            .Select(pair => pair.Body)
            .ToArray();
        Assert.Equal(2, setLocationBodies.Length);
        Assert.Contains("location=%2Fdisk%2Fa%2Ftorrents", setLocationBodies[0]);
        Assert.Contains("location=%2Fdisk%2Fb%2Ftorrents", setLocationBodies[1]);
    }

    [Fact]
    public void DiskStatus_RendersConfiguredDiskValuesForUiStatus()
    {
        var allocator = CreateAllocator(spaces: new() { ["/disk/a"] = new DiskSpace(1_000, 750) }, reserve: 100);

        var disk = allocator.GetDiskStatuses().Single(d => d.Label == "a");

        Assert.Equal("a", disk.Label);
        Assert.Equal("/disk/a", disk.Path);
        Assert.Equal(750, disk.FreeBytes);
        Assert.Equal(25, disk.UsedPercent);
        Assert.True(disk.Enabled);
    }

    [Fact]
    public void AutoDetect_UsesChildDirectoriesWhenNoManualDisksAreConfigured()
    {
        using var temp = new TempDirectory();
        var diskA = Directory.CreateDirectory(Path.Combine(temp.Path, "disk-a")).FullName;
        var diskB = Directory.CreateDirectory(Path.Combine(temp.Path, "disk-b")).FullName;
        var options = CreateOptions();
        options.Disks = [];
        options.AutoDetect.RootPaths = [temp.Path];
        var allocator = new AllocationService(
            Options.Create(options),
            new FakeDiskSpaceProvider(new()
            {
                [diskA] = new DiskSpace(1_000, 500),
                [diskB] = new DiskSpace(1_000, 900)
            }),
            new AutoDetectDiskProvider());

        var result = allocator.Allocate("linux-isos", 100);

        Assert.True(result.Assigned);
        Assert.Equal("disk-b", result.DiskLabel);
        Assert.Equal(PathBuilder.BuildSavePath(diskB, "linux-isos"), result.SavePath);
    }

    [Fact]
    public void AutoDetect_DoesNotAddDisksWhenManualDisksAreConfigured()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(temp.Path, "detected"));
        var options = CreateOptions();
        options.AutoDetect.RootPaths = [temp.Path];
        var allocator = new AllocationService(
            Options.Create(options),
            new FakeDiskSpaceProvider(new()
            {
                ["/disk/a"] = new DiskSpace(1_000, 900),
                ["/disk/b"] = new DiskSpace(1_000, 800)
            }),
            new AutoDetectDiskProvider());

        var disks = allocator.GetDiskStatuses();

        Assert.Equal(["a", "b"], disks.Select(d => d.Label).ToArray());
    }

    private static AllocationService CreateAllocator(
        string policy = AllocationPolicies.MostFree,
        Dictionary<string, DiskSpace>? spaces = null,
        long reserve = 0)
    {
        return new AllocationService(Options.Create(CreateOptions(policy, reserve)), new FakeDiskSpaceProvider(spaces ?? []));
    }

    private static AllocatorOptions CreateOptions(string policy = AllocationPolicies.MostFree, long reserve = 0)
    {
        return new AllocatorOptions
        {
            UpstreamUrl = "http://qbit",
            Policy = policy,
            UnknownSizeBytes = 100,
            Disks =
            [
                new DiskOptions { Label = "a", Path = "/disk/a", MinFreeBytes = reserve },
                new DiskOptions { Label = "b", Path = "/disk/b", MinFreeBytes = reserve }
            ]
        };
    }

    private static ServiceProvider BuildServices(HttpMessageHandler handler, AllocatorOptions options, IDiskSpaceProvider? diskSpaceProvider = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(Options.Create(options));
        services.AddSingleton<IDiskSpaceProvider>(diskSpaceProvider ?? new FakeDiskSpaceProvider(new()));
        services.AddSingleton<AllocatorStatusStore>();
        services.AddSingleton<AllocationService>();
        services.AddSingleton<AddRequestRewriter>();
        services.AddSingleton(new HttpClient(handler) { BaseAddress = new Uri(options.UpstreamUrl + "/") });
        services.AddSingleton<QbitClient>();
        services.AddSingleton<QbitProxyService>();
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<ReconcilerService>>(NullLogger<ReconcilerService>.Instance);
        return services.BuildServiceProvider();
    }

    private static async Task SetMultipartBodyAsync(HttpRequest request, Dictionary<string, string> values)
    {
        using var content = new MultipartFormDataContent();
        foreach (var value in values)
        {
            content.Add(new StringContent(value.Value), value.Key);
        }

        var stream = new MemoryStream();
        await content.CopyToAsync(stream);
        stream.Position = 0;
        request.Body = stream;
        request.ContentType = content.Headers.ContentType!.ToString();
        request.ContentLength = stream.Length;
    }

    private static int CountOccurrences(string value, string search)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(search, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += search.Length;
        }

        return count;
    }
}

internal sealed class FakeDiskSpaceProvider : IDiskSpaceProvider
{
    private readonly Dictionary<string, DiskSpace> _spaces;

    public FakeDiskSpaceProvider(Dictionary<string, DiskSpace> spaces)
    {
        _spaces = spaces;
    }

    public DiskSpace GetSpace(string path) => _spaces.TryGetValue(path, out var space) ? space : new DiskSpace(0, 0);
}

internal sealed class RecordingHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
    public List<HttpRequestMessage> Requests { get; } = [];
    public List<string> RequestBodies { get; } = [];
    public List<(string Path, string Body)> RequestBodyByPath { get; } = [];

    public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        _respond = respond;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Content is not null)
        {
            var body = await request.Content.ReadAsStringAsync(cancellationToken);
            RequestBodies.Add(body);
            RequestBodyByPath.Add((request.RequestUri!.AbsolutePath, body));
        }
        Requests.Add(request);
        return _respond(request);
    }
}

internal sealed class TempDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"qbit-allocator-tests-{Guid.NewGuid():N}");

    public TempDirectory()
    {
        Directory.CreateDirectory(Path);
    }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
