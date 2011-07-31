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
    class MultiWaiter<TTask> : IDisposable where TTask : Task
    {
        private readonly object syncObj = new object();
        private readonly ISet<TTask> waitingTasks = new HashSet<TTask>();

        private readonly LinkedList<TTask> completedTasks = new LinkedList<TTask>();
        private readonly Dictionary<TTask, LinkedListNode<TTask>> completedTaskNodes = new Dictionary<TTask, LinkedListNode<TTask>>();

        private TaskCompletionSource<int> waitForNextCompletedCompletionSource = null;

        private bool moveNextPending = false;
        private bool disposed = false;


        public bool Add(TTask task)
        {
            if (task == null)
                throw new ArgumentNullException("task");

            lock (this.syncObj)
            {
                if (this.disposed)
                    throw new ObjectDisposedException(typeof(MultiWaiter<TTask>).Name);

                if (!this.waitingTasks.Add(task))
                    return false;

                if (this.completedTaskNodes.ContainsKey(task))
                    return false;
            }

            task.ContinueWith(OnTaskCompleted);
            return true;
        }

        private void OnTaskCompleted(Task task)
        {
            TTask taskT = (TTask)task;
            TaskCompletionSource<int> completionSource;
            lock (this.syncObj)
            {
                if (this.disposed)
                    return;

                if (!this.waitingTasks.Remove(taskT))
                {
                    Debug.WriteLine("Task already completed or unknown.");
                    return;
                }

                this.completedTaskNodes.Add(taskT, this.completedTasks.AddLast(taskT));
                completionSource = this.waitForNextCompletedCompletionSource;
            }
            // Set result outside of lock context to avoid concurrency issues in case control
            // is yielded to waiter
            if (completionSource != null)
                completionSource.TrySetResult(1);
        }

        public bool Remove(TTask task)
        {
            if (task == null)
                throw new ArgumentNullException("task");

            lock (this.syncObj)
            {
                if (this.disposed)
                    throw new ObjectDisposedException(typeof(MultiWaiter<TTask>).Name);

                bool wasWaiting = this.waitingTasks.Remove(task);

                // We remove by node to avoid O(N) effort
                LinkedListNode<TTask> node;
                bool wasCompleted = this.completedTaskNodes.TryGetValue(task, out node);
                if (wasCompleted)
                {
                    this.completedTaskNodes.Remove(task);
                    this.completedTasks.Remove(node);
                }
                return wasWaiting || wasCompleted;
            }
        }

        public async Task<TTask> WaitNextAsync()
        {
            if (this.disposed)
                throw new ObjectDisposedException(typeof(MultiWaiter<TTask>).Name);

            if (this.moveNextPending)
                throw new InvalidOperationException();

            this.moveNextPending = true;

            while (!this.disposed)
            {
                lock (this.syncObj)
                {
                    if (this.completedTasks.First != null)
                    {
                        TTask task = this.completedTasks.First.Value;
                        this.completedTaskNodes.Remove(task);
                        this.completedTasks.RemoveFirst();
                        this.moveNextPending = false;
                        return task;
                    }
                    Debug.Assert(this.waitForNextCompletedCompletionSource == null);
                    this.waitForNextCompletedCompletionSource = new TaskCompletionSource<int>();
                }
                await this.waitForNextCompletedCompletionSource.Task;
                this.waitForNextCompletedCompletionSource = null;
            }

            return null;
        }

        public void Dispose()
        {
            TaskCompletionSource<int> completionSource;
            lock (this.syncObj)
            {
                this.waitingTasks.Clear();
                this.completedTasks.Clear();
                this.disposed = true;
                completionSource = this.waitForNextCompletedCompletionSource;
            }
            // Set result outside of lock context to avoid concurrency issues in case control
            // is yielded to waiter
            if (completionSource != null)
                completionSource.TrySetResult(1);
        }
    }
}
