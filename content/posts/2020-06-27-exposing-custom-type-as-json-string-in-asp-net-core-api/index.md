---
layout: post
title: "Exposing a custom type as a JSON string in an ASP.NET Core API"
date: 2020-06-27
url: /2020/06/27/exposing-custom-type-as-json-string-in-asp-net-core-api/
tags:
  - ASP.NET Core
  - Swagger
  - JSON
  - API
---

Sometimes your API needs to expose a non-primitive type that has a "natural" string representation. For instance, a standard representation for a duration is the [ISO 8601 format](https://en.wikipedia.org/wiki/ISO_8601#Durations), where "1 month, 2 days, 3 hours and 4 minutes" can be represented as `P1M2DT3H4M` (note that this isn't the same as a `Timespan`, which has no notion of calendar months and years). A duration could be represented in C# as a custom type, like the `Duration` structure in my [Iso8601DurationHelper](https://github.com/thomaslevesque/Iso8601DurationHelper) project. I'll use this as an example for the rest of this post.

## JSON serialization

Let's assume you want to expose this class in an ASP.NET Core API:

```csharp
public class Vacation
{
    public int Id { get; set; }
    public DateTime StartDate { get; set; }
    public Duration Duration { get; set; }
}
```

Out of the box, if you're using `System.Text.Json` as the JSON serializer (which is the default in ASP.NET Core 3.0 and later), it will be serialized like this:

```json
{
    "id": 1,
    "startDate": "2020-08-01T00:00:00",
    "duration": {
        "years": 0,
        "months": 0,
        "weeks": 3,
        "days": 0,
        "hours": 0,
        "minutes": 0,
        "seconds": 0
    }
}
```

While usable, this representation is quite verbose and not very readable... It would be nicer if the duration was serialized as the string `"P3W"`. So let's talk about how to achieve this!

### Option 1: Use JSON.NET for serialization

If the custom type has an associated `TypeConverter` that can convert to and from `System.String` (which is the case for `Iso8601DurationHelper.Duration`), JSON.NET will automatically use that converter. So, if you enable the JSON.NET serializer as shown in the [documentation](https://docs.microsoft.com/en-us/aspnet/core/migration/22-to-30?view=aspnetcore-3.1&tabs=visual-studio#newtonsoftjson-jsonnet-support), you will get the desired output:

```json
{
    "id": 1,
    "startDate": "2020-08-01T00:00:00",
    "duration": "P3W"
}
```

Well, that was easy! Except... You probably don't want to change your JSON serializer just for this. `System.Text.Json` has many limitations compared to JSON.NET, but it's also considerably faster. So you should consider carefully which one to use, and the serialization behavior in this particular case is probably not the most important criteria.

### Option 2: Add a custom JSON converter for System.Text.Json

While `System.Text.Json` doesn't have as many features as JSON.NET, it's still fairly customizable. For instance, you can define a custom converter to control how values of a given type are serialized or deserialized.

A minimal JSON converter for `Duration` looks like this:

```csharp
public class DurationJsonConverter : JsonConverter<Duration>
{
    public override Duration Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return Duration.Parse(reader.GetString());
    }

    public override void Write(Utf8JsonWriter writer, Duration value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
```

Simple enough, right? Now we just need to configure MVC to use this converter. In the `Startup` class, in the `ConfigureServices` method, locate the call to `AddControllers` (or `AddControllerWithViews`, `AddRazorPages` or `AddMvc`, depending on your setup), and append a call to `AddJsonOptions` like this:

```csharp
services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new DurationJsonConverter());
    });
```

With that done, the duration is now properly serialized to its ISO 8601 representation.

Note that this solution (writing a custom converter) can also be used JSON.NET, although the implementation will be slightly different. I won't cover the details here.

## OpenAPI (Swagger) description

If you expose Swagger documentation for your API using [Swashbuckle](https://github.com/domaindrivendev/Swashbuckle.AspNetCore), you will notice a discrepancy between how the `Duration` is actually serialized in JSON, and how it's represented in the OpenAPI schema. Swagger UI will show the following response example:

```json
{
    "id": 0,
    "startDate": "2020-06-27T14:36:43.417Z",
    "duration": {
        "years": 0,
        "months": 0,
        "weeks": 3,
        "days": 0,
        "hours": 0,
        "minutes": 0,
        "seconds": 0
    }
}
```

Looks like it ignored our custom serialization format! That's because Swashbuckle cannot know how we configured the serializer. So we have to tell it that `Duration` is serialized as a string. Fortunately, it's pretty easy! In the call to `AddSwaggerGen`, just use the `MapType` method like this:

```csharp
services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "My API", Version = "v1" });
    options.MapType(typeof(Duration), () => new OpenApiSchema
    {
        Type = "string",
        Example = new OpenApiString("P3W")
    });
});
```

Swashbuckle will now produce the proper schema for `Duration`, and the example will look like this:

```json
{
  "id": 0,
  "startDate": "2020-06-27T14:36:43.417Z",
  "duration": "P3W"
}
```

Remarks:
- We had to specify an example value in the schema, otherwise the example would have just shown `"string"` instead of an actual ISO 8601 duration.
- With the current version of Swashbuckle.AspNetCore (5.5.1 at the time of writing), if you also expose a nullable `Duration`, you will have to configure the schema for `Duration?` separately. There's an open [issue](https://github.com/domaindrivendev/Swashbuckle.AspNetCore/issues/1648) about this, hopefully it will be resolved so that it's no longer necessary to configure the nullable type separately.