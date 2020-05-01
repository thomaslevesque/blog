---
layout: post
title: '[C# 4.0] Implementing a custom dynamic object'
date: 2009-10-08T00:00:00.0000000
url: /2009/10/08/c-4-0-implementing-a-custom-dynamic-object/
tags:
  - .NET 4.0
  - C# 4.0
  - DLR
  - dynamic
categories:
  - C# 4.0
  - Code sample
---

If you've been following the news about .NET, you probably know that the upcoming version 4.0 of C# introduces a new `dynamic` type. This type allows to access members of an object which are not statically known (at compile time). These members will be resolved at runtime, thanks to the DLR (Dynamic Language Runtime). This feature makes it easier to manipulate COM objects, or any object which type is not statically known. You can find more information about the `dynamic` type [on MSDN](http://msdn.microsoft.com/en-us/library/dd264736%28VS.100%29.aspx).  While playing with Visual Studio 2010 beta, I realized this `dynamic` type enabled very interesting scenarios... It is indeed possible to create your own dynamic objects, with the ability to control the resolution of dynamic members. To do that, you need to implement the [`IDynamicMetaObjectProvider`](http://msdn.microsoft.com/en-us/library/system.dynamic.idynamicmetaobjectprovider%28VS.100%29.aspx) interface. This interface seems pretty simple at first sight, since it only defines one member: the `GetMetaObject` method. But it actually gets trickier when you try to implement this method : you have to build a `DynamicMetaObject` from an `Expression`, which is far from trivial... I must admit I almost gave up when I saw the complexity of the task.  Fortunately, there is a much easier way to create your own dynamic objects: you just have to inherit from the `DynamicObject` class, which provides a basic implementation of `IDynamicMetaObjectProvider`, and override a few methods to achieve the desired behavior.  Here's a simple example, inspired from the Javascript language. In Javascript, it is possible to dynamically add members (properties or methods) to an existing type, as in the following sample:  
```javascript
var x = new Object();
x.Message = "Hello world !";
x.ShowMessage = function()
{
  alert(this.Message);
};
x.ShowMessage();
```
  This code creates an object, add a `Message` property to that object by defining its value, and also adds a `ShowMessage` method to display the message.  In previous versions of C#, it would have been impossible to do such a thing: indeed C# is a statically typed language, which implies that members are resolved at compile time, not at runtime. Since the `Object` class doesn't have a `Message` property or a `ShowMessage` method, the compiler won't accept things like `x.Message` or `x.ShowMessage()`. This is where the `dynamic` type comes to the rescue, since it doesn't resolve the members at compile time...  Now let's try to create a dynamic object that allows to write a C# code similar to the Javascript code above. To do that, we will store the values of dynamic properties in a `Dictionary<string, object>`. To make this class work, we need to override the [`TryGetMember`](http://msdn.microsoft.com/en-us/library/system.dynamic.dynamicobject.trygetmember%28VS.100%29.aspx) and [`TrySetMember`](http://msdn.microsoft.com/en-us/library/system.dynamic.dynamicobject.trygetmember%28VS.100%29.aspx) methods. These methods implement the logic to read or write a member of the dynamic object. To illustrate the idea, let's have a look at the code, I'll comment it later:  
```csharp
public class MyDynamicObject : DynamicObject
{
    private Dictionary<string, object> _properties = new Dictionary<string, object>();

    public override bool TryGetMember(GetMemberBinder binder, out object result)
    {
        return _properties.TryGetValue(binder.Name, out result);
    }

    public override bool TrySetMember(SetMemberBinder binder, object value)
    {
        _properties[binder.Name] = value;
        return true;
    }
}
```
  Now let's explain the code above. The `TryGetMember` tries to find the requested property in the dictionary. Note that the name of the property is exposed as the `Name` property of the `binder` parameter. If the property exists, its value is returned in the `result` output parameter, and the method returns `true`. Otherwise, the method returns `false`, which will cause a `RuntimeBinderException` at the call site. This exception simply means that the dynamic resolution of the property failed.  The `TrySetMember` method performs the opposite task: it defines the value of a property. If the member doesn't exist, it is added to the dictionary, so the method always returns `true`.  The following sample shows how to use this object:  
