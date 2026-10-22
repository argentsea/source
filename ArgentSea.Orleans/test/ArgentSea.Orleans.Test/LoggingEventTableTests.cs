using System.Linq.Expressions;
using System.Reflection;
using ArgentSea.TestKit;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using static ArgentSea.Orleans.LoggingExtensions;
using CoreEvents = ArgentSea.LoggingExtensions.EventIdentifier;

namespace ArgentSea.Orleans.Test
{
    /// <summary>
    /// Every wrapper method of the internal Orleans <see cref="LoggingExtensions"/> (the grain persistence events) is
    /// enumerated in one table and proven to emit exactly one event with its documented id, name, level and message template.
    /// </summary>
    public class LoggingEventTableTests
    {
        public sealed class LogEventCase
        {
            public LogEventCase(string method, LogLevel level, int id, string template, Action<ILogger?> invoke, LogLevel? guard = null)
            {
                Method = method;
                Level = level;
                Id = id;
                Template = template;
                Invoke = invoke;
                Guard = guard;
            }

            public string Method { get; }
            public LogLevel Level { get; }
            public int Id { get; }
            public string Template { get; }
            public Action<ILogger?> Invoke { get; }
            public LogLevel? Guard { get; }
            public override string ToString() => Method;
        }

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

        private const string Grain = "CustomerGrain";
        private static readonly Expression Code = Expression.Constant(1);

