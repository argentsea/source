using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.IO;
using System.Net.Sockets;
using ArgentSea;
using ArgentSea.Pg;
using FluentAssertions;
using Npgsql;
using NpgsqlTypes;
using NSubstitute;
using Xunit;

namespace ArgentSea.Pg.Test
{
    /// <summary>
    /// Spec section postgres-provider-services: the PostgreSQL realization of IDataProviderServiceFactory.
    /// Transient classification delegates to NpgsqlException.IsTransient; command creation is guarded by the connection type;
    /// SetParameters materializes independent clones and prepares the command unless the connection multiplexes.
    /// Ordering and duplicate-name rules are also covered by ProviderServicesTests.
    /// </summary>
    public class PgProviderServicesSpecTests
    {
        private static readonly DataProviderServiceFactory Factory = new DataProviderServiceFactory();
        private const string MultiplexingConnectionString = "Host=db;Database=app;Multiplexing = true";
        private const string PlainConnectionString = "Host=db;Database=app";

        private static NpgsqlParameterCollection Source(params NpgsqlParameter[] parameters)
        {
            var prms = new NpgsqlCommand().Parameters;
            foreach (var prm in parameters)
            {
                prms.Add(prm);
            }
            return prms;
        }

        #region GetIsErrorTransient

        [Fact]
        [Trait("Spec", "blk-e143b09c96ff40c0a037cd6eaf02518a")]
        [Trait("Spec", "blk-efb31799c41b4dd486547dc7dbfe2a45")]
        [Trait("Spec", "blk-581e371f27fe439fa7b4f93812b288b3")]
        public void AnNpgsqlExceptionReportsItsOwnIsTransientValue()
        {
            // Npgsql reports a network-level failure as transient and a constraint violation as not transient.
            var transient = new NpgsqlException("connection reset", new IOException("reset", new SocketException()));
            var tooManyConnections = new PostgresException("too many clients", "FATAL", "FATAL", PostgresErrorCodes.TooManyConnections);
            var uniqueViolation = new PostgresException("duplicate key", "ERROR", "ERROR", PostgresErrorCodes.UniqueViolation);

            transient.IsTransient.Should().BeTrue("the test relies on Npgsql classifying an IOException-wrapped failure as transient");
            tooManyConnections.IsTransient.Should().BeTrue();
            uniqueViolation.IsTransient.Should().BeFalse();

            Factory.GetIsErrorTransient(transient).Should().Be(transient.IsTransient);
            Factory.GetIsErrorTransient(tooManyConnections).Should().Be(tooManyConnections.IsTransient);
            Factory.GetIsErrorTransient(uniqueViolation).Should().Be(uniqueViolation.IsTransient, "the factory reports the exception's own verdict without inspecting the SQL state itself");
        }

        [Fact]
        [Trait("Spec", "blk-16d954e4303949729d75a25b54ed7c98")]
        [Trait("Spec", "blk-efb31799c41b4dd486547dc7dbfe2a45")]
        public void ANonNpgsqlExceptionIsNeverTransient()
        {
            Factory.GetIsErrorTransient(new InvalidOperationException("not Npgsql")).Should().BeFalse();
            Factory.GetIsErrorTransient(new TimeoutException()).Should().BeFalse();
            Factory.GetIsErrorTransient(new OtherProviderException()).Should().BeFalse("a DbException from another provider is not consulted even when it says it is transient");
        }

        private sealed class OtherProviderException : DbException
        {
            public OtherProviderException() : base("other provider") { }
            public override bool IsTransient => true;
        }

        #endregion

        #region NewConnection / NewCommand

        [Fact]
        [Trait("Spec", "blk-6aa4fd7a62484afba217dfb711b0f0a9")]
        [Trait("Spec", "blk-b2755fa9b555433ca6ab1baf9b202e9d")]
        [Trait("Spec", "blk-581e371f27fe439fa7b4f93812b288b3")]
        public void NewConnectionReturnsANewNpgsqlConnectionWithTheExactConnectionString()
        {
            var first = Factory.NewConnection(PlainConnectionString);
            var second = Factory.NewConnection(PlainConnectionString);

            first.Should().BeOfType<NpgsqlConnection>();
            first.ConnectionString.Should().Be(PlainConnectionString);
            first.State.Should().Be(ConnectionState.Closed, "the factory does not open the connection");
            second.Should().NotBeSameAs(first, "each call creates a new connection");
        }

