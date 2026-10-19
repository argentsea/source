using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace ArgentSea.Test
{
    public class ShardFanOutTests
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

        private static async Task<string?> WaitForCancellationAsync(CancellationToken token)
        {
            await Task.Delay(System.Threading.Timeout.Infinite, token);
            return "never";
        }

        private static async Task<string?> FailAfterCancellationAsync(CancellationToken token)
        {
            try
            {
                await Task.Delay(System.Threading.Timeout.Infinite, token);
            }
            catch (OperationCanceledException)
            {
            }
            throw new InvalidOperationException("loser failed while being cancelled");
        }

        private static async Task<string?> FailAsync(string message)
        {
            await Task.Yield();
            throw new InvalidOperationException(message);
        }

        private static async Task<string?> ResultAfterAsync(Task gate, string value)
        {
            await gate;
            return value;
        }

        [Fact]
        public async Task FirstNonNullReturnsWinnerAndCancelsLosers()
        {
            using var cts = new CancellationTokenSource();
            var loser = WaitForCancellationAsync(cts.Token);
            var tasks = new List<Task<string?>> { loser, Task.FromResult<string?>("winner") };

            var result = await ShardFanOut.FirstNonNullAsync(tasks, cts, CancellationToken.None).WaitAsync(Timeout);

            result.Should().Be("winner");
            Func<Task> awaitLoser = () => loser.WaitAsync(Timeout);
            await awaitLoser.Should().ThrowAsync<OperationCanceledException>("the losing shard must receive the cancellation");
            loser.IsCanceled.Should().BeTrue();
        }

        [Fact]
        public async Task FirstNonNullIgnoresLoserFailureAfterResultIsChosen()
        {
            using var cts = new CancellationTokenSource();
            var loser = FailAfterCancellationAsync(cts.Token);
            var tasks = new List<Task<string?>> { loser, Task.FromResult<string?>("winner") };

            var result = await ShardFanOut.FirstNonNullAsync(tasks, cts, CancellationToken.None).WaitAsync(Timeout);

            result.Should().Be("winner");
            Func<Task> awaitLoser = () => loser.WaitAsync(Timeout);
            await awaitLoser.Should().ThrowAsync<InvalidOperationException>("the loser did fail, but the read-first result is unaffected");
        }

        [Fact]
        public async Task FirstNonNullReturnsLaterResultWhenAnotherShardFaults()
        {
            using var cts = new CancellationTokenSource();
            var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var failed = FailAsync("shard down");
            var later = ResultAfterAsync(gate.Task, "found");
            var tasks = new List<Task<string?>> { failed, later };

            var resultTask = ShardFanOut.FirstNonNullAsync(tasks, cts, CancellationToken.None);
            await failed.ContinueWith(_ => { });
            gate.SetResult(true);

            (await resultTask.WaitAsync(Timeout)).Should().Be("found");
        }

        [Fact]
        public async Task FirstNonNullSurfacesFailureWhenNoShardProducesAResult()
        {
            using var cts = new CancellationTokenSource();
            var tasks = new List<Task<string?>> { FailAsync("shard down"), Task.FromResult<string?>(null) };

            Func<Task> act = () => ShardFanOut.FirstNonNullAsync(tasks, cts, CancellationToken.None).WaitAsync(Timeout);

            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("shard down");
        }

        [Fact]
        public async Task FirstNonNullAggregatesMultipleFailuresWhenNoShardProducesAResult()
        {
            using var cts = new CancellationTokenSource();
            var tasks = new List<Task<string?>> { FailAsync("one"), FailAsync("two") };

            Func<Task> act = () => ShardFanOut.FirstNonNullAsync(tasks, cts, CancellationToken.None).WaitAsync(Timeout);

            (await act.Should().ThrowAsync<AggregateException>()).Which.InnerExceptions.Should().HaveCount(2);
        }

        [Fact]
        public async Task FirstNonNullReturnsDefaultWhenAllResultsAreNull()
        {
            using var cts = new CancellationTokenSource();
            var tasks = new List<Task<string?>> { Task.FromResult<string?>(null), Task.FromResult<string?>(null) };

            (await ShardFanOut.FirstNonNullAsync(tasks, cts, CancellationToken.None).WaitAsync(Timeout)).Should().BeNull();
        }

        [Fact]
        public async Task FirstNonNullThrowsWhenCallerCancels()
        {
            using var caller = new CancellationTokenSource();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller.Token);
            var tasks = new List<Task<string?>> { WaitForCancellationAsync(linked.Token), WaitForCancellationAsync(linked.Token) };

            var resultTask = ShardFanOut.FirstNonNullAsync(tasks, linked, caller.Token);
            caller.Cancel();

            Func<Task> act = () => resultTask.WaitAsync(Timeout);
            await act.Should().ThrowAsync<OperationCanceledException>();
        }

        [Fact]
        public async Task WhenAllCancelsRemainingShardsOnFaultAndRethrowsTheFault()
        {
            using var cts = new CancellationTokenSource();
            var sibling = WaitForCancellationAsync(cts.Token);
            var tasks = new List<Task<string?>> { sibling, FailAsync("shard down") };

            Func<Task> act = () => ShardFanOut.WhenAllCancelOnFaultAsync(tasks, cts).WaitAsync(Timeout);

            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("shard down");
            sibling.IsCanceled.Should().BeTrue("the remaining shards are cancelled once one shard fails");
        }

        [Fact]
        public async Task WhenAllCancelOnFaultAggregatesEveryShardFailure()
        {
            using var cts = new CancellationTokenSource();
            var tasks = new List<Task<string?>> { FailAsync("one"), FailAsync("two"), Task.FromResult<string?>("ok") };

            Func<Task> act = () => ShardFanOut.WhenAllCancelOnFaultAsync(tasks, cts).WaitAsync(Timeout);

            var thrown = await act.Should().ThrowAsync<AggregateException>();
            thrown.Which.InnerExceptions.Should().HaveCount(2);
            thrown.Which.InnerExceptions.Select(e => e.Message).Should().BeEquivalentTo("one", "two");
        }

        [Fact]
        public async Task WhenAllCancelOnFaultIgnoresSiblingCancellationCausedByTheFault()
        {
            using var cts = new CancellationTokenSource();
            var sibling = CancelledFaultAfterCancellationAsync(cts.Token);
            var tasks = new List<Task<string?>> { sibling, FailAsync("shard down") };

            Func<Task> act = () => ShardFanOut.WhenAllCancelOnFaultAsync(tasks, cts).WaitAsync(Timeout);

            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("shard down");
        }

        [Fact]
        public async Task WriteAllRethrowsASingleFailureAsIs()
        {
            var tasks = new List<Task> { Task.FromResult("a"), FailAsync("shard down") };

            Func<Task> act = () => ShardFanOut.WhenAllSurfaceAllFailuresAsync(tasks).WaitAsync(Timeout);

            var thrown = await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("shard down");
            thrown.Which.StackTrace.Should().Contain(nameof(FailAsync), "the original stack is preserved");
        }

        [Fact]
        public async Task WriteAllSurfacesEveryShardFailureAsAggregate()
        {
            var tasks = new List<Task> { FailAsync("one"), Task.FromResult("ok"), FailAsync("two") };

            Func<Task> act = () => ShardFanOut.WhenAllSurfaceAllFailuresAsync(tasks).WaitAsync(Timeout);

            var thrown = await act.Should().ThrowAsync<AggregateException>();
            thrown.Which.InnerExceptions.Select(e => e.Message).Should().BeEquivalentTo("one", "two");
        }

        [Fact]
        public async Task WriteAllWaitsForEveryShardAndDoesNotCancelSiblings()
        {
            var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var slow = ResultAfterAsync(gate.Task, "late");
            var tasks = new List<Task> { FailAsync("shard down"), slow };

            var all = ShardFanOut.WhenAllSurfaceAllFailuresAsync(tasks);
            await Task.Delay(50);
            all.IsCompleted.Should().BeFalse("every dispatched write settles before the failure is surfaced");
            gate.SetResult(true);

            Func<Task> act = () => all.WaitAsync(Timeout);
            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("shard down");
            (await slow).Should().Be("late");
        }

        [Fact]
        public async Task WriteAllCompletesWhenNoShardFaults()
        {
            var tasks = new List<Task> { Task.FromResult("a"), Task.CompletedTask };

            await ShardFanOut.WhenAllSurfaceAllFailuresAsync(tasks).WaitAsync(Timeout);
        }

        [Fact]
        public async Task WriteAllSurfacesCallerCancellationWhenNoShardFaults()
        {
            using var caller = new CancellationTokenSource();
            var tasks = new List<Task> { WaitForCancellationAsync(caller.Token), Task.FromResult("ok") };

            var all = ShardFanOut.WhenAllSurfaceAllFailuresAsync(tasks);
            caller.Cancel();

            Func<Task> act = () => all.WaitAsync(Timeout);
            await act.Should().ThrowAsync<OperationCanceledException>();
        }

        private static Task<string?> CancelledFaultAfterCancellationAsync(CancellationToken token)
        {
            // Completes Faulted (not Canceled) with an OperationCanceledException once the token fires.
            var tcs = new TaskCompletionSource<string?>();
            token.Register(() => tcs.TrySetException(new OperationCanceledException("cancelled by sibling fault")));
            return tcs.Task;
        }

        [Fact]
        public async Task WhenAllCompletesWhenNoShardFaults()
        {
            using var cts = new CancellationTokenSource();
            var tasks = new List<Task<string?>> { Task.FromResult<string?>("a"), Task.FromResult<string?>("b") };

            await ShardFanOut.WhenAllCancelOnFaultAsync(tasks, cts).WaitAsync(Timeout);

            cts.IsCancellationRequested.Should().BeFalse();
        }
    }
}
