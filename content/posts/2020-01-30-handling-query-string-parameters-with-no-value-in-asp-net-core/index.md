---
layout: post
title: Handling query string parameters with no value in ASP.NET Core
date: 2020-01-30T00:00:00.0000000
lastmod: 2020-05-05
url: /2020/01/30/handling-query-string-parameters-with-no-value-in-asp-net-core/
tags:
  - asp.net core
  - C#
  - model binding
  - query string
categories:
  - Uncategorized
---

Query strings are typically made of a sequence of key-value pairs, like `?foo=hello&bar=world…`. However, if you look at [RFC 3986](https://tools.ietf.org/html/rfc3986#section-3.4), you can see that query strings are very loosely specified. It mentions that


> query components are often used to carry identifying information in the form of "key=value" pairs


But it's just an observation, not a rule (RFCs usually have very specific wording for rules, with words like MUST, SHOULD, etc.). So basically, a query string can be almost anything, it's not standardized. The use of key-value pairs separated by `&` is just a convention, not a requirement.

And as it happens, it's not uncommon to see URLs with query strings like this: `?foo`, i.e. a key without a value. How it should be interpreted is entirely implementation-dependent, but in most cases, it probably means the same as `?foo=true`: the presence of the parameter is interpreted as an implicit true value.

Unfortunately, in ASP.NET Core MVC, there's no built-in support for this form of query string. If you have a controller action like this:

```csharp
[HttpGet("search")]
public IActionResult Search(
    [FromQuery] string term,
    [FromQuery] bool ignoreCase)
{
    …
}
```

The default [model binder](https://docs.microsoft.com/en-us/aspnet/core/mvc/models/model-binding?view=aspnetcore-3.1) expects the `ignoreCase` parameter to be specified with an explicit `true` or `false` value, e.g. `ignoreCase=true`. If you omit the value, it will be interpreted as empty, and the model binding will fail:

```javascript
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "traceId": "|53613c25-4767e032425dfb92.",
  "errors": {
    "ignoreCase": [
      "The value '' is invalid."
    ]
  }
}
```

It's not a very big issue, but it's annoying… So, let's see what we can do about it!

By default, a boolean parameter is bound using [`SimpleTypeModelBinder`](https://docs.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.mvc.modelbinding.binders.simpletypemodelbinder?view=aspnetcore-3.1), which is used for most primitive types. This model binder uses the `TypeConverter` of the target type to convert a string value to the target type. In this case, the converter is a `BooleanConverter`, which doesn't recognize an empty value…

So we need to create our own model binder, which will interpret the presence of a key with no value as an implicit `true`:

```csharp
class QueryBooleanModelBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        var result = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
        if (result == ValueProviderResult.None)
        {
            // Parameter is missing, interpret as false
            bindingContext.Result = ModelBindingResult.Success(false);
        }
        else
        {
            bindingContext.ModelState.SetModelValue(bindingContext.ModelName, result);
            var rawValue = result.FirstValue;
            if (string.IsNullOrEmpty(rawValue))
            {
                // Value is empty, interpret as true
                bindingContext.Result = ModelBindingResult.Success(true);
            }
            else if (bool.TryParse(rawValue, out var boolValue))
            {
                // Value is a valid boolean, use that value
                bindingContext.Result = ModelBindingResult.Success(boolValue);
            }
            else
            {
                // Value is something else, fail
                bindingContext.ModelState.TryAddModelError(
                    bindingContext.ModelName,
                    "Value must be false, true, or empty.");
            }
        }

        return Task.CompletedTask;
    }
}
```

In order to use this model binder, we also need a model binder provider:

```csharp
class QueryBooleanModelBinderProvider : IModelBinderProvider
{
    public IModelBinder GetBinder(ModelBinderProviderContext context)
    {
        if (context.Metadata.ModelType == typeof(bool) &&
            context.BindingInfo.BindingSource != null &&
            context.BindingInfo.BindingSource.CanAcceptDataFrom(BindingSource.Query))
        {
            return new QueryBooleanModelBinder();
        }

        return null;
    }
}
```

It will return our model binder if the target type is `bool` *and* the binding source is the query string. Now we just need to add this provider to the list of model binder providers:

```csharp
// In Startup.ConfigureServices
services.AddControllers(options =>
{
    options.ModelBinderProviders.Insert(
        0, new QueryBooleanModelBinderProvider());
});
```

*Note: This code is for an ASP.NET Core 3 Web API project.*

- *If your project also has views or pages, replace `AddControllers` with `AddControllersWithViews` or `AddRazorPages`, as appropriate.*
- *If you're using ASP.NET Core 2, replace `AddControllers` with `AddMvc`.*


Note that we need to insert our new model binder provider at the beginning of the list. If we add it at the end, another provider will match first, and our provider won't even be called.

And that's it: you should now be able to call your endpoint with a query string like `?term=foo&ignoreCase`, without explicitly specifying `true` as the value of `ignoreCase`.

A possible improvement to this binder would be to also accept `0` or `1` as valid values for boolean parameters. I'll leave that as an exercise to you!

