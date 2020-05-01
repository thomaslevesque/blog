---
layout: post
title: Test driving C# 7 features in Visual Studio “15” Preview
date: 2016-04-16T20:58:40.0000000
url: /2016/04/16/test-driving-c-7-features-in-visual-studio-15-preview/
tags:
  - C#
  - C# 7
  - Visual Studio
categories:
  - Uncategorized
---


About two weeks ago, Microsoft released the first preview of the next version of Visual Studio. You can read about what’s new in the [release notes](https://www.visualstudio.com/en-us/news/vs15-preview-vs.aspx#CsharpAndVB). Some of the new features are really nice (for instance I love the new “lightweight installer”), but the most interesting for me is that it comes with a version of the compiler that includes a few of the features planned for C# 7. Let’s have a closer look at them!

### Enabling the new features

The new features are not enabled by default. You can enable them individually with `/feature:` command line switches, but the easiest way is to enable them all by adding `__DEMO__` and `__DEMO_EXPERIMENTAL__` to the conditional compilation symbols (in *Project properties*, *Build* tab).

### Local functions

Most functional languages allow you to declare functions in the body of other functions. It’s now possible to do the same in C# 7! The syntax for declaring a method inside another is pretty much what you would expect:

```
long Factorial(int n)
{
    long Fact(int i, long acc)
    {
        return i == 0 ? acc : Fact(i - 1, acc * i);
    }
    return Fact(n, 1);
}
```

Here, the `Fact` method is local to the `Factorial` method (in case you’re wondering, it’s a tail-recursive implementation of the factorial — which doesn’t make much sense, since C# doesn’t support tail recursion, but it’s just an example).

Of course, it was already possible to simulate local functions with lambda expressions, but there were a few drawbacks:

- it’s less readable, because you have to declare the delegate type explicitly
- it’s slower, due to the overhead of creating a delegate instance, and calling the delegate vs. calling the method directly
- writing recursive lambdas is a bit awkward


Local functions have the following benefits:

- when a method is only used as a helper for another method, making it local makes the relation more obvious
- like lambdas, local functions can capture local variables and parameters of their containing method
- local functions support recursion like any normal method


You can read more about this feature [in the Roslyn Github repository](https://github.com/dotnet/roslyn/issues/2930).

### Ref returns and ref locals

Since the first version of C#, it has always been possible to pass parameters by reference, which is conceptually similar to passing a pointer to a variable in languages like C. Until now, this feature was limited to parameters, but in C# 7 it becomes possible to return values by reference, or to have local variables that refer to the location of another variable. Here’s an example:

```
static void TestRefReturn()
{
    var foo = new Foo();
    Console.WriteLine(foo); // 0, 0
    
    foo.GetByRef("x") = 42;

    ref int y = ref foo.GetByRef("y");
    y = 99;

    Console.WriteLine(foo); // 42, 99
}

class Foo
{
    private int x;
    private int y;

    public ref int GetByRef(string name)
    {
        if (name == "x")
            return ref x;
        if (name == "y")
            return ref y;
        throw new ArgumentException(nameof(name));
    }

    public override string ToString() => $"{x},{y}";
}
```

Let’s have a closer look at this code.

- On line 6, it looks like I’m assigning a value to the return of a method; what does this even mean? Well, the `GetByRef` method returns a field of the `Foo` class *by reference* (note the `ref int` return type of `GetByRef`). So, if I pass `"x"` as an argument, it returns the `x` field by reference. If I assign a value to that, it actually assigns a value to the `x` field.
- On line 8, instead of just assigning a value directly to the field returned by `GetByRef`, I use a ref local variable `y`. The local variable now shares the same memory location as the `foo.y` field. So if I assign a value to it, it changes the value of `foo.y`.


Note that you can also return an array location by reference:

```
private MyBigStruct[] array = new MyBigStruct[10];
private int current;

public ref MyBigStruct GetCurrentItem()
{
    return ref array[current];
}
```

It’s likely that most C# developers will never actually need this feature; it’s pretty low level, and not the kind of thing you typically need when writing line-of-business applications. However it’s very useful for code whose performance is critical: copying a large structure is expensive, so if we can return it by reference instead, it can be a non-negligible performance benefit.

You can learn more about this feature [on Github](https://github.com/dotnet/roslyn/issues/118).

### Pattern matching

Pattern matching is a feature very common in functional languages. C# 7 introduces some aspects of pattern matching, in the form of extensions to the `is` operator. For instance, when testing the type of a variable, it lets you introduce a new variable after the type, so that this variable is assigned with the left-hand side operand of the `is`, but with the type specified as the right-hand side operand (it will be clearer with an example).

Typically, if you need to test that a value is of type `DateTime`, then do something with that `DateTime`, you need to test the type, then cast to that type:

```
object o = GetValue();
if (o is DateTime)
{
    var d = (DateTime)o;
    // Do something with d
}
```

In C# 7, you can do this instead:

```
object o = GetValue();
if (o is DateTime d)
{
    // Do something with d
}
```

`d` is now declared directly as part of the `o is DateTime` expression.

This feature can also be used in a switch statement:

```
object v = GetValue();
switch (v)
{
    case string s:
        Console.WriteLine($"{v} is a string of length {s.Length}");
        break;
    case int i:
        Console.WriteLine($"{v} is an {(i % 2 == 0 ? "even" : "odd")} int");
        break;
    default:
        Console.WriteLine($"{v} is something else");
        break;
}
```

In this code, each case introduces a variable of the appropriate type, which you can use in the body of the case.

So far I only covered pattern matching against a simple type, but there are also more advanced forms. For instance:

```
switch (DateTime.Today)
{
    case DateTime(*, 10, 31):
        Console.WriteLine("Happy Halloween!");
        break;
    case DateTime(var year, 7, 4) when year > 1776:
        Console.WriteLine("Happy Independence Day!");
        break;
    case DateTime { DayOfWeek is DayOfWeek.Saturday }:
    case DateTime { DayOfWeek is DayOfWeek.Sunday }:
        Console.WriteLine("Have a nice week-end!");
        break;
    default:
        break;
}
```

How cool is that!

There’s also another (still experimental) form of pattern matching, using a new `match` keyword:

```
object o = GetValue();
string description = o match
    (
        case string s : $"{o} is a string of length {s.Length}"
        case int i : $"{o} is an {(i % 2 == 0 ? "even" : "odd")} int"
        case * : $"{o} is something else"
    );
```

It’s very similar to a switch, except that it’s an expression, not a statement.

There’s a lot more to pattern matching than what I mentioned here. You can look at [the spec](https://github.com/dotnet/roslyn/blob/features/patterns/docs/features/patterns.md) on Github for more details.

### Binary literals and digit separators

These features were not explicitly mentioned in the VS Preview release notes, but I noticed they were included as well. They were initially planned for C# 6, but didn’t make it in the final release. They’re back in C# 7.

You can now write numeric literal in binary, in addition to decimal an hexadecimal:

```
int x = 0b11001010;
```

Very convenient to define bit masks!

To make large numbers more readable, you can also group digits by introducing separators. This can be used for decimal, hexadecimal or binary literals:

```
int oneBillion = 1_000_000_000;
int foo = 0x7FFF_1234;
int bar = 0b1001_0110_1010_0101;
```

### Conclusion

So, with Visual Studio “15” Preview, you can start experimenting with the new C# 7 features; don’t hesitate to share your feedback on Github! And keep in mind that it’s still pre-release software, lots of things can change before the final release.

