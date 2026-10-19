using PriceExplorer.Api.Hosting;
using Serilog;

// Console logger for failures before the host is built. The host gets its own configured logger (see
// AddExplorerObservability), so several hosts in one process (integration tests) never share logger state.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.AddExplorerObservability();
    builder.Services
        .AddExplorerApi()
        .AddExplorerIngestion();

    var app = builder.Build();

    app.UseExplorerApi();

    await app.RunAsync();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Price Explorer API terminated unexpectedly");
    Environment.ExitCode = 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}
