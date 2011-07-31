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
using System.Diagnostics;

namespace Minichmayr.Async
{
    internal class AsyncEnumerator<T> : IAsyncEnumerator<T>
    {
        private enum AsyncEnumeratorState
        {
            /// <summary>
            /// The enumerator is positioned before the first element.
            /// </summary>
            New,
            /// <summary>
            /// The enumerator is waiting for <see cref="MoveNextAsync"/> to be called.
            /// </summary>
            WaitingForMoveNext,
            /// <summary>
            /// The enumerator is waiting for <see cref="YieldAsync"/> to be called.
            /// </summary>
            WaitingForYieldOrCompletion,
            /// The enumerator is positioned after the last element or 
            /// has failed. No more calls to <see cref="YieldAsync"/> are permitted.
            Completed,
        }

        /// <summary>
        /// Simple implementation of <see cref="IAsyncConsumer{T}"/>. We use this
        /// class as a wrapper around the parent instance so we don't have to expose the
        /// parent directly.
        /// </summary>
        internal class AsyncConsumer : IAsyncConsumer<T>
        {
            private readonly AsyncEnumerator<T> parent;

            public AsyncConsumer(AsyncEnumerator<T> parent)
            {
                this.parent = parent;
            }

            /// <summary>
            /// Asynchronously waits for the first item to be requested by the consumer.
            /// </summary>
            /// <returns>A <see cref="Task"/> object allowing to await demand for the first item.</returns>
            public async Task WaitForStartAsync()
            {
                await this.parent.WaitForStartAsync();
            }

            #region IAsyncConsumer<T> Members

            /// <summary>
            /// 
            /// </summary>
            /// <param name="item"></param>
            /// <returns></returns>
            public async Task YieldAsync(T item)
            {
                await this.parent.YieldAsync(item);
            }

            #endregion

            internal void OnComplete(Task completionTask)
            {
                if (completionTask == null)
                    throw new ArgumentNullException("completionTask");

                if (!completionTask.IsCompleted)
                    throw new ArgumentException("The provided completionTask is not completed yet.");

                this.parent.Complete(completionTask);
            }
        }

        /// <summary>
        /// Queue that allows to enqueue into two queues at once but dequeue independently.
        /// </summary>
        private class DualQueue<T1, T2>
        {
            private readonly Queue<T1> queue1 = new Queue<T1>();
            private readonly Queue<T2> queue2 = new Queue<T2>();

            public void Enqueue(T1 v1, T2 v2)
            {
                this.queue1.Enqueue(v1);
                this.queue2.Enqueue(v2);
            }

            public Queue<T1> Queue1 { get { return this.queue1; } }
            public Queue<T2> Queue2 { get { return this.queue2; } }
        }

        
        /// <summary>
        /// synchronizes access to inner resources
        /// </summary>
        private readonly object syncObj = new object();

        /// <summary>
        /// Consumer instance that may be used to populate this instance.
        /// </summary>
        /// <remarks>
        /// Instead of implementing the <c>IAsyncConsumer</c> interface directly in the AsyncEnumerator class,
        /// a separate consumer instance is provided to the iterator function. This is done to prevent the
        /// iterator function from casting and tampering with the instance.
        /// </remarks>
        private readonly AsyncConsumer consumer;

        /// <summary>
        /// Consumer instance that may be used to populate this instance.
        /// </summary>
        public AsyncConsumer Consumer { get { return this.consumer; } }

        /// <summary>
        /// Items are used to propagate calls to <see cref="MoveNextAsync"/> to the producer
        /// function which is awaiting the task returned by <see cref="YieldAsync"/>.
        /// The first queue holds the <c>TaskCompletionSource</c>s that are signalled by the
        /// <see cref="MoveNextAsync"/> method and the second queue holds the <c>Task</c>s
        /// that are returned by <see cref="WaitForNextMoveNextAsync"/> resp. by <see cref="YieldAsync"/>
        /// and used to await the consumer's demand for the next item.
        /// </summary>
        private readonly DualQueue<TaskCompletionSource<int>, Task> waitForMoveNextQueue 
            = new DualQueue<TaskCompletionSource<int>, Task>();

        /// <summary>
        /// Can be used to await the very first call to <see cref="MoveNextAsync"/>.
        /// </summary>
        private readonly Task waitForMoveToFirstTask;


        /// <summary>
        /// The items enqueued by <see cref="YieldAsync"/>.
        /// </summary>
        private readonly Queue<T> items = new Queue<T>();

