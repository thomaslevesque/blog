---
layout: post
title: Handling multipart requests with JSON and file uploads in ASP.NET Core
date: 2018-09-04T00:00:00.0000000
url: /2018/09/04/handling-multipart-requests-with-json-and-file-uploads-in-asp-net-core/
tags:
  - asp.net core
  - C#
  - JSON
  - multipart
  - upload
categories:
  - ASP.NET Core
---


Suppose we're writing an API for a blog. Our "create post" endpoint should receive the title, body, tags and an image to display at the top of the post. This raises a question: how do we send the image? There are at least 3 options:

- Embed the image bytes as base64 in the JSON payload, e.g.

```js
{
    "title": "My first blog post",
    "body": "This is going to be the best blog EVER!!!!",
    "tags": [ "first post", "hello" ],
    "image": "iVBORw0KGgoAAAANSUhEUgAAAAUAAAAFCAYAAACNbyblAAAAHElEQVQI12P4//8/w38GIAXDIBKE0DHxgljNBAAO9TXL0Y4OHwAAAABJRU5ErkJggg=="
}
```
    This works fine, but it's probably not a very good idea to embed an arbitrarily long blob in JSON, because it could use a lot of memory if the image is very large.
- Send the JSON and image as separate requests. Easy, but what if we want the image to be mandatory? There's no guarantee that the client will send the image in a second request, so our post object will be in an invalid state.
- Send the JSON and image as a multipart request.


The last approach seems the most appropriate; unfortunately it's also the most difficult to support… There is no built-in support for this scenario in ASP.NET Core. There is *some* support for the `multipart/form-data` content type, though; for instance, we can bind a model to a multipart request body, like this:

```csharp
public class MyRequestModel
{
    [Required]
    public string Title { get; set; }
    [Required]
    public string Body { get; set; }
    [Required]
    public IFormFile Image { get; set; }
}

public IActionResult Post([FromForm] MyRequestModel request)
{
    ...
}
```

But if we do this, it means that each property maps to a different part of the request; we're completely giving up on JSON.

There's also a [`MultipartReader`](https://docs.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.webutilities.multipartreader?view=aspnetcore-2.1) class that we can use to manually decode the request, but it means we have to give up model binding and automatic model validation entirely.

## Custom model binder

Ideally, we'd like to have a request model like this:

```csharp
public class CreatePostRequestModel
{
    [Required]
    public string Title { get; set; }
    [Required]
    public string Body { get; set; }
    public string[] Tags { get; set; }
    [Required]
    public IFormFile Image { get; set; }
}
```

Where the `Title`, `Body` and `Tags` properties come from a form field containing JSON and the `Image` property comes from the uploaded file. In other words, the request would look like this:

```plain
POST /api/blog/post HTTP/1.1
Content-Type: multipart/form-data; boundary=AaB03x
 
--AaB03x
Content-Disposition: form-data; name="json"
Content-Type: application/json
 
{
    "title": "My first blog post",
    "body": "This is going to be the best blog EVER!!!!",
    "tags": [ "first post", "hello" ]
}
--AaB03x
Content-Disposition: form-data; name="image"; filename="image.jpg"
Content-Type: image/jpeg
 
(... content of the image.jpg file ...)
--AaB03x
```

Fortunately, ASP.NET Core is very flexible, and we can actually make this work, by writing a [custom model binder](https://docs.microsoft.com/en-us/aspnet/core/mvc/advanced/custom-model-binding?view=aspnetcore-2.1).

Here it is:

```csharp
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Binders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace TestMultipart.ModelBinding
{
    public class JsonWithFilesFormDataModelBinder : IModelBinder
    {
        private readonly IOptions<MvcJsonOptions> _jsonOptions;
        private readonly FormFileModelBinder _formFileModelBinder;

        public JsonWithFilesFormDataModelBinder(IOptions<MvcJsonOptions> jsonOptions, ILoggerFactory loggerFactory)
        {
            _jsonOptions = jsonOptions;
            _formFileModelBinder = new FormFileModelBinder(loggerFactory);
        }

        public async Task BindModelAsync(ModelBindingContext bindingContext)
        {
            if (bindingContext == null)
                throw new ArgumentNullException(nameof(bindingContext));

            // Retrieve the form part containing the JSON
            var valueResult = bindingContext.ValueProvider.GetValue(bindingContext.FieldName);
            if (valueResult == ValueProviderResult.None)
            {
                // The JSON was not found
                var message = bindingContext.ModelMetadata.ModelBindingMessageProvider.MissingBindRequiredValueAccessor(bindingContext.FieldName);
                bindingContext.ModelState.TryAddModelError(bindingContext.ModelName, message);
                return;
            }

            var rawValue = valueResult.FirstValue;

            // Deserialize the JSON
            var model = JsonConvert.DeserializeObject(rawValue, bindingContext.ModelType, _jsonOptions.Value.SerializerSettings);

            // Now, bind each of the IFormFile properties from the other form parts
            foreach (var property in bindingContext.ModelMetadata.Properties)
            {
                if (property.ModelType != typeof(IFormFile))
                    continue;

                var fieldName = property.BinderModelName ?? property.PropertyName;
                var modelName = fieldName;
                var propertyModel = property.PropertyGetter(bindingContext.Model);
                ModelBindingResult propertyResult;
                using (bindingContext.EnterNestedScope(property, fieldName, modelName, propertyModel))
                {
                    await _formFileModelBinder.BindModelAsync(bindingContext);
                    propertyResult = bindingContext.Result;
                }

                if (propertyResult.IsModelSet)
                {
                    // The IFormFile was sucessfully bound, assign it to the corresponding property of the model
                    property.PropertySetter(model, propertyResult.Model);
                }
                else if (property.IsBindingRequired)
                {
                    var message = property.ModelBindingMessageProvider.MissingBindRequiredValueAccessor(fieldName);
                    bindingContext.ModelState.TryAddModelError(modelName, message);
                }
            }

            // Set the successfully constructed model as the result of the model binding
            bindingContext.Result = ModelBindingResult.Success(model);
        }
    }
}
```

To use it, just apply this attribute to the `CreatePostRequestModel` class above:

```csharp
[ModelBinder(typeof(JsonWithFilesFormDataModelBinder), Name = "json")]
public class CreatePostRequestModel
```

This tells ASP.NET Core to use our custom model binder to bind this class. The `Name = "json"` part tells our binder from which field of the multipart request it should read the JSON (this is the `bindingContext.FieldName` in the binder code).

Now we just need to pass a `CreatePostRequestModel` to our controller action, and we're done:

```csharp
[HttpPost]
public ActionResult<Post> CreatePost(CreatePostRequestModel post)
{
    ...
}
```

This approach enables us to have a clean controller code and keep the benefits of model binding and validation. It messes up the Swagger/OpenAPI model though, but hey, you can't have everything!

