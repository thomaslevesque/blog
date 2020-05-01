---
layout: post
title: Async unit tests with NUnit
date: 2015-02-01T21:20:08.0000000
url: /2015/02/01/async-unit-tests-with-nunit/
tags:
  - async
  - C#
  - nunit
  - unit testing
categories:
  - Unit testing
---


Recently, my team and I started writing unit tests on an application that uses a lot of async code. We used NUnit (2.6) because we were already familiar with it, but we had never tried it on async code yet.

Let’s assume the system under test is this very interesting `Calculator` class:

```

    public class Calculator
    {
        public async Task<int> AddAsync(int x, int y)
        {
            // simulate long calculation
            await Task.Delay(100).ConfigureAwait(false);
            // the answer to life, the universe and everything.
            return 42;
        }
    }
```

*(Hint: this code has a bug… 42 isn’t always the answer. This came to me as a shock!)*

And here’s a unit test for the `AddAsync` method:



```

        [Test]
        public async void AddAsync_Returns_The_Sum_Of_X_And_Y()
        {
            var calculator = new Calculator();
            int result = await calculator.AddAsync(1, 1);
            Assert.AreEqual(2, result);
        }
```
``
## `async void` vs. `async Task`

Even before trying to run this test, I thought to myself: *This isn’t gonna work! an `async void` method will return immediately on the first `await`, so NUnit will think the test is complete before the assertion is executed, and the test will always pass even if the assertion fails*. So I changed the method signature to `async Task` instead, thinking myself very clever for having avoided this trap…

```

        [Test]
        public async Task AddAsync_Returns_The_Sum_Of_X_And_Y()
```

As expected, the test failed, confirming that NUnit knew how to handle async tests. I fixed the `Calculator` class, and stopped thinking about it. Until one day, I noticed that my colleague was writing test methods with `async void`. So I started to explain to him why it couldn’t work, and tried to demonstrate it by introducing an assertion that would fail… and to my surprise, the test failed, proving that I was wrong. Mind blown!

Having an inquisitive mind, I immediately started to investigate… My first idea was to check the current `SynchronizationContext`, and indeed I saw that NUnit had changed it to an instance of `NUnit.Framework.AsyncSynchronizationContext`. This class maintains a queue of all the continuations that are posted to it. After the `async void` test method has returned (i.e., the first time a not-yet-completed task is awaited), NUnit calls the `WaitForPendingOperationsToComplete` method, which executes all the continuations in the queue, until the queue is empty. Only then is the test considered complete.

So, the moral of the story is: you *can* write `async void` unit tests in NUnit 2.6. It also works for delegates passed to `Assert.Throws`, which can have an `async` modified. Now, just because you can doesn’t mean you should. Not all test frameworks seem to have the same support for this. **The next version of NUnit (3.0, still in alpha) [will not support async void tests](https://github.com/nunit/nunit/blob/d922adae5cd30ad5544ee693f6ae6177722e3569/src/NUnitFramework/framework/Internal/AsyncInvocationRegion.cs#L76).**

So, unless you plan on staying with NUnit 2.6.4 forever, it’s probably better to always use `async Task` in your unit tests.

