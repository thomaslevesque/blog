---
layout: post
title: Using C# 9 records as strongly-typed ids
date: 2020-10-30
url: /2020/10/30/using-csharp-9-records-as-strongly-typed-ids/
tags:
  - C# 9
  - records
  - strong typing
  - strongly-typed ids
---

## Strongly-typed ids

Entities typically have integer, GUID or string ids, because those types are supported directly by databases. However, if all your entities have ids of the same type, it becomes pretty easy to mix them up, and use the id of a `Product` where the id of an `Order` was expected. This is actually a pretty common source of bugs.

```csharp
public void AddProductToOrder(int orderId, int productId, int count)
{
    ...
}

...

// Oops, the arguments are swapped!
AddProductToOrder(productId, orderId, int count);
```

The code above compiles just fine, but will probably not do the right thing at runtime…

Fortunately, there's a cure for this problem: strongly-typed ids. The idea is simple: declare a specific type for the id of each entity. Applied to the previous example, the code would now look like this:

```csharp
// Strongly-typed ids instead of int
public void AddProductToOrder(OrderId orderId, ProductId productId, int count)
{
    ...
}

...

// Oops, the arguments are swapped!
AddProductToOrder(productId, orderId, int count);
```

In the code above, we made the same mistake as in the first example (swapped `productId` and `orderId`), but in this case, the types are different, so the compiler catches the mistake and reports an error. We still need to fix it, but at least it didn't blow up in production!

## Writing a strongly-typed id

[Andrew Lock has a very complete series on his blog about strongly-typed ids](https://andrewlock.net/using-strongly-typed-entity-ids-to-avoid-primitive-obsession-part-1/), which I strongly encourage you to read. But the gist of it is something along those lines:

```csharp
public readonly struct ProductId : IEquatable<ProductId>
{
    public ProductId(int value)
    {
        Value = value;
    }
    
    public int Value { get; }

    public bool Equals(ProductId other) => other.Value == Value;
    public override bool Equals(object obj) => obj is ProductId other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();
    public override string ToString() => $"ProductId {Value}";
    public static bool operator ==(ProductId a, ProductId b) => a.Equals(b);
    public static bool operator !=(ProductId a, ProductId b) => !a.Equals(b);
}
```

Nothing difficult here, but let's be honest: it's a bit of a pain to write this for each and every entity in your model. In his series, Andrew [introduces a library](https://andrewlock.net/generating-strongly-typed-ids-at-build-time-with-roslyn-using-strongly-typed-entity-ids-to-avoid-primitive-obsession-part-5/) to automatically generate this code (and more) for you. Another option would be to use the new C# 9 source generators to achieve the same result. But in fact, C# 9 also introduces another feature that might be even better for this job…

## Record types

[Record types](https://docs.microsoft.com/en-us/dotnet/csharp/whats-new/csharp-9#record-types) are reference types with built-in immutability and value semantics. They automatically provide implementations for all the members we wrote manually in the previous code snippet (`Equals`, `GetHashCode`, etc), and offer a very concise syntax known as *positional records*. If we rewrite our `ProductId` type using records, we get this:

```csharp
public record ProductId(int Value);
```
  
Yes, you read that right, it's just one line, and a short one at that. And it does everything that our manual implementation did (quite a bit more, in fact!).

The main difference is this: our manual implementation was a `struct`, i.e. a value type, but records are reference types, which means they can be null. It might not be a major issue, especially if you use nullable reference types, but it's something to keep in mind.

Suddenly, writing a strongly-typed id for every entity in our model is no longer a daunting task; we get the benefits of strong typing almost for free. Of course there are other issues to consider, like JSON serialization, usage with Entity Framework Core, etc., but that's a story for another post!