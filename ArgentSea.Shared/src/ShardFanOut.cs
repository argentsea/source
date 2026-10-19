// © John Hicks. All rights reserved. Licensed under the MIT license.
// See the LICENSE file in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace ArgentSea
{
    /// <summary>
    /// Coordinates the per-shard tasks of a ShardSet fan-out query.
    /// </summary>
    internal static class ShardFanOut
    {
        /// <summary>
        /// Awaits every shard task. When a shard faults, the remaining shards are cancelled through <paramref name="cancelOnFault"/>
        /// (their results would be discarded anyway) and, once every task has settled, the shard failures are surfaced through
        /// <see cref="ThrowFailures"/>: a single failure is rethrown as-is, several are thrown together as an <see cref="AggregateException"/>.
        /// An <see cref="OperationCanceledException"/> fault raised after this method cancelled the siblings is a consequence of that
        /// cancellation, not an independent failure, and is not surfaced.
        /// </summary>
        /// <param name="tasks">The dispatched shard tasks, which must observe the token of <paramref name="cancelOnFault"/>.</param>
        /// <param name="cancelOnFault">The (linked) token source whose token was passed to the shard tasks.</param>
        internal static async Task WhenAllCancelOnFaultAsync(IReadOnlyList<Task> tasks, CancellationTokenSource cancelOnFault)
        {
            var pending = new List<Task>(tasks);
            List<Exception>? failures = null;
            while (pending.Count > 0)
            {
                var tsk = await Task.WhenAny(pending).ConfigureAwait(false);
                pending.Remove(tsk);
                if (tsk.IsFaulted)
                {
                    var ex = tsk.Exception; // reading the exception also marks it observed
                    if (failures is null)
                    {
                        failures = new List<Exception>(ex.InnerExceptions);
                        cancelOnFault.Cancel();
                    }
                    else
                    {
                        foreach (var inner in ex.InnerExceptions)
                        {
                            if (!(inner is OperationCanceledException))
                            {
                                failures.Add(inner);
                            }
                        }
                    }
                }
            }
            if (failures is not null && failures.Count > 0)
            {
                ThrowFailures(failures);
            }
            // No faults: this surfaces caller cancellation (if any shard was cancelled) or simply completes.
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        /// <summary>
        /// Awaits every shard task without cancelling siblings (the write-all fan-out: every shard's write runs to completion or
        /// caller cancellation). Once every task has settled, every shard failure is surfaced through <see cref="ThrowFailures"/>:
        /// a single failure is rethrown as-is, several are thrown together as an <see cref="AggregateException"/>.
        /// With no failures, caller cancellation (if any shard was cancelled) is surfaced, otherwise the method simply completes.
        /// </summary>
        /// <param name="tasks">The dispatched shard tasks.</param>
        internal static async Task WhenAllSurfaceAllFailuresAsync(IReadOnlyList<Task> tasks)
        {
            var all = Task.WhenAll(tasks);
            try
            {
                await all.ConfigureAwait(false);
            }
            catch when (all.IsFaulted)
            {
                // Task.WhenAll records the failures of every faulted task; awaiting it surfaces only the first.
                ThrowFailures(all.Exception.InnerExceptions);
            }
        }

        /// <summary>
        /// Rethrows a single shard failure as-is (its original stack preserved through <see cref="ExceptionDispatchInfo"/>),
        /// or throws several together as an <see cref="AggregateException"/>.
        /// </summary>
        private static void ThrowFailures(IList<Exception> failures)
        {
            if (failures.Count == 1)
            {
                ExceptionDispatchInfo.Capture(failures[0]).Throw();
            }
            throw new AggregateException(failures);
        }

        /// <summary>
        /// Returns the first non-null shard result and cancels the remaining (losing) shards through <paramref name="cancelLosers"/>.
        /// Failures of losing or cancelled shards are ignored once a result is found. If no shard returns a result,
        /// caller cancellation is rethrown, then any shard failure is rethrown; otherwise the default value is returned.
        /// </summary>
        /// <param name="tasks">The dispatched shard tasks, which must observe the token of <paramref name="cancelLosers"/>.</param>
        /// <param name="cancelLosers">The (linked) token source whose token was passed to the shard tasks.</param>
        /// <param name="cancellationToken">The caller’s cancellation token.</param>
        internal static async Task<TModel?> FirstNonNullAsync<TModel>(IReadOnlyList<Task<TModel>> tasks, CancellationTokenSource cancelLosers, CancellationToken cancellationToken)
        {
            var pending = new List<Task<TModel>>(tasks);
            List<Exception>? failures = null;
            try
            {
                while (pending.Count > 0)
                {
                    var tsk = await Task.WhenAny(pending).ConfigureAwait(false);
                    pending.Remove(tsk);
                    if (tsk.IsFaulted)
                    {
                        if (failures is null)
                        {
                            failures = new List<Exception>();
                        }
                        failures.AddRange(tsk.Exception!.InnerExceptions); // a faulted task always carries its exception
                    }
                    else if (!tsk.IsCanceled)
                    {
                        var tskResult = tsk.Result;
                        if (tskResult != null)
                        {
                            return tskResult;
                        }
                    }
                }
            }
            finally
            {
                if (pending.Count > 0)
                {
                    // A result was chosen (or the wait itself failed): cancel the losers and observe their outcome so a
                    // late failure is neither surfaced to the caller nor reported as an unobserved task exception.
                    cancelLosers.Cancel();
                    foreach (var loser in pending)
                    {
                        _ = loser.ContinueWith(t => _ = t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                    }
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (failures is not null)
            {
                ThrowFailures(failures);
            }
            return default;
        }
    }
}
