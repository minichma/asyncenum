//Copyright (C) 2011 by Markus Minichmayr

//Permission is hereby granted, free of charge, to any person obtaining a copy
//of this software and associated documentation files (the "Software"), to deal
//in the Software without restriction, including without limitation the rights
//to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
//copies of the Software, and to permit persons to whom the Software is
//furnished to do so, subject to the following conditions:

//The above copyright notice and this permission notice shall be included in
//all copies or substantial portions of the Software.

//THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
//IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
//FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
//AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
//LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
//OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
//THE SOFTWARE.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Diagnostics;

namespace Minichmayr.Async
{
    /// <summary>
    /// Provides utility and extension methods for handling of <see cref="IAsyncEnumerable{T}"/>.
    /// </summary>
    public static class AsyncEnum
    {
        /// <summary>
        /// A simple implementation of <c>IAsyncEnumerable</c> that delegates creation of the
        /// enumerator to a user-defined function.
        /// </summary>
        private class DelegatingAsyncEnumerable<T> : IAsyncEnumerable<T>
        {
            private readonly Func<IAsyncEnumerator<T>> enumeratorFactory;

            public DelegatingAsyncEnumerable(Func<IAsyncEnumerator<T>> enumeratorFactory)
            {
                if (enumeratorFactory == null)
                    throw new ArgumentNullException("enumeratorFactory");

                this.enumeratorFactory = enumeratorFactory;
            }

            #region IAsyncEnumerable<T> Members

            public IAsyncEnumerator<T> GetEnumerator()
            {
                return this.enumeratorFactory();
            }

            #endregion
        }

        /// <summary>
        /// Executes a foreach operation over the <c>IAsyncEnumerable</c> where the iteration is
        /// performed asynchronously and the <c>body</c> is called synchronously for each item in <c>source</c>.
        /// </summary>
        /// <typeparam name="T">The type of items in the <c>source</c> instance.</typeparam>
        /// <param name="source">An enumerable data source.</param>
        /// <param name="body">The delegate that is invoked once per iteration.</param>
        /// <returns>An instance of <c>Task</c> representing the async state of the execution.</returns>
        public static async Task ForEachAsync<T>(this IAsyncEnumerable<T> source, Action<T> body)
        {
            if (source == null)
                throw new ArgumentNullException("source");

            if (body == null)
                throw new ArgumentNullException("body");

            var enumerator = source.GetEnumerator();
            while (await enumerator.MoveNextAsync())
                body(enumerator.Current);
        }

        /// <summary>
        /// Executes a foreach operation over the <c>IAsyncEnumerable</c> where the <c>body</c> is 
        /// called for each item in <c>source</c>. The iteration and invocation of <c>body</c> is done asynchronously.
        /// </summary>
        /// <typeparam name="T">The type of items in the <c>source</c> instance.</typeparam>
        /// <param name="source">An enumerable data source.</param>
        /// <param name="asyncBody">The delegate that is invoked once per iteration.</param>
        /// <returns>An instance of <c>Task</c> representing the async state of the execution.</returns>
        public static async Task ForEachAsync<T>(this IAsyncEnumerable<T> source, Func<T, Task> asyncBody)
        {
            if (source == null)
                throw new ArgumentNullException("source");

            if (asyncBody == null)
                throw new ArgumentNullException("asyncBody");

            var enumerator = source.GetEnumerator();
            while (await enumerator.MoveNextAsync())
                await asyncBody(enumerator.Current);
        }

        /// <summary>
        /// Creates an <see cref="IAsyncEnumerable{T}"/> instance from this iterator function.
        /// </summary>
        /// <typeparam name="T">The type of the elements in the returned enumerable.</typeparam>
        /// <param name="producer">The asynchronous function used to populate the returned enumerable.</param>
        /// <returns>An instance of <c>IAsyncEnumerable</c> that represents the items yielded by the function provided in <c>producer</c>.</returns>
        public static IAsyncEnumerable<T> Enumerate<T>(Func<IAsyncConsumer<T>, Task> producer)
        {
            if (producer == null)
                throw new ArgumentNullException("producer");

            return new DelegatingAsyncEnumerable<T>(() =>
                {
                    var enumerator = new AsyncEnumerator<T>();
                    enumerator.PopulateAsync(producer);
                    return enumerator;
                });
        }

        private static async void PopulateAsync<T>(this AsyncEnumerator<T> enumerator, Func<IAsyncConsumer<T>, Task> producer)
        {
            var consumer = enumerator.Consumer;
            await consumer.WaitForStartAsync();

            // Instead of awaiting the producer task, we use an explicit continuation here
            // because we want to propagate any outcome, especially exceptions, to the consumer.
            producer(consumer).ContinueWith(consumer.OnComplete);
        }

