// PhotonCanvas remote server: lets a phone watch and run the laser through this computer.
// It keeps its own connection to the machine (beside the desktop app's) and answers the phone over
// HTTP on the home network (or Tailscale). Every /api request needs the access code. The API is in
// docs/remote-api.md; the phone page it serves at / uses the same API.
using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using PhotonCanvas.Core;
using PhotonCanvas.Device;
using PhotonCanvas.Doc;
using PhotonCanvas.Remote;
using PhotonCanvas.Server;

var config = RemoteConfig.LoadOrCreate();
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = AppContext.BaseDirectory });
builder.Logging.SetMinimumLevel(LogLevel.Warning);
// --local: this computer only (for trying it out); otherwise every network (the phone needs that).
bool localOnly = args.Contains("--local");
builder.WebHost.UseUrls(localOnly ? $"http://localhost:{config.Port}" : $"http://*:{config.Port}");
builder.Services.AddSingleton(config);
builder.Services.AddSingleton<Broadcaster>();
builder.Services.AddSingleton(_ => new JobLog());
builder.Services.AddSingleton<Projects>();
builder.Services.AddSingleton<AccessGuard>();
builder.Services.AddSingleton<MachineLink>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<MachineLink>());
var app = builder.Build();

// Plain-words errors as {"error": "..."}.
app.Use(async (ctx, next) =>
{
    try { await next(ctx); }
    catch (RemoteException e) { await Api.Error(ctx, e.Status, e.Message); }
    catch (DeviceException e) { await Api.Error(ctx, 502, e.Message); }
    catch (OperationCanceledException) when (ctx.RequestAborted.IsCancellationRequested) { }
});

// The access code, on everything under /api.
app.Use(async (ctx, next) =>
{
    if (ctx.Request.Path.StartsWithSegments("/api")
        && ctx.RequestServices.GetRequiredService<AccessGuard>().Check(
            ctx.Connection.RemoteIpAddress?.ToString(), ctx.Request.Headers.Authorization, ctx.Request.Query["access_token"], DateTime.Now) is { } refused)
    {
        await Api.Error(ctx, refused.Status, refused.Error);
        return;
    }
    await next(ctx);
});

// ---- the phone page --------------------------------------------------------------------------
app.MapGet("/", () => Api.Embedded("index.html", "text/html; charset=utf-8"));
app.MapGet("/manifest.webmanifest", () => Api.Embedded("manifest.webmanifest", "application/manifest+json"));
app.MapGet("/icon.svg", () => Api.Embedded("icon.svg", "image/svg+xml"));
// The Android app, for phones to install before it's in the Play store (no access code: it's only the app).
app.MapGet(RemoteConfig.AndroidAppPath, () => RemoteConfig.FindAndroidApp() is { } apk
    ? Results.File(apk, "application/vnd.android.package-archive", "PhotonCanvasRemote.apk")
    : Results.NotFound("This computer doesn't have the Android app to give out."));

// ---- status ----------------------------------------------------------------------------------
app.MapGet("/api/info", (MachineLink m) => new ServerInfo(AppIdentity.ProductName, typeof(Api).Assembly.GetName().Version?.ToString(3), m.Status().Name,
    "The machine blanks its cameras while a job runs: pictures come out black until it ends.",
    AndroidApp: RemoteConfig.FindAndroidApp() is null ? null : RemoteConfig.AndroidAppPath));
app.MapGet("/api/status", (MachineLink m) => m.Status());
app.MapGet("/api/events", Api.Events);

// ---- camera ----------------------------------------------------------------------------------
app.MapGet("/api/camera/{which}", async (string which, int? width, MachineLink m, HttpContext ctx) =>
{
    int stream = which switch
    {
        "overview" => 0,
        "closeup" or "close-up" => 1,
        _ => throw new RemoteException(404, "The cameras are \"overview\" and \"closeup\"."),
    };
    var pic = await m.TakePictureAsync(stream, ctx.RequestAborted);
    return Api.Jpeg(ctx, pic, width);
});
app.MapGet("/api/pictures/{id}", (string id, int? width, MachineLink m, HttpContext ctx) =>
    m.Pictures.Get(id) is { } pic ? Api.Jpeg(ctx, pic, width) : throw new RemoteException(404, "That picture is gone (only the last few are kept). Take a new one."));

