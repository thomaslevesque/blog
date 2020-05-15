---
layout: post
title: "Things every C# developer should know - #1: hash codes"
date: 2020-05-15
url: /2020/05/15/things-every-csharp-developer-should-know-1-hash-codes/
tags:
  - C#
  - GetHashCode
  - hash code
  - hash table
categories:
  - Uncategorized
---

As a C# developer, there are obviously a lot of skills you need to master to be effective: language syntax, framework classes, third-party libraries, databases, regular expressions, the HTTP protocol, etc. But there are a handful of things that I consider to be really fundamental, and I often see C# developers, even experienced ones, who don't master them. So, I'm doing a series about those things! Today: **hash codes**.

## The `GetHashCode` method

OK, I realize that most developers don't need to implement their own hash table, or even implement `GetHashCode` very often, but still, it's important to know about this. Every C# class inherits (directly or indirectly) from `Object`, which exposes only 3 virtual methods. The purpose of `ToString` and `Equals` is quite obvious, but what about the third one, `GetHashCode`?

When I ask candidates about `GetHashCode` in job interviews, I typically get answers along these lines:
- "I don't know" (at least there's nothing to unlearn!)
- "It returns an id for the object"
- "It returns a unique value that I can use as a key"
- "It can be used to test if two objects are equal"

**NO!** A hash code is not an id, and it doesn't return a unique value. This is kind of obvious, when you think about it: `GetHashCode` returns an `Int32`, which has "only" about 4.2 billion possible values, and there's potentially an infinity of different objects, so some of them are bound to have the same hash code.

And no, it can't be used to test if two objects are equal. Two objects that have the same hash code are not necessarily equal, for the reason mentioned above. It works the other way, though: two objects that are equal have the same hash code (or at least they should, if `Equals` and `GetHashCode` are correctly implemented).

## Hash tables

So, if `GetHashCode` doesn't return an id and cannot be used to test equality, what is it good for?

`GetHashCode` mostly exists for one purpose: to serve as a hash function when the object is used as a key in a hash table. OK, but what *is* a hash table? Maybe the term doesn't sound familiar to you, but if you've been programming in C# for more than a few hours, you've probably used one already: the `Dictionary<TKey, TValue>` class is the most commonly used hash table implementation. `HashSet<T>` is also based on a hash table, as the name implies. If you want a complete explanation of hash tables, Wikipedia has a [pretty good article](https://en.wikipedia.org/wiki/Hash_table), but I'll try to give a brief introduction here.

A hash table is a data structure that associates a value with a key. It enables looking up the value for a given key, and does so with an average time complexity of `O(1)`; in other words, the time to find an entry by key doesn't depend on the number of entries in the hash table. The general principle is to place entries in a fixed number of "buckets", according to the *hash code* of the key.

Let's call `B` the number of buckets, and `H` the hash code of the key.

Adding an entry to a hash table looks like this (pseudo code):
```
// Calculate the hash code of the key
H = key.GetHashCode()
// Calculate the index of the bucket where the entry should be added
bucketIndex = H mod B
// Add the entry to that bucket
buckets[bucketIndex].Add(entry)
```

Note that there can be more than one entry per bucket, since there's only a fixed number of buckets. The way this is handled varies between implementations, but a common approach is to use a linked list.

Searching for an entry by key is done following this process:
```
// Calculate the hash code of the key
H = key.GetHashCode()
// Calculate the index of the bucket where the entry would be, if it exists
bucketIndex = H mod B
// Enumerate entries in the bucket to find one whose key is equal to the
// key we're looking for
entry = buckets[bucketIndex].Find(key)
```

What makes hash tables so efficient is that finding which bucket contains a given key is very fast, and doesn't depend on the number of entries. When you have the bucket, there's usually very few entries in it, so searching through them is fast as well. For instance, if you have 2000 entries and the hash table has 1000 buckets, each bucket will contain only 2 entries on average.

But for this to work, an important condition must be met: **the entries have to be evenly distributed among the buckets**. If the 2000 entries end up in the same bucket, you will find the right bucket immediately, but then you will still have to go through each of the 2000 entries to find the right one, so you gain nothing at all compared to a simple list.

## How to achieve an even distribution

To make sure the entries will be evenly distributed among the buckets, the hash code values must also be evenly distributed. This is why `GetHashCode` can't just return any random or arbitrary value, but must strive to return different values for different objects.

This is typically done by combining the values that are considered for equality, with a few prime numbers thrown in to improve the distribution. Here's an example, inspired by [Jon Skeet's answer on StackOverflow](https://stackoverflow.com/a/263416/98713):

```csharp
public class Point3D
{
    public Point3D(double x, double y, double z) => (X, Y, Z) = (x, y, z);

    public double X { get; }
    public double Y { get; }
    public double Z { get; }

    public override bool Equals(object other)
    {
        return other is Point3D p
            && p.X == X
            && p.Y == Y
            && p.Z == Z;
    }

    public override int GetHashCode()
    {
        unchecked // Allow arithmetic overflow, numbers will just "wrap around"
        {
            int hashcode = 1430287;
            hashcode = hashcode * 7302013 ^ X.GetHashCode();
            hashcode = hashcode * 7302013 ^ Y.GetHashCode();
            hashcode = hashcode * 7302013 ^ Z.GetHashCode();
            return hashcode;
        }
    }
}
```

The `Point3D` class above has 3 properties. To be considered equal, two instances of `Point3D` must have the same values for these properties. To compute the hash code, we use the same properties that are considered for equality (this is important to ensure equal objects have the same hash code). The values are then combined using XORs and multiplications with prime numbers. I'm not sure exactly why it's important to use prime numbers, but it is; if you're better at math than me, maybe you can take a guess! Of course, you can use different values than the ones shown above, just make sure they're prime.

## An easier way

The implementation shown above is fine, but it's a little tricky to get right without looking at a reference implementation. Also, you need to be careful with null values.

Fortunately, there's a very easy alternative: tuples! Using the value tuples introduced in C# 7, the `GetHashCode` implementation can be made much simpler by taking advantage of the tuple's `GetHashCode` method:

```csharp
public override bool GetHashCode() => (X, Y, Z).GetHashCode();
```

Note that it can also be used to simplify `Equals` a bit:

```csharp
public override bool Equals(object other) =>
    other is Point3D p && (p.X, p.Y, p.Z).Equals((X, Y, Z));
```

## Closing

Well, this post was a bit longer than I expected. Hopefully you learned something from it! I'll probably write a few more of these articles, depending on my inspiration. I already have a few ideas (reference types vs. value types, memory management...), but feel free to suggest other topics. I can't promise I'll cover them, but at least I'll consider them!