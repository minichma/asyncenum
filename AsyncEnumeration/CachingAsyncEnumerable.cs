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
    /// <summary>
    /// Wraps an instance of <see cref="IAsyncEnumerator{T}"/> and caches the instances returned
    /// by the wrapped interator so that multiple <see cref="IAsyncEnumerator{T}"/> returned by
    /// objects of this type will cause the inner instance of <see cref="IAsyncEnumerator{T}"/>
    /// to be iterated only once.
    /// </summary>
    /// <typeparam name="T">The type of enumerated objects.</typeparam>
    /// <remarks>
    /// <para>
    /// Objects of this type iterate over the wrapped enumerator as requested by the
    /// <see cref="IAsyncEnumerator{T}"/> they produce. If multiple produced enumerators request
    /// the same item, only the first request will cause the item to be retrieved from the inner
    /// enumerator. The returned item is cached and all subsequent requests will be answered from
    /// the cache.
    /// </para><para>
    /// If the wrapped enumerator raises an exception in a MoveNext task, the exception is
    /// propagated to the produced enumerators at exact the same position in the returned sequence
    /// as observed in the wrapped sequence.
    /// </para>
    /// </remarks>
    internal class CachingAsyncEnumerable<T> : IAsyncEnumerable<T>
    {
        private class CachingAsyncEnumerator : IAsyncEnumerator<T>
        {
            private readonly CachingAsyncEnumerable<T> parent;

            private int index = -1;

            private bool moveNextPending = false;

            private bool completed = false;

            public CachingAsyncEnumerator(CachingAsyncEnumerable<T> parent)
            {
                if (parent == null)
                    throw new ArgumentNullException("parent");

                this.parent = parent;
            }

            #region IAsyncEnumerator<T> Members

            public T Current
            {
                get
                {
                    // No locking required because this mustn't be called multi-threaded.
                    if (this.moveNextPending)
                        throw new InvalidOperationException("MoveNext pending");

                    return parent.GetAt(this.index); 
                }
            }

            public async Task<bool> MoveNextAsync()
            {
                // No locking required because this mustn't be called multi-threaded.
                if (this.moveNextPending)
                    throw new InvalidOperationException("MoveNext pending");

                if (this.completed)
                    return false;

                this.moveNextPending = true;
                try
                {
                    this.index++;
                    bool available = await this.parent.MoveToAsync(this.index);
                    this.completed = !available;

                    return available;
                }
                catch
                {
                    // According to the contract of IAsyncEnumerator<T> we throw the inner
                    // exception only on the first failing MoveNext operation and consider the
                    // enumeration as completed afterwards.
                    this.completed = true;
                    throw;
                }
                finally
                {
                    this.moveNextPending = false;
                }
            }

            #endregion
        }

        private readonly object syncObj = new object();

        private readonly IAsyncEnumerator<T> innerEnumerator;

        private readonly List<T> cachedItems = new List<T>();

        private Task<bool> pendingMoveNextTask;


        public CachingAsyncEnumerable(IAsyncEnumerator<T> innerEnumerator)
        {
            if (innerEnumerator == null)
                throw new ArgumentNullException("innerEnumerator");

            this.innerEnumerator = innerEnumerator;
        }

        private async Task<bool> MoveToAsync(int index)
        {
            Task<bool> task;
            lock (this.syncObj)
            {
                var currentCount = this.cachedItems.Count;
                if (currentCount > index)
                    return true;

                Debug.Assert(currentCount == index);

                if (this.pendingMoveNextTask == null)
                {
                    // TODO: This call might switch to the producer function and cause strange
                    // concurrency issues. Should be called outside lock context.
                    this.pendingMoveNextTask = this.innerEnumerator.MoveNextAsync();
                }
                task = this.pendingMoveNextTask;
            }

            // TODO: Exception handling
            bool result = await task;

            if (result)
            {
                lock (this.syncObj)
                {
                    if (this.pendingMoveNextTask == task)
                    {
                        this.pendingMoveNextTask = null;
                        this.cachedItems.Add(this.innerEnumerator.Current);
                    }
                }
            }
            return result;
        }

        private T GetAt(int index)
        {
            lock (this.syncObj)
            {
                return this.cachedItems[index];
            }
        }

        #region IAsyncEnumerable<T> Members

        public IAsyncEnumerator<T> GetEnumerator()
        {
            return new CachingAsyncEnumerator(this);
        }

        #endregion
    }
}