        [Fact]
        [Trait("Spec", "blk-52831fcc68694c78a95f42204c2a07b3")]
        [Trait("Spec", "blk-f8c9932da7564a80b5bb0b8f02dcd526")]
        [Trait("Spec", "blk-581e371f27fe439fa7b4f93812b288b3")]
        public void NewCommandCreatesAnNpgsqlCommandForTheProcedureOnTheSuppliedConnection()
        {
            using var connection = new NpgsqlConnection(PlainConnectionString);

            var command = Factory.NewCommand("get_account", connection);

            command.Should().BeOfType<NpgsqlCommand>();
            command.CommandText.Should().Be("get_account");
            command.Connection.Should().BeSameAs(connection);
        }

        [Fact]
        [Trait("Spec", "blk-443cd4af7fba42939b63655493f30aa7")]
        [Trait("Spec", "blk-b2890a643c89423c915c34524985b919")]
        [Trait("Spec", "blk-29d2cb3037f44be392c325b351bff3c0")]
        [Trait("Spec", "blk-581e371f27fe439fa7b4f93812b288b3")]
        public void NewCommandRejectsAForeignConnectionTypeBeforeCreatingACommand()
        {
            var foreign = NSubstitute.Substitute.For<DbConnection>();

            Action act = () => Factory.NewCommand("get_account", foreign);

            var thrown = act.Should().ThrowExactly<ArgumentException>().Which;
            thrown.ParamName.Should().Be("connection");
            thrown.Message.Should().StartWith("The connection must be an NpgsqlConnection.");
            foreign.DidNotReceiveWithAnyArgs().CreateCommand();
        }

        #endregion

        #region SetParameters

        [Fact]
        [Trait("Spec", "blk-225f1f68e4e546b78e3c2b5220322117")]
        [Trait("Spec", "blk-2a2a41ac73944646b18322571471bb18")]
        [Trait("Spec", "blk-581e371f27fe439fa7b4f93812b288b3")]
        public void WithoutRequestedNamesEverySourceParameterIsClonedInSourceOrder()
        {
            var cmd = new NpgsqlCommand("proc", new NpgsqlConnection(MultiplexingConnectionString));
            var id = new NpgsqlParameter("@id", NpgsqlDbType.Integer) { Value = 7 };
            var code = new NpgsqlParameter("@code", NpgsqlDbType.Varchar, 10) { Value = "A1" };
            var source = Source(id, code);

            Factory.SetParameters(cmd, null, source, null);

            cmd.Parameters.Count.Should().Be(2);
            cmd.Parameters[0].ParameterName.Should().Be("@id");
            cmd.Parameters[0].Value.Should().Be(7);
            cmd.Parameters[0].NpgsqlDbType.Should().Be(NpgsqlDbType.Integer);
            cmd.Parameters[1].ParameterName.Should().Be("@code");
            cmd.Parameters[1].Value.Should().Be("A1");
            cmd.Parameters[1].Size.Should().Be(10);
            cmd.Parameters[0].Should().NotBeSameAs(id, "the command receives an independent clone");
            cmd.Parameters[1].Should().NotBeSameAs(code);
            cmd.Parameters[0].Value = 8;
            id.Value.Should().Be(7, "mutating the clone does not touch the source");
        }

        [Fact]
        [Trait("Spec", "blk-a51a213c941c4a7cbd7fc8ca5a9e8aff")]
        [Trait("Spec", "blk-2a2a41ac73944646b18322571471bb18")]
        public void AParameterValuesEntryReplacesTheClonedValueAndLeavesTheSourceAlone()
        {
            var cmd = new NpgsqlCommand("proc", new NpgsqlConnection(MultiplexingConnectionString));
            var source = Source(new NpgsqlParameter("@id", NpgsqlDbType.Integer) { Value = 7 });

            Factory.SetParameters(cmd, null, source, new Dictionary<string, object> { { "@id", 99 } });

            cmd.Parameters["@id"].Value.Should().Be(99, "the replacement is the value bound on the command");
            source["@id"].Value.Should().Be(7, "the source collection is not modified");
        }

        [Fact]
        [Trait("Spec", "blk-4b3413ffd56746cc97f5061184699698")]
        [Trait("Spec", "blk-2a2a41ac73944646b18322571471bb18")]
        public void ARequestedNameListSelectsOnlyThoseParameters()
        {
            var cmd = new NpgsqlCommand("proc", new NpgsqlConnection(MultiplexingConnectionString));
            var enabled = new NpgsqlParameter("@enabled", NpgsqlDbType.Boolean) { Value = true };
            var source = Source(new NpgsqlParameter("@account_id", NpgsqlDbType.Integer) { Value = 7 }, enabled);

            Factory.SetParameters(cmd, new[] { "@enabled" }, source, null);

            var prm = cmd.Parameters.Should().ContainSingle().Which;
            prm.ParameterName.Should().Be("@enabled");
            prm.Value.Should().Be(true);
            prm.Should().NotBeSameAs(enabled);
        }

