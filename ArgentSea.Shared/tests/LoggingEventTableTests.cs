using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using ArgentSea.TestKit;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xunit;
using static ArgentSea.LoggingExtensions;

namespace ArgentSea.Test
{
    /// <summary>
    /// Spec section core-log-events: every wrapper method of <see cref="LoggingExtensions"/> is enumerated in one table and
    /// proven to emit exactly one event with its documented id, name, level and message template through a recording logger.
    /// This guards the "event defined but never emitted" class of defect (the circuit-breaker events were defined for years
    /// before anything raised them).
    /// </summary>
    public class LoggingEventTableTests
    {
        /// <summary>One row of the table: a wrapper, what it must emit, and how to call it.</summary>
        public sealed class LogEventCase
        {
            public LogEventCase(string method, LogLevel level, EventIdentifier id, string template, Action<ILogger?> invoke, LogLevel? guard = null)
            {
                Method = method;
                Level = level;
                Id = (int)id;
                Template = template;
                Invoke = invoke;
                Guard = guard;
            }

            public string Method { get; }
            public LogLevel Level { get; }
            public int Id { get; }
            public string Template { get; }
            public Action<ILogger?> Invoke { get; }
            /// <summary>The level the wrapper itself checks with IsEnabled before doing any work, when it adds such a guard.</summary>
            public LogLevel? Guard { get; }
            public override string ToString() => Method;
        }

        /// <summary>An expression that records whether the debug-view renderer visited it.</summary>
        private sealed class RenderingProbe : Expression
        {
            public bool Rendered { get; private set; }
            public override ExpressionType NodeType => ExpressionType.Extension;
            public override Type Type => typeof(object);
            public override bool CanReduce
            {
                get
                {
                    Rendered = true;
                    return false;
                }
            }
        }

        private static readonly Type Model = typeof(string);
        private static readonly Expression Code = Expression.Constant(1);

