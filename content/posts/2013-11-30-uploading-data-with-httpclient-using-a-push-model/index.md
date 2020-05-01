---
layout: post
title: Uploading data with HttpClient using a "push" model
date: 2013-11-30T21:47:09.0000000
url: /2013/11/30/uploading-data-with-httpclient-using-a-push-model/
tags:
  - HttpClient
  - HttpContent
  - JSON
  - ObjectContent
  - push
  - PushStreamContent
categories:
  - Uncategorized
---


If you have used the `HttpWebRequest` class to upload data, you know that it uses a “push” model. What I mean is that you call the `GetRequestStream` method, which opens the connection if necessary, sends the headers, and returns a stream on which you can write directly.

.NET 4.5 introduced the `HttpClient` class as a new way to communicate over HTTP. It actually relies on `HttpWebRequest` under the hood, but offers a more convenient and fully asynchronous API. `HttpClient` uses a different approach when it comes to uploading data: instead of writing manually to the request stream, you set the `Content` property of the `HttpRequestMessage` to an instance of a class derived from `HttpContent`. You can also pass the content directly to the `PostAsync` or `PutAsync` methods.

The .NET Framework provides a few built-in implementations of `HttpContent`, here are some of the most commonly used:

- `ByteArrayContent`: represents in-memory raw binary content
- `StringContent`: represents text in a specific encoding (this is a specialization of `ByteArrayContent`)
- `StreamContent`: represents raw binary content in the form of a `Stream`


For instance, here’s how you would upload the content of a file:

```
async Task UploadFileAsync(Uri uri, string filename)
{
    using (var stream = File.OpenRead(filename))
    {
        var client = new HttpClient();
        var response = await client.PostAsync(uri, new StreamContent(stream));
        response.EnsureSuccessStatusCode();
    }
}
```

As you may have noticed, nowhere in this code do we write to the request stream explicitly: the content is *pulled* from the source stream.

This “pull” model is fine most of the time, but it has a drawback: it requires that the data to upload already exists in a form that can be sent directly to the server. This is not always practical, because sometimes you want to generate the request content “on the fly”. For instance, if you want to send an object serialized as JSON, with the “pull” approach you first need to serialize it in memory as a string or `MemoryStream`, then assign that to the request’s content:

```
async Task UploadJsonObjectAsync<T>(Uri uri, T data)
{
    var client = new HttpClient();
    string json = JsonConvert.SerializeObject(data);
    var response = await client.PostAsync(uri, new StringContent(json));
    response.EnsureSuccessStatusCode();
}
```

This is fine for small objects, but obviously not optimal for large object graphs…

So, how could we reverse this pull model to a push model? Well, it’s actually pretty simple: all you have to do is to create a class that inherits `HttpContent`, and override the `SerializeToStreamAsync` method to write to the request stream directly. Actually, I intended to blog about my own implementation, but then I did some research, and it turns out that Microsoft has already done the work: the [Web API 2 Client](https://www.nuget.org/packages/Microsoft.AspNet.WebApi.Client) library provides a `PushStreamContent``` class that does exactly that. Basically, you just pass a delegate that defines what to do with the request stream. Here’s how it works:

```
async Task UploadJsonObjectAsync<T>(Uri uri, T data)
{
    var client = new HttpClient();
    var content = new PushStreamContent((stream, httpContent, transportContext) =>
    {
        var serializer = new JsonSerializer();
        using (var writer = new StreamWriter(stream))
        {
            serializer.Serialize(writer, data);
        }
    });
    var response = await client.PostAsync(uri, content);
    response.EnsureSuccessStatusCode();
}
```

Note that the `PushStreamContent` class also provides a constructor overload that accepts an asynchronous delegate, if you want to write to the stream asynchronously.

Actually, for this specific use case, the Web API 2 Client library provides a less convoluted approach: the `ObjectContent` class. You just pass it the object to send and a `MediaTypeFormatter`, and it takes care of serializing the object to the request stream:

```
async Task UploadJsonObjectAsync<T>(Uri uri, T data)
{
    var client = new HttpClient();
    var content = new ObjectContent<T>(data, new JsonMediaTypeFormatter());
    var response = await client.PostAsync(uri, content);
    response.EnsureSuccessStatusCode();
}
```

By default, the `JsonMediaTypeFormatter` class uses [Json.NET](http://james.newtonking.com/json) as its JSON serializer, but there is an option to use `DataContractJsonSerializer` instead.

Note that if you need to read an object from the response content, this is even easier: just use the [`ReadAsAsync<T>`](http://msdn.microsoft.com/en-us/library/system.net.http.httpcontentextensions.readasasync%28v=vs.118%29.aspx) extension method (also in the Web API 2 Client library). So as you can see, `HttpClient` makes it *very* easy to consume REST APIs.

