---
layout: post
title: '[C#] Parent/child relationship and XML serialization'
date: 2009-06-12T10:18:22.0000000
url: /2009/06/12/c-parentchild-relationship-and-xml-serialization/
tags:
  - C#
  - collection
  - parent/child
  - XML serialization
categories:
  - Code sample
---

Today I'd like to present an idea that occurred to me recently. Nothing about WPF this time, this is all about C# class design !  **The problem**  It's very common in C# programs to have an object that owns a collection of child items with a reference to their parent. For instance, this is the case for Windows Forms controls, which have a collection of child controls (`Controls`), and a reference to their parent control (`Parent`).  This kind of structure is quite easy to implement, it just requires a bit of plumbing to maintain the consistency of the parent/child relationship. However, if you want to serialize the parent object to XML, it can get tricky... Let's take a simple, purely theoretical example :  
```csharp
    public class Parent
    {
        public Parent()
        {
            this.Children = new List<Child>();
        }

        public string Name { get; set; }

        public List<Child> Children { get; set; }

        public void AddChild(Child child)
        {
            child.ParentObject = this;
            this.Children.Add(child);
        }

        public void RemoveChild(Child child)
        {
            this.Children.Remove(child);
            child.ParentObject = null;
        }
    }
```

```csharp
    public class Child
    {
        public string Name { get; set; }

        public Parent ParentObject { get; set; }
    }
```
  Let's create an instance of `Parent` with a few children, and try to serialize it to XML :  
```csharp
            Parent p = new Parent { Name = "The parent" };
            p.AddChild(new Child { Name = "First child" });
            p.AddChild(new Child { Name = "Second child" });

            string xml;
            XmlSerializer xs = new XmlSerializer(typeof(Parent));
            using (StringWriter wr = new StringWriter())
            {
                xs.Serialize(wr, p);
                xml = wr.ToString();
            }

            Console.WriteLine(xml);
```
  When we try to serialize the `Parent` object, an `InvalidOperationException` occurs, saying that a circular reference was detected : indeed, the parent references the children, which in turn reference the parent, which references the children... and so on. The obvious solution to that issue is to suppress the serialization of the `Child.ParentObject` property, which can be done easily by using the `XmlIgnore` attribute. With that change the serialization works fine, but the problem is not solved yet : when we deserialize the object, the `ParentObject` property of the children is not set, since it wasn't serialized... the consistency of the parent/child relationship is broken !  A simple and naive solution would be to loop through the `Children` collection after the deserialization, in order to set the `ParentObject` manually. But it's definitely not an elegant approach... and since I really like elegant code, I thought of something else ;)  **The solution**  The idea I had to solve this problem consists of a specialized generic collection `ChildItemCollection<P,T>`, and a `IChildItem<P>` interface that must be implemented by children.  The `IChildItem<P>` interface just defines a `Parent` property of type P :  
```csharp
    /// <summary>
    /// Defines the contract for an object that has a parent object
    /// </summary>
    /// <typeparam name="P">Type of the parent object</typeparam>
    public interface IChildItem<P> where P : class
    {
        P Parent { get; set; }
    }
```
  The `ChildItemCollection<P,T>` class implements `IList<T>` by delegating the implementation to a `List<T>` (or to a collection passed to the constructor), and maintains the parent/child relationship :  
