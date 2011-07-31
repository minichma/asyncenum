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

using Minichmayr.Async;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
using System.Diagnostics;

namespace Minichmayr.Async.Test
{
    [TestClass()]
    public class AsyncLookupTest
    {
        [TestMethod]
        public void TestEmpty()
        {
            var lookup = AsyncLookup<int, int>.ToLookup(AsyncEnum.Empty<int>(), x => x, null);
            Assert.IsFalse(lookup.ContainsAsync(1).AwaitSynchronously());

            var seq = lookup.GetAsync(1).AwaitSynchronously();
            Assert.IsNotNull(seq);
            Assert.AreEqual(0L, seq.CountAsync().AwaitSynchronously());
        }

        [TestMethod]
        public void TestSimple()
        {
            int n = 100;
            Func<int, int> keySelector = x => x / 10;

            var src = Enumerable.Range(0, n);
            var expectedLookup = src.ToLookup(keySelector);
            var lookup = AsyncLookup<int, int>.ToLookup(src.AsAsyncEnumerable(), keySelector, null);

            foreach (var expected in expectedLookup)
            {
                var actual = lookup.GetAsync(expected.Key).AwaitSynchronously();
                Assert.IsNotNull(actual);
                Assert.IsTrue(expected.SequenceEqual(actual.AsEnumerable()));
                Assert.IsTrue(lookup.ContainsAsync(expected.Key).AwaitSynchronously());
            }

            Assert.AreEqual(expectedLookup.Count, lookup.CountAsync().AwaitSynchronously());
            Assert.IsTrue(expectedLookup.Select(x => x.Key).SequenceEqual(lookup.Select(x => x.Key).AsEnumerable()));

            var emptySeq = lookup.GetAsync(-1).AwaitSynchronously();
            Assert.IsNotNull(emptySeq);
            Assert.AreEqual(0L, emptySeq.CountAsync().AwaitSynchronously());
            Assert.IsFalse(lookup.ContainsAsync(-1).AwaitSynchronously());
        }

        [TestMethod]
        public void TestReverse()
        {
            int n = 100;
            Func<int, int> keySelector = x => x / 10;

            var src = Enumerable.Range(0, n);
            var expectedLookup = src.ToLookup(keySelector);
            var lookup = AsyncLookup<int, int>.ToLookup(src.AsAsyncEnumerable(), keySelector, null);

            foreach (var expected in expectedLookup.Reverse())
            {
                var actual = lookup.GetAsync(expected.Key).AwaitSynchronously();
                Assert.IsNotNull(actual);
                Assert.IsTrue(expected.SequenceEqual(actual.AsEnumerable()));
                Assert.IsTrue(lookup.ContainsAsync(expected.Key).AwaitSynchronously());
            }
        }

        [TestMethod]
        public void TestConcurrentDuringContains()
        {
            var src = TestUtils.EnumerateNothingForever<int>();
            var lookup = AsyncLookup<int, int>.ToLookup(src, x => x / 10, null);

            var t = lookup.ContainsAsync(0);
            Assert.IsFalse(t.IsCompleted);

            try
            {
                lookup.GetAsync(1).AwaitSynchronously();
                Assert.Fail();
            }
            catch (InvalidOperationException)
            { }

            try
            {
                lookup.ContainsAsync(1).AwaitSynchronously();
                Assert.Fail();
            }
            catch (InvalidOperationException)
            { }

        }

        [TestMethod]
        public void TestConcurrentDuringGet()
        {
            var src = TestUtils.EnumerateNothingForever<int>();
            var lookup = AsyncLookup<int, int>.ToLookup(src, x => x / 10, null);

            var t = lookup.GetAsync(0);
            Assert.IsFalse(t.IsCompleted);

            try
            {
                lookup.GetAsync(1).AwaitSynchronously();
                Assert.Fail();
            }
            catch (InvalidOperationException)
            { }

            try
            {
                lookup.ContainsAsync(1).AwaitSynchronously();
                Assert.Fail();
            }
            catch (InvalidOperationException)
            { }
        }
    }
}
