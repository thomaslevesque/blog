---
layout: post
title: Handling type hierarchies in Cosmos DB (part 2)
date: 2019-10-15T06:00:08.0000000
url: /2019/10/15/handling-type-hierarchies-in-cosmos-db-part-2/
tags:
  - C#
  - Cosmos DB
  - JSON
categories:
  - Uncategorized
---


This is the second post in a series of 2:

- [Handling type hierarchies in Cosmos DB (part 1)](/2019/10/14/handling-type-hierarchies-in-cosmos-db-part1/)
- Handling type hierarchies in Cosmos DB (part 2) (this post)


In the [previous post](/2019/10/14/handling-type-hierarchies-in-cosmos-db-part1/), I talked about the difficulty of handling type hierarchies in Cosmos DB, showed that the problem was actually with the JSON serializer, and proposed a solution using JSON.NET's `TypeNameHandling` feature. In this post, I'll show another approach based on custom converters, and how to integrate the solution with the Cosmos DB .NET SDK.

## Custom JSON converter

With JSON.NET, we can create custom converters to tell the serializer how to serialize and deserialize specific types. Let's see how to apply this feature to our problem.

First, let add an abstract `Type` property to the base class of our object model, and implement it in the concrete classes:

```csharp

public abstract class FileSystemItem
{
    [JsonProperty("id")]
    public string Id { get; set; }
    [JsonProperty("$type")]
    public abstract string Type { get; }
    public string Name { get; set; }
    public string ParentId { get; set; }
}

public class FileItem : FileSystemItem
{
    public override string Type => "fileItem";
    public long Size { get; set; }
}

public class FolderItem : FileSystemItem
{
    public override string Type => "folderItem";
    public int ChildrenCount { get; set; }
}
```

There's nothing special to do for serialization, as JSON.NET will automatically serialize the `Type` property. However, we need a converter to handle deserialization when the target type is the abstract `FileSystemItem` class. Here it is:

```csharp

class FileSystemItemJsonConverter : JsonConverter
{
    // This converter handles only deserialization, not serialization.
    public override bool CanRead => true;
    public override bool CanWrite => false;

    public override bool CanConvert(Type objectType)
    {
        // Only if the target type is the abstract base class
        return objectType == typeof(FileSystemItem);
    }

    public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
    {
        // First, just read the JSON as a JObject
        var obj = JObject.Load(reader);
        
        // Then look at the $type property:
        var typeName = obj["$type"]?.Value<string>();
        switch (typeName)
        {
            case "fileItem":
                // Deserialize as a FileItem
                return obj.ToObject<FileItem>(serializer);
            case "folderItem":
                // Deserialize as a FolderItem
                return obj.ToObject<FolderItem>(serializer);
            default:
                throw new InvalidOperationException($"Unknown type name '{typeName}'");
        }
    }

    public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
    {
        throw new NotSupportedException("This converter handles only deserialization, not serialization.");
    }
}
```

And here's how we can now use this converter:

```csharp

var settings = new JsonSerializerSettings
{
    Converters =
    {
        new FileSystemItemJsonConverter()
    }
};
string json = JsonConvert.SerializeObject(items, Formatting.Indented, settings);

...

var deserializedItems = JsonConvert.DeserializeObject<FileSystemItem[]>(json, settings);
```

And we get the same results as with the custom serialization binder, except that we have control over which types are serialized with a `$type` property.

This converter is specific to `FileSystemItem`, but of course, it's possible to make a more generic one, based on reflection.

## Integration with the Cosmos DB SDK

OK, we now have two ways of serializing and deserializing type hierarchies in JSON. In my opinion, the one based on `TypeNameHandling` is either overly verbose when using `TypeNameHandling.Objects`, or a bit risky when using `TypeNameHandling.Auto`, because it's easy to forget to specify the root type and end up with no `$type` property on the root object. So I'll stick to the solution based on a converter, at least until my feature suggestion for JSON.NET is implemented.

Now, let's see how to integrate this with the Cosmos DB .NET SDK.

If you're still using the 2.x SDK, it's trivial: just pass the `JsonSerializerSettings` with the converter to the `DocumentClient` constructor (but you should totally consider switching to 3.X, which is much nicer to work with in my opinion).

In the 3.x SDK, it requires a little more work. The default serializer is based on JSON.NET, so it *should* be easy to pass custom `JsonSerializerSettings`... but unfortunately, the class is not public, so we can't instantiate it ourselves. All we can do is specify `CosmosSerializationOptions` that are passed to it, and those options only expose a very small subset of what is possible with JSON.NET. So the alternative is to implement our own serializer, based on JSON.NET.

