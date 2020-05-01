---
layout: post
title: Tuples in C# 7
date: 2016-07-25T00:00:00.0000000
url: /2016/07/25/tuples-in-c-7/
tags:
  - C#
  - C# 7
  - tuple
  - Visual Studio
categories:
  - Uncategorized
---


A tuple is an finite ordered list of values, of possibly different types, which is used to bundle related values together without having to create a specific type to hold them.

In .NET 4.0, a set of `Tuple` classes has been introduced in the framework, which can be used as follows:

```csharp
private static Tuple<int, double> Tally(IEnumerable<double> values)
{
    int count = 0;
    double sum = 0.0;
    foreach (var value in values)
    {
        count++;
        sum += value;
    }
    return Tuple.Create(count, sum);
}

...

var values = ...
var t = Tally(values);
Console.WriteLine($"There are {t.Item1} values and their sum is {t.Item2}");
```

There are two annoying issues with the `Tuple` classes:

- They’re classes, i.e. reference types. This means they must be allocated on the heap, and garbage collected when they’re no longer used. For applications where performance is critical, it can be an issue. Also, the fact that they can be null is often not desirable.
- The elements in the tuple don’t have names, or rather, they always have the same names (`Item1,` `Item2,` etc), which are not meaningful at all. The `Tuple<T1, T2>` type conveys no information about what the tuple actually represents, which makes it a poor choice in public APIs.


In C# 7, a new feature will be introduced to improve support for tuples: you will be able to declare tuples types “inline”, a little like anonymous types, except that they’re not limited to the current method. Using this new feature, the code above becomes much cleaner:

```csharp
static (int count, double sum) Tally(IEnumerable<double> values)
{
    int count = 0;
    double sum = 0.0;
    foreach (var value in values)
    {
        count++;
        sum += value;
    }
    return (count, sum);
}

...

var values = ...
var t = Tally(values);
Console.WriteLine($"There are {t.count} values and their sum is {t.sum}");
```

Note how the return type of the `Tally` method is declared, and how the result is used. This is much better! The tuple elements now have significant names, and the syntax is nicer too. The feature relies on a new `ValueTuple<T1, T2>` structure, which means it doesn’t involve a heap allocation.

You can try this feature right now in Visual Studio 15 Preview 3. However, the  `ValueTuple<T1, T2>` type is not (yet) part of the .NET Framework; to get this example to work, you’ll need to reference the [System.ValueTuple](https://packages.nuget.org/packages/System.ValueTuple) NuGet package.

Finally, one last remark about the names of tuple members: like many other language features, they’re just syntactic sugar. In the compiled code, the tuple members are only referred to as `Item1` and `Item2`, not `count` and `sum`. The `Tally` method above actually returns a `ValueTuple<int, double>`, not a specially generated type. Note that the compiler that ships with VS 15 Preview 3 emits no metadata about the names of the tuple members. This part of the feature is not yet implemented, but should be included in the final version. This means that in the meantime, you can’t use tuples across assemblies (well, you can, but you will lose the member names and will have to use `Item1` and `Item2` to refer to the tuple members).