// ---- designs ---------------------------------------------------------------------------------
app.MapGet("/api/projects", (Projects p) => p.List().Select(x => new ProjectItem(x.Id, x.Name, x.Folder, x.Modified, x.Recent)).ToList());
app.MapPost("/api/projects/{id}/check", async (string id, Projects p, MachineLink m, HttpContext ctx) =>
    Api.Check(await Api.Build(id, p, m, ctx.RequestAborted), m));
app.MapGet("/api/projects/{id}/preview.png", async (string id, int? width, Projects p, MachineLink m, HttpContext ctx) =>
{
    var b = p.LastBuild(id) ?? await Api.Build(id, p, m, ctx.RequestAborted);
    return Results.Bytes(Projects.Preview(b, width ?? 800), "image/png");
});
app.MapPost("/api/projects/{id}/load", async (string id, Projects p, MachineLink m, HttpContext ctx) =>
{
    var b = await Api.Build(id, p, m, ctx.RequestAborted);
    if (b.Review.Refusal is { } refusal) throw new RemoteException(409, refusal);
    var (taskId, pic) = await m.LoadAsync(b, Api.Who(ctx), ctx.RequestAborted);
    return new LoadResult(taskId, b.Design, b.Job.EstimateS, pic is null ? null : Api.Describe(pic), pic is null
        ? $"\"{b.Design}\" is loaded. Take a picture of the bed, look at it, then start."
        : $"\"{b.Design}\" is loaded. Look at the picture: is the material in place and the bed clear? Then start.");
});

// ---- the job ---------------------------------------------------------------------------------
app.MapPost("/api/job/start", async (StartRequest? body, MachineLink m, HttpContext ctx) =>
{
    string mode = await m.StartAsync(body?.PictureId, Api.Who(ctx), ctx.RequestAborted);
    return new ModeResult(mode, DeviceClient.DescribeMode(mode));
});
foreach (var action in new[] { "pause", "resume", "stop" })
    app.MapPost($"/api/job/{action}", async (MachineLink m, HttpContext ctx) =>
    {
        string mode = await m.ControlAsync(action, Api.Who(ctx), ctx.RequestAborted);
        return new ModeResult(mode, DeviceClient.DescribeMode(mode));
    });

// ---- history ---------------------------------------------------------------------------------
app.MapGet("/api/history", (int? max, JobLog log) => log.Newest(Math.Clamp(max ?? 50, 1, 300)).Select(r => new HistoryItem(
    r.Id, r.Design, r.Source, MachineLink.SourceText(r.Source), r.LoadedAt, r.StartedAt, r.EndedAt, r.Finished, r.StartedFromPhone, r.EstimateS, r.TookS,
    r.HasBefore ? $"/api/history/{r.Id}/before.jpg" : null,
    r.HasAfter ? $"/api/history/{r.Id}/after.jpg" : null)).ToList());
app.MapGet("/api/history/{id}/{name}", (string id, string name, int? width, JobLog log) =>
    log.ReadPicture(id, name) is { } jpg
        ? Results.Bytes(width is { } w ? PictureStore.Scaled(jpg, w) : jpg, "image/jpeg")
        : throw new RemoteException(404, "No such picture."));

// ---- start ----------------------------------------------------------------------------------
Console.WriteLine($"{AppIdentity.ProductName} remote server on port {config.Port}.{(localOnly ? " This computer only: open" : " On the phone, open:")}");
foreach (var (address, tailscale) in localOnly ? [("localhost", false)] : RemoteConfig.Addresses())
    Console.WriteLine($"  http://{address}:{config.Port}{(tailscale ? "   (Tailscale: works away from home)" : "")}");
Console.WriteLine($"Access code: {config.AccessCode}");
ServerLog.Write("server", $"Started on port {config.Port} (data in {AppIdentity.DataDir}).");
try
{
    await app.RunAsync();
}
catch (IOException e)
{
    ServerLog.Write("server", $"Couldn't listen on port {config.Port}: {e.Message} (is the server already running?)");
    return 3;
}
return 0;