        /// <summary>
        /// Completes a consumer.
        /// </summary>
        internal static void Complete<T>(this AsyncEnumerator<T>.AsyncConsumer consumer)
        {
            var src = new TaskCompletionSource<int>();
            src.SetResult(1);
            consumer.OnComplete(src.Task);
        }

        /// <summary>
        /// Completes a consumer with an exception.
        /// </summary>
        internal static void Fail<T>(this AsyncEnumerator<T>.AsyncConsumer consumer, Exception exception)
        {
            var src = new TaskCompletionSource<int>();
            src.SetException(exception);
            consumer.OnComplete(src.Task);
        }

        /// <summary>
        /// Creates an <see cref="IEnumerable{T}"/> from this <see cref="IAsyncEnumerable{T}"/> instance.
        /// </summary>
        /// <typeparam name="T">The types of the elements of <c>source</c>.</typeparam>
        /// <param name="source">The <c>IAsyncEnumerable</c> instance to transform.</param>
        /// <returns>An <c>IEnumerable</c> instance, that represents this <c>IAsyncEnumerable</c></returns>
        public static IEnumerable<T> AsEnumerable<T>(this IAsyncEnumerable<T> source)
        {
            if (source == null)
                throw new ArgumentNullException("source");

            var enumerator = source.GetEnumerator();
            while (enumerator.MoveNextAsync().AwaitSynchronously())
                yield return enumerator.Current;
        }

        /// <summary>
        /// Creates an <see cref="IAsyncEnumerable{T}"/> from this <see cref="IEnumerable{T}"/> instance.
        /// </summary>
        /// <typeparam name="T">The types of the elements of <c>source</c>.</typeparam>
        /// <param name="source">The <c>IEnumerable{T}</c> instance to transform.</param>
        /// <returns>An <c>IAsyncEnumerable</c> instance, that represents this <c>IEnumerable</c></returns>
        public static IAsyncEnumerable<T> AsAsyncEnumerable<T>(this IEnumerable<T> source)
        {
            if (source == null)
                throw new ArgumentNullException("source");

            return Enumerate<T>(async consumer =>
            {
                foreach (var item in source)
                    await consumer.YieldAsync(item);
            });
        }

        /// <summary>
        /// Asynchronously creates a <see cref="List{T}"/> from a <see cref="IAsyncEnumerable{T}"/>.
        /// </summary>
        /// <typeparam name="T">The type of the elements in <c>source</c>.</typeparam>
        /// <param name="source">The source to be transformed into a <c>List</c>.</param>
        /// <returns>A <see cref="List{T}"/> that contains the elements from the input sequence.</returns>
        public static async Task<List<T>> ToListAsync<T>(this IAsyncEnumerable<T> source)
        {
            if (source == null)
                throw new ArgumentNullException("source");

            List<T> result = new List<T>();
            await source.ForEachAsync(item =>
            {
                result.Add(item);
            });

            return result;
        }

        /// <summary>
        /// Asynchronously creates an array from a <see cref="IAsyncEnumerable{T}"/>.
        /// </summary>
        /// <typeparam name="T">The type of the elements in <c>source</c>.</typeparam>
        /// <param name="source">The source to be transformed into an array.</param>
        /// <returns>An array that contains the elements from the input sequence.</returns>
        public static async Task<T[]> ToArrayAsync<T>(this IAsyncEnumerable<T> source)
        {
            return (await source.ToListAsync()).ToArray();
        }

        /// <summary>
        /// Synchronously creates a <see cref="List{T}"/> from a <see cref="IAsyncEnumerable{T}"/>.
        /// </summary>
        /// <typeparam name="T">The type of the elements in <c>source</c>.</typeparam>
        /// <param name="source">The source to be transformed into a <c>List</c>.</param>
        /// <returns>A <see cref="List{T}"/> that contains the elements from the input sequence.</returns>
        public static List<T> ToList<T>(this IAsyncEnumerable<T> source)
        {
            return source.ToListAsync().AwaitSynchronously();
        }

        /// <summary>
        /// Synchronously creates an array from a <see cref="IAsyncEnumerable{T}"/>.
        /// </summary>
        /// <typeparam name="T">The type of the elements in <c>source</c>.</typeparam>
        /// <param name="source">The source to be transformed into an array.</param>
        /// <returns>An array that contains the elements from the input sequence.</returns>
        public static T[] ToArray<T>(this IAsyncEnumerable<T> source)
        {
            return source.ToListAsync().AwaitSynchronously().ToArray();
        }

        /// <summary>
        /// Synchronously awaits an task.
        /// </summary>
        /// <param name="task">The task object.</param>
        internal static T AwaitSynchronously<T>(this Task<T> task)
        {
            if (task == null)
                throw new ArgumentNullException("task");

            AwaitSynchronously((Task)task);
            return task.Result;
        }


