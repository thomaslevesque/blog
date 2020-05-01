---
layout: post
title: Optimize ToArray and ToList by providing the number of elements
date: 2014-12-07T18:10:42.0000000
url: /2014/12/07/optimize-toarray-and-tolist-by-providing-the-number-of-elements/
tags:
  - C#
  - linq
  - optimization
  - performance
  - ToArray
  - ToList
categories:
  - Uncategorized
---


The `ToArray` and `ToList` extension methods are convenient ways to eagerly materialize an enumerable sequence (e.g. a Linq query) into an array or a list. However, there’s something that bothers me: both of these methods are very inefficient if they don’t know the number of elements in the sequence (which is almost always the case when you use them on a Linq query). Let’s focus on `ToArray` for now (`ToList` has a few differences, but the principle is mostly the same).

Basically, `ToArray` takes a sequence, and returns an array that contains all the elements from the sequence. If the sequence implements `ICollection<T>`, it uses the `Count` property to allocate an array of the right size, and copy the elements into it; here’s an example:

```
List<User> users = GetUsers();
User[] array = users.ToArray();
```

In this scenario, `ToArray` is fairly efficient. Now, let’s change that code to extract just the names from the users:

```
List<User> users = GetUsers();
string[] array = users.Select(u => u.Name).ToArray();
```

Now, the argument of `ToArray` is an `IEnumerable<User>` returned by `Select`. It doesn’t implement `ICollection<User>`, so `ToArray` doesn’t know the number of elements, so it cannot allocate an array of the appropriate size. So here’s what it does:

1. start by allocating a small array (4 elements in [the current implementation](http://referencesource.microsoft.com/#System.Core/System/Linq/Enumerable.cs,783a052330e7d48d))
2. copy elements from the source into the array until the array is full
3. if there are no more elements in the source, go to 7
4. otherwise, allocate a new array, twice as large as the previous one
5. copy the items from the old array to the new array
6. repeat from step 2
7. if the array is longer than the number of elements, trim it: allocate a new array with exactly the right size, and copy the elements from the previous array
8. return the array


If there are few elements, this is quite painless; but for a very long sequence, it’s very inefficient, because of the many allocations and copies.

What is annoying is that, in many cases, we *know* the number of elements in the source! In the example above, we only use `Select`, which doesn’t change the number of elements, so we know that it’s the same as in the original list; but `ToArray` doesn’t know, because the information was lost along the way. If only we had a way to help it by providing this information ourselves….

Well, it’s actually very easy to do: all we have to do is create a new extension method that accepts the count as a parameter. Here’s what it might look like:

```
public static TSource[] ToArray<TSource>(this IEnumerable<TSource> source, int count)
{
    if (source == null) throw new ArgumentNullException("source");
    if (count < 0) throw new ArgumentOutOfRangeException("count");
    var array = new TSource[count];
    int i = 0;
    foreach (var item in source)
    {
        array[i++] = item;
    }
    return array;
}
```

Now we can optimize our previous example like this:

```
List<User> users = GetUsers();
string[] array = users.Select(u => u.Name).ToArray(users.Count);
```

Note that if you specify a count that is less than the actual number of elements in the sequence, you will get an `IndexOutOfRangeException`; it’s your responsibility to provide the correct count to the method.

So, what do we actually gain by doing that? From my benchmarks, this improved `ToArray` is about **twice as fast** as the built-in one, for a long sequence (tested with 1,000,000 elements). This is pretty good!

Note that we can improve `ToList` in the same way, by using the `List<T>` constructor that lets us specify the initial capacity:

```
public static List<TSource> ToList<TSource>(this IEnumerable<TSource> source, int count)
{
    if (source == null) throw new ArgumentNullException("source");
    if (count < 0) throw new ArgumentOutOfRangeException("count");
    var list = new List<TSource>(count);
    foreach (var item in source)
    {
        list.Add(item);
    }
    return list;
}
```

In this case, the performance gain is not as as big as for `ToArray` (about 25% instead of 50%), probably because the list doesn’t need to be trimmed, but it’s not negligible.

Obviously, a similar optimization could be made to `ToDictionary` as well, since the `Dictionary<TKey, TValue>` class also has a constructor that lets us specify the initial capacity.

The improved `ToArray` and `ToList` methods are available in my [Linq.Extras](https://github.com/thomaslevesque/Linq.Extras) library, which also provides many useful extension methods for working on sequences and collections.

