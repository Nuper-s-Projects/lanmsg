using LanMsg.Service;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

if (args.Contains("--console"))
{
    await Host.CreateDefaultBuilder(args)
        .UseConsoleLifetime()
        .ConfigureServices(s => s.AddHostedService<LanMsgWorker>())
        .Build()
        .RunAsync();
}
else
{
    await Host.CreateDefaultBuilder(args)
        .UseWindowsService(o => o.ServiceName = "LanMsgService")
        .ConfigureServices(s => s.AddHostedService<LanMsgWorker>())
        .Build()
        .RunAsync();
}
