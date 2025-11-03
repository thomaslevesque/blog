---
layout: post
title: "Interesting new C# 14 features coming with .NET 10"
date: 2025-10-07
url: /2025/10/07/interesting-new-csharp-14-features-coming-with-net-10/
aliases:
  - /2025/04/11/interesting-new-csharp-14-features-coming-with-net-10/
tags:
  - csharp
  - dotnet
  - features
---

With the release of .NET 10 just around the corner (next month!), it's time to take a look at the new features we can expect for C# 14.

> Note: the goal of this post is not to be an exhaustive list of all new features. I will only cover the ones that seem the most interesting to me. This doesn't mean the features I don't mention are useless, but they just have more niche use cases so they probably won't have as much impact on most developers.

## Field-backed properties

Sometimes also called "semi-auto properties", this feature introduces a new `field` keyword in property accessor bodies, that refers to an auto-generated backing field for the property.

> Note: this is not _really_ a new feature, since it was already present in C# 13 as a preview feature (you had to set the language version to `preview` to use it). In C# 14 it will no longer be in preview.

Let's do a bit of history first. In the first version of C#, property accessors had to be fully specified, with a manually declared backing field:

```csharp
private int _foo;

public int Foo
{
    get { return _foo; }
    set { _foo = value; }
}
```

Let's face it: it was pretty tedious. In 95% of cases, there was no logic besides returning or setting the field, so it was really uninteresting code. I'm pretty sure many people just exposed fields publicly to avoid having to deal with that.

With C# 3 came automatically implemented properties (sometimes referred to as "auto properties"), which made it possible to write properties with much less ceremony for the most common case (properties that just wrap a field without doing anything else). The previous code could now be written like this:

```csharp
public int Foo { get; set; }
```

This was a great improvement, but as soon as you needed to do something else in the accessors (lazily initialize the property on the first call, add validation or raise an event in the setter, etc.), you had to fall back to the manual approach, including the declaration of a backing field:

```csharp
private int _foo;

public int Foo
{
    get { return _foo; }
    set
    {
        if (value < 0) throw new ArgumentOutOfRangeException();
        _foo = value;
        OnFooChanged();
    }
}
```

The new "field-backed properties" feature coming in C# 14 lets you have the best of both worlds: use automatically implemented accessors when you don't need to do anything more than getting or setting the field, but still have the option to write your own accessor code without explicitly declaring a backing field. This is achieved by introducing a `field` keyword that refers to the automatically generated backing field. With this new feature, the previous example becomes:

```csharp
public int Foo
{
    get;
    set
    {
        if (value < 0) throw new ArgumentOutOfRangeException();
        field = value;
        OnFooChanged();
    }
}
```

The getter does nothing more than returning the field, so we can just let the compiler generate its implementation. The setter does a bit more work and needs to access the field, but it does so via the `field` keyword. No more explicit field declaration!

## Partial events and constructors

C# 13 had already added partial properties and indexers, C# 14 will complete the set of members you can declare as partial by adding events and constructors.

This is mostly useful in code generation scenarios: you declare a member with no implementation, and a source generator writes the implementation for you.

## Extension members

Since their introduction in C# 3, extension methods have become a cornerstone of the language, enabling expressive and flexible code. They basically let you add methods to a type you don't control, and use them as if they were actually instance members of that type (even though they're actually just static methods with some syntactic sugar).

However, they could only be used to add methods, not properties, and only to an instance of the extended type, not to the type itself (i.e. static). C# 14 will introduce a new, more powerful extension mechanism to address these limitations, enabling instance or static extension methods and properties. The syntax is a bit unusual, but flexible enough. It looks like this:

```csharp
public static class Extensions
{
    // Extension block for String
    extension(string s) 
    {
        // Instance extension method
        // Similar to classic extension methods, but the "target"
        // parameter is declared on the extension block itself.
        public string Capitalize() => s[..1].ToUpper() + s[1..];
    }

    // Generic extension block for IEnumerable<T>
    extension<T>(IEnumerable<T> source)
        where T : INumber<T>
    {

        // Instance extension method
        public IEnumerable<T> WhereGreaterThan(T threshold)
            => source.Where(x => x > threshold);

        // Instance extension property
        public bool IsEmpty => !source.Any();
    }

    // Static extension block that adds static members to List<T>
    extension<T>(List<T>)
    {
        // Static extension method
        public static List<T> Singleton(T value) => [value];
        // Static extension property
        public static List<T> Empty => [];
    }
}
```

All these members can be called as if they were actually members of the type they extend:

```csharp
string name = GetName().Capitalize();

IEnumerable<int> items = GetItems();
if (!items.IsEmpty)
{
  var largeItems = items.WhereGreaterThan(100);
  ...
}

var listWithSingleItem = List<int>.Singleton(42);
var emptyList = List<int>.Empty;
```

I've been waiting for something like this for many years, so I really look forward to this feature!

## Null-conditional assignment

C# 6 introduced the null-conditional operator `?.` to access a property or field on an expression only if it's not null:

```csharp
Item? item = GetItem();
string? name = item?.Name; // only accesses Name if item is not null
```

However, this only worked in "read" scenarios: you couldn't conditionally assign a member if the expression was not null. You still had to explicitly check for null before trying to assign the member:

```csharp
Item? item = GetItem();
if (item is not null)
{
   item.Name = "foo";
}
```

C# 14 will simplify this kind of scenario by allowing this:

```csharp
Item? item = GetItem();
item?.Name = "foo"; // only assigns Name if item is not null
```

It's a small thing, but it helps make code terser and cleaner.

## Conclusion

This concludes our small tour of the main new  features of C# 14. I expect field-backed properties and extension members to be the most impactful, but of course that depends on your use cases. If you want to learn about the features I didn't mention here, [here's the full list](https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/csharp-14).

You can already experiment with these features by installing the [.NET 10 release candidate](https://dotnet.microsoft.com/en-us/download/dotnet/10.0).