```csharp
dynamic x = new MyDynamicObject();
x.Message = "Hello world !";
Console.WriteLine(x.Message);
```
  This code compiles and runs fine, and prints "Hello world !" to the console... easy, isn't it ?  But what about methods ? Well, I could tell you that you need to override the `TryInvokeMember` method, which is used to handle dynamic method calls... but actually it's not even necessary ! Our implementation already handles this feature: we just need to assign a delegate to a property of the object. It won't actually be a real member method, just a property returning a delegate, but since the syntax to call it will be the same as a method call, it will do fine for now. Here's an example of adding a method to the object:  
```csharp
dynamic x = new MyDynamicObject();
x.Message = "Hello world !";
x.ShowMessage = new Action(
    () =>
    {
        Console.WriteLine(x.Message);
    });
x.ShowMessage();
```
  Eventually, we end up with something very close to the Javascript we were trying to imitate, all with a class of less than 10 lines of code (not counting the braces)...  This class can be quite handy to use as an general purpose object, for instance to group some data together without having to create a specific class. In that aspect, it's similar to an anonymous type (already existing in C# 3), but with the benefit that it can be used as a method return value, which is not possible with an anonymous type.  Of course there are many more useful things to do with a custom dynamic object... for instance, here's a simple wrapper for a `DataRow`, to make it easier to access the fields:  
```csharp
public class DynamicDataRow : DynamicObject
{
    private DataRow _dataRow;

    public DynamicDataRow(DataRow dataRow)
    {
        if (dataRow == null)
            throw new ArgumentNullException("dataRow");
        this._dataRow = dataRow;
    }

    public DataRow DataRow
    {
        get { return _dataRow; }
    }

    public override bool TryGetMember(GetMemberBinder binder, out object result)
    {
        result = null;
        if (_dataRow.Table.Columns.Contains(binder.Name))
        {
            result = _dataRow[binder.Name];
            return true;
        }
        return false;
    }

    public override bool TrySetMember(SetMemberBinder binder, object value)
    {
        if (_dataRow.Table.Columns.Contains(binder.Name))
        {
            _dataRow[binder.Name] = value;
            return true;
        }
        return false;
    }
}
```

Let's add a helper extension method to get the wrapper for a row:

```csharp
public static class DynamicDataRowExtensions
{
    public static dynamic AsDynamic(this DataRow dataRow)
    {
        return new DynamicDataRow(dataRow);
    }
}
```

We can now write things like that:

```csharp
DataTable table = new DataTable();
table.Columns.Add("FirstName", typeof(string));
table.Columns.Add("LastName", typeof(string));
table.Columns.Add("DateOfBirth", typeof(DateTime));

dynamic row = table.NewRow().AsDynamic();
row.FirstName = "John";
row.LastName = "Doe";
row.DateOfBirth = new DateTime(1981, 9, 12);
table.Rows.Add(row.DataRow);

// Add more rows...
// ...

var bornInThe20thCentury = from r in table.AsEnumerable()
                           let dr = r.AsDynamic()
                           where dr.DateOfBirth.Year > 1900
                           && dr.DateOfBirth.Year <= 2000
                           select new { dr.LastName, dr.FirstName };

foreach (var item in bornInThe20thCentury)
{
    Console.WriteLine("{0} {1}", item.FirstName, item.LastName);
}
```
  Now that you understand the basic principles for creating custom dynamic objects, you can imagine many more useful applications :)  **Update :** Just after posting this article, I stumbled upon the [`ExpandoObject`](http://msdn.microsoft.com/en-us/library/system.dynamic.expandoobject%28VS.100%29.aspx) class, which does exactly the same thing as the `MyDynamicObject` class above... It seems I reinvented the wheel again ;). Anyway, it's interesting to see how dynamic objects work internally, if only for learning purposes... For more details about the `ExpandoObject` class, check out [this post on the C# FAQ blog](http://blogs.msdn.com/csharpfaq/archive/2009/10/01/dynamic-in-c-4-0-introducing-the-expandoobject.aspx).

