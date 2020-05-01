---
layout: post
title: Handling type hierarchies in Cosmos DB (part 1)
date: 2019-10-14T00:00:00.0000000
url: /2019/10/14/handling-type-hierarchies-in-cosmos-db-part1/
tags:
  - C#
  - Cosmos DB
  - JSON
categories:
  - Uncategorized
---


This is the first post in a series of 2:

- Handling type hierarchies in Cosmos DB (part 1) (this post)
- [Handling type hierarchies in Cosmos DB (part 2)](/2019/10/15/handling-type-hierarchies-in-cosmos-db-part2/)


Azure Cosmos DB is Microsoft's NoSQL cloud database. In Cosmos DB, you store JSON documents in containers. This makes it very easy to model data, because you don't need to split complex objects into multiple tables and use joins like in relational databases. You just serialize your full C# object graph to JSON and save it to the database. The [Cosmos DB .NET SDK](https://github.com/Azure/azure-cosmos-dotnet-v3) takes care of serializing your objects, so you don't need to do it explicitly, and it lets you query the database in a strongly typed manner using Linq:

```csharp
using var client = new CosmosClient(connectionString);
var database = client.GetDatabase(databaseId);
var container = database.GetContainer("Pets");

var pet = new Pet { Id = "max-0001", Name = "Max", Species = "Dog" };
await container.CreateItemAsync(pet);

...

var dogsQuery = container.GetItemLinqQueryable<Pet>()
    .Where(p => p.Species == "Dog");

var iterator = dogsQuery.ToFeedIterator();
while (iterator.HasMoreResults)
{
    var dogs = await iterator.ReadNextAsync();
    foreach (var dog in dogs)
    {
        Console.WriteLine($"{dog.Id}\t{dog.Name}\t{dog.Species}");
    }
}
```

However, there's a little wrinkle... Out of the box, the Cosmos DB .NET SDK doesn't know how to handle type hierarchies. If you have an abstract base class with a few derived classes, and you save instances of those classes to Cosmos, the SDK won't know how to deserialize them, and you will get an exception saying it can't create an instance of an abstract type...

Actually the problem isn't in the Cosmos DB SDK per se, but in [JSON.NET](https://github.com/Azure/azure-cosmos-dotnet-v3), which is used as the default serializer by the SDK. So, before we can solve the problem for Cosmos DB, we first need to solve it for JSON.NET; we'll see later how to integrate the solution with the Cosmos DB SDK.

## A simple class hierarchy

Let's take a concrete example: a (very simple) object model to represent a file system. We have two concrete types, `FileItem` and `FolderItem`, which both inherit from a common abstract base class, `FileSystemItem`. Here's the code:

```csharp
public abstract class FileSystemItem
{
    [JsonProperty("id")]
    public string Id { get; set; }
    public string Name { get; set; }
    public string ParentId { get; set; }
}

public class FileItem : FileSystemItem
{
    public long Size { get; set; }
}

public class FolderItem : FileSystemItem
{
    public int ChildrenCount { get; set; }
}
```

In a real-world scenario, you'd probably want more properties than that, but let's keep things simple for the sake of this demonstration.

If you create a `FileItem` and a `FolderItem` and serialize them to JSON...

```csharp
var items = new FileSystemItem[]
{
    new FolderItem
    {
        Id = "1",
        Name = "foo",
        ChildrenCount = 1
    },
    new FileItem
    {
        Id = "2",
        Name = "test.txt",
        ParentId = "1",
        Size = 42
    }
};
string json = JsonConvert.SerializeObject(items, Formatting.Indented);
```

...you'll notice that the JSON doesn't contain any information about the object's type:

```javascript
[
  {
    "ChildrenCount": 1,
    "id": "1",
    "Name": "foo",
    "ParentId": null
  },
  {
    "Size": 42,
    "id": "2",
    "Name": "test.txt",
    "ParentId": "1"
  }
]
```

If the type information isn't available for deserialization, we can't really blame JSON.NET for not being able to guess. It just needs a bit of help!

## TypeNameHandling

One way to solve this is using a built-in feature of JSON.NET: `TypeNameHandling`. Basically, you tell JSON.NET to include the name of the type in serialized objects, like this:

