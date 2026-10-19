using FluentAssertions;
using Microsoft.Extensions.Logging;
using System;
using System.Linq.Expressions;
using Xunit;

namespace ArgentSea.Test
{
    public class LoggingExtensionsNullLoggerTests
    {
        // The library treats the logger as optional (it is called with ?. throughout), but the LoggingExtensions methods
        // dereferenced it, so a null logger crashed the mapper, batches and other callers that invoke them directly.
        [Fact]
        public void LoggingExtensions_NullLogger_AreNoOps()
        {
            ILogger? logger = null;
            var act = () =>
            {
                logger.SqlInParametersCacheMiss(typeof(string));
                logger.SqlReaderCacheHit(typeof(string));
                logger.SqlParameterNotFound("@Id", typeof(int));
                logger.SqlFieldNotFound("Id", "Model");
                logger.TraceInMapperProperty("Id");
                logger.TraceDbCmdExecuted("query", "connection", 5);
                logger.RetryingDbConnection("connection", 1, new Exception());
                logger.BatchStepStart(0, "connection");
                logger.BatchStepEnd(0, "connection");
                logger.CreatedExpressionTreeForModel(typeof(string), "query", Expression.Constant(1));
                logger.BuildSqlResultsHandlerScope("query", typeof(string)).Should().BeNull();
            };
            act.Should().NotThrow();
        }
    }
}
