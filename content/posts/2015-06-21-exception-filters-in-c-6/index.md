---
layout: post
title: 'Exception filters in C# 6: their biggest advantage is not what you think'
date: 2015-06-21T00:00:00.0000000
url: /2015/06/21/exception-filters-in-c-6/
tags:
  - C# 6
  - exception filters
  - stack
categories:
  - Uncategorized
---


Exception filters are one of the major new features of C# 6. They take advantage of a CLR feature that was there from the start, but wasn’t used in C# until now. They allow you to specify a condition on a catch block:

```csharp
static void Main()
{
    try
    {
        Foo.DoSomethingThatMightFail(null);
    }
    catch (MyException ex) when (ex.Code == 42)
    {
        Console.WriteLine("Error 42 occurred");
    }
}
```

As you might expect, the `catch` block will be entered if and only if `ex.Code == 42`. If the condition is not verified, the exception will bubble up the stack until it’s caught somewhere else or terminates the process.

At first glance, this feature doesn’t seem to bring anything really new. After all, it has always been possible to do this:

```csharp
static void Main()
{
    try
    {
        Foo.DoSomethingThatMightFail(null);
    }
    catch (MyException ex)
    {
        if (ex.Code == 42)
            Console.WriteLine("Error 42 occurred");
        else
            throw;
    }
}
```

Since this piece of code is equivalent to the previous one, exception filters are just syntactic sugar, aren’t they? I mean, they *are* equivalent, right?

WRONG!

### Stack unwinding

There is actually a subtle but important difference: **exception filters don’t unwind the stack**. OK, but what does that mean?

When you enter a `catch` block, the stack is unwound: this means that the stack frames for the method calls “deeper” than the current method are dropped. This implies that all information about current execution state in those stack frames is lost, making it harder to identify the root cause of the exception.

Let’s assume that `DoSomethingThatMightFail` throws a `MyException` with the code 123, and the debugger is configured to break only on uncaught exceptions.

- In the code that doesn’t use exception filters, the catch block is always entered (based on the type of the exception), and the stack is immediately unwound. Since the exception doesn’t satisfy the condition, it is rethrown. So the debugger will break on the `throw;` in the `catch` block; no information on the execution state of the `DoSomethingThatMightFail` method will be available. In other words, we won’t know what was going on in the method that threw the exception.
- In the code with exception filters, on the other hand, the filter won’t match, so the catch block won’t be entered at all, and the stack won’t be unwound. The debugger will break in the `DoSomethingThatMightFail` method, making it easy to see what was going on when the exception was thrown.


Of course, when you’re debugging directly in Visual Studio, you can configure the debugger to break as soon as an exception is thrown, whether it’s caught or not. But you don’t always have that luxury; for instance, if you’re debugging an application in production, you often have just a crash dump to work with, so the fact that the stack wasn’t unwound becomes very useful, since it lets you see what was going on in the method that threw the exception.

### Stack vs. stack trace

You may have noticed that I talked about the stack, not the stack trace. Even though it’s common to refer to “the stack” when we mean “the stack trace”, they’re not the same thing. The call stack is a piece of memory allocated to the thread, that contains information for each method call: return address, arguments, and local variables. The stack trace is just a string that contains the names of the methods currently on the call stack (and the location in those methods, if debug symbols are available). The `Exception.StackTrace` property contains the stack trace as it was when the exception was thrown, and is not affected when the stack is unwound; if you rethrow the same exception with `throw;`, it is left untouched. It is only overwritten if you rethrow the exception with `throw ex;`. The stack itself, on the other hand, is unwound when a catch block is entered, as discussed above.

### Side effects

It’s interesting to note that an exception filter can contain any expression that returns a `bool` (well, almost… you can’t use `await`, for instance). It can be an inline condition, a property, a method call, etc. Technically, there’s nothing to prevent you from causing side effects in the exception filter. In most cases, I would strongly advise against doing that, as it can cause very confusing behavior; it can become really hard to understand the order in which things are executed. However, there is a common scenario that could benefit from side effects in exception filters: logging. You could easily create a method that logs the exception and returns false so that the catch block is not entered. This would allow logging exceptions on the fly without actually catching them, hence without unwinding the stack:

```csharp
try
{
    DoSomethingThatMightFail(s);
}
catch (Exception ex) when (Log(ex, "An error occurred"))
{
    // this catch block will never be reached
}

...

static bool Log(Exception ex, string message, params object[] args)
{
    Debug.Print(message, args);
    return false;
}
```

### Conclusion

As you can see, exception filters are not just syntactic sugar. Contrary to most C# 6 features, they’re not really a “coding” feature (in that they don’t make the code significantly clearer), but rather a “debugging” feature. Correctly understood and used, they can make it much easier to diagnose problems in your code.