/// <summary>Small pieces the endpoints share.</summary>
static class Api
{
    public static Task Error(HttpContext ctx, int status, string message)
    {
        if (ctx.Response.HasStarted) return Task.CompletedTask;
        ctx.Response.StatusCode = status;
        return ctx.Response.WriteAsJsonAsync(new { error = message });
    }

    /// <summary>Who asked, for the log.</summary>
    public static string Who(HttpContext ctx) => ctx.Connection.RemoteIpAddress?.ToString() ?? "?";

    public static IResult Embedded(string name, string contentType) =>
        typeof(Api).Assembly.GetManifestResourceStream("web/" + name) is { } s ? Results.Stream(s, contentType) : Results.NotFound();

    public static PictureInfo Describe(Picture p) => new(p.Id, p.TakenAt, p.Blanked, Routes.Picture(p.Id));

    /// <summary>A picture, smaller if asked (default 1280 px across), with what it is in the headers.</summary>
    public static IResult Jpeg(HttpContext ctx, Picture p, int? width)
    {
        ctx.Response.Headers.CacheControl = "no-store";
        ctx.Response.Headers["X-Picture-Id"] = p.Id;
        ctx.Response.Headers["X-Taken-At"] = p.TakenAt.ToString("O");
        ctx.Response.Headers["X-Camera-Blanked"] = p.Blanked ? "true" : "false";
        return Results.Bytes(PictureStore.Scaled(p.Jpeg, Math.Clamp(width ?? 1280, 160, 4000)), "image/jpeg");
    }

    public static async Task<ProjectBuild> Build(string id, Projects p, MachineLink m, CancellationToken ct) =>
        await p.BuildAsync(p.Find(id) ?? throw new RemoteException(404, "That design isn't in the list any more."), m.FocusDistanceAsync, ct);

    /// <summary>What the desktop's Ship It shows before sending, for the phone.</summary>
    public static CheckResult Check(ProjectBuild b, MachineLink m)
    {
        string? loaded = m.LoadedJobNote();
        var text = JobChecks.Text(b.Review);
        if (loaded is not null) text += $"ⓘ {loaded}\n";
        if (b.PhoneNote is { } note) text += $"ⓘ {note}\n";
        text += "\nAfter loading, the machine waits. Nothing moves or fires until you start it: from the phone (after a look at the bed) or with its button.";
        return new CheckResult(b.Project.Id, b.Design, b.Job.EstimateS, TimeText.Duration(b.Job.EstimateS),
            Math.Round(b.Job.Bounds.Width, 1), Math.Round(b.Job.Bounds.Height, 1), b.Review.Refusal, b.Review.NotInJob,
            [.. b.Review.Lines.Where(l => l.Length > 0)], b.Review.Warnings, loaded, b.PhoneNote, text, Routes.Preview(b.Project.Id));
    }

    /// <summary>GET /api/events: the status now, then every change, as server-sent events.</summary>
    public static async Task Events(HttpContext ctx, Broadcaster b, MachineLink m)
    {
        ctx.Response.Headers.ContentType = "text/event-stream";
        ctx.Response.Headers.CacheControl = "no-cache";
        ctx.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
        var ct = ctx.RequestAborted;
        using var sub = b.Subscribe();
        await Send(new ServerEvent("status", JsonSerializer.Serialize(m.Status(), Broadcaster.JsonOptions)));
        while (!ct.IsCancellationRequested)
        {
            using var tick = CancellationTokenSource.CreateLinkedTokenSource(ct);
            tick.CancelAfter(TimeSpan.FromSeconds(15));
            ServerEvent e;
            try { e = await sub.Reader.ReadAsync(tick.Token); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Nothing for a while: a comment line keeps the connection open.
                await ctx.Response.WriteAsync(": ping\n\n", ct);
                await ctx.Response.Body.FlushAsync(ct);
                continue;
            }
            catch (System.Threading.Channels.ChannelClosedException) { break; }
            await Send(e);
        }

        async Task Send(ServerEvent e)
        {
            await ctx.Response.WriteAsync($"event: {e.Kind}\ndata: {e.Json}\n\n", ct);
            await ctx.Response.Body.FlushAsync(ct);
        }
    }
}