```csharp
var settings = new JsonSerializerSettings
{
    TypeNameHandling = TypeNameHandling.Objects
};
string json = JsonConvert.SerializeObject(items, Formatting.Indented, settings);
```

And you get JSON objects annotated with the assembly-qualified type name of the objects:

```javascript
[
  {
    "$type": "CosmosTypeHierarchy.FolderItem, CosmosTypeHierarchy",
    "id": "1",
    "Name": "foo",
    "ParentId": null
  },
  {
    "$type": "CosmosTypeHierarchy.FileItem, CosmosTypeHierarchy",
    "Size": 42,
    "id": "2",
    "Name": "test.txt",
    "ParentId": "1"
  }
]
```

This is nice! Using the type name and assembly, JSON.NET can then deserialize these objects correctly:

```csharp
var deserializedItems = JsonConvert.DeserializeObject<FileSystemItem[]>(json, settings);
```

There's just one issue, though: if you include actual .NET type names in your JSON documents, what happens when you decide to rename a class, or move it to a different namespace or assembly? Well, your existing documents can no longer be deserialized... Bummer.

On the other hand, if we were able to control the type name written to the document, it would solve this problem. And guess what: we can!

## Serialization binder

We just need to implement our own `ISerializationBinder`:

```csharp
class CustomSerializationBinder : ISerializationBinder
{
    public void BindToName(Type serializedType, out string assemblyName, out string typeName)
    {
        if (serializedType == typeof(FileItem))
        {
            assemblyName = null;
            typeName = "fileItem";
        }
        else if (serializedType == typeof(FolderItem))
        {
            assemblyName = null;
            typeName = "folderItem";
        }
        else
        {
            // Mimic the default behavior
            assemblyName = serializedType.Assembly.GetName().Name;
            typeName = serializedType.FullName;
        }
    }

    public Type BindToType(string assemblyName, string typeName)
    {
        if (string.IsNullOrEmpty(assemblyName))
        {
            if (typeName == "fileItem")
                return typeof(FileItem);
            if (typeName == "folderItem")
                return typeof(FolderItem);
        }

        // Mimic the default behavior
        var assemblyQualifiedName = typeName;
        if (!string.IsNullOrEmpty(assemblyName))
            assemblyQualifiedName += ", " + assemblyName;
        return Type.GetType(assemblyQualifiedName);
    }
}

...

var settings = new JsonSerializerSettings
{
    TypeNameHandling = TypeNameHandling.Objects,
    SerializationBinder = new CustomSerializationBinder()
};
string json = JsonConvert.SerializeObject(items, Formatting.Indented, settings);
```

Which gives us the following JSON:

```javascript
[
  {
    "$type": "folderItem",
    "ChildrenCount": 1,
    "id": "1",
    "Name": "foo",
    "ParentId": null
  },
  {
    "$type": "fileItem",
    "Size": 42,
    "id": "2",
    "Name": "test.txt",
    "ParentId": "1"
  }
]
```

This is more concise, and more flexible. Of course, now we have to keep using the same "JSON names" for these types, but it's not as much of a problem as not being able to rename or move classes.

Overall, this is a pretty solid approach. And if you don't want to explicitly write type/name mappings in the serialization binder, you can always use custom attributes and reflection to do define the mapping without touching the binder itself.

What still bothers me is that with `TypeNameHandling.Objects`, *all* objects will be annotated with their type, including nested ones, even though it's not always necessary. For instance, if you know that a particular class is sealed (or at least, doesn't have any derived class), writing the type name is unnecessary and just adds noise. There's an other option that does almost the right thing: `TypeNameHandling.Auto`. It writes the type if and only if it can't be inferred from context, i.e. if the actual type of the object is different from the statically known type. This is almost perfect, except that it doesn't write the type for the root object, *unless* you specify the "known type" explicitly, which isn't very convenient. What would be ideal would be another option to always write the type for the root object. I suggested this [on GitHub](https://github.com/JamesNK/Newtonsoft.Json/issues/2183), vote if you want it too!

In the meantime, there's another way to achieve the desired result: a custom converter. But this post has been long enough already, so we'll cover that, and the integration with Cosmos DB SDK, in [the next post](/2019/10/15/handling-type-hierarchies-in-cosmos-db-part-2/).

