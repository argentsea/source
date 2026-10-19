using System;
using System.Linq.Expressions;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ArgentSea.Pg.Test
{
    /// <summary>
    /// CopyMapper logs through PgLoggingExtensions unconditionally (cache hits/misses, the generated COPY statements, and
    /// per-property traces from its generated code), but those methods dereferenced the logger, so a COPY batch step
    /// with no logger threw NullReferenceException.
    /// </summary>
    public class PgLoggingExtensionsNullLoggerTests
    {
        [Fact]
        public void PgLoggingExtensions_NullLogger_AreNoOps()
        {
            ILogger? logger = null;
            Action act = () =>
            {
                logger.PgCopyCacheMiss(typeof(string));
                logger.PgCopyCacheHit(typeof(string));
                logger.TraceCopyMapperProperty("Id");
                logger.CreatedExpressionTreeForCopy(typeof(string), Expression.Constant(1));
                logger.CopySqlStatements("CREATE TEMP TABLE t (id int);", "COPY t (id) FROM STDIN (FORMAT BINARY);");
            };
            act.Should().NotThrow();
        }
    }
}
