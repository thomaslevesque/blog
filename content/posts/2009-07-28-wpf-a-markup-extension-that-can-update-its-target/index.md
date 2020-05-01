---
layout: post
title: '[WPF] A markup extension that can update its target'
date: 2009-07-28T12:38:26.0000000
url: /2009/07/28/wpf-a-markup-extension-that-can-update-its-target/
tags:
  - markup extension
  - WPF
  - XAML
categories:
  - Code sample
  - WPF
---

If you have read my previous posts on the topic, you know I'm a big fan of custom markup extensions... However, they have a limitation that can be quite annoying : they are only evaluated once. Yet it would be useful to be able to evaluate them again to update the target property, like a binding... It could be useful in various cases, for instance : 
- if the value of the markup extension can change in response to an event
- if the state of the target object when the markup extension is evaluated doesn't allow to determine the value yet, and the evaluation needs to be deferred (for instance, if the DataContext of the target object is needed, but is not yet defined when the markup extension is evaluated)

  This post explains how to update the target of a markup extension after the initial evaluation.  The  `ProvideValue` method of a markup extension takes a parameter of type `IServiceProvider`, which provides, among others, a `IProvideValueTarget` service. This interface exposes two properties, `TargetObject` and `TargetProperty`, which allow to retrieve the target object and property of the markup extension. It is then possible, if you retain this information, to update the property after the markup extension has already been evaluated.  To carry out this task, we can create an abstract class `UpdatableMarkupExtension`, which saves the target object and property, and provides a method to update the value :  
```csharp

    public abstract class UpdatableMarkupExtension : MarkupExtension
    {
        private object _targetObject;
        private object _targetProperty;

        protected object TargetObject
        {
            get { return _targetObject; }
        }

        protected object TargetProperty
        {
            get { return _targetProperty; }
        }

        public sealed override object ProvideValue(IServiceProvider serviceProvider)
        {
            IProvideValueTarget target = serviceProvider.GetService(typeof(IProvideValueTarget)) as IProvideValueTarget;
            if (target != null)
            {
                _targetObject = target.TargetObject;
                _targetProperty = target.TargetProperty;
            }

            return ProvideValueInternal(serviceProvider);
        }

        protected void UpdateValue(object value)
        {
            if (_targetObject != null)
            {
                if (_targetProperty is DependencyProperty)
                {
                    DependencyObject obj = _targetObject as DependencyObject;
                    DependencyProperty prop = _targetProperty as DependencyProperty;

                    Action updateAction = () =>  obj.SetValue(prop, value);

                    // Check whether the target object can be accessed from the
                    // current thread, and use Dispatcher.Invoke if it can't

                    if (obj.CheckAccess())
                        updateAction();
                    else
                        obj.Dispatcher.Invoke(updateAction);
                }
                else // _targetProperty is PropertyInfo
                {
                    PropertyInfo prop = _targetProperty as PropertyInfo;
                    prop.SetValue(_targetObject, value, null);
                }
            }
        }

        protected abstract object ProvideValueInternal(IServiceProvider serviceProvider);
    }
```
  Since it is essential that the target object and property are saved, we mark the `ProvideValue` method as `sealed` so that it cannot be overriden, and we add an abstract `ProvideValueInternal` method so that inheritors can provide their implementation.  The `UpdateValue` method handles the update of the target property, which can be either a dependency property (`DependencyProperty`), or a standard CLR property (`PropertyInfo`). In the case of a `DependencyProperty`, the target object inherits from `DependencyObject`, which itself inherits from `DispatcherObject` : it is therefore necessary to make sure that the object is only accessed from the thread that owns it, using the `CheckAccess` and `Invoke` methods.  Here's a simple example to illustrate how to use this class. Let's assume we want to create a custom markup extension which indicates whether the network is available. It would be used like that :  
```xml

<CheckBox IsChecked="{my:NetworkAvailable}" Content="Network is available" />
```
  Obviously, we want the checkbox to be updated when the availability of the network changes (e.g. when the network cable is plugged or unplugged, or when the Wifi network is out of reach). So we need to handle the `NetworkChange.NetworkAvailabilityChanged` event, and update the `IsChecked` property accordingly. So the extension will inherit the `UpdatableMarkupExtension` class to take advantage of the `UpdateValue` method :  
```csharp

    public class NetworkAvailableExtension : UpdatableMarkupExtension
    {
        public NetworkAvailableExtension()
        {
            NetworkChange.NetworkAvailabilityChanged += new NetworkAvailabilityChangedEventHandler(NetworkChange_NetworkAvailabilityChanged);
        }

        protected override object ProvideValueInternal(IServiceProvider serviceProvider)
        {
            return NetworkInterface.GetIsNetworkAvailable();
        }

        private void NetworkChange_NetworkAvailabilityChanged(object sender, NetworkAvailabilityEventArgs e)
        {
            UpdateValue(e.IsAvailable);
        }
    }
```
  Note that we subscribe to the `NetworkAvailabilityChanged` event in the class constructor. If we wanted to subscribe to an event of the target object, we would have to do it in the `ProvideValueInternal` method, so that the target object can be accessed.  I hope this post let you see how simple it is to implement a markup extension that can update its target at a later time. This enables a behavior similar to a binding, but is not limited to dependency properties. An example of where I use this technique is to create a localization framework that allows to switch language "on the fly", without restarting the application.   **Update :** In its current state, this markup extension can't be used in a template. For an explanation and a solution to that issue, please read [this post](http://www.thomaslevesque.com/2009/08/23/wpf-markup-extensions-and-templates/).
