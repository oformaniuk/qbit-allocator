using System.Text.Json;
using QbitAllocator;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<AllocatorOptions>(builder.Configuration.GetSection("Allocator"));
builder.Services.AddSingleton<IDiskSpaceProvider, DriveInfoDiskSpaceProvider>();
builder.Services.AddSingleton<IAutoDetectDiskProvider, AutoDetectDiskProvider>();
builder.Services.AddSingleton<AllocatorStatusStore>();
builder.Services.AddSingleton<AllocationService>();
builder.Services.AddSingleton<AddRequestRewriter>();
builder.Services.AddHttpClient<QbitClient>((sp, client) =>
{
    var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AllocatorOptions>>().Value;
    client.BaseAddress = new Uri(options.UpstreamUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(Math.Max(1, options.QbitClientTimeoutSeconds));
});
builder.Services.AddHttpClient<QbitProxyService>((sp, client) =>
{
    var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AllocatorOptions>>().Value;
    client.BaseAddress = new Uri(options.UpstreamUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(Math.Max(1, options.ProxyTimeoutSeconds));
});
builder.Services.AddHostedService<ReconcilerService>();

var app = builder.Build();

app.MapGet("/", () => Results.Content(UiHtml.Page, "text/html"));
app.MapGet("/status", async (
    AllocationService allocator,
    AllocatorStatusStore status,
    QbitClient qbit,
    CancellationToken cancellationToken) =>
{
    var disks = allocator.GetDiskStatuses();
    var upstream = await qbit.GetConnectivityAsync(cancellationToken);
    int? pausedUnassigned = upstream.Authenticated
        ? await qbit.CountPausedNeverStartedUnassignedAsync(cancellationToken)
        : null;

    return Results.Json(new
    {
        upstream,
        policy = allocator.Options.Policy,
        reconciler = new
        {
            enabled = allocator.Options.ReconcilerEnabled,
            intervalSeconds = allocator.Options.ReconcileIntervalSeconds,
            lastRunUtc = status.LastReconcileUtc,
            lastError = status.LastReconcileError
        },
        disks,
        recentDecisions = status.GetDecisions(),
        recentSkipped = status.GetSkipped(),
        pausedNeverStartedUnassigned = pausedUnassigned
    }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
});

app.MapMethods("/api/v2/{**path}", ["GET", "POST", "PUT", "DELETE", "PATCH", "HEAD"], async (
    HttpContext context,
    QbitProxyService proxy,
    CancellationToken cancellationToken) =>
{
    await proxy.ForwardAsync(context, cancellationToken);
});

app.Run();

public partial class Program;