```csharp
    /// <summary>
    /// Collection of child items. This collection automatically set the
    /// Parent property of the child items when they are added or removed
    /// </summary>
    /// <typeparam name="P">Type of the parent object</typeparam>
    /// <typeparam name="T">Type of the child items</typeparam>
    public class ChildItemCollection<P, T> : IList<T>
        where P : class
        where T : IChildItem<P>
    {
        private P _parent;
        private IList<T> _collection;

        public ChildItemCollection(P parent)
        {
            this._parent = parent;
            this._collection = new List<T>();
        }

        public ChildItemCollection(P parent, IList<T> collection)
        {
            this._parent = parent;
            this._collection = collection;
        }

        #region IList<T> Members

        public int IndexOf(T item)
        {
            return _collection.IndexOf(item);
        }

        public void Insert(int index, T item)
        {
            if (item != null)
                item.Parent = _parent;
            _collection.Insert(index, item);
        }

        public void RemoveAt(int index)
        {
            T oldItem = _collection[index];
            _collection.RemoveAt(index);
            if (oldItem != null)
                oldItem.Parent = null;
        }

        public T this[int index]
        {
            get
            {
                return _collection[index];
            }
            set
            {
                T oldItem = _collection[index];
                if (value != null)
                    value.Parent = _parent;
                _collection[index] = value;
                if (oldItem != null)
                    oldItem.Parent = null;
            }
        }

        #endregion

        #region ICollection<T> Members

        public void Add(T item)
        {
            if (item != null)
                item.Parent = _parent;
            _collection.Add(item);
        }

        public void Clear()
        {
            foreach (T item in _collection)
            {
                if (item != null)
                    item.Parent = null;
            }
            _collection.Clear();
        }

        public bool Contains(T item)
        {
            return _collection.Contains(item);
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            _collection.CopyTo(array, arrayIndex);
        }

        public int Count
        {
            get { return _collection.Count; }
        }

        public bool IsReadOnly
        {
            get { return _collection.IsReadOnly; }
        }

        public bool Remove(T item)
        {
            bool b = _collection.Remove(item);
            if (item != null)
                item.Parent = null;
            return b;
        }

        #endregion

        #region IEnumerable<T> Members

        public IEnumerator<T> GetEnumerator()
        {
            return _collection.GetEnumerator();
        }

        #endregion

        #region IEnumerable Members

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return (_collection as System.Collections.IEnumerable).GetEnumerator();
        }

        #endregion
    }
```
  Now let's see how this class can be used in the case of the above example... First let's change the `Child` class so that it implements the `IChildItem<Parent>` interface :  
```csharp
    public class Child : IChildItem<Parent>
    {
        public string Name { get; set; }

        [XmlIgnore]
        public Parent ParentObject { get; internal set; }

        #region IChildItem<Parent> Members

        Parent IChildItem<Parent>.Parent
        {
            get
            {
                return this.ParentObject;
            }
            set
            {
                this.ParentObject = value;
            }
        }

        #endregion
    }
```
  Note that here the `IChildItem<Parent>` interface is implemented *explicitly* : this is a way to "hide" the `Parent` property, that will only be accessible when manipulating the `Child` object through a variable of type `IChildItem<Parent>`. We also define the `set` accessor of the `ParentObject` property as `internal`, so that it can't be modified from another assembly.  In the `Parent` class, the `List<Child>` just has to be replaced by a `ChildItemCollection<Parent, Child>`. We also remove the `AddChild` and `RemoveChild` methods, which are no more necessary since the `ChildItemCollection<P,T>` takes care of setting the `Parent` property.  
```csharp
    public class Parent
    {
        public Parent()
        {
            this.Children = new ChildItemCollection<Parent, Child>(this);
        }

        public string Name { get; set; }

        public ChildItemCollection<Parent, Child> Children { get; private set; }
    }
```
  Note that we give the `ChildItemCollection<Parent, Child>` constructor a reference to the current object : this is how the collection will know what is the parent of its elements.   The code previously used to serialize a `Parent` now works fine. During the deserialization, the `Child.ParentObject` property is not assigned when the `Child` itself is deserialized (since it has the `XmlIgnore` attribute), but when the `Child` is added to the `Parent.Children` collection.  Eventually, we can see that this solution enables us to keep the parent/child relationship when the object graph is serialized to XML, without resorting to unelegant tricks... However, note that the consistency of the relation can still be broken, if the `ParentObject` is changed by code outside the `ChildItemCollection<P,T>` class. To prevent that, some logic must be added to the `set` accessor to maintain the consistency ; I only omitted that part for the sake of clarity and simplicity.
