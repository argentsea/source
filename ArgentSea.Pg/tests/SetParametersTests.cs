using System.Collections.Generic;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace ArgentSea.Pg.Test
{
    /// <summary>
    /// Per-execution parameter values (a shard id or a ShardsValues entry) must survive the final value assignment that follows Prepare.
    /// </summary>
    public class SetParametersTests
    {
        // Multiplexing skips Prepare, which would need an open connection; the final value assignment runs either way.
        private static NpgsqlCommand NewCommand() => new NpgsqlCommand("ws.Test", new NpgsqlConnection("Host=localhost;Multiplexing=true"));

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void PerExecutionValuesOverrideSourceValues(bool namedQueryParameters)
        {
            var prms = new ParameterCollection()
                .AddPgIntegerInputParameter("overridden", 1)
                .AddPgIntegerInputParameter("kept", 2);
            var overriddenName = prms[0].ParameterName;
            var keptName = prms[1].ParameterName;
            var cmd = NewCommand();
            var queryParameterNames = namedQueryParameters ? new[] { overriddenName, keptName } : null;

            new DataProviderServiceFactory().SetParameters(cmd, queryParameterNames, prms, new Dictionary<string, object> { { overriddenName, 99 } });

            cmd.Parameters[overriddenName].Value.Should().Be(99, "the per-execution value overrides the shared source value");
            cmd.Parameters[keptName].Value.Should().Be(2);
            prms[0].Value.Should().Be(1, "the caller’s collection is not changed");
        }
    }
}