        /// <summary>
        /// Synchronously awaits an task.
        /// </summary>
        /// <param name="task">The task object.</param>
        public static void AwaitSynchronously(this Task task)
        {
            if (task == null)
                throw new ArgumentNullException("task");

            try
            {
                task.Wait();
            }
            catch (AggregateException)
            {
                // An AggregateException is raised, if the task faults.
                // In such cases we want the original exception to propagate to the caller,
                // what is why we ignore this exception and let GetResult() do the work.
                // TODO: Find a more elegant way to achieve this!
            }

            task.GetAwaiter().GetResult();
        }

        /// <summary>
        /// Projects each element of a sequence to an <see cref="IAsyncEnumerable{T}"/> and flattens the resulting sequences into one sequence.
        /// </summary>
        /// <typeparam name="TSource">The type of the elements of <c>source</c>.</typeparam>
        /// <typeparam name="TResult">The type of the elements of the sequence returned by <c>selector</c>.</typeparam>
        /// <param name="source">A sequence of values to project.</param>
        /// <param name="selector">A transform function to apply to each element.</param>
        /// <returns>An <see cref="IAsyncEnumerable{T}"/> whose elements are the result of invoking the one-to-many transform function on each element of the input sequence.</returns>
        public static IAsyncEnumerable<TResult> SelectMany<TSource, TResult>(
            this IAsyncEnumerable<TSource> source,
            Func<TSource, IAsyncEnumerable<TResult>> selector)
        {
            if (source == null)
                throw new ArgumentNullException("source");

            if (selector == null)
                throw new ArgumentNullException("selector");

            return Enumerate<TResult>(async consumer =>
            {
                await source.ForEachAsync(async item =>
                {
                    var results = selector(item);
                    // TODO: What should we do with null values here?
                    if (results != null)
                    {
                        await results.ForEachAsync(async result =>
                        {
                            await consumer.YieldAsync(result);
                        });
                    }
                });
            });
        }

        /// <summary>
        /// Projects each element of a sequence into a new form.
        /// </summary>
        /// <typeparam name="TSource">The type of the elements of source.</typeparam>
        /// <typeparam name="TResult">The type of the value returned by selector.</typeparam>
        /// <param name="source">A sequence of values to project.</param>
        /// <param name="selector">A transform function to apply to each element.</param>
        /// <returns>An <see cref="IAsyncEnumerable{T}"/> whose elements are the result of invoking the transform function on each element of source.</returns>
        public static IAsyncEnumerable<TResult> Select<TSource, TResult>(
            this IAsyncEnumerable<TSource> source,
            Func<TSource, TResult> selector
        )
        {
            if (source == null)
                throw new ArgumentNullException("source");

            if (selector == null)
                throw new ArgumentNullException("selector");

            return Enumerate<TResult>(async consumer =>
            {
                await source.ForEachAsync(async item =>
                {
                    await consumer.YieldAsync(selector(item));
                });
            });
        }

        /// <summary>
        /// Filters a sequence of values based on a predicate.
        /// </summary>
        /// <typeparam name="TSource">The type of the elements of source.</typeparam>
        /// <param name="source">A sequence of values to project.</param>
        /// <param name="predicate">A function to test each element for a condition.</param>
        /// <returns>An <see cref="IAsyncEnumerable{T}"/> that contains elements from the input sequence that satisfy the condition.</returns>
        public static IAsyncEnumerable<TSource> Where<TSource>(
            this IAsyncEnumerable<TSource> source,
            Func<TSource, bool> predicate
        )
        {
            if (source == null)
                throw new ArgumentNullException("source");

            if (predicate == null)
                throw new ArgumentNullException("predicate");

            return Enumerate<TSource>(async consumer =>
            {
                await source.ForEachAsync(async item =>
                {
                    if (predicate(item))
                        await consumer.YieldAsync(item);
                });
            });
        }

        /// <summary>
        /// Filters a sequence of values based on a predicate.
        /// </summary>
        /// <typeparam name="TSource">The type of the elements of source.</typeparam>
        /// <param name="source">A sequence of values to project.</param>
        /// <param name="asyncPredicate">A function to test each element for a condition.</param>
        /// <returns>An <see cref="IAsyncEnumerable{T}"/> that contains elements from the input sequence that satisfy the condition.</returns>
        public static IAsyncEnumerable<TSource> Where<TSource>(
            this IAsyncEnumerable<TSource> source,
            Func<TSource, Task<bool>> asyncPredicate
        )
        {
            if (source == null)
                throw new ArgumentNullException("source");

            if (asyncPredicate == null)
                throw new ArgumentNullException("asyncPredicate");

            return Enumerate<TSource>(async consumer =>
            {
                await source.ForEachAsync(async item =>
                {
                    if (await asyncPredicate(item))
                        await consumer.YieldAsync(item);
                });
            });
        }

