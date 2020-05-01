---
layout: post
title: Passing parameters by reference to an asynchronous method
date: 2014-11-04T00:00:00.0000000
url: /2014/11/04/passing-parameters-by-reference-to-an-asynchronous-method/
tags:
  - async
  - asynchronous
  - await
  - byref
  - C#
  - out
  - ref
  - reference
categories:
  - Tips and tricks
---


Asynchrony in C# 5 is awesome, and I’ve been using it a lot since it was introduced. But there are few annoying limitations; for instance, you cannot pass parameters by reference (`ref` or `out`) to an asynchronous method. There are good reasons for that; the most obvious is that if you pass a local variable by reference, it is stored on the stack, but the current stack won’t remain available during the whole execution of the async method (only until the first `await`), so the location of the variable won’t exist anymore.

However, it’s pretty easy to work around that limitation : you only need to create a `Ref<T>` class to hold the value, and pass an instance of this class by value to the async method:

```
async void btnFilesStats_Click(object sender, EventArgs e)
{
    var count = new Ref<int>();
    var size = new Ref<ulong>();
    await GetFileStats(tbPath.Text, count, size);
    txtFileStats.Text = string.Format("{0} files ({1} bytes)", count, size);
}

async Task GetFileStats(string path, Ref<int> totalCount, Ref<ulong> totalSize)
{
    var folder = await StorageFolder.GetFolderFromPathAsync(path);
    foreach (var f in await folder.GetFilesAsync())
    {
        totalCount.Value += 1;
        var props = await f.GetBasicPropertiesAsync();
        totalSize.Value += props.Size;
    }
    foreach (var f in await folder.GetFoldersAsync())
    {
        await GetFilesCountAndSize(f, totalCount, totalSize);
    }
}
```

The `Ref<T>` class looks like this:

```
public class Ref<T>
{
    public Ref() { }
    public Ref(T value) { Value = value; }
    public T Value { get; set; }
    public override string ToString()
    {
        T value = Value;
        return value == null ? "" : value.ToString();
    }
    public static implicit operator T(Ref<T> r) { return r.Value; }
    public static implicit operator Ref<T>(T value) { return new Ref<T>(value); }
}
```

As you can see, it’s pretty straightforward. This approach can also be used in iterator blocks (i.e. `yield return`), that also don’t allow `ref` and `out` parameters. It also has an advantage over standard `ref` and `out` parameters: you can make the parameter optional, if for instance you’re not interested in the result (obviously, the callee must handle that case appropriately).

