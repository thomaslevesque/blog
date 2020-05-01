---
layout: post
title: Detecting dependency property changes in WinRT
date: 2013-04-21T00:00:00.0000000
url: /2013/04/21/detecting-dependency-property-changes-in-winrt/
tags:
  - binding
  - C#
  - dependency property
  - winrt
  - XAML
categories:
  - Tips and tricks
  - WinRT
---


Today I’d like to share a trick I used while developing my first Windows Store application. I’m very new to this technology and it’s my first article about it, so I hope I won’t make a fool of myself…

It’s often useful to be notified when the value of a dependency property changes; many controls expose events for that purpose, but it’s not always the case. For instance, recently I was trying to detect when the `Content` property of a `ContentControl` changed. In WPF, I would have used the [`DependencyPropertyDescriptor`](http://msdn.microsoft.com/en-us/library/system.componentmodel.dependencypropertydescriptor.aspx) class, but it’s not available in WinRT.

Fortunately, there is a mechanism which is available on all XAML platforms, and can solve this problem: binding. So, the solution is just to create a class with a dummy property that is bound to the property we want to watch, and call a handler when the value of the dummy property changes. To make it cleaner and hide the actual implementation, I wrapped it as an extension method that returns an `IDisposable`:

```
    public static class DependencyObjectExtensions
    {
        public static IDisposable WatchProperty(this DependencyObject target,
                                                string propertyPath,
                                                DependencyPropertyChangedEventHandler handler)
        {
            return new DependencyPropertyWatcher(target, propertyPath, handler);
        }

        class DependencyPropertyWatcher : DependencyObject, IDisposable
        {
            private DependencyPropertyChangedEventHandler _handler;

            public DependencyPropertyWatcher(DependencyObject target,
                                             string propertyPath,
                                             DependencyPropertyChangedEventHandler handler)
            {
                if (target == null) throw new ArgumentNullException("target");
                if (propertyPath == null) throw new ArgumentNullException("propertyPath");
                if (handler == null) throw new ArgumentNullException("handler");

                _handler = handler;

                var binding = new Binding
                {
                    Source = target,
                    Path = new PropertyPath(propertyPath),
                    Mode = BindingMode.OneWay
                };
                BindingOperations.SetBinding(this, ValueProperty, binding);
            }

            private static readonly DependencyProperty ValueProperty =
                DependencyProperty.Register(
                    "Value",
                    typeof(object),
                    typeof(DependencyPropertyWatcher),
                    new PropertyMetadata(null, ValuePropertyChanged));

            private static void ValuePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            {
                var watcher = d as DependencyPropertyWatcher;
                if (watcher == null)
                    return;

                watcher.OnValueChanged(e);
            }

            private void OnValueChanged(DependencyPropertyChangedEventArgs e)
            {
                var handler = _handler;
                if (handler != null)
                    handler(this, e);
            }

            public void Dispose()
            {
                _handler = null;
                // There is no ClearBinding method, so set a dummy binding instead
                BindingOperations.SetBinding(this, ValueProperty, new Binding());
            }
        }
    }
```

It can be used like this:

```
// Subscribe
watcher = myControl.WatchProperty("Content", myControl_ContentChanged);

// Unsubscribe
watcher.Dispose();
```

I hope you will find this useful!

