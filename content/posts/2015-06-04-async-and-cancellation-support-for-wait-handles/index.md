---
layout: post
title: Async and cancellation support for wait handles
date: 2015-06-04T00:00:00.0000000
url: /2015/06/04/async-and-cancellation-support-for-wait-handles/
tags:
  - async
  - C#
  - waithandle
categories:
  - Code sample
---


The .NET framework comes with a number of low-level synchronization primitives. The most commonly used are collectively known as “wait handles”, and inherit the `WaitHandle` class: `Semaphore`, `Mutex`, `AutoResetEvent` and `ManualResetEvent`. These classes have been there since at least .NET 2.0 (1.1 for some of them), but they haven’t evolved much since they were introduced, which means they don’t support common features that were introduced later. In particular, they don’t provide support for waiting asynchronously, nor do they support cancelling the wait. Fortunately, it’s actually quite easy to add these features via extension methods.

### Cancellation

Let’s start with the easiest one: cancellation. There are cases where it would be useful to pass a `CancellationToken` to `WaitHandle.WaitOne`, but none of the overloads supports it. Note that some more recent variants of the synchronization primitives, such as `SemaphoreSlim` and `ManualResetEventSlim`, do support cancellation; however, they’re not necessarily suitable for all use cases, because they’re designed for when the wait times are expected to be very short.

A `CancellationToken` exposes a `WaitHandle`, which is signaled when cancellation is requested. We can take advantage of this to implement a cancellable wait on a wait handle:

```csharp
public static bool WaitOne(this WaitHandle handle, int millisecondsTimeout, CancellationToken cancellationToken)
{
    int n = WaitHandle.WaitAny(new[] { handle, cancellationToken.WaitHandle }, millisecondsTimeout);
    switch (n)
    {
        case WaitHandle.WaitTimeout:
            return false;
        case 0:
            return true;
        default:
            cancellationToken.ThrowIfCancellationRequested();
            return false; // never reached
    }
}
```

We use `WaitHandle.WaitAny` to wait for either the original wait handle or the cancellation token’s wait handle to be signaled. `WaitAny` returns the index of the first wait handle that was signaled, or `WaitHandle.WaitTimeout` if a timeout occurred before any of the wait handles was signaled. So we can have 3 possible outcomes:
- a timeout occurred: we return false (like the standard `WaitOne` method);
- the original wait handle is signaled first: we return true (like the standard `WaitOne` method);
- the cancellation token’s wait handle is signaled first: we throw an `OperationCancelledException`. <br><br>  For completeness, let’s add some overloads for common use cases:

```csharp
public static bool WaitOne(this WaitHandle handle, TimeSpan timeout, CancellationToken cancellationToken)
{
    return handle.WaitOne((int)timeout.TotalMilliseconds, cancellationToken);
}

public static bool WaitOne(this WaitHandle handle, CancellationToken cancellationToken)
{
    return handle.WaitOne(Timeout.Infinite, cancellationToken);
}
```

And that’s it, we now have a cancellable `WaitOne` method!

### Asynchronous wait
Now, what about asynchronous wait? That’s a bit harder. What we want here is a `WaitOneAsync` method that returns a `Task<bool>` (and since we’re at it, we might as well include cancellation support). The typical approach to create a `Task` wrapper for a non-task-based asynchronous operation is to use a `TaskCompletionSource<T>`, so that’s what we’ll do. When the wait handle is signaled, we’ll set the task’s result to true; if a timeout occurs, we’ll set it to false; and if the cancellation token is signaled, we’ll mark the task as cancelled.
I struggled a bit to find a way to execute a delegate when a wait handle is signaled, but I eventually found the `ThreadPool.RegisterWaitForSingleObject` method, which exists for this exact purpose. I’m not sure why it’s in the `ThreadPool` class; I think it would have made more sense to put it in the `WaitHandle` class, but I assume there’s a good reason.
So here’s what we’ll do:
- create a `TaskCompletionSource<bool>`;
- register a delegate to set the result to true when the wait handle is signaled, or false if a timeout occurs, using `ThreadPool.RegisterWaitForSingleObject`;
- register a delegate to mark the task as cancelled when the cancellation token is signaled, using `CancellationToken.Register`;
- unregister both delegates after the task completes <br>  Here’s the implementation:

```csharp
public static async Task<bool> WaitOneAsync(this WaitHandle handle, int millisecondsTimeout, CancellationToken cancellationToken)
{
    RegisteredWaitHandle registeredHandle = null;
    CancellationTokenRegistration tokenRegistration = default(CancellationTokenRegistration);
    try
    {
        var tcs = new TaskCompletionSource<bool>();
        registeredHandle = ThreadPool.RegisterWaitForSingleObject(
            handle,
            (state, timedOut) => ((TaskCompletionSource<bool>)state).TrySetResult(!timedOut),
            tcs,
            millisecondsTimeout,
            true);
        tokenRegistration = cancellationToken.Register(
            state => ((TaskCompletionSource<bool>)state).TrySetCanceled(),
            tcs);
        return await tcs.Task;
    }
    finally
    {
        if (registeredHandle != null)
            registeredHandle.Unregister(null);
        tokenRegistration.Dispose();
    }
}

public static Task<bool> WaitOneAsync(this WaitHandle handle, TimeSpan timeout, CancellationToken cancellationToken)
{
    return handle.WaitOneAsync((int)timeout.TotalMilliseconds, cancellationToken);
}

public static Task<bool> WaitOneAsync(this WaitHandle handle, CancellationToken cancellationToken)
{
    return handle.WaitOneAsync(Timeout.Infinite, cancellationToken);
}
```

Note that the lambda expressions could have used the `tcs` variable directly; this would make the code more readable, but it would cause a closure to be created, so as a small performance optimization, `tcs` is passed as the `state` parameter.
We can now use the `WaitOneAsync` method like this:

```csharp
var mre = new ManualResetEvent(false);
…
if (await mre.WaitOneAsync(2000, cancellationToken))
{
    …
}
```

Important note: this method will not work for a `Mutex`, because it relies on `RegisterWaitForSingleObject`, which is documented to work only on wait handles other than `Mutex`.

### Conclusion

We saw that with just a few extension methods, we made the standard synchronization primitives much more usable in typical modern code involving asynchrony and cancellation. However, I can hardly finish this post without mentioning Stephen Cleary’s [AsyncEx](https://github.com/StephenCleary/AsyncEx) library; it’s a rich toolbox which offers async-friendly versions of most standard primitives, some of which will let you achieve the same result as the code above. I encourage you to have a look at it, there’s plenty of good stuff in it.

