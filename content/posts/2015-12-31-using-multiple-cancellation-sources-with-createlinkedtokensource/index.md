---
layout: post
title: Using multiple cancellation sources with CreateLinkedTokenSource
date: 2015-12-31T13:27:14.0000000
url: /2015/12/31/using-multiple-cancellation-sources-with-createlinkedtokensource/
tags:
  - async
  - C#
  - cancellation
  - CreateLinkedTokenSource
categories:
  - Uncategorized
---


Async programming in C# used to be hard; thanks to .NET 4’s Task Parallel Library and C# 5’s async/await feature, it has become fairly easy, and as a result, is becoming much more common. At the same time, [a standardized approach to cancellation](https://msdn.microsoft.com/en-us/library/dd997364%28v=vs.110%29.aspx) has been introduced : cancellation tokens. The basic idea is that you create a `CancellationTokenSource` that controls the cancellation, and pass the token it provides to the method that you want to be able to cancel. That method will then pass it to the other methods it calls, if they can be canceled, and/or regularly check if cancellation was requested. Upon cancellation, the method will typically throw an `OperationCanceledException`. Quick and dirty example:

```

private readonly IBusinessService _businessService;
private CancellationTokenSource _cancellationSource;
private Task _asyncOperation;

private void StartAsyncOperation()
{
    if (_asyncOperation != null)
        return;
    var _cancellationSource = new CancellationTokenSource();
    _asyncOperation = _businessService.DoSomethingAsync(_cancellationSource.Token);
}

// async void is bad; like I said, this is a quick and dirty example
private async void StopAsyncOperation()
{
    try
    {
        _cancellationSource.Cancel();
        // wait for the operation to finish
        await _asyncOperation;
    }
    catch (OperationCanceledException)
    {
        // Operation was successfully canceled
    }
    catch (Exception)
    {
        // Oops, something went wrong
    }
    finally
    {
        _asyncOperation = null;
        _cancellationSource.Dispose();
        _cancellationSource = null;
    }

...

class BusinessService : IBusinessService
{
    public async Task DoSomethingAsync(CancellationToken cancellationToken)
    {
        var data = await GetDataFromServerAsync(cancellationToken);
        foreach (string line in data)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ProcessLineAsync(line, cancellationToken);
        }
    }

    ...
}
```

In this case, `StopAsyncOperation` would be called, for instance, if the user chooses to abort the operation.

This all works pretty well and is rather easy to setup. But what if there is another reason to cancel the operation, known only by the `BusinessService` and outside the control of the calling method? That’s where the `CancellationSource.CreateLinkedTokenSource` method comes into play; basically, this method creates a cancellation source that will be canceled when any of the specified tokens is canceled.

Let’s start with a simple case: you have another cancellation token that you also want to take into account. The code would look like this:

```

    public async Task DoSomethingAsync(CancellationToken cancellationToken)
    {
        var otherToken = GetOtherCancellationToken();
        using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, otherToken))
        {
            var data = await GetDataFromServerAsync(linkedCts.Token);
            foreach (string line in data)
            {
                linkedCts.Token.ThrowIfCancellationRequested();
                await ProcessLineAsync(line, linkedCts.Token);
            }
        }
    }
```

We created a linked cancellation source based on the two cancellation tokens, then used the token from this new source instead of `cancellationToken`. If either `cancellationToken` or `otherToken` is canceled, `linkedCts.Token` will be canceled as well. If necessary, the calling code can detect how the operation was canceled by checking the `CancellationToken` property of the `OperationCanceledException`.

Now let’s see a slightly more difficult case: the second cancellation source is actually an event. You want to cancel the operation when the event occurs, in addition to user cancellation represented by the `cancellationToken` parameter. So you need to subscribe to the event and trigger the cancellation when it occurs. Here’s a way to do it:

```

    public async Task DoSomethingAsync(CancellationToken cancellationToken)
    {
        using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            EventHandler handler = (sender, e) => linkedCts.Cancel();
            try
            {
                SomeEvent += handler;
                var data = await GetDataFromServerAsync(linkedCts.Token);
                foreach (string line in data)
                {
                    linkedCts.Token.ThrowIfCancellationRequested();
                    await ProcessLineAsync(line, linkedCts.Token);
                }
            }
            finally
            {
                SomeEvent -= handler;
            }
        }
    }
```

Here we only pass `cancellationToken` to `CreateLinkedTokenSource`, and we directly cancel `linkedCts` when the event is raised. The code is getting a bit convoluted, but it achieves the desired result.

I can’t really give you a specific real-world use case of this technique, because the cases where I used it are too specific to be of public interest, but I can outline the general scenario. I have a long running operation that is made up of multiple long running operations. The whole operation can be canceled globally, and each of the sub-operations can also be canceled individually, without affecting the others. Here’s the rough outline of what it looks like:

```

async Task GlobalOperationAsync(CancellationToken cancellationToken)
{
    foreach (var subOperation is SubOperations)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var subToken = subOperation.GetSpecificCancellationToken();
        using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, subToken))
        {
            try
            {
                await subOperation.RunAsync(linkedCts.Token);
            }
            catch (OperationCanceledException ex)
            {
                // Rethrow only if global cancellation was requested
                if (cancellationToken.IsCancellationRequested)
                    throw;
                    
                // otherwise continue running the other sub-operations
            }
        }
    }
}
```

Note that even though `CancellationToken` was introduced with the TPL and all the examples I gave were asynchronous, nothing prevents you from using this technique with synchronous code.

I hope you find this helpful. Have a great New Year’s Eve celebration and a happy new year!