        private static readonly LogEventCase[] Table =
        {
            new LogEventCase(nameof(LoggingExtensions.SqlConnectionStringBuilt), LogLevel.Debug, EventIdentifier.ConnectionStringBuilt, "A new connection string was built with a value of {ConnectString}.", l => l.SqlConnectionStringBuilt("Server=.")),
            new LogEventCase(nameof(LoggingExtensions.SqlInParametersCacheMiss), LogLevel.Debug, EventIdentifier.MapperCacheStatus, "No cached delegate for creating input parameters was initialized for type {TModel}; this is normal for the first execution.", l => l.SqlInParametersCacheMiss(Model)),
            new LogEventCase(nameof(LoggingExtensions.SqlInParametersCacheHit), LogLevel.Trace, EventIdentifier.MapperCacheStatus, "The cached delegate for creating input parameters was already initialized for type {TModel}.", l => l.SqlInParametersCacheHit(Model)),
            new LogEventCase(nameof(LoggingExtensions.SqlSetOutParametersCacheMiss), LogLevel.Debug, EventIdentifier.MapperCacheStatus, "No cached delegate for creating output parameters was initialized for type {TModel}; this is normal for the first execution.", l => l.SqlSetOutParametersCacheMiss(Model)),
            new LogEventCase(nameof(LoggingExtensions.SqlSetOutParametersCacheHit), LogLevel.Trace, EventIdentifier.MapperCacheStatus, "The cached delegate for creating output parameters was already initialized for type {TModel}.", l => l.SqlSetOutParametersCacheHit(Model)),
            new LogEventCase(nameof(LoggingExtensions.SqlReadOutParametersCacheMiss), LogLevel.Debug, EventIdentifier.MapperCacheStatus, "No cached delegate for reading output parameters was initialized for type {TModel}; this is normal for the first execution.", l => l.SqlReadOutParametersCacheMiss(Model)),
            new LogEventCase(nameof(LoggingExtensions.SqlReadOutParametersCacheHit), LogLevel.Trace, EventIdentifier.MapperCacheStatus, "The cached delegate for reading output parameters was already initialized for type {TModel}.", l => l.SqlReadOutParametersCacheHit(Model)),
            new LogEventCase(nameof(LoggingExtensions.SqlReaderCacheMiss), LogLevel.Debug, EventIdentifier.MapperCacheStatus, "No cached delegate for mapping a data reader was initialized for type {TModel}; this is normal for the first execution.", l => l.SqlReaderCacheMiss(Model)),
            new LogEventCase(nameof(LoggingExtensions.SqlReaderCacheHit), LogLevel.Trace, EventIdentifier.MapperCacheStatus, "The cached delegate for mapping a data reader was already initialized for type {TModel}.", l => l.SqlReaderCacheHit(Model)),
            new LogEventCase(nameof(LoggingExtensions.SqlParameterNotFound), LogLevel.Debug, EventIdentifier.MapperSqlParameterNotFound, "Sql Parameter {parameterName} was defined on {Type} but was not found among the provided parameters.", l => l.SqlParameterNotFound("@Id", typeof(int))),
            new LogEventCase(nameof(LoggingExtensions.SqlFieldNotFound), LogLevel.Debug, EventIdentifier.MapperSqlColumnNotFound, "Column {columnName} was defined on {modelName} but was not found in the result set.", l => l.SqlFieldNotFound("Id", "Customer")),
            new LogEventCase(nameof(LoggingExtensions.TraceInMapperProperty), LogLevel.Trace, EventIdentifier.MapperProcessTrace, "In-parameter mapper is now processing property {name}.", l => l.TraceInMapperProperty("Id")),
            new LogEventCase(nameof(LoggingExtensions.TraceSetOutMapperProperty), LogLevel.Trace, EventIdentifier.MapperProcessTrace, "Set out-parameter mapper is now processing property {name}.", l => l.TraceSetOutMapperProperty("Id")),
            new LogEventCase(nameof(LoggingExtensions.TraceGetOutMapperProperty), LogLevel.Trace, EventIdentifier.MapperProcessTrace, "Get out-parameter mapper is now processing property {name}.", l => l.TraceGetOutMapperProperty("Id")),
            new LogEventCase(nameof(LoggingExtensions.TraceRdrMapperProperty), LogLevel.Trace, EventIdentifier.MapperProcessTrace, "Data reader field mapper is now processing property {name}.", l => l.TraceRdrMapperProperty("Id")),
            new LogEventCase(nameof(LoggingExtensions.NullShardChildArguments), LogLevel.Information, EventIdentifier.UnexpectedDbNull, "The {name} shard child could not be built because one or two of the input values was dbNull. The shard child value was {shardChild}.", l => l.NullShardChildArguments("ChildKey", default(ShardKey<string, int>)), LogLevel.Information),
            new LogEventCase(nameof(LoggingExtensions.TraceDbCmdExecuted), LogLevel.Trace, EventIdentifier.CmdExecuted, "Executed command {name} on Db connection {connectionName} in {milliseconds} milliseconds.", l => l.TraceDbCmdExecuted("rd.Get", "MainDb", 12), LogLevel.Trace),
            new LogEventCase(nameof(LoggingExtensions.TraceShardCmdExecuted), LogLevel.Trace, EventIdentifier.CmdExecuted, "Executed command {name} on ShardSet {shardSet} connection {shardId} in {milliseconds} milliseconds.", l => l.TraceShardCmdExecuted("rd.Get", "Set", 3, 12), LogLevel.Trace),
            new LogEventCase(nameof(LoggingExtensions.RetryingDbConnection), LogLevel.Warning, EventIdentifier.ConnectRetry, "Initiating automatic connection retry for transient error on Db connection {connectionName}. This is attempt number {attempt}.", l => l.RetryingDbConnection("MainDb", 1, new TimeoutException())),
            new LogEventCase(nameof(LoggingExtensions.CiruitBreakingDbConnection), LogLevel.Critical, EventIdentifier.CircuitBreaker, "Circuit breaking failing connection on Db connection {connectionName}. Most subsequent calls to this connection will fail.", l => l.CiruitBreakingDbConnection("MainDb")),
            new LogEventCase(nameof(LoggingExtensions.CiruitBreakingDbCommand), LogLevel.Critical, EventIdentifier.CircuitBreaker, "Circuit breaking failing command {name} on Db connection {connectionName}.", l => l.CiruitBreakingDbCommand("rd.Get", "MainDb")),
            new LogEventCase(nameof(LoggingExtensions.CiruitBrokenDbConnectionTest), LogLevel.Information, EventIdentifier.CircuitBreaker, "Circuit broken connection {connectionName} is being retested.", l => l.CiruitBrokenDbConnectionTest("MainDb")),
            new LogEventCase(nameof(LoggingExtensions.CiruitBrokenDbCommandTest), LogLevel.Information, EventIdentifier.CircuitBreaker, "Circuit broken command {name} on Db connection {connectionName} is being retested.", l => l.CiruitBrokenDbCommandTest("rd.Get", "MainDb")),
            new LogEventCase(nameof(LoggingExtensions.CiruitBrokenDbConnectionRestored), LogLevel.Information, EventIdentifier.CircuitBreaker, "Circuit broken connection {connectionName} is restored.", l => l.CiruitBrokenDbConnectionRestored("MainDb")),
            new LogEventCase(nameof(LoggingExtensions.CiruitBrokenDbCommandRestored), LogLevel.Information, EventIdentifier.CircuitBreaker, "Circuit broken command {name} on Db connection {connectionName} is restored.", l => l.CiruitBrokenDbCommandRestored("rd.Get", "MainDb")),
            new LogEventCase(nameof(LoggingExtensions.DataReaderIsNull), LogLevel.Error, EventIdentifier.MapperResultsReaderInvalid, "Expected data reader object was null from procedure {sproc} on connection {connectionName}.", l => l.DataReaderIsNull("rd.Get", "MainDb")),
            new LogEventCase(nameof(LoggingExtensions.DataReaderIsClosed), LogLevel.Error, EventIdentifier.MapperResultsReaderInvalid, "Expected data reader object was closed from procedure {sproc} on connection {connectionName}.", l => l.DataReaderIsClosed("rd.Get", "MainDb")),
            new LogEventCase(nameof(LoggingExtensions.RequiredPropertyIsDbNull), LogLevel.Debug, EventIdentifier.UnexpectedDbNull, "Request for object {model} returned null because database parameter {parameterName} was null. This may mean the object was not found in the database.", l => l.RequiredPropertyIsDbNull("Customer", "@Id")),
            new LogEventCase(nameof(LoggingExtensions.CreatedExpressionTreeForSetInParameters), LogLevel.Debug, EventIdentifier.ExpressionTreeCreation, "Compiled code to map model {model} to input parameters as:\r\n{code}.", l => l.CreatedExpressionTreeForSetInParameters(Model, Code), LogLevel.Debug),
            new LogEventCase(nameof(LoggingExtensions.CreatedExpressionTreeForSetOutParameters), LogLevel.Debug, EventIdentifier.ExpressionTreeCreation, "Compiled code to map model {model} to set output parameters as:\r\n{code}.", l => l.CreatedExpressionTreeForSetOutParameters(Model, Code), LogLevel.Debug),
            new LogEventCase(nameof(LoggingExtensions.CreatedExpressionTreeForReadOutParameters), LogLevel.Debug, EventIdentifier.ExpressionTreeCreation, "Compiled code to map model {model} to read output parameters as:\r\n{code}.", l => l.CreatedExpressionTreeForReadOutParameters(Model, Code), LogLevel.Debug),
            new LogEventCase(nameof(LoggingExtensions.CreatedExpressionTreeForReaderOrdinals), LogLevel.Debug, EventIdentifier.ExpressionTreeCreation, "Compiled code to map model {model} ordinals to data reader values as:\r\n{code}.", l => l.CreatedExpressionTreeForReaderOrdinals(Model, Code), LogLevel.Debug),
            new LogEventCase(nameof(LoggingExtensions.CreatedExpressionTreeForReaderRowData), LogLevel.Debug, EventIdentifier.ExpressionTreeCreation, "Compiled code to map model {model} to data reader row values as:\r\n{code}.", l => l.CreatedExpressionTreeForReaderRowData(Model, Code), LogLevel.Debug),
            new LogEventCase(nameof(LoggingExtensions.CreatedExpressionTreeForModel), LogLevel.Debug, EventIdentifier.ExpressionTreeCreation, "Compiled code to map model {model} to stored procedure {sproc} as:\r\n{code}.", l => l.CreatedExpressionTreeForModel(Model, "rd.Get", Code), LogLevel.Debug),
            new LogEventCase(nameof(LoggingExtensions.EmptyResult), LogLevel.Debug, EventIdentifier.UnexpectedDbNull, "The base object could not be built because the query returned an empty result. The query was: “{ queryName }” ", l => l.EmptyResult("rd.Get")),
            new LogEventCase(nameof(LoggingExtensions.BatchStepStart), LogLevel.Debug, EventIdentifier.BatchStep, "Starting batch step { stepNumber.ToString() } on connection “{ connection }” ", l => l.BatchStepStart(2, "ShardMap01")),
            new LogEventCase(nameof(LoggingExtensions.BatchStepEnd), LogLevel.Debug, EventIdentifier.BatchStep, "Completed batch step { stepNumber.ToString() } on connection “{ connection }” ", l => l.BatchStepEnd(2, "ShardMap01")),
        };