        private static readonly LogEventCase[] Table =
        {
            new LogEventCase(nameof(LoggingExtensions.PersistanceLifecycleOnStart), LogLevel.Debug, (int)EventIdentifier.PersistanceStart, "Grain persistance {name} is initializing.", l => l.PersistanceLifecycleOnStart("ArgentSea")),
            new LogEventCase(nameof(LoggingExtensions.PersistanceLifecycleOnStop), LogLevel.Debug, (int)EventIdentifier.PersistanceStop, "Grain persistance {name} is terminating.", l => l.PersistanceLifecycleOnStop("ArgentSea")),
            new LogEventCase(nameof(LoggingExtensions.OrleansDbCacheMiss), LogLevel.Debug, (int)EventIdentifier.MapperCacheStatus, "No cached delegate for handling orleans database keys was initialized for type {grainType}; this is normal for the first execution.", l => l.OrleansDbCacheMiss(Grain)),
            new LogEventCase(nameof(LoggingExtensions.OrleansDbCacheHit), LogLevel.Trace, (int)EventIdentifier.MapperCacheStatus, "The cached delegate for handling orleans database keys was already initialized for type {grainType}.", l => l.OrleansDbCacheHit(Grain)),
            new LogEventCase(nameof(LoggingExtensions.OrleansShardCacheMiss), LogLevel.Debug, (int)EventIdentifier.MapperCacheStatus, "No cached delegate for handling orleans sharded keys was initialized for type {grainType}; this is normal for the first execution.", l => l.OrleansShardCacheMiss(Grain)),
            new LogEventCase(nameof(LoggingExtensions.OrleansShardCacheHit), LogLevel.Trace, (int)EventIdentifier.MapperCacheStatus, "The cached delegate for handling orleans sharded keys was already initialized for type {grainType}.", l => l.OrleansShardCacheHit(Grain)),
            new LogEventCase(nameof(LoggingExtensions.TraceMapperGrainPersistance), LogLevel.Trace, (int)EventIdentifier.MapperProcessTrace, "Grain persistance mapper is now processing property {name}.", l => l.TraceMapperGrainPersistance("Id")),
            new LogEventCase(nameof(LoggingExtensions.CreatedExpressionTreeForDatabaseKey), LogLevel.Debug, (int)EventIdentifier.ExpressionTreeCreation, "Compiled code to expression tree for grain {grainType} key mapping for database as:\r\n{code}.", l => l.CreatedExpressionTreeForDatabaseKey(Grain, Code), LogLevel.Debug),
            new LogEventCase(nameof(LoggingExtensions.CreatedExpressionTreeForShardKey), LogLevel.Debug, (int)EventIdentifier.ExpressionTreeCreation, "Compiled code to expression tree for grain {grainType} key mapping for shards as:\r\n{code}.", l => l.CreatedExpressionTreeForShardKey(Grain, Code), LogLevel.Debug),
            new LogEventCase(nameof(LoggingExtensions.CreatedExpressionTreeForActivation), LogLevel.Debug, (int)EventIdentifier.ExpressionTreeCreation, "Compiled code to expression tree for grain {grainType} activation, setting the new state's record identifiers from the grain key, as:\r\n{code}.", l => l.CreatedExpressionTreeForActivation(Grain, Code), LogLevel.Debug),
            new LogEventCase(nameof(LoggingExtensions.TraceDbReadCmdExecuted), LogLevel.Trace, (int)EventIdentifier.CmdExecuted, "Executed read Db command on grainType {grainType} in {milliseconds} milliseconds.", l => l.TraceDbReadCmdExecuted(Grain, 12), LogLevel.Trace),
            new LogEventCase(nameof(LoggingExtensions.TraceDbWriteCmdExecuted), LogLevel.Trace, (int)EventIdentifier.CmdExecuted, "Executed write Db command on grainType {grainType} in {milliseconds} milliseconds.", l => l.TraceDbWriteCmdExecuted(Grain, 12), LogLevel.Trace),
            new LogEventCase(nameof(LoggingExtensions.TraceDbClearCmdExecuted), LogLevel.Trace, (int)EventIdentifier.CmdExecuted, "Executed clear Db command on grainType {grainType} in {milliseconds} milliseconds.", l => l.TraceDbClearCmdExecuted(Grain, 12), LogLevel.Trace),
            new LogEventCase(nameof(LoggingExtensions.TraceShardReadCmdExecuted), LogLevel.Trace, (int)EventIdentifier.CmdExecuted, "Executed read command on shard {shardId} for grainType {grainType} in {milliseconds} milliseconds.", l => l.TraceShardReadCmdExecuted(3, Grain, 12), LogLevel.Trace),
            new LogEventCase(nameof(LoggingExtensions.TraceShardWriteCmdExecuted), LogLevel.Trace, (int)EventIdentifier.CmdExecuted, "Executed write command on shard {shardId} for grainType {grainType} in {milliseconds} milliseconds.", l => l.TraceShardWriteCmdExecuted(3, Grain, 12), LogLevel.Trace),
            new LogEventCase(nameof(LoggingExtensions.TraceShardClearCmdExecuted), LogLevel.Trace, (int)EventIdentifier.CmdExecuted, "Executed clear command on shard {shardId} for grainType {grainType} in {milliseconds} milliseconds.", l => l.TraceShardClearCmdExecuted(3, Grain, 12), LogLevel.Trace),
        };

        public static IEnumerable<object[]> Cases => Table.Select(c => new object[] { c });

        public static IEnumerable<object[]> GuardedCases => Table.Where(c => c.Guard.HasValue).Select(c => new object[] { c });

        private static readonly Dictionary<string, Func<ILogger?, Expression, Action>> ExpressionTreeWrappers = new()
        {
            [nameof(LoggingExtensions.CreatedExpressionTreeForDatabaseKey)] = (l, e) => () => l.CreatedExpressionTreeForDatabaseKey(Grain, e),
            [nameof(LoggingExtensions.CreatedExpressionTreeForShardKey)] = (l, e) => () => l.CreatedExpressionTreeForShardKey(Grain, e),
            [nameof(LoggingExtensions.CreatedExpressionTreeForActivation)] = (l, e) => () => l.CreatedExpressionTreeForActivation(Grain, e),
        };

        public static IEnumerable<object[]> ExpressionTreeCases => ExpressionTreeWrappers.Keys.Select(k => new object[] { k });

