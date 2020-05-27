---
layout: post
title: Exciting new features in C# 9
date: 2020-05-25
url: /2020/05/25/exciting-new-features-in-csharp-9
tags:
  - C#
  - C# 9
---

Last week at Microsoft Build, there have been a *lot* of exciting annoucements! .NET 5, Blazor WebAssembly, .NET MAUI, WinUI… But the thing I'm most eager to get my hands on is C# 9, which introduces [many interesting new features](https://github.com/dotnet/roslyn/blob/master/docs/Language%20Feature%20Status.md), so let's take a quick tour! There's a long list, so I won't cover all of them here, but I will highlight the ones I find the most interesting.

*Note: Unfortunately the new C# features aren't supported yet in the latest SDK preview, so we can't test them in actual projects. Some features can be tried in [SharpLab](https://sharplab.io/), but things are moving fast, so the bits available in SharpLab don't always reflect what has been announced at Build.*

## Target typed `new`

In C# 9, it will be possible to omit the type in object creation expressions, if the type can be inferred from the context, making code terser and less repetitive:

```csharp
private Dictionary<string, object> _properties = new();
```

## Parameter null-checking

This feature introduces a simple syntax to automate null checks on method parameters. For instance, this code :

```csharp
public string SayHello(string name)
{
    if (name == null)
        throw new ArgumentNullException(nameof(name));
    return $"Hello {name}";
}
```

Can be simplified to this:

```csharp
public string SayHello(string name!) => $"Hello {name}";
```

The `!` after the parameter name automatically inserts a null check for that parameter.

## Pattern matching improvements

C# 9 comes with a few improvements to pattern matching. The most useful, in my opinion, is the `not` pattern, which lets you write code like this:

```csharp
if (foo is not null) { ... }
if (animal is not Dog) { ... }
```

Relational (`<`, `>=`, etc.) and logical operators (`and` and `or`) can also be used in pattern matching:

```csharp
return size switch
{
    < 10 => "small",
    >= 10 and < 100 => "medium",
    _ => "large"
}
```

## Records and `with` expressions

This is the big one, in my opinion. Creating simple data types in C# have always been more painful than it should be; you have to create a class, declare properties, add a constructor if you want your type to be immutable, override `Equals` and `GetHashCode`, maybe add a deconstructor, etc. C# 7 tuples made this a little easier, but still not ideal since a tuple is anonymous. The new Record feature in C# 9 makes things *much* easier!

For instance, a simple class representing a point might look like this, if you implement equality, deconstructor, etc.

```csharp
public class Point : IEquatable<Point>
{
    public Point(int x, int y) =>
        (X, Y) = (x, y);

    public int X { get; }

    public int Y { get; }

    public bool Equals(Point p) =>
        (p.X, p.Y) == (X, Y)

    public override bool Equals(object other) =>
        other is Point p && Equals(p);

    public override int GetHashCode() =>
        (X, Y).GetHashCode();

    public void Deconstruct(out int x, out int y) =>
        (x, y) = (X, Y);
}
```

In C# 9, using the Records feature, the above class can be reduced to this:

```csharp
public data class Point(int X, int Y);
```

Yup, just one line, and not even a long one! How great is that? Note that it also works with structs, if you need a value type.

Note that records are immutable: you can't change the values of their properties. So if you want to modify an instance of a record type, you need to create a new one (this should be familiar, since the same is true of dates and strings, for instance). The current approach would be to do something like this:

```csharp
Point p1 = new Point(1, 2);
Point p2 = new Point(p1.X, 3);
```

Basically, copy all properties from the original instance, except the ones you want to change. In this case, it's OK because there are only 2 properties, but it can quickly become annoying when there are many properties.

C# 9 introduces `with` expressions, which let you do this instead:

```csharp
Point p1 = new Point(1, 2);
Point p2 = p1 with { Y = 3 };
```

A `with` expression makes a clone of the original object, with the modified properties specified between curly brackets.

There are several sub-features related to records (e.g. init-only properties), that I won't cover here. Check out [Mads Torgersen's article](https://devblogs.microsoft.com/dotnet/welcome-to-c-9-0/) for a more in-depth description.

## Target-typed conditionals

There's a small thing that has been annoying C# developers for years: when using the conditional operator (also known as "ternary"), there must be a type conversion from one side to the other. For instance, this code doesn't compile:

```csharp
Stream s = inMemory ? new MemoryStream() : new FileStream(path);
```

Because there's no conversion between `MemoryStream` and `FileStream`. To fix it, one side has to be explicitly cast to `Stream`.

In C# 9, the code above will be allowed, if both sides are convertible to the target type (in this case, `Stream`).

## Covariant return

Currently, when you override a method from a base class, the overriding method **must** return the same type as the base class method. In some situations, it would be more practical to return a more specific type. C# 9 makes this possible by allowing overriding methods to return a type that derives from the base method's return type:

```csharp
public class Thing
{
    public virtual Thing Clone() => new Thing();
}

public class MoreSpecificThing : Thing
{
    // Override with a more specific return type
    public override MoreSpecificThing Clone() => new MoreSpecificThing();
}
```

## Top-level statements

This feature aims to reduce boilerplate code for simple programs. Currently, even the simplest program needs a class with a `Main` method:

```csharp
using System;

public class Program
{
    public static void Main()
    {
        Console.WriteLine("Hello world");
    }
}
```

This just adds noise, and make things confusing for beginners. C# 9 will make it possible to omit the `Program` class and `Main` method, so that the code above can be simplified to this:

```csharp
using System;
Console.WriteLine("Hello world");
```

## Conclusion

Most of the features introduced by C# 9 are relatively small ones, designed to make code simpler, less cluttered and more readable; they're very convenient, but probably won't change how we write code in a very fundamental way. Records are another story, though; they make it much easier and less painful to write immutable types, which I hope will encourage developers to take advantage of immutability whenever possible.

Note that the release of C# 9 is still a few months away, and things are still moving, so some of the features I mentioned in this post could be modified, postponed to a later version, or even abandoned completely.