        /// <summary>
        /// Concatenates a numer of <see cref="IAsyncEnumerable{T}"/>s.
        /// </summary>
        /// <typeparam name="T">The type of the elements.</typeparam>
        /// <param name="first">The first sequence.</param>
        /// <param name="second">The other sequence.</param>
        /// <returns>A sequence that contains all items from <paramref name="first"/> and <paramref name="second"/>.</returns>
        public static IAsyncEnumerable<T> Concat<T>(this IAsyncEnumerable<T> first, IAsyncEnumerable<T> second)
        {
            if (first == null)
                throw new ArgumentNullException("first");
            if (second == null)
                throw new ArgumentNullException("second");

            return Enumerate<T>(async consumer =>
            {
                await first.FeedAsync(consumer);
                await second.FeedAsync(consumer);
            });
        }

        private static async Task FeedAsync<T>(this IAsyncEnumerable<T> source, IAsyncConsumer<T> consumer)
        {
            if (source == null)
                throw new ArgumentNullException("source");

            if (consumer == null)
                throw new ArgumentNullException("consumer");

            await source.ForEachAsync(item => consumer.YieldAsync(item));
        }

        internal static IAsyncEnumerable<T> AsCachedEnumerable<T>(this IAsyncEnumerator<T> source)
        {
            if (source == null)
                throw new ArgumentNullException("source");

            return new CachingAsyncEnumerable<T>(source);
        }

        /// <summary>
        /// Returns a <see cref="IAsyncEnumerable{T}"/> that wraps the source sequence but will
        /// iterate over the source only once even if enumerated multiple times.
        /// </summary>
        /// <typeparam name="T">The type of items in source.</typeparam>
        /// <param name="source">The source sequence.</param>
        /// <returns>A caching wraper around the source sequence.</returns>
        /// <remarks>
        /// <para>
        /// The returned wrapper iterates over the wrapped enumerator as requested by the
        /// <see cref="IAsyncEnumerator{T}"/> instances it produces. If multiple produced enumerators
        /// request the same item, only the first request will cause the according item to be
        /// retrieved from the inner enumerator. The returned item is cached and all subsequent requests will be
        /// answered from the cache. The items from the source sequence are retrieved lazily.
        /// </para><para>
        /// If the wrapped enumerator raises an exception in a MoveNext task, the exception is
        /// propagated to the produced enumerators at exact the same position in the returned sequence
        /// as observed in the wrapped sequence.
        /// </para>
        /// </remarks>
        internal static IAsyncEnumerable<T> WithCache<T>(this IAsyncEnumerable<T> source)
        {
            if (source == null)
                throw new ArgumentNullException("source");

            return source.GetEnumerator().AsCachedEnumerable();
        }

        /// <summary>
        /// Returns a <see cref="IAsyncEnumerable{T}"/> that represents the items from the source sequence
        /// but doesn't await consumer's demand for the next item before retrieveing items from <c>source</c>.
        /// </summary>
        /// <typeparam name="T">The type of items in <c>source</c>.</typeparam>
        /// <param name="source">The source sequence.</param>
        /// <returns>A representation of source that doesn't wait for demand when iterating over the elements in <paramref name="source"/>.</returns>
        public static IAsyncEnumerable<T> WithNoWait<T>(this IAsyncEnumerable<T> source)
        {
            if (source == null)
                throw new ArgumentNullException("source");

            return Enumerate<T>(async consumer =>
            {
                await source.ForEachAsync(item =>
                {
                    // Don't await here
                    consumer.YieldAsync(item);
                });
            });
        }

        /// <summary>
        /// Groups the elements of a sequence according to a specified key selector function.
        /// </summary>
        /// <typeparam name="TSource">The type of the elements of <c>source</c>.</typeparam>
        /// <typeparam name="TKey">The type of the key returned by <c>keySelector</c>.</typeparam>
        /// <param name="source">An IEnumerable{T} whose elements to group.</param>
        /// <param name="keySelector">A function to extract the key for each element.</param>
        /// <returns>An IAsyncEnumerable{IAsyncGrouping{Key, TSource}} where each IAsyncGrouping object contains a sequence of objects and a key.</returns>
        public static IAsyncEnumerable<IAsyncGrouping<TKey, TSource>> GroupBy<TSource, TKey>(
            this IAsyncEnumerable<TSource> source,
            Func<TSource, TKey> keySelector
        )
        {
            return source.GroupBy(
                keySelector,
                null);
        }

        /// <summary>
        /// Groups the elements of a sequence according to a specified key selector function.
        /// </summary>
        /// <typeparam name="TSource">The type of the elements of <c>source</c>.</typeparam>
        /// <typeparam name="TKey">The type of the key returned by <c>keySelector</c>.</typeparam>
        /// <param name="source">An IEnumerable{T} whose elements to group.</param>
        /// <param name="keySelector">A function to extract the key for each element.</param>
        /// <param name="comparer">An IEqualityComparer{T} to compare keys with.</param>
        /// <returns>An IAsyncEnumerable{IAsyncGrouping{Key, TSource}} where each IAsyncGrouping object contains a sequence of objects and a key.</returns>
        public static IAsyncEnumerable<IAsyncGrouping<TKey, TSource>> GroupBy<TSource, TKey>(
            this IAsyncEnumerable<TSource> source,
            Func<TSource, TKey> keySelector,
            IEqualityComparer<TKey> comparer
        )
        {
            return source.GroupBy(
                keySelector,
                x => x,
                (key, group) => new AsyncGrouping<TKey, TSource>(key, group),
                comparer);
        }

