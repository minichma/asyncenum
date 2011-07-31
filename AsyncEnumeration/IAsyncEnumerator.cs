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

namespace Minichmayr.Async
{
    /// <summary>
    /// Supports asynchronous iteration through a collection.
    /// </summary>
    /// <typeparam name="T">The type of objects to enumerate.</typeparam>
    /// <remarks>
    /// <para>
    /// <c>IAsyncEnumerator{T}</c> supports asynchronous iteration through a collection. It's semantics are closely related to
    /// thoese of <c>IEnumerator{T}</c> except that instead of an synchronous <c>MoveNext</c> method, an asynchronous
    /// <c>MoveNextAsync</c> method is provided that allows to <c>await</c> the arrival of the next element.
    /// </para><para>
    /// Initially, the enumerator is positioned before the first element in the collection. At this position, <see cref="Current"/>
    /// is undefined. Therefore, you must call <see cref="MoveNextAsync"/> to advance the enumerator to the first element of the 
    /// collection before reading the value of Current.
    /// </para><para>
    /// The conent of <see cref="Current"/> is undefined in following situations:
    /// </para>
    /// <list type="bullet">
    ///     <listheader><description>
    ///         <see cref="MoveNextAsync"/> has not been called and the enumerator is therefore positioned 
    ///         before the first element in the collection.
    ///     </description></listheader>
    ///     <listheader><description>
    ///         A call to <c>MoveNextAsync</c> is pending, that is, <c>MoveNextAsync</c> has been called but the returned task
    ///         was not yet completed.
    ///     </description></listheader>
    ///     <listheader><description>
    ///         The enumerator is positioned after the last element in the collection, that is, the task returned by the last
    ///         call to <c>MoveNextAsync</c> completed with a result of <c>false</c>.
    ///     </description></listheader>
    ///     <listheader><description>
    ///         The task returned by the last call to <c>MoveNextAsync</c> completed faulted.
    ///     </description></listheader>
    /// </list>
    /// <para>
    /// Once the enumerator has completed successfully, that is, the task returned by the last call to <see cref="MoveNextAsync"/>
    /// completed with result <c>false</c>, all subsequent calls to <c>MoveNextAsync</c> will return equal results. 
    /// If the iterator completed in faulted state, that is, the task returned by the last call to <see cref="MoveNextAsync"/>
    /// completed exceptional, subsequent calls may either continue to return Tasks that complete exceptional or return Tasks
    /// that evaluate to <c>false</c>.
    /// </para><para>
    /// Instances implementing this interface may only be enumerated in a sequential manner. I.e. on an individual instance
    /// <see cref="MoveNextAsync"/> may only be called when no other MoveNext task is pending, that is, when
    /// <c>MoveNextAsync</c> is called on this instance for the first time or the Task returned by the previous call has
    /// completed.
    /// </para>
    /// </remarks>
    public interface IAsyncEnumerator<out T>
    {
        /// <summary>
        /// Gets the element in the collection at the current position of the enumerator.
        /// </summary>
        /// <seealso cref="IAsyncEnumerator{T}"/>
        T Current { get; }

        /// <summary>
        /// Proceeds the enumerator to the next item asynchronously.
        /// </summary>
        /// <returns>A <c>Task{bool}</c> that can be used to await the restult of the MoveNext operation.
        /// The <c>Task</c> will return <c>true</c> if the enumerator was proceeded to the next element and
        /// <c>false</c> if no more elements exist and the enumerator was positioned after the last element.</returns>
        /// <seealso cref="IAsyncEnumerator{T}"/>
        Task<bool> MoveNextAsync();
    }
}
