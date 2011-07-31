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
using System.Threading;
using Minichmayr.Async;
using System.Diagnostics;

namespace Minichmayr.Async.Test
{
    [TestClass]
    public class AsyncEnumTest
    {
        [TestMethod]
        public void TestSimple()
        {
            var src = Enumerable.Range(0, 1000);

            Assert.IsTrue(src.SequenceEqual(src.AsAsyncEnumerable().AsEnumerable()));
        }

        [TestMethod]
        public void TestSimpleSyncNoWait()
        {
            var src = Enumerable.Range(0, 1000);
            Assert.IsTrue(src.SequenceEqual(TestUtils.EnumerateSyncNoWait(src).AsEnumerable()));
        }

        [TestMethod]
        public void TestSimpleSyncNoWaitDelayConsumer()
        {
            var src = Enumerable.Range(0, 3);

            List<int> res = new List<int>();
            foreach (var item in TestUtils.EnumerateSyncNoWait(src).AsEnumerable())
            {
                res.Add(item);
                Thread.Sleep(200);
            }

            Assert.IsTrue(src.SequenceEqual(res));
        }

        [TestMethod]
        public void TestSimpleWithDelay()
        {
            var src = Enumerable.Range(0, 3);
            Assert.IsTrue(src.SequenceEqual(TestUtils.EnumerateWithDelay(src, 200).AsEnumerable()));
        }

        [TestMethod]
        public void TestGroupByComplexityOrder()
        {
            var asyncGroups = Enumerable.Range(0, 10000).Select(x => x).AsAsyncEnumerable().GroupBy(x => x);
            var execEnumerationTask = Task.Factory.StartNew(() => asyncGroups.ToList());

            // Heuristic to test whether we have fast growth (>= O(N^2) )
            Assert.AreEqual(0, Task.WaitAny(execEnumerationTask, Task.Delay(TimeSpan.FromSeconds(10))));
        }

