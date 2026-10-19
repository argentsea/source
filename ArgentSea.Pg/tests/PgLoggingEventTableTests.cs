using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using ArgentSea.TestKit;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xunit;
using static ArgentSea.Pg.PgLoggingExtensions;
using CoreEvents = ArgentSea.LoggingExtensions.EventIdentifier;

namespace ArgentSea.Pg.Test
{
    /// <summary>
    /// Every wrapper method of <see cref="PgLoggingExtensions"/> (the COPY-load events) is enumerated in one table and proven
    /// to emit exactly one event with its documented id, name, level and message template. The class has no enum of its own:
    /// every id is a core <see cref="ArgentSea.LoggingExtensions.EventIdentifier"/> category.
    /// </summary>
    public class PgLoggingEventTableTests
    {
        public sealed class LogEventCase
        {
            public LogEventCase(string method, LogLevel level, CoreEvents id, string template, Action<ILogger?> invoke, LogLevel? guard = null)
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

        private static readonly Type Model = typeof(string);
        private static readonly Expression Code = Expression.Constant(1);

        private static readonly LogEventCase[] Table =
        {
            new LogEventCase(nameof(PgLoggingExtensions.PgCopyCacheMiss), LogLevel.Information, CoreEvents.MapperCacheStatus, "No cached delegate was found for mapping type {modelT} to Pg row metadata; this is normal for the first execution.", l => l.PgCopyCacheMiss(Model)),
            new LogEventCase(nameof(PgLoggingExtensions.PgCopyCacheHit), LogLevel.Debug, CoreEvents.MapperCacheStatus, "A cached delegate for mapping type {modelT} to Pg row metadata was found.", l => l.PgCopyCacheHit(Model)),
            new LogEventCase(nameof(PgLoggingExtensions.TraceCopyMapperProperty), LogLevel.Trace, CoreEvents.MapperProcessTrace, "Copy mapper is now processing property {name}.", l => l.TraceCopyMapperProperty("Id")),
            new LogEventCase(nameof(PgLoggingExtensions.CreatedExpressionTreeForCopy), LogLevel.Debug, CoreEvents.ExpressionTreeCreation, "Compiled code to map model {model} to Pg COPY row values as:\r\n{code}.", l => l.CreatedExpressionTreeForCopy(Model, Code), LogLevel.Debug),
            new LogEventCase(nameof(PgLoggingExtensions.CopySqlStatements), LogLevel.Debug, CoreEvents.CmdExecuted, "Copy mapper used a create table definition of: \r\n{tableDef} \r\n and used the copy import statment of:\r\n{copyDef}.", l => l.CopySqlStatements("CREATE TEMP TABLE t (id int);", "COPY t (id) FROM STDIN (FORMAT BINARY);")),
        };

        public static IEnumerable<object[]> Cases => Table.Select(c => new object[] { c });

        [Theory]
        [MemberData(nameof(Cases))]
        [Trait("Spec", "blk-b2f1ecbf8fdc46818abe840ed7750aa2")]
        [Trait("Spec", "blk-1aa134af0f084bb785fd7742011220c3")]
        [Trait("Spec", "blk-0065a202e5464441ba6e78fee28cbee0")]
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
            var wrappers = typeof(PgLoggingExtensions)
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(m => !m.IsSpecialName && m.GetParameters().Length > 0 && m.GetParameters()[0].ParameterType == typeof(ILogger))
                .Select(m => m.Name)
                .Distinct();

            wrappers.Should().BeEquivalentTo(Table.Select(c => c.Method));
            Table.Select(c => c.Method).Should().OnlyHaveUniqueItems();
        }

        [Fact]
        [Trait("Spec", "blk-b2f1ecbf8fdc46818abe840ed7750aa2")]
        public void EveryEventUsesACoreCategoryAndEventsAreToldApartByName()
        {
            var coreIds = Enum.GetValues(typeof(CoreEvents)).Cast<CoreEvents>().Select(m => (int)m).ToList();

            Table.Select(c => c.Id).Distinct().Should().BeSubsetOf(coreIds, "COPY events share the core categories");
            Table.Select(c => (c.Id, c.Method)).Should().OnlyHaveUniqueItems("event ids are unique within the class when paired with the event name");
            Table.Select(c => c.Template).Should().OnlyHaveUniqueItems();
        }

        [Fact]
        public void EveryEventDelegateIsCreatedOnceInTheStaticConstructor()
        {
            typeof(PgLoggingExtensions).TypeInitializer.Should().NotBeNull();
            var delegateFields = typeof(PgLoggingExtensions)
                .GetFields(BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(f => typeof(Delegate).IsAssignableFrom(f.FieldType))
                .ToList();

            delegateFields.Should().HaveCount(Table.Length);
            delegateFields.Should().OnlyContain(f => f.IsInitOnly && f.GetValue(null) != null);
        }

        [Fact]
        [Trait("Spec", "blk-b2f1ecbf8fdc46818abe840ed7750aa2")]
        [Trait("Spec", "blk-1aa134af0f084bb785fd7742011220c3")]
        public void CreatedExpressionTreeForCopyRendersOnlyWhenDebugIsEnabled()
        {
            var disabled = new RecordingLogger { MinimumLevel = LogLevel.Information };
            var skipped = new RenderingProbe();
            disabled.CreatedExpressionTreeForCopy(Model, skipped);
            skipped.Rendered.Should().BeFalse();
            disabled.Entries.Should().BeEmpty();

            var enabled = new RecordingLogger { MinimumLevel = LogLevel.Debug };
            var rendered = new RenderingProbe();
            enabled.CreatedExpressionTreeForCopy(Model, rendered);
            rendered.Rendered.Should().BeTrue();
            enabled.Entries.Should().ContainSingle().Which.Message.Should().Contain(".Extension<");
        }
    }
}