        /// <summary>
        /// The <c>TaskCompletionSource</c> that can be used to await the next yielded item or the
        /// enumerator's completion.
        /// </summary>
        /// <remarks>
        /// This field is only set if <see cref="MoveNextAsync"/> is called while neither an item
        /// is currently available in <see cref="items"/> nor the iterator function has completed
        /// (in which case <see cref="completionTask"/> would be set).
        /// </remarks>
        private TaskCompletionSource<int> nextYieldItemCompletionSource = null;

        /// <summary>
        /// The <c>Task</c> representing the completed iterator function. This field is only set
        /// once the iterator function has completed, therefore <see cref="Task.IsCompleted"/>
        /// will return <c>true</c>. If the iterator function failed or was cancelled, the instance
        /// will hold the according exception.
        /// </summary>
        private Task completionTask = null;

        /// <summary>
        /// The current value. Only meaningful when the instance is in <see cref="state"/>
        /// <see cref="AsyncEnumeratorState.WaitingForMoveNext"/>.
        /// </summary>
        private T current;


        /// <summary>
        /// The current state.
        /// </summary>
        private AsyncEnumeratorState state = AsyncEnumeratorState.New;



        public AsyncEnumerator()
        {
            this.consumer = new AsyncConsumer(this);

            // This call is very important because it dequeues one Task from the
            // waitForMoveNextQueue while leaving the corresponding TaskCompletionSource there.
            // This asymmetry ensures that the Task objects returned by YieldAsync correspond
            // to the *next* call to MoveNextAsync rather thant to the one corresponding to the
            // currently yielded item.
            this.waitForMoveToFirstTask = this.WaitForNextMoveNextAsync();
        }

        #region IAsyncEnumerable<T> members

        public async Task<bool> MoveNextAsync()
        {
            Task nextItemWaitTask;
            TaskCompletionSource<int> waitForMoveNextCompletionSource;

            lock (this.syncObj)
            {
                this.VerifyState(AsyncEnumeratorState.New, AsyncEnumeratorState.WaitingForMoveNext, AsyncEnumeratorState.Completed);

                switch (this.state)
                {
                    case AsyncEnumeratorState.New:
                        // Called for the first time.
                        this.state = AsyncEnumeratorState.WaitingForMoveNext;
                        break;
                    case AsyncEnumeratorState.Completed:
                        // Called after enumeration has already completed.
                        // Don't need to signal consumer because they may not yield nor wait
                        // in this state.
                        // In case iteration faulted, we only return the exception on the first
                        // call to MoveNextAsync. As this is a subsequent call, we are allowed
                        // to silently return false.
                        return false;
                }

                // Might happen if consumer consumes faster than producer produces
                if (this.waitForMoveNextQueue.Queue1.Count == 0)
                    this.EnqueueWaitForMoveNextTask();

                // We get our TaskCompletionSource to signal the consumer already here but don't
                // signal it yet because it might otherwise yield control to the consumer.
                waitForMoveNextCompletionSource = this.waitForMoveNextQueue.Queue1.Dequeue();

                // Producer is faster
                if (this.items.Count > 0)
                {
                    this.current = this.items.Dequeue();

                    // Signal consumer that we consumed this item. Most likely, this 
                    // Might switch to iterator
                    nextItemWaitTask = null;
                }
                else
                {

                    // We are not currently waiting, so no task completion source may be present
                    Debug.Assert(this.nextYieldItemCompletionSource == null);

                    if (this.completionTask != null)
                    {
                        // No pending items and iteration has completed
                        // --> we are completed. Just will await the completion task to yield any
                        // results (i.e. exceptions)
                        nextItemWaitTask = this.completionTask;
                        this.state = AsyncEnumeratorState.Completed;
                    }
                    else
                    {
                        // So we have to wait for either the next item (yield) or for completion.
                        this.nextYieldItemCompletionSource = new TaskCompletionSource<int>();
                        nextItemWaitTask = this.nextYieldItemCompletionSource.Task;
                        this.state = AsyncEnumeratorState.WaitingForYieldOrCompletion;
                    }
                    // We don't need to remember the previous item as access isn't allowed in the
                    // current state
                    this.current = default(T);
                }
            }

            // Might switch to iterator
            // This mustn't be called inside a lock context to avoid deadlocks that might be
            // caused by side effects of the iterator.
            waitForMoveNextCompletionSource.SetResult(1);

            if (nextItemWaitTask == null)
                return true;

            // await the next item or the completion task. If this is the completion task, it's
            // probably (must be) completed already any may or may not be in failed state.
            await nextItemWaitTask;

            lock (this.syncObj)
            {
                if (this.state == AsyncEnumeratorState.Completed)
                {
                    // If we have completed in failed state, we won't reach this point as the previous
                    // await threw an exception. So we will reach this point only if we completed
                    // successfully
                    return false;
                }

                this.VerifyState(AsyncEnumeratorState.WaitingForYieldOrCompletion);

                // Items arrived?
                if (this.items.Count > 0)
                {
                    this.current = this.items.Dequeue();
                    this.state = AsyncEnumeratorState.WaitingForMoveNext;
                    return true;
                }

                // Somebody signalled us that an item arrived or we are completed. As no item
                // arived, we must be completed.
                Debug.Assert(this.completionTask != null);

                // completionTask may only be set once it is completed. We only use it to propagate
                // exceptions.
                Debug.Assert(this.completionTask.IsCompleted);
                nextItemWaitTask = this.completionTask;
                this.state = AsyncEnumeratorState.Completed;
            }

            await nextItemWaitTask;
            return false;
        }