        [TestMethod]
        public void TestGroupBySimple()
        {
            var rnd = new Random(0);
            var src = new List<int>();
            var expectedGroups = src.GroupBy(k => k % 10);

            for (int i = 0; i < 100; i++)
                src.Add(rnd.Next(1000));

            var asyncGroups =
                src.AsAsyncEnumerable().GroupBy(k => k % 10).ToList();

            var expectedKeys = expectedGroups.Select(g => g.Key);
            var actualKeys = asyncGroups.Select(g => g.Key);

            Assert.IsTrue(expectedKeys.SequenceEqual(actualKeys));
            foreach (var k in actualKeys)
            {
                var actualGroup = asyncGroups.Single(x => x.Key == k);
                var expectedGroup = expectedGroups.Single(x => x.Key == k);
                Assert.IsTrue(expectedGroup.SequenceEqual(actualGroup.AsEnumerable()));
            }
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void TestGroupByNullSource()
        {
            AsyncEnum.GroupBy<int, int>(null, x => x);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void TestGroupByNullKeySelector()
        {
            TestUtils.GetEmptyEnumerable<int>().GroupBy<int, int>(null);
        }

        [TestMethod]
        public void TestGroupByEmpty()
        {
            TestGroupBy(Enumerable.Range(0, 0), k => k);
        }

        [TestMethod]
        public void TestGroupBySingleGroup()
        {
            TestGroupBy(Enumerable.Range(0, 100), k => 1);
        }

        [TestMethod]
        public void TestGroupBy100Mod10()
        {
            TestGroupBy(Enumerable.Range(0, 100), k => k % 10);
        }

        [TestMethod]
        public void TestGroupBy100Div10()
        {
            TestGroupBy(Enumerable.Range(0, 100), k => k / 10);
        }

        [TestMethod]
        public void TestGroupBySingleItemPerGroup()
        {
            TestGroupBy(Enumerable.Range(0, 100), k => k);
        }

        private void TestGroupBy(IEnumerable<int> src, Func<int, int> groupFunction)
        {
            TestGroupBy(src, groupFunction, true, true, false);
            TestGroupBy(src, groupFunction, false, true, false);
            TestGroupBy(src, groupFunction, true, false, false);
            TestGroupBy(src, groupFunction, false, false, false);
            TestGroupBy(src, groupFunction, true, false, true);
            TestGroupBy(src, groupFunction, false, false, true);
        }

        private void TestGroupBy(IEnumerable<int> src, Func<int, int> groupFunction, bool sourceFaster, bool outerFirst, bool reverse)
        {
            var srcAsync = src.AsAsyncEnumerable();
            if (sourceFaster)
                srcAsync = srcAsync.WithNoWait();

            var expectedGroups = src.GroupBy(groupFunction);

            var actualAsyncGroups = srcAsync.GroupBy(groupFunction);
            if (outerFirst)
                actualAsyncGroups = actualAsyncGroups.WithNoWait();

            if (reverse)
                actualAsyncGroups = actualAsyncGroups.AsEnumerable().Take(expectedGroups.Count()).Reverse().AsAsyncEnumerable();

            List<int> actualKeys = new List<int>();
            actualAsyncGroups.ForEachAsync(async actualGroup =>
            {
                actualKeys.Add(actualGroup.Key);
                List<int> actualGroupValues = await actualGroup.ToListAsync();
                var expectedGroup = expectedGroups.Single(x => x.Key == actualGroup.Key);
                Assert.IsTrue(expectedGroup.SequenceEqual(actualGroupValues));
            }).Wait();

            var expectedKeys = expectedGroups.Select(x => x.Key).ToList();
            if (reverse)
                expectedKeys.Reverse();

            Assert.IsTrue(expectedKeys.SequenceEqual(actualKeys));
        }

        [TestMethod]
        public void TestGroupByRandomAccess()
        {
            TestGroupByRandomAccessAsync().AwaitSynchronously();
        }

        private async Task TestGroupByRandomAccessAsync()
        {
            Func<int, int> groupFunction = k => k % 10;
            int n = 10000;

            var rnd = new Random(0);
            var src = Enumerable.Range(0, n).Select(x => rnd.Next(1000)).ToList();
            var expectedGroups = src.GroupBy(groupFunction);
            var expectedKeys = expectedGroups.Select(g => g.Key);

            var asyncSrc = AsyncEnum.Enumerate<int>(async consumer =>
            {
                foreach (var srcItem in src)
                {
                    //if (rnd.Next(10) == 0)
                    //    await TaskEx.Delay(rnd.Next(10) * 10);
                    var awaitDemandTask = consumer.YieldAsync(srcItem);
                    if (rnd.Next(4) == 0)
                        await awaitDemandTask;
                }
            });

            var asyncGroupsEnumerator =
                asyncSrc.GroupBy(groupFunction).GetEnumerator();

            var actualKeys = new List<int>();

            var actualAsyncEnumerators = new List<IAsyncEnumerator<int>>();
            var pendingAsyncEnumerators = new List<Tuple<List<int>, IAsyncEnumerator<int>>>();
            var actualGroupValues = new List<List<int>>();

            int foundValues = 0;
            while (foundValues < n)
            {
                if ((pendingAsyncEnumerators.Count == 0) || (rnd.Next(2000) == 0))
                {
                    if (await asyncGroupsEnumerator.MoveNextAsync())
                    {
                        actualKeys.Add(asyncGroupsEnumerator.Current.Key);
                        var innerAsyncEnumerator = asyncGroupsEnumerator.Current.GetEnumerator();
                        actualAsyncEnumerators.Add(innerAsyncEnumerator);
                        var values = new List<int>();
                        pendingAsyncEnumerators.Add(Tuple.Create(values, innerAsyncEnumerator));
                        actualGroupValues.Add(values);
                    }
                }
                else
                {
                    int nextGroupIdx = rnd.Next(pendingAsyncEnumerators.Count);
                    var pendingGroup = pendingAsyncEnumerators[nextGroupIdx];
                    if (await pendingGroup.Item2.MoveNextAsync())
                    {
                        pendingGroup.Item1.Add(pendingGroup.Item2.Current);
                        foundValues++;
                    }
                    else
                    {
                        pendingAsyncEnumerators.RemoveAt(nextGroupIdx);
                    }
                }
            }

            Assert.IsTrue(expectedKeys.SequenceEqual(actualKeys));
            Assert.AreEqual(expectedGroups.Count(), actualGroupValues.Count);
            foreach (var tuple in expectedGroups.Zip(actualGroupValues, Tuple.Create))
                Assert.IsTrue(tuple.Item1.SequenceEqual(tuple.Item2));

        }

        [TestMethod]
        public void TestGroupByException()
        {
            var srcAsync = AsyncEnum.Enumerate<int>(async consumer =>
            {
                await consumer.YieldAsync(1);
                throw new TestUtils.SomeTestException();
            });

            var asyncGroupsEnum = srcAsync.GroupBy(x => x).GetEnumerator();
            Assert.IsTrue(asyncGroupsEnum.MoveNextAsync().AwaitSynchronously());
            var asyncGroup1Enum = asyncGroupsEnum.Current.GetEnumerator();

            try
            {
                asyncGroupsEnum.MoveNextAsync().AwaitSynchronously();
                Assert.Fail();
            }
            catch (TestUtils.SomeTestException)
            { }

            Assert.IsTrue(asyncGroup1Enum.MoveNextAsync().AwaitSynchronously());
            Assert.AreEqual(1, asyncGroup1Enum.Current);

            try
            {
                asyncGroup1Enum.MoveNextAsync().AwaitSynchronously();
                Assert.Fail();
            }
            catch (TestUtils.SomeTestException)
            { }
        }

        [TestMethod]
        public void TestWhere()
        {
            Func<int, bool> predicate = x => ((x % 10) == 0);
            TestSimpleOperator(x => x.Where(predicate), x => x.Where(predicate));
        }

        [TestMethod]
        public void TestSelect()
        {
            Func<int, int> f = x => x * 10 + 5;
            TestSimpleOperator(x => x.Select(f), x => x.Select(f));
        }

        [TestMethod]
        public void TestDistinct()
        {
            TestSimpleOperator(x => x.Distinct(), x => x.Distinct(null));
        }

        [TestMethod]
        public void TestConcat()
        {
            TestCombiningOperator((f, s) => f.Concat(s), (f, s) => f.Concat(s));
        }

        [TestMethod]
        public void TestUnion()
        {
            TestCombiningOperator((f, s) => f.Union(s), (f, s) => f.Union(s));
        }

        [TestMethod]
        public void TestJoin()
        {
            Func<int, int> keySelector = x => x;
            Func<int, int, int> resultSelector = (a, b) => (a << 16) + b;
            TestCombiningOperator((f, s) => f.Join(s, keySelector, keySelector, resultSelector), (f, s) => f.Join(s, keySelector, keySelector, resultSelector, null));
        }

        [TestMethod]
        public void TestCount()
        {
            TestScalarOperator(e => e.Count(), e => e.CountAsync());
        }

        [TestMethod]
        public void TestCountWithPredicate()
        {
            Func<int, bool> predicate = x => (x % 10) == 0;
            TestScalarOperator(e => e.Count(predicate), e => e.CountAsync(predicate));
        }

        [TestMethod]
        public void TestIntersect()
        {
            TestCombiningOperator((f, s) => f.Intersect(s), (f, s) => f.Intersect(s, null));
        }

        private void TestCombiningOperator(
            Func<IEnumerable<int>, IEnumerable<int>, IEnumerable<int>> expectedOperator,
            Func<IAsyncEnumerable<int>, IAsyncEnumerable<int>, IAsyncEnumerable<int>> actualOperator)
        {
            var second = TestUtils.GetRandomEnumerable(1, 50, 10);
            TestSimpleOperator(first => expectedOperator(first, second), first => actualOperator(first, second.AsAsyncEnumerable()));

            second = Enumerable.Empty<int>();
            TestSimpleOperator(first => expectedOperator(first, second), first => actualOperator(first, second.AsAsyncEnumerable()));
        }

        private void TestSimpleOperator(
            Func<IEnumerable<int>, IEnumerable<int>> expectedOperator,
            Func<IAsyncEnumerable<int>, IAsyncEnumerable<int>> actualOperator)
        {
            TestSimpleOperator(TestUtils.GetRandomEnumerable(0, 1000, 100), expectedOperator, actualOperator);
            TestSimpleOperator(Enumerable.Empty<int>(), expectedOperator, actualOperator);
        }

        private void TestScalarOperator(
            Func<IEnumerable<int>, int> expectedOperator,
            Func<IAsyncEnumerable<int>, Task<int>> actualOperator)
        {
            TestScalarOperator(TestUtils.GetRandomEnumerable(0, 1000, 100), expectedOperator, actualOperator);
            TestScalarOperator(Enumerable.Empty<int>(), expectedOperator, actualOperator);
        }

        private void TestSimpleOperator<TSource, TResult>(
            IEnumerable<TSource> data,
            Func<IEnumerable<TSource>, IEnumerable<TResult>> expectedOperator,
            Func<IAsyncEnumerable<TSource>, IAsyncEnumerable<TResult>> actualOperator)
        {
            var asyncData = data.AsAsyncEnumerable();
            var expectedSeq = expectedOperator(data);
            var actualSeq = actualOperator(asyncData);

            if (expectedSeq == null)
            {
                Assert.IsNull(actualOperator);
            }
            else
            {
                var actualValues = actualSeq.ToList();
                if (!expectedSeq.SequenceEqual(actualValues))
                {
                    Trace.WriteLine(string.Format("expected: {0}, actual: {1}", string.Join(",", expectedSeq), string.Join(",", actualValues)));
                    Assert.Fail();
                }
            }
        }

        private void TestScalarOperator<TSource, TResult>(
            IEnumerable<TSource> data,
            Func<IEnumerable<TSource>, TResult> expectedOperator,
            Func<IAsyncEnumerable<TSource>, Task<TResult>> actualOperator)
        {
            var asyncData = data.AsAsyncEnumerable();
            var expectedVal = expectedOperator(data);
            var actualVal = actualOperator(asyncData).AwaitSynchronously();

            Assert.AreEqual(expectedVal, actualVal);
        }
    }
}
