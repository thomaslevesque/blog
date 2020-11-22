---
layout: post
title: "C# 9 records as strongly-typed ids - Part 2: ASP.NET Core route and query parameters"
date: 2020-11-23
url: /2020/11/23/csharp-9-records-as-strongly-typed-ids-part-2-aspnet-core-route-and-query-parameters/
tags:
  - C# 9
  - records
  - strong typing
  - strongly-typed ids
  - ASP.NET Core
series:
  - Using C# 9 records as strongly-typed ids
---

[Last time](/2020/10/30/using-csharp-9-records-as-strongly-typed-ids/), I explained how easy it is to use C# 9 record types as strongly-typed ids:

```csharp
public record ProductId(int Value);
```

But unfortunately, we're not quite done yet: there are a few issues to fix before our strongly-typed ids are really usable. For instance, ASP.NET Core doesn't know how to handle them in route parameters or query string parameters. In this post, I'll show how to address this issue.

## Model binding of route and query string parameters

Let's say we have an entity like this:

```csharp
public record ProductId(int Value);

public class Product
{
    public ProductId Id { get; set; }
    public string Name { get; set; }
    public decimal UnitPrice { get; set; }
}
```

And an API endpoint like this:

```csharp
[ApiController]
[Route("api/[controller]")]
public class ProductController : ControllerBase
{
    ...

    [HttpGet("{id}")]
    public ActionResult<Product> GetProduct(ProductId id)
    {
        // implementation not relevant...
    }
}
```

Now let's try to call this endpoint with a `GET` request to `/api/product/1`…

```json
{
    "type": "https://tools.ietf.org/html/rfc7231#section-6.5.13",
    "title": "Unsupported Media Type",
    "status": 415,
    "traceId": "00-3600640f4e053b43b5ccefabe7eebd5a-159f5ca18d189142-00"
}
```

Oops! Not very encouraging… The problem is that ASP.NET Core doesn't know how to convert the `1` in the URL to a `ProductId` instance. Since it's not a primitive type, and doesn't have an associated type converter, ASP.NET assumes this parameter must be read from the request body. But we don't have a body, since it's a `GET` request.

## Implementing a type converter

The solution here is to implement a type converter for `ProductId`. It's easy enough:

```csharp
public class ProductIdConverter : TypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType) =>
        sourceType == typeof(string);
    public override bool CanConvertTo(ITypeDescriptorContext context, Type destinationType) =>
        destinationType == typeof(string);

    public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value)
    {
        return value switch
        {
            string s => new ProductId(int.Parse(s)),
            null => null,
            _ => throw new ArgumentException($"Cannot convert from {value} to ProductId", nameof(value))
        };
    }

    public override object ConvertTo(ITypeDescriptorContext context, CultureInfo culture, object value, Type destinationType)
    {
        if (destinationType == typeof(string))
        {
            return value switch
            {
                ProductId id => id.Value.ToString(),
                null => null,
                _ => throw new ArgumentException($"Cannot convert {value} to string", nameof(value))
            };
        }

        throw new ArgumentException($"Cannot convert {value ?? "(null)"} to {destinationType}", nameof(destinationType));
    }
}
```

