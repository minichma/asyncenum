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
using System.Text;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading.Tasks;

namespace Minichmayr.Async.Test
{
    [TestClass]
    public class CachingAsyncEnumerableTest
    {
        // * Exception propagation to multiple instances
        // * empty sequences
        // * multiple enumerators waiting for the same item concurrently.
        // * propagation of completion

        [TestMethod]
        public void TestSimpleSequential()
        {
            var src = Enumerable.Range(1, 100);
            var cachedEnumerable = new CachingAsyncEnumerable<int>(src.AsAsyncEnumerable().GetEnumerator());
            for (int i = 0; i < 10; i++)
            {
                var actual = cachedEnumerable.AsEnumerable();
                Assert.IsTrue(src.SequenceEqual(actual));
            }
        }

        [TestMethod]
        public void TestSimpleSimultanous()
        {
            var src = Enumerable.Range(1, 10);
            var cachedEnumerable = new CachingAsyncEnumerable<int>(src.AsAsyncEnumerable().GetEnumerator());

            List<Tuple<IAsyncEnumerator<int>, List<int>>> actualResults = new List<Tuple<IAsyncEnumerator<int>, List<int>>>();
            for (int i = 0; i < 10; i++)
                actualResults.Add(Tuple.Create(cachedEnumerable.GetEnumerator(), new List<int>()));

            for (int i = 0; i < 20; i++)
            {
                var moveNextTasks = new List<Task<bool>>();
                actualResults.ForEach(x => moveNextTasks.Add(x.Item1.MoveNextAsync()));
                Task.WaitAll(moveNextTasks.ToArray());
                bool success = moveNextTasks.First().Result;
                Assert.IsTrue(moveNextTasks.All(x => x.Result == success));

                if (success)
                    actualResults.ForEach(x => x.Item2.Add(x.Item1.Current));
            }

            Assert.IsTrue(actualResults.All(x => x.Item2.SequenceEqual(src)));
        }

        [TestMethod]
        public void TestSimultanousWait()
        {
            var src = Enumerable.Range(1, 10);
            var srcEnumerator = src.GetEnumerator();
            var srcAsyncEnumerator = new AsyncEnumerator<int>();
            var cachedEnumerable = new CachingAsyncEnumerable<int>(srcAsyncEnumerator);

            List<Tuple<IAsyncEnumerator<int>, List<int>>> actualResults = new List<Tuple<IAsyncEnumerator<int>, List<int>>>();
            for (int i = 0; i < 10; i++)
                actualResults.Add(Tuple.Create(cachedEnumerable.GetEnumerator(), new List<int>()));

            bool wasCompleted = false;
            for (int i = 0; i < 20; i++)
            {
                var moveNextTasks = new List<Task<bool>>();
                actualResults.ForEach(x => moveNextTasks.Add(x.Item1.MoveNextAsync()));

                if (!wasCompleted)
                    Assert.IsTrue(moveNextTasks.All(x => !x.IsCompleted));

                bool success = srcEnumerator.MoveNext();
                if (success)
                    srcAsyncEnumerator.Consumer.YieldAsync(srcEnumerator.Current);
                else
                    srcAsyncEnumerator.Consumer.Complete();

                Task.WaitAll(moveNextTasks.ToArray());
                Assert.IsTrue(moveNextTasks.All(x => x.Result == success));

                if (success)
                    actualResults.ForEach(x => x.Item2.Add(x.Item1.Current));

                wasCompleted = !success;
            }

            Assert.IsTrue(actualResults.All(x => x.Item2.SequenceEqual(src)));
        }

        [TestMethod]
        public void TestExceptionPropagation()
        {
            var srcAsyncEnumerator = new AsyncEnumerator<int>();
            srcAsyncEnumerator.Consumer.Fail(new TestUtils.SomeTestException());

            var cachedEnumerable = new CachingAsyncEnumerable<int>(srcAsyncEnumerator);
            for (int i = 0; i < 10; i++)
            {
                var innerEnumerator = cachedEnumerable.GetEnumerator();
                try
                {
                    innerEnumerator.MoveNextAsync().AwaitSynchronously();
                    Assert.Fail();
                }
                catch (TestUtils.SomeTestException)
                {
                }

                // CachingAsyncEnumerable is implemented to propagate Exception only once and to return
                // false on subsequent calls to MoveNext. Contract also allows to propagate exception
                // multiple times
                Assert.IsFalse(innerEnumerator.MoveNextAsync().AwaitSynchronously());
            }
        }
    }
}