        /// <summary>
        /// Groups the elements of a sequence according to a specified key selector function.
        /// </summary>
        /// <typeparam name="TSource">The type of the elements of <c>source</c>.</typeparam>
        /// <typeparam name="TKey">The type of the key returned by <c>keySelector</c>.</typeparam>
        /// <typeparam name="TElement"></typeparam>
        /// <typeparam name="TResult"></typeparam>
        /// <param name="source">An IEnumerable{T} whose elements to group.</param>
        /// <param name="keySelector">A function to extract the key for each element.</param>
        /// <param name="elementSelector">A function to create an intermediate item for each element.</param>
        /// <param name="resultSelector">A function that creates an element in the returned sequence from a key and an intermediate item.</param>
        /// <param name="comparer">An IEqualityComparer{T} to compare keys with.</param>
        /// <returns>An IAsyncEnumerable{TResult} where each item represents a group of source items and an according key.</returns>
        public static IAsyncEnumerable<TResult> GroupBy<TSource, TKey, TElement, TResult>(
            this IAsyncEnumerable<TSource> source,
            Func<TSource, TKey> keySelector,
            Func<TSource, TElement> elementSelector,
            Func<TKey, IAsyncEnumerable<TElement>, TResult> resultSelector,
            IEqualityComparer<TKey> comparer
        )
        {
            if (source == null)
                throw new ArgumentNullException("source");

            if (keySelector == null)
                throw new ArgumentNullException("keySelector");

            if (elementSelector == null)
                throw new ArgumentNullException("elementSelector");

            if (resultSelector == null)
                throw new ArgumentNullException("resultSelector");

            return Enumerate<TResult>(async consumer =>
            {
                // consumers of inner grouping enumerators by key
                var innerConsumersByKeys = new Dictionary<TKey, AsyncEnumerator<TElement>.AsyncConsumer>(comparer);

                // The following dictionaries hold tasks for awaiting demand in inner enumerators and according keys
                // keys by task
                var keysByWaitingForInnerMoveNextTask = new Dictionary<Task, TKey>();
                // tasks by key
                var waitingForInnerMoveNextTasksByKey = new Dictionary<TKey, Task>(comparer);

                // Waiter used to await demand for either the outer enumerator or inner grouping enumerators
                var waiter = new MultiWaiter<Task>();

                // Keys that currently exists demanded for. These keys are not currently awaited by waiter
                var pendingKeys = new HashSet<TKey>(comparer);

                // Task for awaiting demand for the next grouping (outer enumerator)
                Task waitingOuterMoveNextTask = null;

                try
                {
                    var sourceEnumerator = source.GetEnumerator();
                    bool completed = false;
                    do
                    {
                        do
                        {
                            completed = !await sourceEnumerator.MoveNextAsync();
                            if (completed)
                                break;

                            var sourceItem = sourceEnumerator.Current;
                            var intermediateItem = elementSelector(sourceItem);
                            var key = keySelector(sourceItem);

                            AsyncEnumerator<TElement>.AsyncConsumer innerConsumer;
                            if (!innerConsumersByKeys.TryGetValue(key, out innerConsumer))
                            {
                                // So we have a new key and therefore a new grouping
                                var groupEnumerator = new AsyncEnumerator<TElement>();
                                innerConsumer = groupEnumerator.Consumer;
                                innerConsumersByKeys.Add(key, innerConsumer);

                                if (waitingOuterMoveNextTask != null)
                                {
                                    // If we are currently waiting for demand of the outer enumerator, we remove the task from the waiter
                                    // as we will get a new waiter shortly.
                                    waiter.Remove(waitingOuterMoveNextTask);
                                }
                                waitingOuterMoveNextTask = consumer.YieldAsync(resultSelector(key, groupEnumerator.AsCachedEnumerable()));
                                if (waitingOuterMoveNextTask.IsCompleted)
                                    waitingOuterMoveNextTask = null;
                                else
                                {
                                    waiter.Add(waitingOuterMoveNextTask);
                                    // It's a new key, so we definitely have demand for it. However, we will satisfy
                                    // the demand inside this iteration.
                                    pendingKeys.Add(key);
                                }
                            }
                            else
                            {
                                // So we created the grouping already.
                                Task oldTask;
                                if (waitingForInnerMoveNextTasksByKey.TryGetValue(key, out oldTask))
                                {
                                    // remove this group from pending waiters because we will get a new waiter task shortly
                                    waitingForInnerMoveNextTasksByKey.Remove(key);
                                    keysByWaitingForInnerMoveNextTask.Remove(oldTask);
                                    waiter.Remove(oldTask);
                                }
                                else
                                {
                                    // The only reason why we wouldn't be waiting for demand is, that
                                    // the group is already pending.
                                    Debug.Assert(pendingKeys.Contains(key));
                                }
                            }

                            var newTask = innerConsumer.YieldAsync(intermediateItem);

                            if (newTask.IsCompleted)
                            {
                                // If the task was completed synchronously, we just need to add the key
                                // to the set of pending keys but don't need to remember the completed task.
                                pendingKeys.Add(key);
                            }
                            else
                            {
                                waitingForInnerMoveNextTasksByKey.Add(key, newTask);
                                keysByWaitingForInnerMoveNextTask.Add(newTask, key);
                                waiter.Add(newTask);
                                pendingKeys.Remove(key);
                            }
                        } while ((pendingKeys.Count > 0) || (waitingOuterMoveNextTask == null));

                        if (completed)
                            break;

                        // We use a MultiWaiter to await demand instead of using TaskEx.WhenAny because
                        // of performance issues ( O(N log(N)) instead of O(N^2) )
                        Task nextTask = await waiter.WaitNextAsync();

                        if (nextTask == waitingOuterMoveNextTask)
                            waitingOuterMoveNextTask = null;
                        else
                            pendingKeys.Add(keysByWaitingForInnerMoveNextTask[nextTask]);
                    } while (!completed);

                }
                catch (Exception e)
                {
                    foreach (var innerConsumer in innerConsumersByKeys.Values)
                        innerConsumer.Fail(e);

                    throw;
                }
                finally
                {
                    waiter.Dispose();
                }

                foreach (var innerConsumer in innerConsumersByKeys.Values)
                    innerConsumer.Complete();
            });
        }

