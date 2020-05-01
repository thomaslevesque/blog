---
layout: post
title: 'Pitfall: using var and async together'
date: 2016-06-21T20:43:41.0000000
url: /2016/06/21/pitfall-using-var-and-async-together/
tags:
  - async
  - bug
  - C#
  - ReSharper
  - testing
  - unit
categories:
  - Uncategorized
---


A few days ago at work, I stumbled upon a sneaky bug in our main app. The code looked innocent enough, and at first glance I couldn’t understand what was wrong… The code was similar to the following:

```csharp
public async Task<bool> BookExistsAsync(int id)
{
    var store = await GetBookStoreAsync();
    var book = store.GetBookByIdAsync(id);
    return book != null;
}

// For completeness, here are the types and methods used in BookExistsAsync:

private Task<IBookStore> GetBookStoreAsync()
{
    // actual implementation irrelevant
    // ...
}


public interface IBookStore
{
    Task<Book> GetBookByIdAsync(int id);
    // other members omitted for brevity
}

public class Book
{
    public int Id { get; set; }
    // other members omitted for brevity
}
```

The `BookExistsAsync` method always returns true. Can you see why ?

Look at this line:

```csharp
var book = store.GetBookByIdAsync(id);
```

Quick, what’s the type of `book`? If you answered `Book`, think again: it’s `Task<Book>`. The `await` is missing! And an `async` method always returns a non-null task, so `book`  is never null.

When you have an `async` method with no `await`, the compiler warns you, but in this case there is an `await` on the line above. The only thing we do with `book` is to check that it’s not `null`; since `Task<T>` is a reference type, there’s nothing suspicious in comparing it to `null`. So, the compiler sees nothing wrong; the static code analyzer (ReSharper in this case) sees nothing wrong; and of course the feeble human brain reviewing the code sees nothing wrong either… Obviously, it could easily have been detected with adequate unit test coverage, but unfortunately this method wasn’t covered.

So, how to avoid this kind of mistake? Stop using `var` and always specify types explicitly? But I *like* `var`, I use it almost everywhere! Besides, I think it’s the first time I ever found a bug caused by the use of `var`. I’m really not willing to give it up…

Ideally, I would have liked ReSharper to spot the issue; perhaps it should consider all `Task`-returning methods to be implicitly `[NotNull]`, unless specified otherwise. Until then, I don’t have a silver bullet against this issue; just pay attention when you call an async method, and write unit tests!

