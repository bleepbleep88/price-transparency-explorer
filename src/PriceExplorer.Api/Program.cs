using PriceExplorer.Api.Hosting;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.AddExplorerObservability();
    builder.Services.AddExplorerApi();

    var app = builder.Build();

    app.UseExplorerApi();

    await app.RunAsync();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Price Explorer API terminated unexpectedly");
}
finally
{
    await Log.CloseAndFlushAsync();
}
