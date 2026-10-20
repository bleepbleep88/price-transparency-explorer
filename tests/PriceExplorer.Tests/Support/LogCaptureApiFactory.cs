using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;
using Serilog.Events;

namespace PriceExplorer.Tests.Support;

/// <summary>The real API (see <see cref="ExplorerApiFactory"/>) with a sink that keeps every warning and error it logs.</summary>
public class LogCaptureApiFactory : ExplorerApiFactory
{
    public ConcurrentQueue<LogEvent> Events { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        // The app's Serilog logger reads sinks registered as services.
        builder.ConfigureTestServices(services => services.AddSingleton<ILogEventSink>(new Sink(Events)));
    }

    private sealed class Sink(ConcurrentQueue<LogEvent> events) : ILogEventSink
    {
        public void Emit(LogEvent logEvent) => events.Enqueue(logEvent);
    }
}