        internal static IAsyncEnumerable<T> Empty<T>()
        {
            return new DelegatingAsyncEnumerable<T>(() =>
            {
                var enumerator = new AsyncEnumerator<T>();
                enumerator.Consumer.Complete();
                return enumerator;
            });
        }

        /// <summary>
        /// Creates a <see cref="IAsyncLookup{TKey, TElement}"/> from an <see cref="IAsyncEnumerable{T}"/> according to a specified key selector function.
        /// </summary>
        /// <typeparam name="TKey">The type of the keys.</typeparam>
        /// <typeparam name="TElement">The type of the elements in <paramref name="source"/>.</typeparam>
        /// <param name="source">The asynchronous source sequence.</param>
        /// <param name="keySelector">The function to extract a key from each element.</param>
        /// <returns>An <see cref="IAsyncLookup{TKey, TElement}"/> that allows to look up the items in <paramref name="source"/> by an associated key.</returns>
        /// <remarks>
        /// <para>
        /// The returned object looks up the items in <paramref name="source"/> lazily. I.e. the source sequence is iterated just in time and only as far as required
        /// to return the requested data.
        /// </para><para>
        /// If the returned object's <see cref="IAsyncLookup{TKey, TElement}.GetAsync(TKey)"/> method is used to look up a key that doesn't exist (i.e. the task returned
        /// by <see cref="IAsyncLookup{TKey, TElement}.ContainsAsync(TKey)"/> evaluates to <c>false</c>) an empty sequence will be returned.
        /// </para>
        /// </remarks>
        public static IAsyncLookup<TKey, TElement> ToAsyncLookup<TKey, TElement>(this IAsyncEnumerable<TElement> source, Func<TElement, TKey> keySelector)
        {
            return source.ToAsyncLookup(keySelector, EqualityComparer<TKey>.Default);
        }

        /// <summary>
        /// Creates a <see cref="IAsyncLookup{TKey, TElement}"/> from an <see cref="IAsyncEnumerable{T}"/> according to a specified key selector function.
        /// </summary>
        /// <typeparam name="TKey">The type of the keys.</typeparam>
        /// <typeparam name="TElement">The type of the elements in <paramref name="source"/>.</typeparam>
        /// <param name="source">The asynchronous source sequence.</param>
        /// <param name="keySelector">The function to extract a key from each element.</param>
        /// <param name="comparer">The <see cref="IEqualityComparer{T}"/> to compare the equality of individual keys.</param>
        /// <returns>An <see cref="IAsyncLookup{TKey, TElement}"/> that allows to look up the items in <paramref name="source"/> by an associated key.</returns>
        /// <remarks>
        /// <para>The returned object looks up the items in <paramref name="source"/> lazily. I.e. the source sequence is iterated just in time and only as far as required
        /// to return the requested data.
        /// </para><para>
        /// If the returned object's <see cref="IAsyncLookup{TKey, TElement}.GetAsync(TKey)"/> method is used to look up a key that doesn't exist (i.e. the task returned
        /// by <see cref="IAsyncLookup{TKey, TElement}.ContainsAsync(TKey)"/> evaluates to <c>false</c>) an empty sequence will be returned.
        /// </para>
        /// </remarks>
        public static IAsyncLookup<TKey, TElement> ToAsyncLookup<TKey, TElement>(this IAsyncEnumerable<TElement> source, Func<TElement, TKey> keySelector, IEqualityComparer<TKey> comparer)
        {
            return AsyncLookup<TKey, TElement>.ToLookup(source, keySelector, comparer);
        }

