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
    /// Abstract representation of a consumer. Allows to yield a result to the consumer and await
    /// the demand for the next value.
    /// </summary>
    /// <typeparam name="T">The type of objects to consume.</typeparam>
    /// <remarks>
    /// <para>
    /// Instances implementing this interface are usally provided to iterator functions that 
    /// produce values that may be enumerated via the <see cref="IAsyncEnumerable{T}"/> interface
    /// (see <see cref="AsyncEnum.Enumerate{T}"/>).
    /// </para><para>
    /// The <see cref="YieldAsync"/> method is used to yield a value to the consumer. The returned
    /// task may be used to await the consumer's demand for the next item. This allows to implement
    /// semantics similar to <c>yield return</c>. However, awaiting the returned <c>Task</c> is
    /// optional. If not awaited, the producer might produce items faster than they are consumed
    /// in which case they are getting queued by the consumer.
    /// </para>
    /// </remarks>
    /// <seealso cref="IAsyncEnumerable{T}"/>
    /// <seealso cref="AsyncEnum.Enumerate{T}"/>
    public interface IAsyncConsumer<in T>
    {
        /// <summary>
        /// Yields an item to the consumer and returns a <c>Task</c> object that allows 
        /// <c>await</c>ing demand for the next item.
        /// </summary>
        /// <param name="item">The item to yield to the consumer.</param>
        /// <returns>A <see cref="Task"/> object that allows to await demand for the
        /// next item</returns>
        Task YieldAsync(T item);
    }
}
