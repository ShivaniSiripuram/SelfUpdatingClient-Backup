using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using System.Text.Json.Serialization;
using POS_UPDATER_SYSTEM.Api.Models;
using POS_UPDATER_SYSTEM.Api.Options;
using POS_UPDATER_SYSTEM.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Ensure ContentRootPath points to the project content root instead of the
// runtime output (bin) folder when running from the debugger or as a
// framework-dependent executable. If the environment's ContentRootPath
// appears to be inside a build output folder (contains "bin" and a
// target framework segment like "net"), climb up to the project root
// so that Storage resolves to <project-root>/Storage instead of
// <project-root>/bin/.../Storage.
var initialContentRoot = builder.Environment.ContentRootPath ?? string.Empty;
bool LooksLikeBuildOutput(string path)
{
    if (string.IsNullOrWhiteSpace(path)) return false;
    var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    var hasBin = parts.Any(p => string.Equals(p, "bin", StringComparison.OrdinalIgnoreCase));
    var hasNet = parts.Any(p => p.StartsWith("net", StringComparison.OrdinalIgnoreCase));
    return hasBin && hasNet;
}

if (LooksLikeBuildOutput(initialContentRoot))
{
    // move up three levels: bin/{Configuration}/{TargetTFM} -> project root
    var candidate = Path.GetFullPath(Path.Combine(initialContentRoot, "..", "..", ".."));
    try
    {
        // Only override if candidate looks like a reasonable folder (contains project file)
        var projFiles = Directory.EnumerateFiles(candidate, "*.csproj", SearchOption.TopDirectoryOnly);
        if (projFiles.Any())
        {
            builder.Host.UseContentRoot(candidate);
            Console.WriteLine($"Overriding ContentRootPath from '{initialContentRoot}' to project root '{candidate}'");
        }
    }
    catch
    {
        // ignore and keep original content root if any IO error
    }
}

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();
builder.Logging.AddProvider(new OperationFileLoggerProvider());

builder.Services.Configure<UpdaterOptions>(builder.Configuration.GetSection(UpdaterOptions.SectionName));
builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddHttpClient();
builder.Services.AddCors(options =>
{
    options.AddPolicy("UpdaterFrontend", policy =>
    {
        policy.WithOrigins(
                "http://localhost:4200",
                "https://localhost:4200",
                "http://localhost:5075")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddSingleton<StoragePaths>();
builder.Services.AddSingleton<IDeploymentStateStore, DeploymentStateStore>();
builder.Services.AddSingleton<IUpdateClient>(sp =>
{
    var factory = sp.GetRequiredService<IHttpClientFactory>();
    return new UpdateClient(
        factory.CreateClient(nameof(UpdateClient)),
        sp.GetRequiredService<StoragePaths>(),
        sp.GetRequiredService<IOptions<UpdaterOptions>>());
});
builder.Services.AddSingleton<IPackageVerifier, PackageVerifier>();
builder.Services.AddSingleton<IStagingService, StagingService>();
builder.Services.AddSingleton<IActivationService, ActivationService>();
builder.Services.AddSingleton<IRollbackService, RollbackService>();
builder.Services.AddSingleton<IHostRuntimeManager, StaticFileHostRuntimeManager>();
builder.Services.AddSingleton<ILiveAppValidator>(sp =>
{
    var factory = sp.GetRequiredService<IHttpClientFactory>();
    return new LiveAppValidator(
        factory.CreateClient(nameof(LiveAppValidator)),
        sp.GetRequiredService<StoragePaths>(),
        sp.GetRequiredService<IOptions<UpdaterOptions>>());
});
builder.Services.AddSingleton<IDeploymentOrchestrator, DeploymentOrchestrator>();
builder.Services.AddHostedService<UpdateWatcherService>();

var app = builder.Build();
Console.WriteLine($"CONTENT ROOT: {builder.Environment.ContentRootPath}");

var paths = app.Services.GetRequiredService<StoragePaths>();
Console.WriteLine($"CURRENT PATH: {paths.Current}");
Console.WriteLine($"STAGING PATH: {paths.Staging}");
Console.WriteLine($"DOWNLOADS PATH: {paths.Downloads}");
paths.EnsureInitialized();

var stateStore = app.Services.GetRequiredService<IDeploymentStateStore>();
await stateStore.UpdateAsync(stateObj =>
{
    var state = stateObj;

    if (state.IsUpdating || IsTransientStatus(state.Status))
    {
        state.IsUpdating = false;
        state.Status = DeploymentStatus.FAILED;
        state.LastError = "Updater restarted while a deployment transition was in progress.";
    }

    return state;
}, CancellationToken.None);

app.Use(async (context, next) =>
{
    if (!context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
    {
        var state = await stateStore.GetAsync(CancellationToken.None);
        if (state.IsUpdating)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.Headers.CacheControl = "no-store";
            try
            {
                await context.Response.WriteAsync("""
                    <!doctype html>
                    <html lang="en">
                    <head>
                      <meta charset="utf-8">
                      <meta name="viewport" content="width=device-width, initial-scale=1">
                      <title>System Updating</title>
                      <style>
                        body{margin:0;min-height:100vh;display:grid;place-items:center;font-family:Segoe UI,Arial,sans-serif;background:#f4f7f6;color:#18201d}
                        main{max-width:520px;padding:32px;text-align:center}
                        h1{margin:0 0 12px;font-size:32px}
                        p{margin:0;color:#52645e;font-size:16px;line-height:1.5}
                      </style>
                    </head>
                    <body><main><h1>System Updating...</h1><p>The POS application is temporarily unavailable while a deployment is being applied.</p></main></body>
                    </html>
                    """, context.RequestAborted);
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                return;
            }

            return;
        }
    }

    await next();
});

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(paths.Current),
    RequestPath = string.Empty
});

app.UseCors("UpdaterFrontend");
app.UseAuthorization();

app.MapControllers();

app.MapFallback(async context =>
{
    var indexPath = Path.Combine(paths.Current, "index.html");

    Console.WriteLine($"INDEX PATH: {indexPath}");
    if (!File.Exists(indexPath))
{
    Console.WriteLine($"INDEX PATH NOT FOUND: {indexPath}");

    context.Response.StatusCode = StatusCodes.Status404NotFound;

    await context.Response.WriteAsync(
        "No deployed Mini POS app exists in Storage/Current.");

    return;
}

    context.Response.ContentType = "text/html";
    await context.Response.SendFileAsync(indexPath);
});

app.Run();

static bool IsTransientStatus(DeploymentStatus status)
{
    return status is DeploymentStatus.CHECKING
        or DeploymentStatus.DOWNLOADING
        or DeploymentStatus.VERIFYING
        or DeploymentStatus.STAGING
        or DeploymentStatus.VALIDATING_STATIC
        or DeploymentStatus.VALIDATING_RUNTIME
        or DeploymentStatus.BACKING_UP
        or DeploymentStatus.ACTIVATING
        or DeploymentStatus.RESTARTING
        or DeploymentStatus.ROLLBACK;
}
