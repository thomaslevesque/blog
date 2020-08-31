---
layout: post
title: "Inject a service into a System.Text.Json converter"
date: 2020-08-31
url: /2020/08/31/inject-service-into-system-text-json-converter
tags:
  - ASP.NET Core
  - JSON
  - System.Text.JSON
  - Dependency injection
---

Most JSON converters are fairly simple, and typically self-contained. But once 
in a while, you need to do something a little more complex in a converter, and
you end up needing to call a service. However, there's no built-in dependency
injection in System.Text.Json converters… How can you access the service you
need?

There are basically two variants of this problem. One has a simple solution,
the other is a bit of a hack…

## Global converter

What I mean by "global converter" is a converter that applies to all instances
of the type(s) it supports. You just add it to
`JsonSerializerOptions.Converters`, and it handles the conversion of any value
of the supported type(s).

In this case, it's pretty simple: create the converter manually by passing the
service it depends on to the constructor, and add it to the
`JsonSerializerOptions`:

```csharp
var options = new JsonSerializerOptions
{
    Converters =
    {
        new FooConverter(fooService)
    }
};
```

This works fine when you're explicitly serializing something, but typically in
an ASP.NET Core app, JSON serialization is done automatically by the MVC
framework. You configure the JSON serialization with the `AddJsonOptions`,
where you don't have access to the services, because the service provider isn't
built yet. In this case, you can register a class that will configure the
options, like this:

```csharp

services.ConfigureOptions<ConfigureJsonOptions>();

...

private class ConfigureJsonOptions : IConfigureOptions<JsonOptions>
{
    private readonly IFooService _fooService;

    public ConfigureJsonOptions(IFooService fooService)
    {
        _fooService = fooService;
    }

    public void Configure(JsonOptions options)
    {
        options.JsonSerializerOptions.Converters
            .Add(new FooConverter(_fooService));
    }
}
```

## Case-by-case converter

What I mean by that is a converter that you apply on a case by case basis, by
adding a `JsonConverter` attribute to properties that need to use the
converter. For instance:

```csharp
[JsonConverter(typeof(FooConverter))]
public Foo Foo { get; set; }
```

In this situation, you have no control over how the converter is instantiated,
so you can't inject a service in the constructor. The situation looks hopeless…
Time to cheat!

The `Read` and `Write` methods of a `JsonConverter` have the
`JsonSerializerOptions` as a parameter. Maybe we can use this? I half expected
to find a `ServiceProvider` property or something similar on that object, but
unfortunately, there isn't one. Maybe we could hijack one of the other
properties? Most of them are primitive types or enums; `PropertyNamingPolicy`
and `Encoder` are probably not very good candidates. That leaves `Converters`:
we could add a "dummy" converter, that doesn't actually convert anything, but
exposes a `ServiceProvider`. We could then retrieve it from the options and use
it to resolve the service we need. Let's do this!

First, the dummy converter itself. We could just inject a `IServiceProvider`
into it, but it would be the root provider, so we wouldn't be able to resolve
scoped services. We could, instead, inject a `IHttpContextAccessor`, from which 
we'll be able to access the `IServiceProvider` for the current request. But
then we would only be able to resolve services in the context of handling a
HTTP request. So let's combine both approaches:

```csharp
/// <summary>
/// This isn't a real converter. It only exists as a hack to expose
/// IServiceProvider on the JsonSerializerOptions.
/// </summary>
public class ServiceProviderDummyConverter :
    JsonConverter<object>,
    IServiceProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IServiceProvider _serviceProvider;

    public ServiceProviderDummyConverter(
        IHttpContextAccessor httpContextAccessor,
        IServiceProvider serviceProvider)
    {
        _httpContextAccessor = httpContextAccessor;
        _serviceProvider = serviceProvider;
    }

    public object GetService(Type serviceType)
    {
        // Use the request services, if available, to be able to resolve
        // scoped services.
        // If there isn't a current HttpContext, just use the root service
        // provider.
        var services = _httpContextAccessor.HttpContext?.RequestServices
            ?? _serviceProvider;
        return services.GetService(serviceType);
    }

    public override bool CanConvert(Type typeToConvert) => false;

    public override object Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        throw new NotSupportedException();
    }

    public override void Write(
        Utf8JsonWriter writer,
        object value,
        JsonSerializerOptions options)
    {
        throw new NotSupportedException();
    }
}
```
**Note**: `CanConvert` always returns false, and the `Read` and `Write` methods
always throw: this converter can't actually convert anything, it only exists to
expose a service provider.


Then, let's add this converter to the JSON options, using `IConfigureOptions`.
Note that we also need to register the `IHttpContextAccessor`, which isn't
registered by default:

```csharp
services.AddHttpContextAccessor();
services.ConfigureOptions<ConfigureJsonOptions>();

...

private class ConfigureJsonOptions : IConfigureOptions<JsonOptions>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IServiceProvider _serviceProvider;

    public ConfigureJsonOptions(
        IHttpContextAccessor httpContextAccessor,
        IServiceProvider serviceProvider)
    {
        _httpContextAccessor = httpContextAccessor;
        _serviceProvider = serviceProvider;
    }

    public void Configure(JsonOptions options)
    {
        options.JsonSerializerOptions.Converters.Add(
            new ServiceProviderDummyConverter(
                _httpContextAccessor,
                _serviceProvider));
    }
}
```

Finally, let's write an extension method to easily retrieve the
`IServiceProvider` from the JSON options:

```csharp
public static IServiceProvider GetServiceProvider(
    this JsonSerializerOptions options)
{
    return options.Converters.OfType<IServiceProvider>().FirstOrDefault()
        ?? throw new InvalidOperationException(
            "No service provider found in JSON converters");
}
```

We now have everything we need. In our real JSON converter, we can now retrieve
the service we need like this:

```csharp
public override object Read(
    ref Utf8JsonReader reader,
    Type typeToConvert,
    JsonSerializerOptions options)
{
    var fooService = options.GetServiceProvider()
        .GetRequiredService<IFooService>();
    // Do something with the service...
}
```

Well, that's an ugly hack! But it serves its purpose, and until there's an
official solution to the problem, I can't think of a better workaround…