        /// <summary>The members that open a logging scope rather than emit an event.</summary>
        private static readonly string[] ScopeMethods = { nameof(LoggingExtensions.BuildSqlResultsHandlerScope) };

        public static IEnumerable<object[]> Cases => Table.Select(c => new object[] { c });

        public static IEnumerable<object[]> GuardedCases => Table.Where(c => c.Guard.HasValue).Select(c => new object[] { c });

        private static readonly Dictionary<string, Func<ILogger?, Expression, Action>> ExpressionTreeWrappers = new Dictionary<string, Func<ILogger?, Expression, Action>>
        {
            [nameof(LoggingExtensions.CreatedExpressionTreeForSetInParameters)] = (l, e) => () => l.CreatedExpressionTreeForSetInParameters(Model, e),
            [nameof(LoggingExtensions.CreatedExpressionTreeForSetOutParameters)] = (l, e) => () => l.CreatedExpressionTreeForSetOutParameters(Model, e),
            [nameof(LoggingExtensions.CreatedExpressionTreeForReadOutParameters)] = (l, e) => () => l.CreatedExpressionTreeForReadOutParameters(Model, e),
            [nameof(LoggingExtensions.CreatedExpressionTreeForReaderOrdinals)] = (l, e) => () => l.CreatedExpressionTreeForReaderOrdinals(Model, e),
            [nameof(LoggingExtensions.CreatedExpressionTreeForReaderRowData)] = (l, e) => () => l.CreatedExpressionTreeForReaderRowData(Model, e),
            [nameof(LoggingExtensions.CreatedExpressionTreeForModel)] = (l, e) => () => l.CreatedExpressionTreeForModel(Model, "rd.Get", e),
        };

