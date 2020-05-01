---
layout: post
title: '[WPF] Binding to an asynchronous collection'
date: 2009-04-17T01:00:19.0000000
url: /2009/04/17/wpf-binding-to-an-asynchronous-collection/
tags:
  - asynchronous
  - binding
  - collection
  - MVVM
  - WPF
categories:
  - Code sample
  - WPF
---

As you may have noticed, it is not possible to modify the contents of an `ObservableCollection` on a separate thread if a view is bound to this collection : the `CollectionView` raises a `NotSupportedException` :  

> This type of CollectionView does not support changes to its SourceCollection from a thread different from the Dispatcher thread

  To illustrate this, let's take a simple example : a `ListBox` bound to a collection of strings in the ViewModel :  
```csharp
        private ObservableCollection<string> _strings = new ObservableCollection<string>();
        public ObservableCollection<string> Strings
        {
            get { return _strings; }
            set
            {
                _strings = value;
                OnPropertyChanged("Strings");
            }
        }
```

```xml
    <ListBox ItemsSource="{Binding Strings}"/>
```
  If we add items to this collection out of the main thread, we get the exception mentioned above. A possible solution would be to create a new collection, and assign it to the `Strings` property when it is filled, but in this case the UI won't reflect progress : all items will appear in the `ListBox` at the same time after the collection is filled, instead of appearing as they are added to the collection. It can be annoying in some cases : for instance, if the `ListBox` is used to display search results, the user expects to see the results as they are found, like in Windows Search.  A simple way to achieve the desired behavior is to inherit `ObservableCollection` and override `OnCollectionChanged` and `OnPropertyChanged` so that the events are raised on the main thread (actually, the thread that created the collection). The `AsyncOperation` class is perfectly suited for this need : it allows to "post" a method call on the thread that created it. It is used, for instance, in the `BackgroundWorker` component, and in many asynchronous methods in the framework (`PictureBox.LoadAsync`, `WebClient.DownloadAsync`, etc...).  So, here's the code of an `AsyncObservableCollection` class, that can be modified from any thread, and still notify the UI when it is modified :  
```csharp
    public class AsyncObservableCollection<T> : ObservableCollection<T>
    {
        private AsyncOperation asyncOp = null;

        public AsyncObservableCollection()
        {
            CreateAsyncOp();
        }

        public AsyncObservableCollection(IEnumerable<T> list)
            : base(list)
        {
            CreateAsyncOp();
        }

        private void CreateAsyncOp()
        {
            // Create the AsyncOperation to post events on the creator thread
            asyncOp = AsyncOperationManager.CreateOperation(null);
        }

        protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
        {
            // Post the CollectionChanged event on the creator thread
            asyncOp.Post(RaiseCollectionChanged, e);
        }

        private void RaiseCollectionChanged(object param)
        {
            // We are in the creator thread, call the base implementation directly
           base.OnCollectionChanged((NotifyCollectionChangedEventArgs)param);
        }

        protected override void OnPropertyChanged(PropertyChangedEventArgs e)
        {
            // Post the PropertyChanged event on the creator thread
            asyncOp.Post(RaisePropertyChanged, e);
        }

        private void RaisePropertyChanged(object param)
        {
            // We are in the creator thread, call the base implementation directly
            base.OnPropertyChanged((PropertyChangedEventArgs)param);
        }
    }
```
  The only constraint when using this class is that instances of the collection must be created on the UI thread, so that events are raised on that thread.  In the previous example, the only thing to change to make the collection modifiable across threads is the instantiation of the collection in the ViewModel :  
```csharp
private ObservableCollection<string> _strings = new AsyncObservableCollection<string>();
```
  The `ListBox` can now reflect in real-time the changes made on the collection.  Enjoy ;)  **Update :** I just found a bug in my implementation : in some cases, using `Post` to raise the event when the collection is modified from the main thread can cause unpredictable behavior. In that case, the event should of course be raised directly on the main thread, after checking that the current `SynchronizationContext` is the one in which the collection was created. This also made me realize that the `AsyncOperation` actually doesn't bring any benefit : we can use the `SynchronizationContext` directly instead. So here's the new implementation :  
```csharp
    public class AsyncObservableCollection<T> : ObservableCollection<T>
    {
        private SynchronizationContext _synchronizationContext = SynchronizationContext.Current;

        public AsyncObservableCollection()
        {
        }

        public AsyncObservableCollection(IEnumerable<T> list)
            : base(list)
        {
        }

        protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
        {
            if (SynchronizationContext.Current == _synchronizationContext)
            {
                // Execute the CollectionChanged event on the current thread
                RaiseCollectionChanged(e);
            }
            else
            {
                // Raises the CollectionChanged event on the creator thread
                _synchronizationContext.Send(RaiseCollectionChanged, e);
            }
        }

        private void RaiseCollectionChanged(object param)
        {
            // We are in the creator thread, call the base implementation directly
            base.OnCollectionChanged((NotifyCollectionChangedEventArgs)param);
        }

        protected override void OnPropertyChanged(PropertyChangedEventArgs e)
        {
            if (SynchronizationContext.Current == _synchronizationContext)
            {
                // Execute the PropertyChanged event on the current thread
                RaisePropertyChanged(e);
            }
            else
            {
                // Raises the PropertyChanged event on the creator thread
                _synchronizationContext.Send(RaisePropertyChanged, e);
            }
        }

        private void RaisePropertyChanged(object param)
        {
            // We are in the creator thread, call the base implementation directly
            base.OnPropertyChanged((PropertyChangedEventArgs)param);
        }
    }
```
**Update:** changed the code to use `Send` instead of `Post`. Using `Post` caused the event to be raised *asynchronously* on the UI thread, which could cause a race condition if the collection was modified again before the previous event was handled.