        /// <summary>
        /// Correlates the elements of two asynchronous sequences based on equality of keys and groups the results.
        /// </summary>
        /// <typeparam name="TOuter">The type of the elements of the first sequence.</typeparam>
        /// <typeparam name="TInner">The type of the elements of the second sequence.</typeparam>
        /// <typeparam name="TKey">The type of the keys returned by the key selector functions.</typeparam>
        /// <typeparam name="TResult">The type of the result elements.</typeparam>
        /// <param name="outer">The first sequence to join.</param>
        /// <param name="inner">The second sequence to join.</param>
        /// <param name="outerKeySelector">A function to extract the join key from each element of the first sequence.</param>
        /// <param name="innerKeySelector">A function to extract the join key from each element of the second sequence.</param>
        /// <param name="resultSelector">A function to create a result element from an element from the first sequence and a collection of matching elements from the second sequence.</param>
        /// <param name="comparer">The <see cref="IEqualityComparer{T}"/> to compare individual keys for equality.</param>
        /// <returns>An <see cref="IAsyncEnumerable{T}"/> that contains elements of type TResult that are obtained by performing a grouped join on two sequences.</returns>
        public static IAsyncEnumerable<TResult> GroupJoin<TOuter, TInner, TKey, TResult>(
            this IAsyncEnumerable<TOuter> outer,
            IAsyncEnumerable<TInner> inner,
            Func<TOuter, TKey> outerKeySelector,
            Func<TInner, TKey> innerKeySelector,
            Func<TOuter, IAsyncEnumerable<TInner>, TResult> resultSelector,
            IEqualityComparer<TKey> comparer)
        {
            if (outer == null)
                throw new ArgumentNullException("outer");
            if (inner == null)
                throw new ArgumentNullException("inner");
            if (outerKeySelector == null)
                throw new ArgumentNullException("outerKeySelector");
            if (innerKeySelector == null)
                throw new ArgumentNullException("innerKeySelector");
            if (resultSelector == null)
                throw new ArgumentNullException("resultSelector");

            return Enumerate<TResult>(async consumer =>
            {
                var innerLookup = inner.ToAsyncLookup(innerKeySelector, comparer);

                await outer.ForEachAsync(async firstItem =>
                {
                    var key = outerKeySelector(firstItem);

                    // wont return null
                    var innerGroup = await innerLookup.GetAsync(key);
                    Debug.Assert(innerGroup != null);
                    await consumer.YieldAsync(resultSelector(firstItem, innerGroup));
                });
            });
        }

        /// <summary>
        /// Correlates the elements of two asynchronous sequences based on equality of keys.
        /// </summary>
        /// <typeparam name="TOuter">The type of the elements of the first sequence.</typeparam>
        /// <typeparam name="TInner">The type of the elements of the second sequence.</typeparam>
        /// <typeparam name="TKey">The type of the keys returned by the key selector functions.</typeparam>
        /// <typeparam name="TResult">The type of the result elements.</typeparam>
        /// <param name="outer">The first sequence to join.</param>
        /// <param name="inner">The second sequence to join.</param>
        /// <param name="outerKeySelector">A function to extract the join key from each element of the first sequence.</param>
        /// <param name="innerKeySelector">A function to extract the join key from each element of the second sequence.</param>
        /// <param name="resultSelector">A function to create a result element from an element from the first sequence and a collection of matching elements from the second sequence.</param>
        /// <param name="comparer">The <see cref="IEqualityComparer{T}"/> to compare individual keys for equality.</param>
        /// <returns>An <see cref="IAsyncEnumerable{T}"/> that contains elements of type TResult that are obtained by performing a grouped join on two sequences.</returns>
        public static IAsyncEnumerable<TResult> Join<TOuter, TInner, TKey, TResult>(
            this IAsyncEnumerable<TOuter> outer,
            IAsyncEnumerable<TInner> inner,
            Func<TOuter, TKey> outerKeySelector,
            Func<TInner, TKey> innerKeySelector,
            Func<TOuter, TInner, TResult> resultSelector,
            IEqualityComparer<TKey> comparer)
        {
            if (resultSelector == null)
                throw new ArgumentNullException("resultSelector");

            return outer
                .GroupJoin(inner, outerKeySelector, innerKeySelector, Tuple.Create, comparer)
                .SelectMany(group => group.Item2.Select(innerItem => resultSelector(group.Item1, innerItem)));
        }

