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
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading.Tasks;
using System.Diagnostics;
using Minichmayr.Async;

namespace Minichmayr.Async.Test
{
    [TestClass]
    public class AsyncEnumeratorTest
    {
        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void TestConcurrentMoveNext()
        {
            var enumerator = TestUtils.EnumerateNothingForever<int>().GetEnumerator();

            enumerator.MoveNextAsync();
            enumerator.MoveNextAsync().AwaitSynchronously();
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void TestCurrentBeforeMoveNext()
        {
            var enumerator = TestUtils.EnumerateNothingForever<int>().GetEnumerator();
            var x = enumerator.Current;
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void TestCurrentAfterLast()
        {
            var enumerator = TestUtils.GetEmptyEnumerable<int>().GetEnumerator();
            Assert.IsFalse(enumerator.MoveNextAsync().AwaitSynchronously());
            var x = enumerator.Current;
        }

        [TestMethod]
        public void TestMultiMoveNextAfterLast()
        {
            var enumerator = TestUtils.GetEmptyEnumerable<int>().GetEnumerator();
            for (int i = 0; i < 10; i++)
                Assert.IsFalse(enumerator.MoveNextAsync().AwaitSynchronously());
        }

        [TestMethod]
        public void TestMultiMoveNextAfterFail()
        {
            var enumerator = TestUtils.GetFailingEnumerator<int>().GetEnumerator();
            try
            {
                enumerator.MoveNextAsync().AwaitSynchronously();
                Assert.Fail();
            }
            catch (TestUtils.SomeTestException)
            { }

            for (int i = 0; i < 10; i++)
            {
                // According to the contract of IAsyncEnumerator<T> it would be OK if we would
                // receive an exception here but the actual implementation of AsyncEnumerator<T>
                // simply returns false.
                Assert.IsFalse(enumerator.MoveNextAsync().AwaitSynchronously());
            }
        }

        [TestMethod]
        [ExpectedException(typeof(TestUtils.SomeTestException))]
        public void TestFailBeforeFirst()
        {
            var enumerator = TestUtils.GetFailingEnumerator<int>();
            enumerator.GetEnumerator().MoveNextAsync().AwaitSynchronously();
        }

        [TestMethod]
        [ExpectedException(typeof(TestUtils.SomeTestException))]
        public void TestFailEnroute()
        {
            int n = 10;
            var items = Enumerable.Range(0, n).AsAsyncEnumerable().Concat(TestUtils.GetFailingEnumerator<int>());

            int count = 0;

            try
            {
                items.ForEachAsync(item =>
                {
                    count++;
                }).AwaitSynchronously();
            }
            catch
            {
                Assert.AreEqual(n, count);
                throw;
            }
        }

        [TestMethod]
        public void TestMultithreadedFeed()
        {
            IEnumerable<IEnumerable<Tuple<object, int>>> expectedItemGroups = null;
            var items = AsyncEnum.Enumerate<Tuple<object, int>>(async consumer =>
            {
                List<Task<IEnumerable<Tuple<object, int>>>> tasks = new List<Task<IEnumerable<Tuple<object, int>>>>();
                for (int i = 0; i < 10; i++)
                {
                    tasks.Add(Task.Factory.StartNew(threadIdx =>
                    {
                        List<Tuple<object, int>> threadItems = new List<Tuple<object, int>>();
                        for (int j = 0; j < 1000; j++)
                        {
                            var item = Tuple.Create(threadIdx, j);
                            consumer.YieldAsync(item);
                            threadItems.Add(item);
                        }

                        return (IEnumerable<Tuple<object, int>>)threadItems;
                    }, i));
                }
                expectedItemGroups = await Task.WhenAll(tasks);
            });

            var actualItems = items.ToList();

            var match = from actualItem in actualItems
                        group actualItem by actualItem.Item1 into actualGroup
                        join expectedItemGroup in expectedItemGroups on actualGroup.Key equals expectedItemGroup.First().Item1
                        select new { Actual = actualGroup, Expected = expectedItemGroup };

            Assert.IsTrue(match.All(x => x.Actual.SequenceEqual(x.Expected)));
        }

        [TestMethod]
        public void TestAllYieldTasksCompleteIfProducerFaster()
        {
            List<Task> yieldTasks = new List<Task>();

            AsyncEnumerator<int> enumerator = new AsyncEnumerator<int>();
            var consumer = enumerator.Consumer;
            int cnt = 0;
            yieldTasks.Add(consumer.YieldAsync(cnt++));
            yieldTasks.Add(consumer.YieldAsync(cnt++));

            // 0
            enumerator.MoveNextAsync().AwaitSynchronously();
            
            // 1
            enumerator.MoveNextAsync().AwaitSynchronously();

            yieldTasks[0].Wait();
            Assert.IsFalse(yieldTasks[1].Wait(0));
            Assert.IsTrue(yieldTasks[0].Status == TaskStatus.RanToCompletion);
            Assert.IsTrue(yieldTasks[1].Status != TaskStatus.RanToCompletion);

            var moveTo3rdTask = enumerator.MoveNextAsync();
            yieldTasks[1].Wait();
            Assert.IsTrue(yieldTasks[1].Status == TaskStatus.RanToCompletion);

            for (int i = 0; i < 10; i++)
                yieldTasks.Add(consumer.YieldAsync(cnt++));

            consumer.Complete();

            while (enumerator.MoveNextAsync().AwaitSynchronously());

            Assert.IsTrue(yieldTasks.All(x => yieldTasks[1].Status == TaskStatus.RanToCompletion));
        }

        [TestMethod]
        public void TestCompleteSuccessfullyWithFasterProducer()
        {
            AsyncEnumerator<int> enumerator = new AsyncEnumerator<int>();
            var consumer = enumerator.Consumer;

            int cnt = 0;
            List<Task> yieldTasks = new List<Task>();
            
            yieldTasks.Add(consumer.YieldAsync(cnt++));
            yieldTasks.Add(consumer.YieldAsync(cnt++));
            yieldTasks.Add(consumer.YieldAsync(cnt++));

            consumer.Complete();

            enumerator.ProceedToCompletion();

            Task.WaitAll(yieldTasks.ToArray());

            try
            {
                consumer.YieldAsync(1); // must raise exception
            }
            catch (InvalidOperationException)
            { }
        }

        [TestMethod]
        public void TestComplexWithFastFeeder()
        {
            TestComplex(false, false);
        }

        [TestMethod]
        public void TestComplexWithFastFeederAndFault()
        {
            TestComplex(false, true);
        }

        [TestMethod]
        public void TestComplexWithSlowFeeder()
        {
            TestComplex(true, false);
        }

        [TestMethod]
        public void TestComplexWithSlowFeederAndFault()
        {
            TestComplex(true, true);
        }

        private void TestComplex(bool feedAhead, bool fail)
        {
            AsyncEnumerator<int> enumerator = new AsyncEnumerator<int>();
            var consumer = enumerator.Consumer;

            var src = Enumerable.Range(0, 3);

            List<Task> yieldTasks;
            if (feedAhead)
            {
                yieldTasks = consumer.FeedFast(src);

                if (fail)
                    consumer.Fail(new TestUtils.SomeTestException());
                else
                    consumer.Complete();

                VerifyYieldAfterLast(consumer);
                var actual = enumerator.Consume(src.Count());
            }
            else
            {
                yieldTasks = new List<Task>();
                foreach (var item in src)
                {
                    var moveNextTask = enumerator.MoveNextAsync();
                    yieldTasks.Add(consumer.YieldAsync(item));
                    moveNextTask.Wait();
                    Assert.IsTrue(moveNextTask.IsCompleted);
                    Assert.IsTrue(moveNextTask.AwaitSynchronously());
                }

                if (fail)
                    consumer.Fail(new TestUtils.SomeTestException());
                else
                    consumer.Complete();

                VerifyYieldAfterLast(consumer);
            }

            foreach (var yieldTask in yieldTasks.Take(yieldTasks.Count - 1))
            {
                yieldTask.Wait();
                Assert.IsTrue(yieldTask.IsCompleted);
            }
            Assert.IsFalse(yieldTasks.Last().IsCompleted);

            try
            {
                bool res = enumerator.MoveNextAsync().AwaitSynchronously();
                if (fail)
                    Assert.Fail();

                Assert.IsFalse(res);
            }
            catch (TestUtils.SomeTestException)
            {
                if (!fail)
                    Assert.Fail();
            }
        }

        private void VerifyYieldAfterLast(AsyncEnumerator<int>.AsyncConsumer consumer)
        {
            try
            {
                consumer.YieldAsync(1).AwaitSynchronously();
                Assert.Fail();
            }
            catch (InvalidOperationException)
            { }
        }
    }
}