        [Fact]
        [Trait("Spec", "blk-6d4eaff56bc24604a752e6ee80f771c8")]
        [Trait("Spec", "blk-b918756788854fbe8f42b1dc1051afb3")]
        [Trait("Spec", "blk-7df5c269006f4eeb87215f230f1e442b")]
        public void AMissingRequestedNameThrowsParameterNotFoundNamingThatParameter()
        {
            var cmd = new NpgsqlCommand("proc", new NpgsqlConnection(MultiplexingConnectionString));
            var source = Source(new NpgsqlParameter("@id", NpgsqlDbType.Integer) { Value = 7 });

            Action act = () => Factory.SetParameters(cmd, new[] { "@id", "@label" }, source, null);

            act.Should().ThrowExactly<ParameterNotFoundException>()
                .WithMessage("Expected parameter “@label”, but it was not found in the parameter list.", "the parameterName constructor is used");
            cmd.Parameters.Count.Should().Be(1, "@id was added before the failure and nothing was added for @label");
            cmd.Parameters[0].ParameterName.Should().Be("@id");
        }

        [Fact]
        [Trait("Spec", "blk-b918756788854fbe8f42b1dc1051afb3")]
        public void AMissingNameStopsProcessingBeforeLaterNames()
        {
            var cmd = new NpgsqlCommand("proc", new NpgsqlConnection(MultiplexingConnectionString));
            var source = Source(new NpgsqlParameter("@id", NpgsqlDbType.Integer) { Value = 7 });

            Action act = () => Factory.SetParameters(cmd, new[] { "@label", "@id" }, source, null);

            act.Should().ThrowExactly<ParameterNotFoundException>().WithMessage("*“@label”*");
            cmd.Parameters.Count.Should().Be(0, "no later name is processed once one is missing");
        }

        [Fact]
        [Trait("Spec", "blk-332046760fae41769f34d787c1cde3e5")]
        [Trait("Spec", "blk-b196c063278a4942b872c563c3803816")]
        [Trait("Spec", "blk-2a2a41ac73944646b18322571471bb18")]
        public void AMultiplexingConnectionSkipsPreparation()
        {
            var cmd = new NpgsqlCommand("proc", new NpgsqlConnection(MultiplexingConnectionString));
            var source = Source(new NpgsqlParameter("@id", NpgsqlDbType.Integer) { Value = 7 });

            Factory.SetParameters(cmd, null, source, null);

            cmd.IsPrepared.Should().BeFalse("Prepare() is not called when the connection string enables multiplexing");
            cmd.Parameters.Count.Should().Be(1);
        }

        [Fact]
        [Trait("Spec", "blk-81f7015bc0f442c6bd0226f0e1fa87ed")]
        [Trait("Spec", "blk-b196c063278a4942b872c563c3803816")]
        public void ANonMultiplexingConnectionIsPreparedAfterTheParametersAreAdded()
        {
            // Prepare() needs an open connection, so on a closed connection Npgsql throws from inside Prepare(); that throw is
            // the observable proof that preparation was attempted, and it happens only after every parameter was bound.
            var cmd = new NpgsqlCommand("proc", new NpgsqlConnection(PlainConnectionString));
            var source = Source(new NpgsqlParameter("@id", NpgsqlDbType.Integer) { Value = 7 }, new NpgsqlParameter("@code", NpgsqlDbType.Text) { Value = "x" });

            Action act = () => Factory.SetParameters(cmd, null, source, new Dictionary<string, object> { { "@code", "y" } });

            act.Should().Throw<InvalidOperationException>("Prepare() was called on a command whose connection is not open");
            cmd.Parameters.Count.Should().Be(2, "the parameters were added before preparation was attempted");
            cmd.Parameters["@code"].Value.Should().Be("y", "overrides were applied before preparation was attempted");
        }

        [Fact]
        [Trait("Spec", "blk-b196c063278a4942b872c563c3803816")]
        public void AnEmptyConnectionStringCountsAsNotMultiplexing()
        {
            var cmd = new NpgsqlCommand("proc", new NpgsqlConnection());
            var source = Source(new NpgsqlParameter("@id", NpgsqlDbType.Integer) { Value = 7 });

            Action act = () => Factory.SetParameters(cmd, null, source, null);

            act.Should().Throw<InvalidOperationException>("an empty connection string is treated as not multiplexing, so Prepare() is attempted");
        }

        #endregion
    }
}