        /// <summary>
        /// Produces the set union of two sequences by using the default equality comparer.
        /// </summary>
        /// <typeparam name="T">The type of elements.</typeparam>
        /// <param name="first">The first sequence.</param>
        /// <param name="second">The second sequence.</param>
        /// <returns>
        /// An <see cref="IAsyncEnumerable{T}"/> that contains all elements from <paramref name="first"/> and
        /// <paramref name="second"/> but returns each item only once.
        /// </returns>
        public static IAsyncEnumerable<T> Union<T>(this IAsyncEnumerable<T> first, IAsyncEnumerable<T> second)
        {
            return first.Union(second, null);
        }

        /// <summary>
        /// Produces the set union of two sequences by using the specified equality comparer.
        /// </summary>
        /// <typeparam name="T">The type of elements.</typeparam>
        /// <param name="first">The first sequence.</param>
        /// <param name="second">The second sequence.</param>
        /// <param name="comparer">The <see cref="IEqualityComparer{T}"/> to compare individual keys for equality.</param>
        /// <returns>
        /// An <see cref="IAsyncEnumerable{T}"/> that contains all elements from <paramref name="first"/> and
        /// <paramref name="second"/> but returns each item only once.
        /// </returns>
        public static IAsyncEnumerable<T> Union<T>(this IAsyncEnumerable<T> first, IAsyncEnumerable<T> second, IEqualityComparer<T> comparer)
        {
            if (first == null)
                throw new ArgumentNullException("first");
            if (second == null)
                throw new ArgumentNullException("second");

            return first.Concat(second).Distinct(comparer);
        }

        /// <summary>
        /// Produces an asynchronous sequence that contains each element from <paramref name="source"/> only once.
        /// </summary>
        /// <typeparam name="T">The type of elements in <paramref name="source"/>.</typeparam>
        /// <param name="source">The source sequence.</param>
        /// <param name="comparer">The <see cref="IEqualityComparer{T}"/> to compare individual keys for equality.</param>
        /// <returns>
        /// An <see cref="IAsyncEnumerable{T}"/> that contains each element from <paramref name="source"/> only once.
        /// </returns>
        public static IAsyncEnumerable<T> Distinct<T>(this IAsyncEnumerable<T> source, IEqualityComparer<T> comparer)
        {
            if (source == null)
                throw new ArgumentNullException("source");

            return Enumerate<T>(async consumer =>
            {
                var foundItems = new HashSet<T>(comparer);
                await source.ForEachAsync(async item =>
                {
                    if (foundItems.Add(item))
                        await consumer.YieldAsync(item);
                });
            });
        }

        /// <summary>
        /// Produces the set intersection between two asynchronous sequences.
        /// </summary>
        /// <typeparam name="T">The type of elements in <paramref name="first"/> and <paramref name="second"/>.</typeparam>
        /// <param name="first">The first sequence.</param>
        /// <param name="second">The second sequence.</param>
        /// <param name="comparer">The comparer used to compare the items from <paramref name="first"/> and <paramref name="second"/> for equality.</param>
        /// <returns>An asynchronous sequence that contains all items from <paramref name="first"/> that are also contained in <paramref name="second"/>.</returns>
        public static IAsyncEnumerable<T> Intersect<T>(this IAsyncEnumerable<T> first, IAsyncEnumerable<T> second, IEqualityComparer<T> comparer)
        {
            if (first == null)
                throw new ArgumentNullException("first");
            if (second == null)
                throw new ArgumentNullException("second");

            return Enumerate<T>(async consumer =>
            {
                var secondLookup = second.ToAsyncLookup(x => x, comparer);
                await first.Distinct(comparer).Where(item => secondLookup.ContainsAsync(item)).FeedAsync(consumer);
            });
        }


        /// <summary>
        /// Counts the number of elements in a given sequence that match the given predicate.
        /// </summary>
        /// <typeparam name="T">The type of elements in <paramref name="source"/>.</typeparam>
        /// <param name="source">The source sequence.</param>
        /// <param name="predicate">The predicate.</param>
        /// <returns>The number of items in <paramref name="source"/> that match the predicate in <paramref name="predicate"/>.</returns>
        public static Task<int> CountAsync<T>(this IAsyncEnumerable<T> source, Func<T, bool> predicate)
        {
            return source.Where(predicate).CountAsync();
        }

        /// <summary>
        /// Counts the number of elements in a given sequence.
        /// </summary>
        /// <typeparam name="T">The type of elements in <paramref name="source"/>.</typeparam>
        /// <param name="source">The source sequence.</param>
        /// <returns>The number of items in <paramref name="source"/>.</returns>
        public async static Task<int> CountAsync<T>(this IAsyncEnumerable<T> source)
        {
            if (source == null)
                throw new ArgumentNullException("source");

            int cnt = 0;
            await source.ForEachAsync(item =>
            {
                checked { cnt++; };
            });

            return cnt;
        }
    }
}
