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

#pragma warning disable

namespace Minichmayr.Async.Test
{
    static class TestUtils
    {
        public class SomeTestException : Exception
        {}

        public static IAsyncEnumerable<T> EnumerateSyncNoWait<T>(IEnumerable<T> source)
        {
            return AsyncEnum.Enumerate<T>(consumer => GetSyncNoWaitIterator(source, consumer));
        }

        static async Task GetSyncNoWaitIterator<T>(IEnumerable<T> source, IAsyncConsumer<T> consumer)
        {
            foreach (T item in source)
                consumer.YieldAsync(item);
        }

        public static IAsyncEnumerable<T> EnumerateWithDelay<T>(IEnumerable<T> source, int delayMs)
        {
            return AsyncEnum.Enumerate<T>(async consumer =>
            {
                foreach (T item in source)
                {
                    await Task.Delay(delayMs);
                    await consumer.YieldAsync(item);
                }
            });
        }

        public static IAsyncEnumerable<T> EnumerateNothingForever<T>()
        {
            return AsyncEnum.Enumerate<T>(async consumer =>
            {
                while (true)
                    await Task.Delay(TimeSpan.FromHours(1));
            });
        }

        public static IAsyncEnumerable<T> GetFailingEnumerator<T>()
        {
            return AsyncEnum.Enumerate<T>(async consumer =>
            {
                throw new SomeTestException();
            });
        }

        public static IAsyncEnumerable<T> GetEmptyEnumerable<T>()
        {
            return Enumerable.Empty<T>().AsAsyncEnumerable();
        }

        public static List<T> Consume<T>(this IAsyncEnumerator<T> enumerator, int n)
        {
            List<T> res = new List<T>(n);
            for (int i = 0; i < n; i++)
            {
                if (!enumerator.MoveNextAsync().AwaitSynchronously())
                    throw new ArgumentOutOfRangeException();

                res.Add(enumerator.Current);
            }
            return res;
        }

        public static void ProceedToCompletion<T>(this IAsyncEnumerator<T> enumerator)
        {
            while (enumerator.MoveNextAsync().AwaitSynchronously()) ;
        }

        public static List<Task> FeedFast<T>(this IAsyncConsumer<T> consumer, IEnumerable<T> source)
        {
            List<Task> tasks = new List<Task>();
            foreach (var item in source)
                tasks.Add(consumer.YieldAsync(item));

            return tasks;
        }

        public static IEnumerable<int> GetRandomEnumerable(int seed, int count, int maxValue)
        {
            Random rnd = new Random(seed);
            return Enumerable.Range(0, count).Select(x => rnd.Next(maxValue)).ToList();
        }
    }
}
