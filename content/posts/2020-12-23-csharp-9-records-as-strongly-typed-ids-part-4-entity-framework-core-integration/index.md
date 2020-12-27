---
layout: post
title: "C# 9 records as strongly-typed ids - Part 4: Entity Framework Core integration"
date: 2020-12-23
url: /2020/12/23/csharp-9-records-as-strongly-typed-ids-part-4-entity-framework-core-integration/
tags:
  - C# 9
  - records
  - strong typing
  - strongly-typed ids
  - Entity Framework Core
  - EFCore
series:
  - Using C# 9 records as strongly-typed ids
---

So far in this series, I showed [how to use C# 9 records to declare strongly-typed ids](/2020/10/30/using-csharp-9-records-as-strongly-typed-ids/) as easily as this:

```csharp
public record ProductId(int Value) : StronglyTypedId<int>(Value);
```

I also explained how to make them work correctly [with ASP.NET Core model binding](/2020/11/23/csharp-9-records-as-strongly-typed-ids-part-2-aspnet-core-route-and-query-parameters/) and [JSON serialization](/2020/10/30/using-csharp-9-records-as-strongly-typed-ids/).

Today, I'll present another piece of the puzzle: how to make Entity Framework core handle strongly-typed ids correctly.

## Value conversion for a specific strongly-typed id

Out of the box, EF Core doesn't know anything about our strongly-typed ids. It just sees a custom type with no known conversion to a database type, so it assumes that it's an entity. Which means that if we don't do anything, it will attempt to map `ProductId` to a `ProductId` table with a `Value` column. Definitely *not* what we want!

A strongly-typed id is just wrapper for a single value, so it should be mapped to a single column in the same table as its declaring entity. That column should be of a type compatible with the underlying type of the strongly-typed id, i.e. `int` in the case of `ProductId`.

The way to tell EF Core how to do that is to to configure a value converter for properties that are strongly-typed ids. The simplest way to do this is to specify expressions for converting the property to and from the database type:

```csharp
modelBuilder.Entity<Product>(builder =>
{
    ...
    builder.Property(p => p.Id)
        .HasConversion(id => id.Value, value => new ProductId(value));
    ...
});
```

You can also wrap the two expressions in a `ValueConverter<ProductId, int>` object and reuse that object for multiple properties.

Note that this has to be done for each property that is a strongly-typed id (whether it's a primary key or foreign key). There's currently no way to say "apply this conversion for all properties of that type", although it's [being considered for EF Core 6.0](https://github.com/dotnet/efcore/issues/10784).

## Applying the conversion to all strongly-typed id properties

Manually applying these conversions to each and every strongly-typed id in the model is going to get old pretty fast, right? So let's fix that!

We're going to examine each property of each entity in the EF Core model, and if it's a strongly-typed id, we'll use reflection to generate the appropriate converter, and apply it to the property. This is done by the following method, to be called from `OnModelCreating`:

```csharp
private static void AddStronglyTypedIdConversions(ModelBuilder modelBuilder)
{
    foreach (var entityType in modelBuilder.Model.GetEntityTypes())
    {
        foreach (var property in entityType.GetProperties())
        {
            if (StronglyTypedIdHelper.IsStronglyTypedId(property.ClrType, out var valueType))
            {
                var converter = StronglyTypedIdConverters.GetOrAdd(
                    property.ClrType,
                    _ => CreateStronglyTypedIdConverter(property.ClrType, valueType));
                property.SetValueConverter(converter);
            }
        }
    }
}

private static readonly ConcurrentDictionary<Type, ValueConverter> StronglyTypedIdConverters = new();

private static ValueConverter CreateStronglyTypedIdConverter(
    Type stronglyTypedIdType,
    Type valueType)
{
    // id => id.Value
    var toProviderFuncType = typeof(Func<,>)
        .MakeGenericType(stronglyTypedIdType, valueType);
    var stronglyTypedIdParam = Expression.Parameter(stronglyTypedIdType, "id");
    var toProviderExpression = Expression.Lambda(
        toProviderFuncType,
        Expression.Property(stronglyTypedIdParam, "Value"),
        stronglyTypedIdParam);

    // value => new ProductId(value)
    var fromProviderFuncType = typeof(Func<,>)
        .MakeGenericType(valueType, stronglyTypedIdType);
    var valueParam = Expression.Parameter(valueType, "value");
    var ctor = stronglyTypedIdType.GetConstructor(new[] { valueType });
    var fromProviderExpression = Expression.Lambda(
        fromProviderFuncType,
        Expression.New(ctor, valueParam),
        valueParam);

    var converterType = typeof(ValueConverter<,>)
        .MakeGenericType(stronglyTypedIdType, valueType);

    return (ValueConverter)Activator.CreateInstance(
        converterType,
        toProviderExpression,
        fromProviderExpression,
        null);
}
```

The difficult part here is the `CreateStronglyTypedIdConverter` method. It dynamically generates the `id => id.Value` and `value => new ProductId(value)` expressions, using reflection and the Linq Expression API. We use a cache to avoid redoing the same work multiple times.

If we explicitly configure our entity primary keys and relations, this works fine. However, if we just rely on the EF Core conventions, we're going to run into a problem: `entityType.GetProperties()` does *not* return the `Id` property of `Product`! Let's take a step back to understand why.

Before calling `OnModelCreating` to let the user customize the model, EF Core creates the model based on conventions. One of the conventions is that a property named `Id` is assumed to be the key for the entity. However, because `ProductId` is a "complex" type, EF Core assumes that it's an entity, rather than a scalar value. So it considers the `Id` property to be a navigation property to the `ProductId` entity. As a result, it implicitly introduces an `IdTempId` property as the foreign key, and `Id` itself doesn't appear as a property (it's a *navigation* property instead).

To fix this, we need to explicitly configure `Id` as the key for the entity, for each entity type:

```csharp
modelBuilder.Entity<Product>(builder => builder.HasKey(p => p.Id));
```

(note that it has to be done *before* the call to `AddStronglyTypedIdConversions`)

Similarly, relations between entities should be configured explicitly so that foreign keys are handled correctly.

Personally it doesn't really bother me, because I prefer to configure my entities explicitly anyway, rather than just rely on conventions. If you prefer to rely on conventions, there's probably a way to make it work without manual configuration, but I haven't found a satisfying solution yet. It should be easier when [#10784](https://github.com/dotnet/efcore/issues/10784) is resolved.

So, in the end, the `OnModelCreating` method looks like this:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    base.OnModelCreating(modelBuilder);

    // Configure each entity type
    modelBuilder.Entity<Product>(builder => builder.HasKey(p => p.Id));

    AddStronglyTypedIdConversions(modelBuilder);
}
```

At this point, everything should be working as expected. Since records automatically generate the `==` operator, you can do this:

```csharp
public Product GetProductById(ProductId id)
{
    return _dbContext.Products.SingleOrDefault(p => p.Id == id);
}
```

Note that this works in EF Core 5.0, but not necessarily with older versions, where [it could cause client-side evaluation](https://andrewlock.net/using-strongly-typed-entity-ids-to-avoid-primitive-obsession-part-3/#custom-value-converters-result-in-client-side-evaluation). Not sure when this was fixed exactly, but at least in 5.0 it works fine.

## Conclusion

In this post we've seen how to make strongly-typed ids work with Entity Framework Core.

Most pieces are in place now; there are still a few issues to resolve, which I'll cover in the next post.