---
layout: post
title: Weak events in C#, take two
date: 2015-08-16T00:00:00.0000000
url: /2015/08/16/weak-events-in-c-take-two/
tags:
  - events
  - memory leak
  - open-instance delegate
  - weak event
categories:
  - Libraries
---


A few years ago, I blogged about a [generic implementation of the weak event pattern in C#](/2010/05/17/c-a-simple-implementation-of-the-weakevent-pattern/). The goal was to mitigate the memory leaks associated with events when you forget to unsubscribe. The implementation was based on the use of weak references to the subscribers, to allow them to be garbage collected.

My initial solution was more a proof of concept than anything else, and had a major performance issue, due to the use of `DynamicInvoke` every time the event was raised. Over the years, I revisited the weak event problem several times and came up with various solutions, improving a little every time, and I now have an implementation that should be good enough for most use cases. The public API is similar to that of my first solution. Basically, instead of writing an event like this:

```
public event EventHandler MyEvent;
```

You write it like this:

```
private readonly WeakEventSource _myEventSource = new WeakEventSource();
public event EventHandler MyEvent
{
    add { _myEventSource.Subscribe(value); }
    remove { _myEventSource.Unsubscribe(value); }
}
```

From the subscriber’s point of view, this is no different from a normal event, but the subscriber will be eligible to garbage collection if it’s not referenced anywhere else.

The event publisher can raise the event like this:

```
_myEventSource.Raise(this, e);
```

There is a small limitation: the signature of the event *has* to be `EventHandler<TEventArgs>` (with any `TEventArgs` you like, of course). It can’t be something like `FooEventHandler`, or a custom delegate type. I don’t think this is a major issue, because the vast majority of events in the .NET world follow the recommended pattern `void (sender, args)`, and specific delegate types like `FooEventHandler` actually have the same signature as `EventHandler<FooEventArgs>`. I initially tried to make a solution that could work with any delegate signature, but it turned out to be too much of a challenge… for now at least ![Winking smile](wlEmoticon-winkingsmile.png).



### How does it work

This new solution is still based on weak references, but changes the way the target method is called. Rather than using `DynamicInvoke`, it creates an open-instance delegate for the method when the weak handler is subscribed. What this means is that for an event signature like `void EventHandler(object sender, EventArgs e)`,  it creates a delegate with the signature `void OpenEventHandler(object target, object sender, EventArgs e)`. The extra `target` parameter represents the instance on which the method is called. To invoke the handler, we just need to get the target from the weak reference, and if it’s still alive,  pass it to the open-instance delegate.

For better performance, this delegate is created only the first time a given handler method is encountered, and is cached for later reuse. This way, if multiple instances of an object subscribe to an event using the same handler method, the delegate is only created the first time, and is reused for subsequent subscribers.

Note that technically, the created delegate is not a “real” open-instance delegate such as those created with `Delegate.CreateDelegate`. Instead it is created using Linq expressions. The reason is that in a real open-instance delegate, the type of the first parameter must be the type that declares the method, rather than object. Since this information isn’t known statically, I have to dynamically introduce a cast.



You can find the source code on GitHub: [WeakEvent](https://github.com/thomaslevesque/WeakEvent). A NuGet package is available here: [ThomasLevesque.WeakEvent](https://www.nuget.org/packages/ThomasLevesque.WeakEvent/).

The repository also include code snippets for [Visual Studio](https://github.com/thomaslevesque/WeakEvent/blob/master/tools/Snippets/VisualStudio/wevt.snippet) and [ReSharper](https://github.com/thomaslevesque/WeakEvent/blob/master/tools/Snippets/ReSharper/wevt.DotSettings), to make it easier to write the boilerplate code for a weak event.

