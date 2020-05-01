---
layout: post
title: Explicitly switch to the UI thread in an async method
date: 2015-11-11T17:00:16.0000000
url: /2015/11/11/explicitly-switch-to-the-ui-thread-in-an-async-method/
tags:
  - async
  - await
  - C#
  - synchronization context
  - UI
categories:
  - Code sample
  - Tips and tricks
---


Async code is a great way to keep your app’s UI responsive. You can start an async operation from the UI thread, `await` it without blocking the UI thread, and naturally resume on the UI thread when it’s done. This is a very powerful feature, and most of the time you don’t even need to think about it; it “just works”. However, this works only if the async operation is started from a thread that has a [synchronization context](https://msdn.microsoft.com/en-us/library/system.threading.synchronizationcontext.aspx) (such as the UI thread in Windows Forms, WPF or WinRT). If you don’t have a sync context when the async operation starts, or if resuming on the sync context is explicitly disabled with `ConfigureAwait(false)`, then the method resumes on a thread pool thread after the `await`, and there is no obvious way to get back to the UI thread.

For instance, let’s assume you want to do something like this:

```

public async void btnStart_Click(object sender, RoutedEventArgs e)
{
    lblStatus.Text = "Working...";
    
    // non blocking call
    var data = await GetDataFromRemoteServerAsync().ConfigureAwait(false);
    // blocking call, but runs on a worker thread
    DoSomeCpuBoundWorkWithTheData(data);
    
    // Oops, not on the UI thread!
    lblStatus.Text = "Done";
}
```

This method starts an async operation to retrieve some data, and doesn’t resume on the UI thread, because it has some work to do in the background. When it’s done, it tries to update the UI, but since it’s not on the UI thread, it fails. This is a well known issue, and there are several workarounds. The most obvious one is to explicitly marshall the action to the dispatcher thread:

```

Dispatcher.Invoke(new Action(() => lblStatus.Text = "Done"));
```

But it’s not very elegant and readable. A better way to do it is simply to extract the part of the method that does the actual work to another method:

```

public async void btnStart_Click(object sender, RoutedEventArgs e)
{
    lblStatus.Text = "Working...";
    await DoSomeWorkAsync();
    lblStatus.Text = "Done";
}

private async Task DoSomeWorkAsync()
{
    // non blocking call
    var data = await GetDataFromRemoteServerAsync().ConfigureAwait(false);
    // blocking call, but runs on a worker thread
    DoSomeCpuBoundWorkWithTheData(data);
}
```

It takes advantage of the normal behavior of `await`, which is to resume on the captured sync context if it was available.

However, there might be some cases where it’s not practical to split the method like this. Or perhaps you want to switch to the UI thread in a method that didn’t start on the UI thread. Of course, you can use `Dispatcher.Invoke`, but it doesn’t look very nice with all those lambda expressions. What would be nice would be to be able to write something like this:

```

await syncContext;
```

Well, it’s actually pretty simple to do: we just need to create a custom awaiter. The `async`/`await` feature is pattern-based; for something to be “awaitable”, it must:

- have a `GetAwaiter()`method (which can be an extension method), which returns an object that:
    - implements the `INotifyCompletion` interface
    - has an `IsCompleted` boolean property
    - has a `GetResult()` method that synchronously returns the result (or void if there is no result)


So, we need to create an awaiter that captures a synchronization context, and causes the awaiting method to resume on this synchronization context. Here’s how it looks like:

```

public struct SynchronizationContextAwaiter : INotifyCompletion
{
    private static readonly SendOrPostCallback _postCallback = state => ((Action)state)();

    private readonly SynchronizationContext _context;
    public SynchronizationContextAwaiter(SynchronizationContext context)
    {
        _context = context;
    }

    public bool IsCompleted => _context == SynchronizationContext.Current;

    public void OnCompleted(Action continuation) => _context.Post(_postCallback, continuation);

    public void GetResult() { }
}
```

- The constructor takes the synchronization context that the continuation needs to resume on.
- The `IsCompleted` property returns true only if we’re already on this synchronization context.
- `OnCompleted` is called only if `IsCompleted` was false. It accepts a “continuation”, i.e. that code that must be executed when the operation completes. In an async method, the continuation is just the code that is after the awaited call. Since we want that continuation to run on the UI thread, we just post it to the sync context.
- `GetResult()` doesn’t need to do anything; it will just be called as part of the continuation.


Now that we have this awaiter, we just need to create a `GetAwaiter` extension method for the synchronization context that returns this awaiter:

```

public static SynchronizationContextAwaiter GetAwaiter(this SynchronizationContext context)
{
    return new SynchronizationContextAwaiter(context);
}
```

And we’re done!

The original method can now be rewritten like this:

```

public async void btnStart_Click(object sender, RoutedEventArgs e)
{
    var syncContext = SynchronizationContext.Current;
    lblStatus.Text = "Working...";
    
    // non blocking call
    var data = await GetDataFromRemoteServerAsync().ConfigureAwait(false);
    // blocking call, but runs on a worker thread
    DoSomeCpuBoundWorkWithTheData(data);
    
    // switch back to the UI thread
    await syncContext;
    lblStatus.Text = "Done";
}
```