To do this, we must derive from the [`CosmosSerializer`](https://github.com/Azure/azure-cosmos-dotnet-v3/blob/06b961a6c181995590097f764b296403169974f8/Microsoft.Azure.Cosmos/src/Serializer/CosmosSerializer.cs) abstract class:

```plain

public abstract class CosmosSerializer
{
    public abstract T FromStream<T>(Stream stream);
    public abstract Stream ToStream<T>(T input);
}
```

`FromStream` takes a stream and reads an object of the specified type from the stream. `ToStream` takes an object, writes it to a stream and returns the stream.

***Aside**: To be honest, I don't think it's a very good abstraction... Returning a `Stream` is weird, it would be more natural to receive a stream and write to it. The way it's designed, you **have** to create a new `MemoryStream` for every object you serialize, and then the data will be copied from that stream to the document. That's hardly efficient... Also, you must dispose the stream you receive in `FromStream`, which is unusual (you're usually not responsible for disposing an object you didn't create); it also means that the SDK creates a new stream for each document to read, which is, again, inefficient. Ah, well... It's too late to fix it v3 (it would be a breaking change), but maybe in v4?*

Fortunately, we don't have to reinvent the wheel: we can just copy the code from the [default implementation](https://github.com/Azure/azure-cosmos-dotnet-v3/blob/06b961a6c181995590097f764b296403169974f8/Microsoft.Azure.Cosmos/src/Serializer/CosmosJsonDotNetSerializer.cs), and adapt it to our needs. Here it goes:

```csharp

public class NewtonsoftJsonCosmosSerializer : CosmosSerializer
{
    private static readonly Encoding DefaultEncoding = new UTF8Encoding(false, true);

    private readonly JsonSerializer _serializer;

    public NewtonsoftJsonCosmosSerializer(JsonSerializerSettings settings)
    {
        _serializer = JsonSerializer.Create(settings);
    }

    public override T FromStream<T>(Stream stream)
    {
        string text;
        using (var reader = new StreamReader(stream))
        {
            text = reader.ReadToEnd();
        }

        if (typeof(Stream).IsAssignableFrom(typeof(T)))
        {
            return (T)(object)stream;
        }

        using (var sr = new StringReader(text))
        {
            using (var jsonTextReader = new JsonTextReader(sr))
            {
                return _serializer.Deserialize<T>(jsonTextReader);
            }
        }
    }

    public override Stream ToStream<T>(T input)
    {
        var streamPayload = new MemoryStream();
        using (var streamWriter = new StreamWriter(streamPayload, encoding: DefaultEncoding, bufferSize: 1024, leaveOpen: true))
        {
            using (JsonWriter writer = new JsonTextWriter(streamWriter))
            {
                writer.Formatting = _serializer.Formatting;
                _serializer.Serialize(writer, input);
                writer.Flush();
                streamWriter.Flush();
            }
        }

        streamPayload.Position = 0;
        return streamPayload;
    }
}
```

We now have a serializer for which we can specify the `JsonSerializerSettings`. To use it, we just need to specify it when we create the `CosmosClient`:

```csharp

var serializerSettings = new JsonSerializerSettings
{
    Converters =
    {
        new FileSystemItemJsonConverter()
    }
};
var clientOptions = new CosmosClientOptions
{
    Serializer = new NewtonsoftJsonCosmosSerializer(serializerSettings)
};
var client = new CosmosClient(connectionString, clientOptions);
```

And that's it! We can now query our collection of mixed `FileItem`s and `FolderItem`s, and have them deserialized to the proper type:

```csharp

var query = container.GetItemLinqQueryable<FileSystemItem>();
var iterator = query.ToFeedIterator();
while (iterator.HasMoreResults)
{
    var items = await iterator.ReadNextAsync();
    foreach (var item in items)
    {
        var description = item switch
        {
            FileItem file =>
                $"File {file.Name} (id {file.Id}) has a size of {file.Size} bytes",
            FolderItem folder =>
                $"Folder {folder.Name} (id {folder.Id}) has {folder.ChildrenCount} children",
            _ =>
                $"Item {item.Name} (id {item.Id}) is of type {item.GetType()}... I don't know what that is."
        };
        Console.WriteLine(description);
    }
}
```

There might be better solutions out there. If you're using Entity Framework Core 3.0, which supports Cosmos DB, this scenario *seems* to be supported, but I was unable to make it work so far. In the meantime, this solution is working very well for me, and I hope it helps you too!

