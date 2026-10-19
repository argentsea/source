using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace ArgentSea.Sql.Test
{
    /// <summary>
    /// ReturnValueAsync unboxed the return parameter's Value without checking it, so a command that produced no return value
    /// (for example, a text statement) threw NullReferenceException instead of a descriptive error.
    /// </summary>
    public class ReturnValueTests
    {
        [Fact]
        public async Task ReturnValueWithoutAReturnedValueThrowsUnexpectedSqlResult()
        {
            var shard = InMemoryShardSets.FromTestConfiguration()["Inherit"][0];

            Func<Task> act = () => shard.Write.ReturnValueAsync(new QueryProcedure("ws.NoReturnValue"), CancellationToken.None);

            await act.Should().ThrowAsync<UnexpectedSqlResultException>();
        }
    }
}
