---
layout: post
title: Better timeout handling with HttpClient
date: 2018-02-25T00:00:00.0000000
url: /2018/02/25/better-timeout-handling-with-httpclient/
tags:
  - .NET
  - C#
  - handler
  - HTTP
  - HttpClient
  - timeout
categories:
  - Uncategorized
---


## The problem

If you often use `HttpClient` to call REST APIs or to transfer files, you may have been annoyed by the way this class handles request **timeout**. There are two major issues with timeout handling in `HttpClient`:

- **The timeout is defined at the `HttpClient` level** and applies to all requests made with this `HttpClient`; it would be more convenient to be able to specify a timeout individually for each request.
- The exception thrown when the timeout is elapsed **doesn't let you determine the cause of the error**. When a timeout occurs, you'd expect to get a `TimeoutException`, right? Well, surprise, it throws a `TaskCanceledException`! So, there's no way to tell from the exception if the request was actually canceled, or if a timeout occurred.


Fortunately, thanks to `HttpClient`'s flexibility, it's quite easy to make up for this design flaw.

So we're going to implement a workaround for these two issues. Let's recap what we want:

- the ability to **specify timeout on a per-request basis**
- to **receive a `TimeoutException` rather than a `TaskCanceledException`** when a timeout occurs.


## Specifying the timeout on a per-request basis

Let's see how we can associate a timeout value to a request. The `HttpRequestMessage` class has a `Properties` property, which is a dictionary in which we can put whatever we need. We're going to use this to store the timeout for a request, and to make things easier, we'll create extension methods to access the value in a strongly-typed fashion:

```csharp
public static class HttpRequestExtensions
{
    private static string TimeoutPropertyKey = "RequestTimeout";

    public static void SetTimeout(
        this HttpRequestMessage request,
        TimeSpan? timeout)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        request.Properties[TimeoutPropertyKey] = timeout;
    }

    public static TimeSpan? GetTimeout(this HttpRequestMessage request)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        if (request.Properties.TryGetValue(
                TimeoutPropertyKey,
                out var value)
            && value is TimeSpan timeout)
            return timeout;
        return null;
    }
}
```

Nothing fancy here, the timeout is an optional value of type `TimeSpan`. We can now associate a timeout value with a request, but of course, at this point there's no code that makes use of the value...

## HTTP handler

The `HttpClient` uses a **pipeline architecture**: each request is sent through a chain of handlers (of type `HttpMessageHandler`), and the response is passed back through these handlers in reverse order. [This article](/2016/12/08/fun-with-the-httpclient-pipeline/) explains this in greater detail if you want to know more. We're going to insert our own handler into the pipeline, which will be in charge of handling timeouts.

Our handler is going to inherit `DelegatingHandler`, a type of handler designed to be chained to another handler. To implement a handler, we need to **override the `SendAsync` method**. A minimal implementation would look like this:

```csharp
class TimeoutHandler : DelegatingHandler
{
    protected async override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        return await base.SendAsync(request, cancellationToken);
    }
}
```

The call to `base.SendAsync` just passes the request to the next handler. Which means that at this point, our handler does absolutely nothing useful, but we're going to augment it gradually.

## Taking into account the timeout for a request

First, let's add a `DefaultTimeout` property to our handler; it will be used for requests that don't have their timeout explicitly set:

```csharp
public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(100);
```

The default value of 100 seconds is the same as that of `HttpClient.Timeout`.

To actually implement the timeout, we're going to get the timeout value for the request (or `DefaultTimeout` if none is defined), **create a `CancellationToken` that will be canceled after the timeout duration, and pass this `CancellationToken` to the next handler**: this way, the request will be canceled after the timout is elapsed (this is actually what `HttpClient` does internally, except that it uses the same timeout for all requests).

To create a `CancellationToken` whose cancellation we can control, **we need a `CancellationTokenSource`**, which we're going to create based on the request's timeout:

```csharp
private CancellationTokenSource GetCancellationTokenSource(
    HttpRequestMessage request,
    CancellationToken cancellationToken)
{
    var timeout = request.GetTimeout() ?? DefaultTimeout;
    if (timeout == Timeout.InfiniteTimeSpan)
    {
        // No need to create a CTS if there's no timeout
        return null;
    }
    else
    {
        var cts = CancellationTokenSource
            .CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);
        return cts;
    }
}
```

