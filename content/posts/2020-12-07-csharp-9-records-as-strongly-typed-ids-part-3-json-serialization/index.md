---
layout: post
title: "C# 9 records as strongly-typed ids - Part 3: JSON serialization"
date: 2020-12-07
url: /2020/12/07/csharp-9-records-as-strongly-typed-ids-part-3-json-serialization/
tags:
  - C# 9
  - records
  - strong typing
  - strongly-typed ids
  - ASP.NET Core
  - JSON
  - serialization
series:
  - Using C# 9 records as strongly-typed ids
---

In the [previous post](/2020/11/23/csharp-9-records-as-strongly-typed-ids-part-2-aspnet-core-route-and-query-parameters/) in this [series](https://thomaslevesque.com/series/using-c%23-9-records-as-strongly-typed-ids/), we noticed that the strongly-typed id was serialized to JSON in an unexpected way:

```json
{
    "id": {
        "value": 1
    },
    "name": "Apple",
    "unitPrice": 0.8
}
```

When you think about it, it's not really unexpected: the strongly-typed id is a "complex" object, not a primitive type, so it makes sense that it's serialized as an object. But it's clearly not what we want… Let's see how to fix that.

## Using System.Text.Json

In recent versions of ASP.NET Core (starting with 3.0, IIRC), the default JSON serializer is System.Text.Json, so let's cover this scenario first.

In order to serialize the strongly-typed id as its value rather than as an object, we need to write a custom `JsonConverter`:

```csharp
public class StronglyTypedIdJsonConverter<TStronglyTypedId, TValue> : JsonConverter<TStronglyTypedId>
    where TStronglyTypedId : StronglyTypedId<TValue>
    where TValue : notnull
{
    public override TStronglyTypedId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType is JsonTokenType.Null)
            return null;

        var value = JsonSerializer.Deserialize<TValue>(ref reader, options);
        var factory = StronglyTypedIdHelper.GetFactory<TValue>(typeToConvert);
        return (TStronglyTypedId)factory(value);
    }

    public override void Write(Utf8JsonWriter writer, TStronglyTypedId value, JsonSerializerOptions options)
    {
        if (value is null)
            writer.WriteNullValue();
        else
            JsonSerializer.Serialize(writer, value.Value, options);
    }
}
```
The logic is pretty simple:
- for deserialization, we read the value (which is an `int` for `ProductId`) and create an instance of the strongly-typed id (`ProductId`) with that value
- for serialization, we just write out the strongly-typed id's value

If we add that converter to the serializer configuration like this:

```csharp
services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(
            new StronglyTypedIdJsonConverter<ProductId, int>());
    });
```

We now get the expected result:

```json
{
    "id": 1,
    "name": "Apple",
    "unitPrice": 0.8
}
```

Nice! There's just one problem, though: we only added a converter for `ProductId`, but we don't want to add another converter for each type of strongly-typed id! We want one converter that applies to all strongly-typed ids…

We could *probably* rewrite the converter to be non-generic, but it would be a bit messy. Fortunately, there's an easier option: create a converter factory. Here it goes:

```csharp
public class StronglyTypedIdJsonConverterFactory : JsonConverterFactory
{
    private static readonly ConcurrentDictionary<Type, JsonConverter> Cache = new();

    public override bool CanConvert(Type typeToConvert)
    {
        return StronglyTypedIdHelper.IsStronglyTypedId(typeToConvert);
    }

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        return Cache.GetOrAdd(typeToConvert, CreateConverter);
    }

    private static JsonConverter CreateConverter(Type typeToConvert)
    {
        if (!StronglyTypedIdHelper.IsStronglyTypedId(typeToConvert, out var valueType))
            throw new InvalidOperationException($"Cannot create converter for '{typeToConvert}'");

        var type = typeof(StronglyTypedIdJsonConverter<,>).MakeGenericType(typeToConvert, valueType);
        return (JsonConverter)Activator.CreateInstance(type);
    }
}
```

Again, nothing very difficult here: we look at the type we need to convert, check that it's actually a strongly-typed id, and create an instance of the specific converter for that type. We add some caching to avoid doing the reflection work every time.

Now, instead of adding the specific converter to the serializer options, we add the factory:

```csharp
services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(
            new StronglyTypedIdJsonConverterFactory());
    });
```

And our converter will now apply to every strongly-typed id.

## Using Newtonsoft.Json (a.k.a. JSON.NET)

If your project is using Newtonsoft.Json for JSON serialization, good news: you don't have anything to do, it already works as expected! Well, almost…

When it serializes a value, Newtonsoft.Json looks for a compatible `JsonConverter`, and if it doesn't find one, looks for a `TypeConverter` associated with the value's type. If that `TypeConverter` exists and can convert the value to `string`, then it's used to serialize the value as a string. Since we defined a `TypeConverter` for our strongly-typed ids last time, Newtonsoft.Json picks it up, and we get this result:

```json
{
    "id": "1",
    "name": "Apple",
    "unitPrice": 0.8
}
```

That's almost correct… Except that the id value shouldn't be serialized as a string, but as a number. If the id value is a GUID or string rather than an int, that's fine (since these types are represented as strings in JSON anyway). If it's a int, depending on your scenario, it *might* be acceptable. If it's not, you will need to write a custom converter.

It's very similar as the converter for System.Text.Json, except that Newtonsoft.Json doesn't have the concept of a converter factory. Instead, we'll write a non-generic converter, that will create an instance of the specific converter and delegate the work to it:

```csharp
public class StronglyTypedIdNewtonsoftJsonConverter : JsonConverter
{
    private static readonly ConcurrentDictionary<Type, JsonConverter> Cache = new();

    public override bool CanConvert(Type objectType)
    {
        return StronglyTypedIdHelper.IsStronglyTypedId(objectType);
    }

    public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
    {
        var converter = GetConverter(objectType);
        return converter.ReadJson(reader, objectType, existingValue, serializer);
    }

    public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
    {
        if (value is null)
        {
            writer.WriteNull();
        }
        else
        {
            var converter = GetConverter(value.GetType());
            converter.WriteJson(writer, value, serializer);
        }
    }

    private static JsonConverter GetConverter(Type objectType)
    {
        return Cache.GetOrAdd(objectType, CreateConverter);
    }

    private static JsonConverter CreateConverter(Type objectType)
    {
        if (!StronglyTypedIdHelper.IsStronglyTypedId(objectType, out var valueType))
            throw new InvalidOperationException($"Cannot create converter for '{objectType}'");

        var type = typeof(StronglyTypedIdNewtonsoftJsonConverter<,>).MakeGenericType(objectType, valueType);
        return (JsonConverter)Activator.CreateInstance(type);
    }
}

public class StronglyTypedIdNewtonsoftJsonConverter<TStronglyTypedId, TValue> : JsonConverter<TStronglyTypedId>
    where TStronglyTypedId : StronglyTypedId<TValue>
    where TValue : notnull
{
    public override TStronglyTypedId ReadJson(JsonReader reader, Type objectType, TStronglyTypedId existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        if (reader.TokenType is JsonToken.Null)
            return null;

        var value = serializer.Deserialize<TValue>(reader);
        var factory = StronglyTypedIdHelper.GetFactory<TValue>(objectType);
        return (TStronglyTypedId)factory(value);
    }

    public override void WriteJson(JsonWriter writer, TStronglyTypedId value, JsonSerializer serializer)
    {
        if (value is null)
            writer.WriteNull();
        else
            writer.WriteValue(value.Value);
    }
}
```

We add it to the serializer settings:

```csharp
    services.AddControllers()
        .AddNewtonsoftJson(options =>
        {
            options.SerializerSettings.Converters.Add(
                new StronglyTypedIdNewtonsoftJsonConverter());
        });
```

And we're done! We now have the expected output:

```json
{
    "id": 1,
    "name": "Apple",
    "unitPrice": 0.8
}
```

## Summary

In this post, I showed how to properly serialize our strongly-typed ids to JSON.

There's still one problem, though: many ASP.NET Core applications use Entity Framework Core to access the database, and EF Core doesn't know how to handle our strongly-typed ids. In the next article, we'll see how to fix that problem.