        public T Current
        {
            get
            {
                lock (this.syncObj)
                {
                    this.VerifyState(AsyncEnumeratorState.WaitingForMoveNext);
                    return this.current;
                }
            }
        }

#endregion

        private void VerifyState(params AsyncEnumeratorState[] allowedStates)
        {
            if (!allowedStates.Contains(this.state))
                throw new InvalidOperationException();
        }

        private void EnqueueWaitForMoveNextTask()
        {
            var completionSource = new TaskCompletionSource<int>();
            this.waitForMoveNextQueue.Enqueue(completionSource, completionSource.Task);
        }

        private void Complete(Task completedTask)
        {
            Action completePendingYieldAction;
            // May be called from outside (i.e. the consumer) and therefore needs locking
            lock (this.syncObj)
            {
                this.completionTask = completedTask;

                // Might switch to MoveNextAsync
                completePendingYieldAction = this.CompletePendingYieldItemTask();
            }
            if (completePendingYieldAction != null)
                completePendingYieldAction();
        }

        /// <summary>
        /// Checks whether <see cref="MoveNextAsync"/> is waiting to continue. If so, it signals
        /// the continue.
        /// <para>
        /// Caution: This method might yield control over to <see cref="MoveNextAsync"/>, probably
        /// on the current thread, so be careful with synchronization!
        /// </para>
        /// </summary>
        /// <returns>A <see cref="Action"/> object that must be run by the caller outside of any
        /// lock context to avoid deadlock issues in case control is yielded to MoveNextAsync.
        /// Might also return null which should be ignored.</returns>
        private Action CompletePendingYieldItemTask()
        {
            var yieldItemCompletionSource = this.nextYieldItemCompletionSource;
            if (yieldItemCompletionSource == null)
                return null;

            this.nextYieldItemCompletionSource = null;

            return () =>
            {
                // Might switch to MoveNextAsync
                yieldItemCompletionSource.SetResult(1);
            };
        }

        /// <summary>
        /// Dequeues a wait task to wait for the next call to MoveNextAsync.
        /// </summary>
        private Task WaitForNextMoveNextAsync()
        {
            this.VerifyState(AsyncEnumeratorState.New, AsyncEnumeratorState.WaitingForMoveNext, AsyncEnumeratorState.WaitingForYieldOrCompletion);

            if (this.waitForMoveNextQueue.Queue2.Count == 0)
                this.EnqueueWaitForMoveNextTask();

            return this.waitForMoveNextQueue.Queue2.Dequeue();
        }

        private Task YieldAsync(T value)
        {
            Action completePendingYieldAction;
            Task res;
            lock (this.syncObj)
            {
                this.VerifyState(AsyncEnumeratorState.New, AsyncEnumeratorState.WaitingForMoveNext, AsyncEnumeratorState.WaitingForYieldOrCompletion);

                if (this.completionTask != null)
                    throw new InvalidOperationException("Enumerator has completed already.");

                this.items.Enqueue(value);

                // Might switch to MoveNextAsync
                completePendingYieldAction = this.CompletePendingYieldItemTask();

                res = this.WaitForNextMoveNextAsync();
            }

            if (completePendingYieldAction != null)
                completePendingYieldAction();

            return res;
        }

        private Task WaitForStartAsync()
        {
            // No synchronization required because field is readonly.
            return this.waitForMoveToFirstTask;
        }
    }
}
