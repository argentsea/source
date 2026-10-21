using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Configuration;
using Orleans.Providers;

[assembly: RegisterProvider("ArgentSeaPg", "Reminders", "Silo", typeof(ArgentSea.Orleans.Pg.ArgentSeaRemindersProviderBuilder))]

namespace ArgentSea.Orleans.Pg;

internal sealed class ArgentSeaRemindersProviderBuilder : IProviderBuilder<ISiloBuilder>
{
    public void Configure(ISiloBuilder builder, string? name, IConfigurationSection configurationSection)
    {
        builder.Services.AddOptions<ClusterOptions>();
        builder.UseArgentSeaPgReminderService(_ => { });
    }
}
