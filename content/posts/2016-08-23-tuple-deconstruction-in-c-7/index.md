---
layout: post
title: Tuple deconstruction in C# 7
date: 2016-08-22T22:54:17.0000000
url: /2016/08/23/tuple-deconstruction-in-c-7/
tags:
  - C#
  - C# 7
  - deconstruction
  - tuple
  - Visual Studio 15
categories:
  - Uncategorized
---

[Last time](http://www.thomaslevesque.com/2016/07/25/tuples-in-c-7/) on this blog I talked about the new tuple feature of C# 7. In Visual Studio 15 Preview 3, the feature wasn’t quite finished; it lacked 2 important aspects:  
- emitting metadata for the names of tuple elements, so that the names are preserved across assemblies
- deconstruction of tuples into separate variables

  Well, it looks like the C# language team has been busy during the last month, because both items are now implemented in VS 15 Preview 4, which was [released today](https://blogs.msdn.microsoft.com/visualstudio/2016/08/22/visual-studio-15-preview-4/)! They’ve also written nice startup guides about [tuples](https://github.com/dotnet/roslyn/blob/master/docs/features/tuples.md) and [deconstruction](https://github.com/dotnet/roslyn/blob/master/docs/features/deconstruction.md).  It is now possible to write something like this:  
```csharp
var values = ...
var (count, sum) = Tally(values);
Console.WriteLine($"There are {count} values and their sum is {sum}");
```
  (the `Tally` method is the one from the previous post)  Note that the intermediate variable `t` from the previous post has disappeared; we now assign the `count` and `sum` variables directly from the method result, which looks much nicer IMHO. There doesn’t seem to be a way to ignore part of the tuple (i.e. not assign it to a variable), hopefully it will come later.  An interesting aspect of deconstruction is that it’s not limited to tuples; any type can be deconstructed, as long as it has a `Deconstruct` method with the appropriate `out` parameters:  
```csharp
class Point
{
    public int X { get; }
    public int Y { get; }

    public Point(int x, int y)
    {
        X = x;
        Y = y;
    }

    public void Deconstruct(out int x, out int y)
    {
        x = X;
        y = Y;
    }
}

...

var (x, y) = point;
Console.WriteLine($"Coordinates: ({x}, {y})");
```
  The `Deconstruct` method can also be an extension method, which can be useful if you want to deconstruct a type that you don’t own. The old `System.Tuple` classes, for example, can be deconstructed using extension methods like this one:  
```csharp
public static void Deconstruct<T1, T2>(this Tuple<T1, T2> tuple, out T1 item1, out T2 item2)
{
    item1 = tuple.Item1;
    item2 = tuple.Item2;
}

...

var tuple = Tuple.Create("foo", 42);
var (name, value) = tuple;
Console.WriteLine($"Name: {name}, Value = {value}");
```
  Finally, methods that return tuples are now decorated with a `[TupleElementNames]` attribute that indicates the names of the tuple members:  
```csharp
// Decompiled code
[return: TupleElementNames(new[] { "count", "sum" })]
public static ValueTuple<int, double> Tally(IEnumerable<double> values)
{
   ...
}
```
  (the attribute is emitted by the compiler, you don’t actually need to write it yourself)  This makes it possible to share the tuple member names across assemblies, and lets tools like Intellisense provide helpful information about the method.  So, the tuple feature of C# 7 seems to be mostly complete; however, keep in mind that it’s still a preview, and some things could change between now and the final release.

