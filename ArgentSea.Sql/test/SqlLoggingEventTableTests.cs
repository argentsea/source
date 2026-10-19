using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ArgentSea.TestKit;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xunit;
using static ArgentSea.Sql.SqlLoggingExtensions;

namespace ArgentSea.Sql.Test
{
    /// <summary>
    /// Every wrapper method of <see cref="SqlLoggingExtensions"/> (the table-valued-parameter mapping events) is enumerated in
    /// one table and proven to emit exactly one event with its documented id, name, level and message template.
    /// </summary>
    public class SqlLoggingEventTableTests
    {
        public sealed class LogEventCase
        {
            public LogEventCase(string method, LogLevel level, SqlEventIdentifier id, string template, Action<ILogger?> invoke)
            {
                Method = method;
                Level = level;
                Id = (int)id;
                Template = template;
                Invoke = invoke;
            }

            public string Method { get; }
            public LogLevel Level { get; }
            public int Id { get; }
            public string Template { get; }
            public Action<ILogger?> Invoke { get; }
            public override string ToString() => Method;
        }

        private static readonly Type Model = typeof(string);

        private static readonly LogEventCase[] Table =
        {
            new LogEventCase(nameof(SqlLoggingExtensions.SqlTvpCacheMiss), LogLevel.Information, SqlEventIdentifier.MapperTvpCacheStatus, "No cached delegate was found for mapping type {modelT} to Sql row metadata; this is normal for the first execution.", l => l.SqlTvpCacheMiss(Model)),
            new LogEventCase(nameof(SqlLoggingExtensions.SqlTvpCacheHit), LogLevel.Debug, SqlEventIdentifier.MapperTvpCacheStatus, "A cached delegate for mapping type {modelT} to Sql row metadata was found.", l => l.SqlTvpCacheHit(Model)),
            new LogEventCase(nameof(SqlLoggingExtensions.TraceTvpMapperProperty), LogLevel.Debug, SqlEventIdentifier.MapperTvpTrace, "Tvp mapper is now processing property {name}.", l => l.TraceTvpMapperProperty("Id")),
        };

        private static readonly string[] ScopeMethods = { nameof(SqlLoggingExtensions.BuildTvpScope) };

        public static IEnumerable<object[]> Cases => Table.Select(c => new object[] { c });

        [Theory]
        [MemberData(nameof(Cases))]
        [Trait("Spec", "blk-704f4b571f09478084ce83cf7fe88225")]
        [Trait("Spec", "blk-f8bd3bf6109449d9863f4016a9ff86cb")]
        [Trait("Spec", "blk-6ede686128d540138594c38740e2775d")]
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
            var wrappers = typeof(SqlLoggingExtensions)
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(m => !m.IsSpecialName && m.GetParameters().Length > 0 && m.GetParameters()[0].ParameterType == typeof(ILogger))
                .Select(m => m.Name)
                .Distinct();

            wrappers.Should().BeEquivalentTo(Table.Select(c => c.Method).Concat(ScopeMethods));
            Table.Select(c => c.Method).Should().OnlyHaveUniqueItems();
        }

        [Fact]
        [Trait("Spec", "blk-704f4b571f09478084ce83cf7fe88225")]
        public void SqlEventIdentifierIsAClosedPairOfDistinctCategories()
        {
            var members = Enum.GetValues(typeof(SqlEventIdentifier)).Cast<SqlEventIdentifier>().ToList();

            members.Select(m => m.ToString()).Should().Equal("MapperTvpCacheStatus", "MapperTvpTrace");
            members.Select(m => (int)m).Should().OnlyHaveUniqueItems("event ids are unique within the class");
            Table.Select(c => c.Id).Distinct().Should().BeEquivalentTo(members.Select(m => (int)m), "every category has an emitting wrapper");
            Table.Select(c => (c.Id, c.Method)).Should().OnlyHaveUniqueItems();
            Table.Select(c => c.Template).Should().OnlyHaveUniqueItems();
        }

        [Fact]
        public void EveryEventDelegateIsCreatedOnceInTheStaticConstructor()
        {
            typeof(SqlLoggingExtensions).TypeInitializer.Should().NotBeNull();
            var delegateFields = typeof(SqlLoggingExtensions)
                .GetFields(BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(f => typeof(Delegate).IsAssignableFrom(f.FieldType))
                .ToList();

            delegateFields.Should().HaveCount(Table.Length + ScopeMethods.Length);
            delegateFields.Should().OnlyContain(f => f.IsInitOnly && f.GetValue(null) != null);
        }

        [Fact]
        public void BuildTvpScopeOpensAScopeAndReturnsNullForANullLogger()
        {
            var logger = new RecordingLogger();
            using (var scope = logger.BuildTvpScope(Model))
            {
                scope.Should().NotBeNull();
            }
            logger.Entries.Should().BeEmpty("a scope is not an event");

            ILogger? none = null;
            none.BuildTvpScope(Model).Should().BeNull();
        }
    }
}
