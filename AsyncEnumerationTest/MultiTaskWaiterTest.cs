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
using Minichmayr.Async;

namespace Minichmayr.Async.Test
{
    [TestClass]
    public class MultiTaskWaiterTest
    {
        [TestMethod]
        public void TestSimple()
        {
            using (MultiWaiter<Task> waiter = new MultiWaiter<Task>())
            {
                var waitForNextTask = waiter.WaitNextAsync();
                TaskCompletionSource<int> tcs1 = new TaskCompletionSource<int>();
                TaskCompletionSource<int> tcs2 = new TaskCompletionSource<int>();
                TaskCompletionSource<int> tcs3 = new TaskCompletionSource<int>();

                waiter.Add(tcs1.Task);
                waiter.Add(tcs2.Task);
                waiter.Add(tcs3.Task);

                Assert.IsFalse(waitForNextTask.IsCompleted);

                tcs1.SetResult(1);
                var completedTask = waitForNextTask.AwaitSynchronously();
                Assert.AreSame(tcs1.Task, completedTask);

                waitForNextTask = waiter.WaitNextAsync();
                Assert.IsFalse(waitForNextTask.IsCompleted);
            }
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void TestAddNull()
        {
            using (MultiWaiter<Task> waiter = new MultiWaiter<Task>())
            {
                waiter.Add(null);
            }
        }

        [TestMethod]
        public void TestAddTwice()
        {
            using (MultiWaiter<Task> waiter = new MultiWaiter<Task>())
            {
                Task t = new Task(() => { });
                t.RunSynchronously();

                Assert.IsTrue(waiter.Add(t));
                Assert.IsFalse(waiter.Add(t));

                Assert.AreEqual(t, waiter.WaitNextAsync().AwaitSynchronously());

                Assert.IsTrue(waiter.Add(t));
                Assert.IsFalse(waiter.Add(t));
            }
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void TestRemoveNull()
        {
            using (MultiWaiter<Task> waiter = new MultiWaiter<Task>())
            {
                waiter.Remove(null);
            }
        }

        [TestMethod]
        public void TestRemoveUncompleted()
        {
            using (MultiWaiter<Task> waiter = new MultiWaiter<Task>())
            {
                TaskCompletionSource<int> tcs1 = new TaskCompletionSource<int>();
                TaskCompletionSource<int> tcs2 = new TaskCompletionSource<int>();

                waiter.Add(tcs1.Task);
                waiter.Add(tcs2.Task);

                waiter.Remove(tcs1.Task);

                tcs2.SetResult(1);

                Task t = waiter.WaitNextAsync().AwaitSynchronously();
                Assert.AreSame(tcs2.Task, t);

                Assert.IsFalse(waiter.WaitNextAsync().IsCompleted);
            }
        }

        [TestMethod]
        public void TestRemoveCompleted()
        {
            using (MultiWaiter<Task> waiter = new MultiWaiter<Task>())
            {
                TaskCompletionSource<int> tcs1 = new TaskCompletionSource<int>();
                TaskCompletionSource<int> tcs2 = new TaskCompletionSource<int>();

                waiter.Add(tcs1.Task);
                waiter.Add(tcs2.Task);

                tcs1.SetResult(1);
                tcs1.Task.AwaitSynchronously();

                waiter.Remove(tcs1.Task);

                tcs2.SetResult(1);

                Task t = waiter.WaitNextAsync().AwaitSynchronously();
                Assert.AreSame(tcs2.Task, t);

                Assert.IsFalse(waiter.WaitNextAsync().IsCompleted);
            }
        }

        [TestMethod]
        public void TestCompleteRemoved()
        {
            using (MultiWaiter<Task> waiter = new MultiWaiter<Task>())
            {
                TaskCompletionSource<int> tcs1 = new TaskCompletionSource<int>();

                waiter.Add(tcs1.Task);
                waiter.Remove(tcs1.Task);

                tcs1.SetResult(1);
                tcs1.Task.AwaitSynchronously();

                Assert.IsFalse(waiter.WaitNextAsync().IsCompleted);
            }
        }

        [TestMethod]
        public void TestCompleteRemoved2()
        {
            using (MultiWaiter<Task> waiter = new MultiWaiter<Task>())
            {
                TaskCompletionSource<int> tcs1 = new TaskCompletionSource<int>();

                waiter.Add(tcs1.Task);

                tcs1.SetResult(1);
                tcs1.Task.AwaitSynchronously();

                waiter.Remove(tcs1.Task);

                Assert.IsFalse(waiter.WaitNextAsync().IsCompleted);
            }
        }

        [TestMethod]
        public void TestDispose()
        {
            MultiWaiter<Task> waiter = new MultiWaiter<Task>();
            var waitForNextTask = waiter.WaitNextAsync();
            Assert.IsFalse(waitForNextTask.IsCompleted);

            waiter.Dispose();

            // Calling Dispose twice is allowed
            waiter.Dispose();

            waitForNextTask.Wait();
            Assert.IsTrue(waitForNextTask.IsCompleted);

            try
            {
                waiter.Add(new Task(() => { }));
                Assert.Fail();
            }
            catch (ObjectDisposedException)
            { }

            try
            {
                waiter.Remove(new Task(() => { }));
                Assert.Fail();
            }
            catch (ObjectDisposedException)
            { }

            try
            {
                waiter.WaitNextAsync().AwaitSynchronously();
                Assert.Fail();
            }
            catch (ObjectDisposedException)
            { }
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void TestMultiMoveNext()
        {
            using (MultiWaiter<Task> waiter = new MultiWaiter<Task>())
            {
                waiter.WaitNextAsync();
                waiter.WaitNextAsync().AwaitSynchronously();
            }
        }
    }
}