Two points of interest here:

- If the request's timeout is infinite, we don't create a `CancellationTokenSource`; it would never be canceled, so we save a useless allocation.
- If not, we create a `CancellationTokenSource` that will be canceled after the timeout is elapsed (`CancelAfter`). Note that **this CTS is *linked to the `CancellationToken` we receive as a parameter in `SendAsync`***: this way, it will be canceled either when the timeout expires, or when the `CancellationToken` parameter will itself be canceled. You can get more details on linked cancellation tokens in [this article](/2015/12/31/using-multiple-cancellation-sources-with-createlinkedtokensource/).


Finally, let's change the `SendAsync` method to use the `CancellationTokenSource` we created:

```csharp
protected async override Task<HttpResponseMessage> SendAsync(
    HttpRequestMessage request,
    CancellationToken cancellationToken)
{
    using (var cts = GetCancellationTokenSource(request, cancellationToken))
    {
        return await base.SendAsync(
            request,
            cts?.Token ?? cancellationToken);
    }
}
```

We get the CTS and pass its token to `base.SendAsync`. Note that we use `cts?.Token`, because `GetCancellationTokenSource` can return null; if that happens, we use the `cancellationToken` parameter directly.

At this point, we have a handler that lets us specify a different timeout for each request. But we still get a `TaskCanceledException` when a timeout occurs... Well, this is going to be easy to fix!

## Throwing the correct exception

All we need to do is catch the `TaskCanceledException` (or rather its base class, `OperationCanceledException`), and **check if the `cancellationToken` parameter is canceled**: if it is, the cancellation was caused by the caller, so we let it bubble up normally; if not, this means the cancellation was caused by the timeout, so we throw a `TimeoutException`. Here's the final  `SendAsync` method:

```csharp
protected async override Task<HttpResponseMessage> SendAsync(
    HttpRequestMessage request,
    CancellationToken cancellationToken)
{
    using (var cts = GetCancellationTokenSource(request, cancellationToken))
    {
        try
        {
            return await base.SendAsync(
                request,
                cts?.Token ?? cancellationToken);
        }
        catch(OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException();
        }
    }
}
```

Note that we use an [exception filter](/2015/06/21/exception-filters-in-c-6/) : this way we don't actually catch the `OperationException` when we want to let it propagate, and we avoid unnecessarily unwinding the stack.

Our handler is done, now let's see how to use it.

## Using the handler

When creating an `HttpClient`, it's possible to **specify the first handler of the pipeline**. If none is specified, an `HttpClientHandler` is used; this handler sends requests directly to the network. To use our new `TimeoutHandler`, we're going to create it, attach an `HttpClientHandler` as its next handler, and pass it to the `HttpClient`:

```csharp
var handler = new TimeoutHandler
{
    InnerHandler = new HttpClientHandler()
};

using (var client = new HttpClient(handler))
{
    client.Timeout = Timeout.InfiniteTimeSpan;
    ...
}
```

Note that we need to **disable the `HttpClient`'s timeout by setting it to an infinite value**, otherwise the default behavior will interfere with our handler.

Now let's try to send a request with a timeout of 5 seconds to a server that takes to long to respond:

```csharp
var request = new HttpRequestMessage(HttpMethod.Get, "http://foo/");
request.SetTimeout(TimeSpan.FromSeconds(5));
var response = await client.SendAsync(request);
```

If the server doesn't respond within 5 seconds, we get a `TimeoutException` instead of a `TaskCanceledException`, so things seem to be working as expected.

Let's now check that cancellation still works correctly. To do this, we pass a `CancellationToken` that will be cancelled after 2 seconds (i.e. before the timeout expires):

```csharp
var request = new HttpRequestMessage(HttpMethod.Get, "http://foo/");
request.SetTimeout(TimeSpan.FromSeconds(5));
var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
var response = await client.SendAsync(request, cts.Token);
```

This time, we receive a `TaskCanceledException`, as expected.

By implementing our own HTTP handler, we were able to solve the initial problem and have a smarter timeout handling.

The full code for this article is available [here](https://gist.github.com/thomaslevesque/b4fd8c3aa332c9582a57935d6ed3406f).