        public static IEnumerable<object[]> ExpressionTreeCases => ExpressionTreeWrappers.Keys.Select(k => new object[] { k });

        [Theory]
        [MemberData(nameof(Cases))]
        [Trait("Spec", "blk-abc42ce2cbf448278919e14586a16c1a")]
        [Trait("Spec", "blk-36c34508e6e1465d9f851eb2ae4766fd")]
        [Trait("Spec", "blk-ca70152bf9fa440d9eb6d57fc5ad30ef")]
        [Trait("Spec", "blk-860f5b1abf194ddea94eea2d0f7e237d")]
        [Trait("Spec", "blk-805df5b3d8854fca85f8eac3741f9b6a")]
        [Trait("Spec", "blk-5bb293884a0642ec95a6af4951dc3712")]
        [Trait("Spec", "blk-a8f95790014d4e30a5b095f38dc8cafb")]
        [Trait("Spec", "blk-b93bf711042342e09e6c0850221ae7d7")]
        [Trait("Spec", "blk-fde5bc17103f4097a05f6df357f04705")]
        [Trait("Spec", "blk-6c3a5dabb90943cfb4d1d234eac1033c")]
        [Trait("Spec", "blk-3e4daa26d49e4a889e6812830124cfb5")]
        [Trait("Spec", "blk-64c08c5b6cd742c393b1320b2bd16cf3")]
        [Trait("Spec", "blk-5340d35b9eb3497c87d01ced6700bd3e")]
        [Trait("Spec", "blk-504a75adc6494b1b9275f6ce68126570")]
        [Trait("Spec", "blk-6565dc6a77354ba8a2692ce5e2955f78")]
        [Trait("Spec", "blk-8cc0d157123145c488c9b2cae55023af")]
        [Trait("Spec", "blk-5ac10e97a55d4b65823a1e2e4d68bc99")]
        [Trait("Spec", "blk-6cf8bb8f39a441549df1df52a8f40372")]
        public void EveryWrapperEmitsExactlyOneEventWithItsDocumentedIdNameLevelAndTemplate(LogEventCase wrapper)
        {
            var logger = new RecordingLogger();

            wrapper.Invoke(logger);

            var entry = logger.Entries.Should().ContainSingle($"{wrapper.Method} emits exactly one event").Subject;
            entry.Id.Should().Be(wrapper.Id);
            entry.Name.Should().Be(wrapper.Method, "the event name is the wrapper method's name");
            entry.Level.Should().Be(wrapper.Level);
            entry.Template.Should().Be(wrapper.Template);
            entry.Message.Should().NotBeNullOrEmpty();
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void EveryWrapperToleratesANullLogger(LogEventCase wrapper)
        {
            Action act = () => wrapper.Invoke(null);
            act.Should().NotThrow();
        }

        [Fact]
        public void TheTableNamesEveryWrapperMethodOfTheClass()
        {
            var wrappers = typeof(LoggingExtensions)
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(m => !m.IsSpecialName && m.GetParameters().Length > 0 && m.GetParameters()[0].ParameterType == typeof(ILogger))
                .Select(m => m.Name)
                .Distinct()
                .OrderBy(n => n, StringComparer.Ordinal);

            wrappers.Should().BeEquivalentTo(Table.Select(c => c.Method).Concat(ScopeMethods), "every ILogger extension method must be in the table (or be a scope method)");
            Table.Select(c => c.Method).Should().OnlyHaveUniqueItems();
        }

        [Fact]
        [Trait("Spec", "blk-b7995b773d6c451eb11adf89f4a25ac5")]
        [Trait("Spec", "blk-82df34ff3b7e4f3288908b82a80a5ba6")]
        public void EventIdentifiersAreTheDocumentedThirteenDistinctCategories()
        {
            var members = Enum.GetValues(typeof(EventIdentifier)).Cast<EventIdentifier>().ToList();

            members.Select(m => (int)m).Should().OnlyHaveUniqueItems("event ids are unique within the class");
            members.Select(m => (m.ToString(), (int)m)).Should().Equal(
                ("ConnectionStringBuilt", 0), ("ExpressionTreeCreation", 1), ("MapperSqlParameterNotFound", 2), ("MapperSqlColumnNotFound", 3),
                ("MapperCacheStatus", 4), ("MapperProcessTrace", 5), ("UnexpectedDbNull", 6), ("MapperResultsReaderInvalid", 7),
                ("CmdExecuted", 8), ("ConnectRetry", 9), ("CommandRetry", 10), ("CircuitBreaker", 11), ("BatchStep", 12));
            Table.Select(c => c.Id).Distinct().Should().BeSubsetOf(members.Select(m => (int)m), "every emitted id is a declared category");
            Table.Select(c => (c.Id, c.Method)).Should().OnlyHaveUniqueItems("events sharing a category id are told apart by name");
            Table.Select(c => c.Template).Should().OnlyHaveUniqueItems("every event has its own message");
        }

        [Fact]
        [Trait("Spec", "blk-37977366bbdb485ebad77635ea3f17fd")]
        public void EveryEventDelegateIsCreatedOnceInTheStaticConstructor()
        {
            typeof(LoggingExtensions).TypeInitializer.Should().NotBeNull("the delegates are compiled in a static constructor");
            var delegateFields = typeof(LoggingExtensions)
                .GetFields(BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(f => typeof(Delegate).IsAssignableFrom(f.FieldType))
                .ToList();

            delegateFields.Should().NotBeEmpty();
            delegateFields.Should().OnlyContain(f => f.IsInitOnly, "a readonly field can only be assigned by the static constructor");
            delegateFields.Should().OnlyContain(f => f.GetValue(null) != null, "every delegate is compiled when the type initializes");
            delegateFields.Count.Should().BeGreaterThanOrEqualTo(Table.Length + ScopeMethods.Length, "each wrapper has a delegate of its own");
        }

        [Theory]
        [MemberData(nameof(GuardedCases))]
        [Trait("Spec", "blk-37977366bbdb485ebad77635ea3f17fd")]
        public void GuardedWrappersEmitNothingWhenTheirLevelIsDisabled(LogEventCase wrapper)
        {
            var logger = new RecordingLogger { MinimumLevel = wrapper.Guard!.Value + 1 };

            wrapper.Invoke(logger);

            logger.Entries.Should().BeEmpty();
        }

        [Theory]
        [MemberData(nameof(ExpressionTreeCases))]
        [Trait("Spec", "blk-8cc0d157123145c488c9b2cae55023af")]
        [Trait("Spec", "blk-6565dc6a77354ba8a2692ce5e2955f78")]
        [Trait("Spec", "blk-e6b2448d351a47e6b17d152202f47d05")]
        public void ExpressionTreeWrappersRenderOnlyWhenDebugIsEnabled(string method)
        {
            var disabled = new RecordingLogger { MinimumLevel = LogLevel.Information };
            var skipped = new RenderingProbe();
            ExpressionTreeWrappers[method](disabled, skipped)();
            skipped.Rendered.Should().BeFalse("no rendering work is done when Debug is disabled");
            disabled.Entries.Should().BeEmpty();

            var enabled = new RecordingLogger { MinimumLevel = LogLevel.Debug };
            var rendered = new RenderingProbe();
            ExpressionTreeWrappers[method](enabled, rendered)();
            rendered.Rendered.Should().BeTrue("the tree is rendered when Debug is enabled");
            enabled.Entries.Should().ContainSingle().Which.Message.Should().Contain(".Extension<", "the rendered debug view is part of the message");
        }

        [Fact]
        [Trait("Spec", "blk-3e4daa26d49e4a889e6812830124cfb5")]
        [Trait("Spec", "blk-64c08c5b6cd742c393b1320b2bd16cf3")]
        public void BuildSqlResultsHandlerScopeOpensADisposableScopeAndReturnsNullForANullLogger()
        {
            var logger = new ScopeRecordingLogger();

            using (var scope = logger.BuildSqlResultsHandlerScope("rd.Get", typeof(int)))
            {
                scope.Should().NotBeNull();
                logger.OpenScopes.Should().ContainSingle().Which.Should().Contain("rd.Get").And.Contain("System.Int32");
            }
            logger.OpenScopes.Should().BeEmpty("disposing the scope closes it");
            logger.Entries.Should().BeEmpty("a scope is not an event");

            ILogger? none = null;
            none.BuildSqlResultsHandlerScope("rd.Get", typeof(int)).Should().BeNull();
        }

        [Fact]
        [Trait("Spec", "blk-0cfffb849a1844d884112eb51625d35e")]
        public void BatchStepEndNamesTheStepAndConnection()
        {
            var logger = new RecordingLogger();

            logger.BatchStepEnd(2, "ShardMap01");

            var entry = logger.Entries.Should().ContainSingle().Subject;
            entry.Level.Should().Be(LogLevel.Debug);
            entry.Id.Should().Be((int)EventIdentifier.BatchStep);
            entry.Name.Should().Be(nameof(LoggingExtensions.BatchStepEnd));
            entry.Message.Should().StartWith("Completed batch step 2 on connection").And.Contain("ShardMap01");
        }

        [Fact]
        [Trait("Spec", "blk-344945c1c13043a3aa52a115b6b863ba")]
        public void DataReaderIsNullReportsTheProcedureAndConnection()
        {
            var logger = new RecordingLogger();

            logger.DataReaderIsNull("usp_GetCustomer", "Shard03");

            var entry = logger.Entries.Should().ContainSingle().Subject;
            entry.Level.Should().Be(LogLevel.Error);
            entry.Id.Should().Be((int)EventIdentifier.MapperResultsReaderInvalid);
            entry.Message.Should().Be("Expected data reader object was null from procedure usp_GetCustomer on connection Shard03.");
        }

        [Fact]
        [Trait("Spec", "blk-9d51af3c3e2e4c659ff706705a75c92e")]
        [Trait("Spec", "blk-6c3a5dabb90943cfb4d1d234eac1033c")]
        public void NullShardChildArgumentsLogsOnlyWhenAnIdentityIsNull()
        {
            var logger = new RecordingLogger { MinimumLevel = LogLevel.Information };

            logger.NullShardChildArguments("ChildKey", default(ShardKey<string, int>));
            var entry = logger.Entries.Should().ContainSingle("a null record id is reported").Subject;
            entry.Level.Should().Be(LogLevel.Information);
            entry.Id.Should().Be((int)EventIdentifier.UnexpectedDbNull);
            entry.Name.Should().Be(nameof(LoggingExtensions.NullShardChildArguments));
            entry.Message.Should().Contain("ChildKey").And.Contain(default(ShardKey<string, int>).ToString());

            logger.Clear();
            logger.NullShardChildArguments("ChildKey", new ShardKey<int, int>(0, 5, 0));
            logger.Entries.Should().BeEmpty("value-typed identities can never be null");

            var disabled = new RecordingLogger { MinimumLevel = LogLevel.Warning };
            disabled.NullShardChildArguments("ChildKey", default(ShardKey<string, int>));
            disabled.Entries.Should().BeEmpty("the wrapper guards at Information before formatting the key");
        }

        [Fact]
        [Trait("Spec", "blk-860f5b1abf194ddea94eea2d0f7e237d")]
        public void TheFourCacheKindsAreDistinguishableByEventNameAndMessage()
        {
            var logger = new RecordingLogger();
            logger.SqlInParametersCacheMiss(Model);
            logger.SqlSetOutParametersCacheMiss(Model);
            logger.SqlReadOutParametersCacheMiss(Model);
            logger.SqlReaderCacheMiss(Model);

            logger.Entries.Should().HaveCount(4).And.OnlyContain(e => e.Id == (int)EventIdentifier.MapperCacheStatus && e.Level == LogLevel.Debug);
            logger.Entries.Select(e => e.Name).Should().OnlyHaveUniqueItems();
            logger.Entries.Select(e => e.Message).Should().OnlyHaveUniqueItems();
            logger.Entries.Select(e => e.Message).Should().SatisfyRespectively(
                m => m.Should().Contain("input parameters"),
                m => m.Should().Contain("output parameters"),
                m => m.Should().Contain("reading output parameters"),
                m => m.Should().Contain("data reader"));
            logger.Entries.Should().OnlyContain(e => e.Message!.Contains(Model.ToString()), "the model type is the cache key");
        }

        /// <summary>A recording logger that also tracks the scopes currently open.</summary>
        private sealed class ScopeRecordingLogger : RecordingLogger
        {
            private readonly List<string> _openScopes = new List<string>();
            public IReadOnlyList<string> OpenScopes => _openScopes;

            public override IDisposable? BeginScope<TState>(TState state)
            {
                var text = state?.ToString() ?? string.Empty;
                _openScopes.Add(text);
                return new Closer(() => _openScopes.Remove(text));
            }

            private sealed class Closer : IDisposable
            {
                private readonly Action _onDispose;
                public Closer(Action onDispose) => _onDispose = onDispose;
                public void Dispose() => _onDispose();
            }
        }
    }
}
