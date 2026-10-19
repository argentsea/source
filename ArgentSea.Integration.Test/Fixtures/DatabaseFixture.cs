using DotNet.Testcontainers.Containers;

namespace ArgentSea.Integration.Test.Fixtures;

/// <summary>
/// Starts one database container per test run, or uses an explicit connection string from the environment instead.
/// When neither is possible (no Docker engine, image pull failure, start timeout) the fixture records a reason and every
/// test in the collection skips through <see cref="SkipUnlessAvailable"/> rather than failing.
/// </summary>
public abstract class DatabaseFixture : IAsyncLifetime
{
    private IDatabaseContainer? _container;

    /// <summary>Human name used in skip reasons.</summary>
    protected abstract string DisplayName { get; }

    /// <summary>Environment variable that, when set, supplies a connection string to an existing disposable server and bypasses containers.</summary>
    protected abstract string OverrideVariable { get; }

    protected abstract IDatabaseContainer CreateContainer();

    /// <summary>
    /// Upper bound on the container start, image pull included. A missing Docker engine fails within seconds (Testcontainers
    /// cannot resolve an endpoint), so this only guards against a hung daemon; ARGENTSEA_CONTAINER_START_TIMEOUT_SECONDS overrides it.
    /// </summary>
    public static TimeSpan StartTimeout
    {
        get
        {
            var raw = Environment.GetEnvironmentVariable("ARGENTSEA_CONTAINER_START_TIMEOUT_SECONDS");
            return int.TryParse(raw, out var seconds) && seconds > 0 ? TimeSpan.FromSeconds(seconds) : TimeSpan.FromMinutes(5);
        }
    }

    /// <summary>The connection string of the server under test, or null when no server is available.</summary>
    public string? ConnectionString { get; protected set; }

    /// <summary>Why no server is available; null when <see cref="IsAvailable"/>.</summary>
    public string? SkipReason { get; private set; }

    public bool IsAvailable => ConnectionString is not null;

    /// <summary>True when the server is a Testcontainers container started by this fixture (as opposed to an environment override).</summary>
    public bool UsesContainer => IsAvailable && _container is not null;

    public async Task InitializeAsync()
    {
        var @override = Environment.GetEnvironmentVariable(OverrideVariable);
        if (!string.IsNullOrWhiteSpace(@override))
        {
            ConnectionString = @override;
            await OnAvailableAsync();
            return;
        }
        if (Environment.GetEnvironmentVariable("ARGENTSEA_SKIP_CONTAINERS") == "1")
        {
            SkipReason = $"ARGENTSEA_SKIP_CONTAINERS=1 and {OverrideVariable} is not set, so no {DisplayName} server is available.";
            return;
        }
        try
        {
            _container = CreateContainer();
            using var timeout = new CancellationTokenSource(StartTimeout);
            await _container.StartAsync(timeout.Token);
            ConnectionString = _container.GetConnectionString();
        }
        catch (OperationCanceledException)
        {
            SkipReason = $"The {DisplayName} container did not start within {StartTimeout.TotalSeconds:0} s. Set {OverrideVariable} to use an existing server.";
            await DisposeContainerQuietlyAsync();
            return;
        }
        catch (Exception ex)
        {
            var detail = ex.Message.Split('\n')[0].Trim();
            SkipReason = $"Docker is unavailable or the {DisplayName} container could not be started ({ex.GetType().Name}: {detail}). Set {OverrideVariable} to use an existing server.";
            await DisposeContainerQuietlyAsync();
            return;
        }
        await OnAvailableAsync();
    }

    /// <summary>Runs once a server is reachable: create databases, deploy objects.</summary>
    protected virtual Task OnAvailableAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await DisposeContainerQuietlyAsync();
    }

    private async Task DisposeContainerQuietlyAsync()
    {
        if (_container is null)
        {
            return;
        }
        try
        {
            await _container.DisposeAsync();
        }
        catch
        {
            // Nothing to do if the container was never created or Docker went away.
        }
        _container = null;
    }

    /// <summary>Skips the calling test when no server is available, naming the reason.</summary>
    public void SkipUnlessAvailable() => Skip.If(!IsAvailable, SkipReason ?? $"No {DisplayName} server is available.");
}