        [Theory]
        [MemberData(nameof(Cases))]
        [Trait("Spec", "blk-d0cc6db246764e9eb27c2cf3f1941f24")]
        [Trait("Spec", "blk-72f17c7ae63e45af81152a200221bf01")]
        [Trait("Spec", "blk-96f914b6d2ba4a4cb2baed7c60cd508d")]
        [Trait("Spec", "blk-50b608096168489f837b63331c85832e")]
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
                .Distinct();

            wrappers.Should().BeEquivalentTo(Table.Select(c => c.Method));
            Table.Select(c => c.Method).Should().OnlyHaveUniqueItems();
        }

        [Fact]
        [Trait("Spec", "blk-d0cc6db246764e9eb27c2cf3f1941f24")]
        public void EventIdentifiersReuseTheCoreCategoriesAndExtendThemWithTheLifecyclePair()
        {
            var members = Enum.GetValues(typeof(EventIdentifier)).Cast<EventIdentifier>().ToList();

            members.Select(m => (int)m).Should().OnlyHaveUniqueItems("event ids are unique within the class");
            members.Select(m => (m.ToString(), (int)m)).Should().BeEquivalentTo(new[]
            {
                ("ExpressionTreeCreation", 1), ("MapperCacheStatus", 4), ("MapperProcessTrace", 5), ("CmdExecuted", 8), ("PersistanceStart", 13), ("PersistanceStop", 14),
            });
            foreach (var shared in members.Where(m => (int)m <= (int)CoreEvents.BatchStep))
            {
                Enum.GetName(typeof(CoreEvents), (int)shared).Should().Be(shared.ToString(), "a shared id names the same category as the core enum");
            }
            Table.Select(c => c.Id).Distinct().Should().BeEquivalentTo(members.Select(m => (int)m), "every category has an emitting wrapper");
            Table.Select(c => (c.Id, c.Method)).Should().OnlyHaveUniqueItems("events sharing a category id are told apart by name");
            Table.Select(c => c.Template).Should().OnlyHaveUniqueItems();
        }

        [Fact]
        public void EveryEventDelegateIsCreatedOnceInTheStaticConstructor()
        {
            typeof(LoggingExtensions).TypeInitializer.Should().NotBeNull();
            var delegateFields = typeof(LoggingExtensions)
                .GetFields(BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(f => typeof(Delegate).IsAssignableFrom(f.FieldType))
                .ToList();

            delegateFields.Should().HaveCount(Table.Length);
            delegateFields.Should().OnlyContain(f => f.IsInitOnly && f.GetValue(null) != null);
        }

        [Theory]
        [MemberData(nameof(GuardedCases))]
        [Trait("Spec", "blk-72f17c7ae63e45af81152a200221bf01")]
        public void GuardedWrappersEmitNothingWhenTheirLevelIsDisabled(LogEventCase wrapper)
        {
            var logger = new RecordingLogger { MinimumLevel = wrapper.Guard!.Value + 1 };

            wrapper.Invoke(logger);

            logger.Entries.Should().BeEmpty();
        }

        [Theory]
        [MemberData(nameof(ExpressionTreeCases))]
        [Trait("Spec", "blk-d0cc6db246764e9eb27c2cf3f1941f24")]
        public void ExpressionTreeWrappersRenderOnlyWhenDebugIsEnabled(string method)
        {
            var disabled = new RecordingLogger { MinimumLevel = LogLevel.Information };
            var skipped = new RenderingProbe();
            ExpressionTreeWrappers[method](disabled, skipped)();
            skipped.Rendered.Should().BeFalse();
            disabled.Entries.Should().BeEmpty();

            var enabled = new RecordingLogger { MinimumLevel = LogLevel.Debug };
            var rendered = new RenderingProbe();
            ExpressionTreeWrappers[method](enabled, rendered)();
            rendered.Rendered.Should().BeTrue();
            enabled.Entries.Should().ContainSingle().Which.Message.Should().Contain(".Extension<");
        }
    }
}
