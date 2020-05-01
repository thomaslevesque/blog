---
layout: post
title: Using foreach with index in C#
date: 2019-11-18T07:00:11.0000000
url: /2019/11/18/using-foreach-with-index-in-c/
tags:
  - C#
  - foreach
  - index
  - linq
  - tuple
categories:
  - Uncategorized
---


Just a quick tip today!

`for` and `foreach` loops are among the most useful constructs in a C# developer's toolbox. To iterate a collection, `foreach` is, in my opinion, more convenient than `for` in most cases. It works with all collection types, including those that are not indexable such as `IEnumerable<T>`, and doesn't require to access the current element by its index.

But sometimes, you do need the index of the current item; this usually leads to one of these patterns:

```csharp

// foreach with a "manual" index
int index = 0;
foreach (var item in collection)
{
    DoSomething(item, index);
    index++;
}

// normal for loop
for (int index = 0; index < collection.Count; index++)
{
    var item = collection[index];
    DoSomething(item, index);
}
```

It's something that has always annoyed me; couldn't we have the benefits of both `foreach` and `for`? It turns out that there's a simple solution, using Linq and tuples. Just write an extension method like this:

```csharp

using System.Linq;
...

public static IEnumerable<(T item, int index)> WithIndex<T>(this IEnumerable<T> source)
{
    return source.Select((item, index) => (item, index));
}
```

And now you can do this:

```csharp

foreach (var (item, index) in collection.WithIndex())
{
    DoSomething(item, index);
}
```

I hope you find this useful!

