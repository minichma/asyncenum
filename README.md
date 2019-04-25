C# `async`/`await` opens up a whole new world of possibilities to C# developers but unfortunately it doesn't integrate with `yield return` (yet). This lib introduces the `IAsyncEnumerable<T>` interface along with libs and simple LINQ integration which attempt to easen the pain.

Since C# 2.0 we can use the `yield return` keywords to write iterator functions like the following:

```cs
IEnumerable<string> GetAnswers()
{
    foreach (var question in GetQuestions())
    {
        string theAnswer = answeringService.GetAnswer(question);
        yield return theAnswer;
    }
}
```

In this example we iterate over a sequence of questions and synchronously get the answer from some answering service.

With the C# 5 CTP the async/await keywords were introduced and we can now write asynchronous code like the following:

```cs
async Task<string> GetAnswerAsync(string question)
{
    return await answeringService.GetAnswerAsync(question);
}
```

Unfortunately those approaches don't compose (yet). We can write synchronous iterator functions and asynchronous scalar functions. But wouldn't it be great to easily write iterator functions that asynchronously iterate over a sequence of values, i.e. that may yield control back to the caller using the await keyword and yield items to the returned sequence by using the `yield return` keywords? 

Imagine we could write something like the following:

```cs
async IAsyncEnumerable<string> GetAsyncAnswers()
{
    foreach (var question in GetQuestions())
    {
        string theAnswer = await answeringService.GetAnswerAsync(question);
        yield return theAnswer;
    }
}
```

The code iterates over a sequence of questions and retrieves the answers from an answering service. Note that the answering service is implemented asynchronously and we use the await keyword to await the answer and yield control back to the caller in the meantime. Also note, that we return an object of type `IAsyncEnumerable<T>`, which is introduced in this project, instead of returning `IEnumerable<T>` because the latter doesn't support asynchronous iteration.

We could then imagine an asynchronous version of foreach which would take advantage of asynchronous iterators and would itselfe yield control back to the caller, once iteration started and return a Task object which would allow to await completion. The code would look like the following:

```cs
async Task SendAnswersAsync(IAsyncEnumerable<string> answers)
{
    await foreach (var answer in answers)
        await SendAnswerAsync(answer);
}
```

Unfortunately these techniques don't exist yet. I'm pretty confident that Microsoft will add similar features soon. Maybe we will see something like it as soon as on the upcoming BUILD conference 2011 this september. However, although we cannot easily extend the C# compiler, it is tempting to write a library which supports comparable techniques. Even if such would get obsolete once adequate support is built into the .NET framework and C# languate, it's at least fun to write it. ;-)

In this project we introduce the `IAsyncEnumerable<T>` and `IAsyncEnumerator<T>` types which are asynchronous counterparts to `IEnumerable<T>` and `IEnumerator<T>`. In contrast to the latter, `IAsyncEnumerator<T>` declares a method `Task<bool> MoveNextAsync()` which allows to asynchronously await a `MoveNext` operation. We also provide methods that take async delegates that allow to easily create and populate instances of `IAsyncEnumerator<T>`. With these techniques we can write code like the following which is pretty close to a comparable language-integrated approach as proposed previously:

```cs
IAsyncEnumerable<string> GetAsyncAnswers()
{
    return AsyncEnum.Enumerate<string>(async consumer =>
    {
        foreach (var question in GetQuestions())
        {
            string theAnswer = await answeringService.GetAnswer(question);
            await consumer.YieldAsync(theAnswer);
        }
    });
}
```

Instead of using the yield return keywords, we pass an async lambda to the `AsyncEnum.Enumerate` method. The lambda takes an argument of type `IAsyncConsumer<T>` which is used to populate the returned `IAsyncEnumerable<T>`. The consumer provides a method `Task YieldAsync(T)` which takes an item of `T` and returns a `Task` object. The item is returned by the `IAsyncEnumerable<T>`. The Task object can be used to await the next call to the `IAsyncEnumerable<T>`s `MoveNextAsync` method which allows easy synchronization between the producer and the consumer and provides similar semantics as the `yield return` keywords do.

For iterating over an asynchronous sequence, an extension method `AsyncEnum.ForEachAsync` is provided. It extends the `IAsyncEnumerable<T>` interface and takes a (synchronous or asynchronous) delegate that is invoked for each elemeint in the sequence. It allows to write code like the following:


```cs
async Task SendAnswersAsync(IAsyncEnumerable<string> answers)
{
    await answers.ForEachAsync(async answer =>
        await SendAnswerAsync(answer));
}
```

To go one step further, several LINQ operators have been provided, that allow seamless integration with the LINQ language features and allow to write code like the following:

```cs
async Task ProcessQuestionsAsync()
{
    IAsyncEnumerable<AnswerGroup> asyncGoodAndBadAnswers =
        from answer in GetAsyncAnswers()
        where !string.IsNullOrWhiteSpace(answer)
        group answer by answer.Contains("42") into g
        select new AnswerGroup() { IsGood = g.Key, Answers = g };

    await ProcessGoodAndBadAnswersAsync(asyncGoodAndBadAnswers);
}
```

In the previous example LINQ operators are used to filter and group items form an `IAsyncEnumerabl<T>` source sequence. Please note that the resulting object again is an `IAsyncEnumerabl<T>` which perserves the asynchrony of the source sequence. Actual evaluation is deferred until iteration is started.
