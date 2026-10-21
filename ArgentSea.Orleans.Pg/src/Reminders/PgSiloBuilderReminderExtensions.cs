using Microsoft.Extensions.DependencyInjection;

namespace ArgentSea.Orleans.Pg;

/// <summary>
/// Registers the PostgreSQL reminder table. The method names carry “Pg” so they never collide with
/// ArgentSea.Orleans.Sql's UseArgentSeaReminderService.
/// </summary>
public static class PgSiloBuilderReminderExtensions
{
    /// <summary>
    /// Adds reminder storage backed by ArgentSea over PostgreSQL.
    /// </summary>
    /// <param name="builder">The builder.</param>
    /// <param name="configure">The delegate used to configure the reminder store.</param>
    /// <returns>The provided <see cref="ISiloBuilder"/>, for chaining.</returns>
    public static ISiloBuilder UseArgentSeaPgReminderService(this ISiloBuilder builder, Action<ReminderOptions> configure)
    {
        builder.ConfigureServices(services => services.UseArgentSeaPgReminderService(configure));
        return builder;
    }

    /// <summary>
    /// Adds reminder storage backed by ArgentSea over PostgreSQL.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">The delegate used to configure the reminder store.</param>
    /// <returns>The provided <see cref="IServiceCollection"/>, for chaining.</returns>
    public static IServiceCollection UseArgentSeaPgReminderService(this IServiceCollection services, Action<ReminderOptions> configure)
    {
        services.AddReminders();
        services.Configure(configure);
        services.AddSingleton<IReminderTable, ArgentSeaOrleansReminderTable>();
        services.AddSingleton<IConfigurationValidator, ArgentSeaReminderOptionsValidator>();
        return services;
    }
}
