using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using ArgentSea.TestKit;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ArgentSea.Test
{
    /// <summary>
    /// Spec section connection-configuration-hierarchy: the retry back-off curves, PropertyChanged on credential settings,
    /// resilience inheritance through the hierarchy levels, SimpleDbConnection, the configuration option interfaces, and how the
    /// Databases and ShardSets collections treat missing or null configuration.
    /// </summary>
    public class ConnectionConfigurationHierarchyTests
    {
        /// <summary>A bare hierarchy level, exposing the protected inheritance members for assertion.</summary>
        private class Level : DataConnectionConfigurationBase
        {
            public void Inherit(DataConnectionConfigurationBase? global, DataConnectionConfigurationBase? shardSet, DataConnectionConfigurationBase? readWrite, DataConnectionConfigurationBase? shard)
                => SetAmbientResilience(global, shardSet, readWrite, shard);

            public int? ResolvedRetryCount => InheritedRetryCount;
            public int? ResolvedCircuitBreakerFailureCount => InheritedCircuitBreakerFailureCount;
            public int? ResolvedCircuitBreakerTestInterval => InheritedCircuitBreakerTestInterval;
        }

        private static List<string> Observe(INotifyPropertyChanged source)
        {
            var names = new List<string>();
            source.PropertyChanged += (_, e) => names.Add(e.PropertyName ?? "");
            return names;
        }

        #region Retry curves

        [Theory]
        [Trait("Spec", "blk-ecbac29d34b74fcf9f65a7f88fb2a972")]
        [Trait("Spec", "blk-72ccc2fd25164b36b613de540315a781")]
        [Trait("Spec", "blk-5eaad96969414f45912adf725d770e76")]
        [InlineData(SequenceLengthening.Linear, 750)]
        [InlineData(SequenceLengthening.HalfSquare, 1000)]
        [InlineData(SequenceLengthening.Squaring, 1000)]
        [InlineData(SequenceLengthening.Lengthening, 1250)]
#pragma warning disable CS0618
        [InlineData(SequenceLengthening.Fibonacci, 1250)]
#pragma warning restore CS0618
        public void EachLengtheningModeYieldsTheSpecifiedDelayForAttemptThreeAtTheDefaultInterval(SequenceLengthening mode, int expectedMilliseconds)
        {
            var simple = new SimpleDbConnection { RetryLengthening = mode, RetryInterval = 250 };
            var level = new Level { RetryLengthening = mode, RetryInterval = 250 };

            simple.GetRetryTimespan(3).Should().Be(TimeSpan.FromMilliseconds(expectedMilliseconds));
            level.GetRetryTimespan(3).Should().Be(TimeSpan.FromMilliseconds(expectedMilliseconds), "a hierarchy level computes the same curve");
            simple.RetryLengthening.Should().Be(mode, "the settings are not modified");
            simple.RetryInterval.Should().Be(250);
        }

        [Fact]
        [Trait("Spec", "blk-1d8c077ec9ac40808d4951be5af6283c")]
        [Trait("Spec", "blk-efd6ea6a5e1e47ae8bdbcd4e1ad96d09")]
        [Trait("Spec", "blk-5eaad96969414f45912adf725d770e76")]
        public void SquaringAtAHundredMillisecondsAndTheDefaultsMatchTheSpecifiedValues()
        {
            new SimpleDbConnection { RetryLengthening = SequenceLengthening.Squaring, RetryInterval = 100 }.GetRetryTimespan(3).Should().Be(TimeSpan.FromMilliseconds(400));
            new SimpleDbConnection { RetryLengthening = SequenceLengthening.Lengthening, RetryInterval = 100 }.GetRetryTimespan(3).Should().Be(TimeSpan.FromMilliseconds(500), "1, 3, 5 times the interval");
            new SimpleDbConnection().GetRetryTimespan(2).Should().Be(TimeSpan.FromMilliseconds(750), "the defaults are 250 ms and Lengthening");
            new Level().GetRetryTimespan(2).Should().Be(TimeSpan.FromMilliseconds(750));
            new SimpleDbConnection().GetRetryTimespan(1).Should().Be(TimeSpan.FromMilliseconds(250));
        }

        [Fact]
        [Trait("Spec", "blk-94549f430b8c4cb99db26efb95e3a4e2")]
        [Trait("Spec", "blk-b7157e6187b34d4cb5eb55d0b25c9cdf")]
        public void SequenceLengtheningDeclaresFourModesAndAnObsoleteFibonacciAlias()
        {
            ((int)SequenceLengthening.Linear).Should().Be(0);
            ((int)SequenceLengthening.Lengthening).Should().Be(1);
            ((int)SequenceLengthening.HalfSquare).Should().Be(2);
            ((int)SequenceLengthening.Squaring).Should().Be(3);
#pragma warning disable CS0618
            SequenceLengthening.Fibonacci.Should().Be(SequenceLengthening.Lengthening);
#pragma warning restore CS0618
            typeof(SequenceLengthening).GetField("Fibonacci")!.GetCustomAttribute<ObsoleteAttribute>().Should().NotBeNull();
            Enum.Parse<SequenceLengthening>("fibonacci", ignoreCase: true).Should().Be(SequenceLengthening.Lengthening, "configuration binding parses the alias case-insensitively");
            Enum.GetValues<SequenceLengthening>().Distinct().Should().HaveCount(4);
        }

        #endregion

        #region PropertyChanged

        [Fact]
        [Trait("Spec", "blk-d408f46971a040e292e8898732234253")]
        [Trait("Spec", "blk-5693d8698f54493981583731d7cb74d7")]
        [Trait("Spec", "blk-5cddfb6eede14ab7a93540bca81a1393")]
        public void CredentialPropertiesRaisePropertyChangedOnlyWhenTheValueActuallyChanges()
        {
            var level = new Level { UserName = "svc_shard01", Password = "OldP@ss1" };
            var raised = Observe(level);

            level.UserName = "svc_shard01";
            level.Password = "NewP@ss2";
            level.WindowsAuth = true;
            level.WindowsAuth = true;

            raised.Should().Equal("Password", "WindowsAuth");
            level.Password.Should().Be("NewP@ss2");
        }

        [Fact]
        [Trait("Spec", "blk-51e9323067b543f49dfae73626ea678b")]
        [Trait("Spec", "blk-94614e41ce874ded97b9ffd09b398796")]
        public void ResilienceSettingsNeverRaisePropertyChanged()
        {
            var level = new Level();
            var raised = Observe(level);

            level.RetryCount = 3;
            level.RetryInterval = 10;
            level.RetryLengthening = SequenceLengthening.Linear;
            level.CircuitBreakerFailureCount = 4;
            level.CircuitBreakerTestInterval = 500;

            raised.Should().BeEmpty();
            (level.RetryCount, level.RetryInterval, level.RetryLengthening, level.CircuitBreakerFailureCount, level.CircuitBreakerTestInterval).Should().Be((3, 10, SequenceLengthening.Linear, 4, 500));
            foreach (var name in new[] { "WindowsAuth", "RetryCount", "RetryInterval", "RetryLengthening", "CircuitBreakerFailureCount", "CircuitBreakerTestInterval" })
            {
                Nullable.GetUnderlyingType(typeof(DataConnectionConfigurationBase).GetProperty(name)!.PropertyType).Should().NotBeNull($"{name} is nullable so an unset level can be told from an explicit one");
            }
        }

        #endregion

        #region Resilience inheritance

        [Fact]
        [Trait("Spec", "blk-e762b989990a42f6a1bca7346f9c068d")]
        public void ResilienceSettingsResolveOwnThenShardThenReadWriteThenShardSetThenGlobal()
        {
            var global = new Level { RetryCount = 1, CircuitBreakerFailureCount = 1, CircuitBreakerTestInterval = 1, RetryInterval = 10, RetryLengthening = SequenceLengthening.Linear };
            var shardSet = new Level { RetryCount = 2, CircuitBreakerFailureCount = 2 };
            var readWrite = new Level { RetryCount = 3 };
            var shard = new Level { CircuitBreakerTestInterval = 4 };
            var connection = new Level();
            connection.Inherit(global, shardSet, readWrite, shard);

            connection.ResolvedRetryCount.Should().Be(3, "the read/write level is the most specific one that sets it");
            connection.ResolvedCircuitBreakerFailureCount.Should().Be(2);
            connection.ResolvedCircuitBreakerTestInterval.Should().Be(4);
            connection.GetRetryTimespan(2).Should().Be(TimeSpan.FromMilliseconds(20), "interval and lengthening inherit too");
            connection.RetryCount.Should().BeNull("the public property keeps only the instance's own value");
            connection.RetryCount = 9;
            connection.ResolvedRetryCount.Should().Be(9, "the connection's own value wins");
            shard.RetryCount = 5;
            connection.RetryCount = null;
            connection.ResolvedRetryCount.Should().Be(5, "levels are read on every access");
            new Level().ResolvedRetryCount.Should().BeNull("unset at every level resolves to null");
        }

        [Fact]
        [Trait("Spec", "blk-e762b989990a42f6a1bca7346f9c068d")]
        public void NullLevelsAreSkippedAndAnUnsetSettingFallsThroughToGlobal()
        {
            var global = new Level { CircuitBreakerFailureCount = 7, RetryLengthening = SequenceLengthening.Squaring, RetryInterval = 100 };
            var connection = new Level();
            connection.Inherit(global, null, null, null);

            connection.ResolvedCircuitBreakerFailureCount.Should().Be(7);
            connection.ResolvedRetryCount.Should().BeNull();
            connection.GetRetryTimespan(3).Should().Be(TimeSpan.FromMilliseconds(400));
        }

        #endregion

        #region SimpleDbConnection and interfaces

        [Fact]
        [Trait("Spec", "blk-2906dc101b084aa4a9bb7fd5c934a3fc")]
        [Trait("Spec", "blk-babf779e363c4343b33c62efa8e84b8d")]
        public void SimpleDbConnectionReturnsItsStringUnchangedAndIgnoresAmbientConfiguration()
        {
            var simple = new SimpleDbConnection { ConnectionString = "Server=x;Database=y", ConnectionDescription = "desc", RetryCount = 2, CircuitBreakerFailureCount = 3, CircuitBreakerTestInterval = 4 };
            IDataConnection connection = simple;

            simple.SetAmbientConfiguration(new Level { RetryCount = 99, UserName = "u" }, new Level { RetryCount = 98 }, null, null);

            connection.GetConnectionString(null).Should().Be("Server=x;Database=y");
            connection.ConnectionDescription.Should().Be("desc");
            connection.RetryCount.Should().Be(2, "ambient levels do not alter the stored values");
            connection.CircuitBreakerFailureCount.Should().Be(3);
            connection.CircuitBreakerTestInterval.Should().Be(4);
            typeof(IDataConnection).GetMembers().Select(m => m.Name).Should().Contain(new[] { "ConnectionDescription", "GetConnectionString", "RetryCount", "GetRetryTimespan", "CircuitBreakerFailureCount", "CircuitBreakerTestInterval", "SetAmbientConfiguration" });
            typeof(IDataConnection).GetMethod(nameof(IDataConnection.SetAmbientConfiguration))!.GetParameters().Should().HaveCount(4).And.OnlyContain(p => p.ParameterType == typeof(DataConnectionConfigurationBase));
        }

        [Fact]
        [Trait("Spec", "blk-3dea74e8f57942d39ed5c3f7debc53d7")]
        [Trait("Spec", "blk-26f0c8879c4f4171a5fe113e4a7731ff")]
        [Trait("Spec", "blk-d4ca447227c6464aab769f60463e0a83")]
        [Trait("Spec", "blk-a2cae80999924a728548648ce1dcafaf")]
        [Trait("Spec", "blk-95c703c8194c42a493e3c2691211ecc1")]
        [Trait("Spec", "blk-a0b018fdab264091a08ee9ac4b949739")]
        [Trait("Spec", "blk-a1acbcd61af44beca41e4daf9694977e")]
        [Trait("Spec", "blk-13849b75939440c88be41f1d85e6e078")]
        [Trait("Spec", "blk-eff2763239614e469469ff8fc2d907e5")]
        [Trait("Spec", "blk-2fbc46234dd8400ea009add81047fb8a")]
        public void TheConfigurationInterfacesExposeCollectionsAndConnectionsAsGetOnlyMembers()
        {
            static PropertyInfo Prop(Type type, string name) => type.GetProperty(name)!;

            Prop(typeof(IShardSetsConfigurationOptions), "ShardSetsConfigInternal").CanWrite.Should().BeFalse();
            Prop(typeof(IShardSetsConfigurationOptions), "ShardSetsConfigInternal").PropertyType.Should().Be(typeof(IShardSetConnectionsConfiguration[]));
            Prop(typeof(IDatabaseConfigurationOptions), "DbConnectionsInternal").CanWrite.Should().BeFalse();
            Prop(typeof(IDatabaseConfigurationOptions), "DbConnectionsInternal").PropertyType.Should().Be(typeof(IDatabaseConnectionConfiguration[]));
            Prop(typeof(IShardConnectionConfiguration), "ShardId").CanWrite.Should().BeTrue();
            Prop(typeof(IShardConnectionConfiguration), "ShardId").PropertyType.Should().Be(typeof(short));
            Prop(typeof(IShardConnectionConfiguration), "ReadConnectionInternal").CanWrite.Should().BeFalse();
            Prop(typeof(IShardConnectionConfiguration), "WriteConnectionInternal").CanWrite.Should().BeFalse();
            Prop(typeof(IDatabaseConnectionConfiguration), "DatabaseKey").CanWrite.Should().BeTrue();
            Prop(typeof(IDatabaseConnectionConfiguration), "ReadConnectionInternal").CanWrite.Should().BeFalse();
            Prop(typeof(IDatabaseConnectionConfiguration), "WriteConnectionInternal").CanWrite.Should().BeFalse();
            Prop(typeof(IShardSetConnectionsConfiguration), "ShardSetName").CanWrite.Should().BeTrue();
            Prop(typeof(IShardSetConnectionsConfiguration), "DefaultShardId").CanWrite.Should().BeTrue();
            foreach (var name in new[] { "ShardsConfigInternal", "ReadConfigInternal", "WriteConfigInternal" })
            {
                Prop(typeof(IShardSetConnectionsConfiguration), name).CanWrite.Should().BeFalse($"{name} is exposed through a getter only");
            }
        }

        #endregion

        #region Collections over options

        [Fact]
        [Trait("Spec", "blk-92efd03a756341caaa721c9919a97e2c")]
        [Trait("Spec", "blk-cdfd8954887b402b8ce552f47d42b1f8")]
        public void MissingConfigurationLogsAWarningAndYieldsAnEmptyCollection()
        {
            var services = new FakeProviderServices();
            var databaseLogger = new RecordingLogger<DatabasesBase<FakeDatabasesOptions>>();
            var shardLogger = new RecordingLogger<ShardSetsBase<FakeShardSetsOptions>>();

            var databases = new FakeDatabases(new FakeDatabasesOptions { Databases = null! }, services, null, databaseLogger);
            var shardSets = new FakeShardSets(new FakeShardSetsOptions { ShardSets = null! }, services, null, shardLogger);

            databases.Count.Should().Be(0);
            shardSets.Count.Should().Be(0);
            databaseLogger.Entries.Should().ContainSingle().Which.Should().Match<LogEntry>(e => e.Level == LogLevel.Warning && e.Message!.Contains("missing a database configuration section"));
            shardLogger.Entries.Should().ContainSingle().Which.Should().Match<LogEntry>(e => e.Level == LogLevel.Warning && e.Message!.Contains("missing a shard configuration section"));
        }

        [Fact]
        [Trait("Spec", "blk-92efd03a756341caaa721c9919a97e2c")]
        [Trait("Spec", "blk-cdfd8954887b402b8ce552f47d42b1f8")]
        public void ANullConfigurationEntryIsRejected()
        {
            var services = new FakeProviderServices();

            Action databases = () => new FakeDatabases(new FakeDatabasesOptions { Databases = new FakeDatabaseConfiguration[] { null! } }, services);
            Action shardSets = () => new FakeShardSets(new FakeShardSetsOptions { ShardSets = new FakeShardSetConfiguration[] { null! } }, services);

            databases.Should().Throw<Exception>().WithMessage("*returned null*");
            shardSets.Should().Throw<Exception>().WithMessage("*returned null*");
        }

        [Fact]
        [Trait("Spec", "blk-92efd03a756341caaa721c9919a97e2c")]
        [Trait("Spec", "blk-cdfd8954887b402b8ce552f47d42b1f8")]
        public void EntriesAreKeyedByTheirNameAndTheirEndpointsReceiveTheGlobalLevel()
        {
            var services = new FakeProviderServices();
            var global = new Level { RetryCount = 4 };
            var databases = FakeDatabases.Builder(services).AddDatabase("Main").WithGlobalConfiguration(global).Build();
            var shardSets = FakeShardSets.Builder(services).AddShardSet("Set", 1, 2).WithGlobalConfiguration(global).Build();

            databases["Main"].Should().NotBeNull();
            shardSets["Set"][2].Should().NotBeNull();
            shardSets["Set"].Count.Should().Be(2);
            databases.Settings("Main", FakeConnectionAccess.Read).RetryCount.Should().Be(0, "the fake settings keep their own value; the global level is handed to SetAmbientConfiguration, which SimpleDbConnection-style endpoints ignore");
        }

        #endregion
    }
}