(Note that for the sake of brevity, I only handled conversion to and from `string`. In a real case scenario we'd probably want to support conversion to and from `int` as well.)

We associate this converter with the `ProductId` record using the `TypeConverter` attribute:

```csharp
[TypeConverter(typeof(ProductIdConverter))]
public record ProductId(int Value);
```

Now let's try calling our API endpoint again:

```json
{
    "id": {
        "value": 1
    },
    "name": "Apple",
    "unitPrice": 0.8
}
```

It… kinda works. The fact that the id appears as an object in JSON is unfortunate, of course, but we'll address this later. Another annoying problem is the amount of code we had to write for just *one* strongly-typed id. If we need to do that for each id type, we lose all the benefit of having a concise syntax to declare them. What we need is some kind of generic converter that can handle any strongly-typed id.

## Common base type for strongly-typed ids

In order to be able to write a single converter that works for any strongly-typed id, our ids need to have something in common, like an base record or interface. An base record makes the syntax a bit clunkier because we need to pass arguments to the base type, but there are other benefits, so let's do that for now.

```csharp
public abstract record StronglyTypedId<TValue>(TValue Value)
    where TValue : notnull
{
    public override string ToString() => Value.ToString();
}
```

Note that we need to override `ToString()` to return the string representation of the value: the default record implementation would return something like `"ProductId { Value = 1 }"`, which is nice for debugging, but will cause issues down the road (e.g. in URL generation).

We can now declare our strongly-typed id like this:

```csharp
public record ProductId(int Value) : StronglyTypedId<int>(Value);
```

OK, there are a bit more keystrokes than before to declare a strongly-typed id, but it's still reasonably short, and we'll reap many benefits from having this base type.

## Generic strongly-typed id converter

Now that we have a common base type, we can write a generic converter. It's going to be a bit more involved than the one for just `ProductId`, but we'll only have to write it once.

First, let's create a helper class to
- check if a type is a strongly-typed id, and get the type of the value
- create and cache a delegate to create an instance of the strongly-typed id from a value

```csharp
public static class StronglyTypedIdHelper
{
    private static readonly ConcurrentDictionary<Type, Delegate> StronglyTypedIdFactories = new();

    public static Func<TValue, object> GetFactory<TValue>(Type stronglyTypedIdType)
        where TValue : notnull
    {
        return (Func<TValue, object>)StronglyTypedIdFactories.GetOrAdd(
            stronglyTypedIdType,
            CreateFactory<TValue>);
    }

    private static Func<TValue, object> CreateFactory<TValue>(Type stronglyTypedIdType)
        where TValue : notnull
    {
        if (!IsStronglyTypedId(stronglyTypedIdType))
            throw new ArgumentException($"Type '{stronglyTypedIdType}' is not a strongly-typed id type", nameof(stronglyTypedIdType));

        var ctor = stronglyTypedIdType.GetConstructor(new[] { typeof(TValue) });
        if (ctor is null)
            throw new ArgumentException($"Type '{stronglyTypedIdType}' doesn't have a constructor with one parameter of type '{typeof(TValue)}'", nameof(stronglyTypedIdType));

        var param = Expression.Parameter(typeof(TValue), "value");
        var body = Expression.New(ctor, param);
        var lambda = Expression.Lambda<Func<TValue, object>>(body, param);
        return lambda.Compile();
    }

    public static bool IsStronglyTypedId(Type type) => IsStronglyTypedId(type, out _);

    public static bool IsStronglyTypedId(Type type, [NotNullWhen(true)] out Type idType)
    {
        if (type is null)
            throw new ArgumentNullException(nameof(type));

        if (type.BaseType is Type baseType &&
            baseType.IsGenericType &&
            baseType.GetGenericTypeDefinition() == typeof(StronglyTypedId<>))
        {
            idType = baseType.GetGenericArguments()[0];
            return true;
        }

        idType = null;
        return false;
    }
}
```

This helper will help us write the type converter, and will also be useful for other things in the future. We can now write our generic converter, which isn't too difficult now that the hardest part is done:

```csharp
public class StronglyTypedIdConverter<TValue> : TypeConverter
    where TValue : notnull
{
    private static readonly TypeConverter IdValueConverter = GetIdValueConverter();

    private static TypeConverter GetIdValueConverter()
    {
        var converter = TypeDescriptor.GetConverter(typeof(TValue));
        if (!converter.CanConvertFrom(typeof(string)))
            throw new InvalidOperationException(
                $"Type '{typeof(TValue)}' doesn't have a converter that can convert from string");
        return converter;
    }

    private readonly Type _type;
    public StronglyTypedIdConverter(Type type)
    {
        _type = type;
    }

    public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType)
    {
        return sourceType == typeof(string)
            || sourceType == typeof(TValue)
            || base.CanConvertFrom(context, sourceType);
    }

    public override bool CanConvertTo(ITypeDescriptorContext context, Type destinationType)
    {
        return destinationType == typeof(string)
            || destinationType == typeof(TValue)
            || base.CanConvertTo(context, destinationType);
    }

    public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value)
    {
        if (value is string s)
        {
            value = IdValueConverter.ConvertFrom(s);
        }

        if (value is TValue idValue)
        {
            var factory = StronglyTypedIdHelper.GetFactory<TValue>(_type);
            return factory(idValue);
        }

        return base.ConvertFrom(context, culture, value);
    }

    public override object ConvertTo(ITypeDescriptorContext context, CultureInfo culture, object value, Type destinationType)
    {
        if (value is null)
            throw new ArgumentNullException(nameof(value));

        var stronglyTypedId = (StronglyTypedId<TValue>)value;
        TValue idValue = stronglyTypedId.Value;
        if (destinationType == typeof(string))
            return idValue.ToString()!;
        if (destinationType == typeof(TValue))
            return idValue;
        return base.ConvertTo(context, culture, value, destinationType);
    }
}
```

This converter can convert to and from string and `TValue`, which should cover our needs.

OK, this looks good, but how do we apply this converter to all strongly-typed ids? Well, we apply it to the `StronglyTypedId<TValue>` base record, of course! But… the converter is generic. If we try to set `typeof(StronglyTypedIdConverter<>)` as the converter, we'll get an error, because the converter type can't be an open generic type. So, we need a non-generic intermediate converter that will create the actual converter and delegate to it:

```csharp
public class StronglyTypedIdConverter : TypeConverter
{
    private static readonly ConcurrentDictionary<Type, TypeConverter> ActualConverters = new();

    private readonly TypeConverter _innerConverter;

    public StronglyTypedIdConverter(Type stronglyTypedIdType)
    {
        _innerConverter = ActualConverters.GetOrAdd(stronglyTypedIdType, CreateActualConverter);
    }

    public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType) =>
        _innerConverter.CanConvertFrom(context, sourceType);
    public override bool CanConvertTo(ITypeDescriptorContext context, Type destinationType) =>
        _innerConverter.CanConvertTo(context, destinationType);
    public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value) =>
        _innerConverter.ConvertFrom(context, culture, value);
    public override object ConvertTo(ITypeDescriptorContext context, CultureInfo culture, object value, Type destinationType) =>
        _innerConverter.ConvertTo(context, culture, value, destinationType);


    private static TypeConverter CreateActualConverter(Type stronglyTypedIdType)
    {
        if (!StronglyTypedIdHelper.IsStronglyTypedId(stronglyTypedIdType, out var idType))
            throw new InvalidOperationException($"The type '{stronglyTypedIdType}' is not a strongly typed id");

        var actualConverterType = typeof(StronglyTypedIdConverter<>).MakeGenericType(idType);
        return (TypeConverter)Activator.CreateInstance(actualConverterType, stronglyTypedIdType)!;
    }
}
```

We can now apply that converter to our base record type:

```csharp
[TypeConverter(typeof(StronglyTypedIdConverter))]
public abstract record StronglyTypedId<TValue>(TValue Value)
    where TValue : notnull
{
    public override string ToString() => Value.ToString();
}
```

And we can remove `ProductIdConverter`, which is no longer necessary. Model binding of route or query string parameters to strongly-typed ids now works correctly.

This article is long enough already, so let's stop there for today. Next time, we'll tackle JSON serialization!