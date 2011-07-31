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
    class AsyncLookup<TKey, TElement> : IAsyncLookup<TKey, TElement>
    {
        private readonly IAsyncEnumerable<IAsyncGrouping<TKey, TElement>> sourceGroups;

        private readonly IAsyncEnumerator<IAsyncGrouping<TKey, TElement>> sourceEnumerator;

        private readonly Dictionary<TKey, IAsyncGrouping<TKey, TElement>> groupsByKey;


        private bool retrievePending = false;


        private AsyncLookup(IAsyncEnumerable<IAsyncGrouping<TKey, TElement>> sourceGroups, IEqualityComparer<TKey> comparer)
        {
            if (sourceGroups == null)
                throw new ArgumentNullException();

            // With cache because this might be iterated several times but as a grouping it's
            // expected to cache the source items.
            this.sourceGroups = sourceGroups.WithCache();

            this.sourceEnumerator = this.sourceGroups.GetEnumerator();

            this.groupsByKey = new Dictionary<TKey, IAsyncGrouping<TKey, TElement>>(comparer);
        }

        /// <summary>
        /// Asynchronously retrieves the <see cref="IAsyncGrouping{TKey, TElement}"/> with the specified key.
        /// </summary>
        /// <param name="key">The key to retrieve.</param>
        /// <returns>The <see cref="GetAsync(TKey)"/> that is associated with the specified key. If no grouping with the specified key is found, <c>null</c> is returned.</returns>
        /// <remarks>
        /// In contrast to <see cref="GetAsync(TKey)"/> this method returns <c>null</c> if
        /// no grouping related to the specified key is found.
        /// </remarks>
        private async Task<IAsyncGrouping<TKey, TElement>> RetrieveAsync(TKey key)
        {
            if (this.retrievePending)
                throw new InvalidOperationException("multi-threaded access");

            this.retrievePending = true;
            try
            {
                IAsyncGrouping<TKey, TElement> group;
                if (this.groupsByKey.TryGetValue(key, out group))
                    return group;

                while (await this.sourceEnumerator.MoveNextAsync())
                {
                    group = this.sourceEnumerator.Current;

                    // We mustn't have duplicates here. We might have duplicates if the source
                    // sequence is not grouped correctly but as the source will always be created
                    // via static methods of this class, we can be pretty sure that we did grouping
                    // right.
                    this.groupsByKey.Add(group.Key, group);

                    if (this.groupsByKey.Comparer.Equals(key, group.Key))
                        return group;
                }
            }
            finally
            {
                this.retrievePending = false;
            }

            return null;
        }

        public async Task<bool> ContainsAsync(TKey key)
        {
            return (await RetrieveAsync(key)) != null;
        }

        /// <summary>
        /// Asynchronously retrieves the <see cref="IAsyncEnumerable{TElement}"/> associated with the specified key.
        /// </summary>
        /// <param name="key">The key to retrieve.</param>
        /// <returns>The <see cref="GetAsync(TKey)"/> that is associated with the specified key. If no grouping with the specified key is found, an empty sequence is returned.</returns>
        /// <remarks>
        /// In contrast to <see cref="RetrieveAsync(TKey)"/> this method returns an empty sequence if
        /// no elements related to the specified key are found.
        /// </remarks>
        public async Task<IAsyncEnumerable<TElement>> GetAsync(TKey key)
        {
            var group = await RetrieveAsync(key);
            if (group == null)
                return AsyncEnum.Empty<TElement>();
            
            return group;
        }

        public IAsyncEnumerator<IAsyncGrouping<TKey, TElement>> GetEnumerator()
        {
            return this.sourceGroups.GetEnumerator();
        }


        public static IAsyncLookup<TKey, TElement> ToLookup(IAsyncEnumerable<TElement> source, Func<TElement, TKey> keySelector, IEqualityComparer<TKey> comparer)
        {
            if (source == null)
                throw new ArgumentNullException("source");
            if (keySelector == null)
                throw new ArgumentNullException("keySelector");

            return new AsyncLookup<TKey, TElement>(source.GroupBy(keySelector, comparer), comparer);
        }
    }
}
