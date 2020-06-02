---
layout: post
title: '[WPF 4.5] Subscribing to an event using a markup extension'
date: 2011-09-23T00:00:00.0000000
url: /2011/09/23/wpf-4-5-subscribing-to-an-event-using-a-markup-extension/
tags:
  - .NET 4.5
  - events
  - markup extension
  - WPF
  - XAML
categories:
  - WPF
---

It's been a while since I last wrote about markup extensions... The release of Visual Studio 11 Developer Preview, which introduces a number of [new features](http://msdn.microsoft.com/en-us/library/bb613588%28v=VS.110%29.aspx) to WPF, just gave me a reason to play with them again. The feature I'm going to discuss here is perhaps not the most impressive, but it fills in a gap of the previous versions: the support of markup extensions for events.

Until now, it was possible to use a markup extension in XAML to assign a value to a property, but we couldn't do the same to subscribe to an event. In WPF 4.5, it is now possible. So here is a small example of the kind we can do with it...

When using the MVVM pattern, we often associate commands of the ViewModel with controls of the view, via the binding mechanism. This approach usually works well, but it has some downsides:

- it introduces a lot of boilerplate code in the ViewModel
- not all controls have a `Command` property (actually, most don't), and when this property exists, it corresponds only to one event of the control (e.g. the click on a button). There is no really easy way to "bind" the other events to commands of the ViewModel

It would be nice to be able to bind events directly to ViewModel methods, like this:

```xml
<Button Content="Click me"
        Click="{my:EventBinding OnClick}" />
```

With the `OnClick` method defined in the ViewModel:

```csharp
public void OnClick(object sender, EventArgs e)
{
    MessageBox.Show("Hello world!");
}
```

Well, this is now possible! Here's a proof of concept... The `EventBindingExtension` class shown below first gets the `DataContext` of the control, then looks for the specified method on the `DataContext`, and eventually returns a delegate for this method:

```csharp
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Markup;


    public class EventBindingExtension : MarkupExtension
    {
        public EventBindingExtension() { }

        public EventBindingExtension(string eventHandlerName)
        {
            this.EventHandlerName = eventHandlerName;
        }

        [ConstructorArgument("eventHandlerName")]
        public string EventHandlerName { get; set; }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            if (string.IsNullOrEmpty(EventHandlerName))
                throw new ArgumentException("The EventHandlerName property is not set", "EventHandlerName");

            var target = (IProvideValueTarget)serviceProvider.GetService(typeof(IProvideValueTarget));

            EventInfo eventInfo = target.TargetProperty as EventInfo;
            if (eventInfo == null)
                throw new InvalidOperationException("The target property must be an event");
            
            object dataContext = GetDataContext(target.TargetObject);
            if (dataContext == null)
                throw new InvalidOperationException("No DataContext found");

            var handler = GetHandler(dataContext, eventInfo, EventHandlerName);
            if (handler == null)
                throw new ArgumentException("No valid event handler was found", "EventHandlerName");

            return handler;
        }

        #region Helper methods

        static object GetHandler(object dataContext, EventInfo eventInfo, string eventHandlerName)
        {
            Type dcType = dataContext.GetType();

            var method = dcType.GetMethod(
                eventHandlerName,
                GetParameterTypes(eventInfo));
            if (method != null)
            {
                if (method.IsStatic)
                    return Delegate.CreateDelegate(eventInfo.EventHandlerType, method);
                else
                    return Delegate.CreateDelegate(eventInfo.EventHandlerType, dataContext, method);
            }

            return null;
        }

        static Type[] GetParameterTypes(EventInfo eventInfo)
        {
            var invokeMethod = eventInfo.EventHandlerType.GetMethod("Invoke");
            return invokeMethod.GetParameters().Select(p => p.ParameterType).ToArray();
        }

        static object GetDataContext(object target)
        {
            var depObj = target as DependencyObject;
            if (depObj == null)
                return null;

            return depObj.GetValue(FrameworkElement.DataContextProperty)
                ?? depObj.GetValue(FrameworkContentElement.DataContextProperty);
        }

        #endregion
    }
```

This class can be used as shown in the example above.

As it is now, this markup extension has an annoying limitation: the `DataContext` must be set before the call to `ProvideValue`, otherwise it won't be possible to find the event handler method. A solution could be to subscribe to the `DataContextChanged` event to look for the method after the `DataContext` is set, but in the meantime we still need to return something... and we can't return null, because it would cause an exception (since you can't subscribe to an event with a null handler). So we need to return a dummy handler generated dynamically from the event signature. It makes things a bit harder... but it's still feasible.

Here's a second version that implements this improvement :

```csharp
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Windows;
using System.Windows.Markup;

    public class EventBindingExtension : MarkupExtension
    {
        private EventInfo _eventInfo;

        public EventBindingExtension() { }

        public EventBindingExtension(string eventHandlerName)
        {
            this.EventHandlerName = eventHandlerName;
        }

        [ConstructorArgument("eventHandlerName")]
        public string EventHandlerName { get; set; }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            if (string.IsNullOrEmpty(EventHandlerName))
                throw new ArgumentException("The EventHandlerName property is not set", "EventHandlerName");

            var target = (IProvideValueTarget)serviceProvider.GetService(typeof(IProvideValueTarget));

            var targetObj = target.TargetObject as DependencyObject;
            if (targetObj == null)
                throw new InvalidOperationException("The target object must be a DependencyObject");

            _eventInfo = target.TargetProperty as EventInfo;
            if (_eventInfo == null)
                throw new InvalidOperationException("The target property must be an event");

            object dataContext = GetDataContext(targetObj);
            if (dataContext == null)
            {
                SubscribeToDataContextChanged(targetObj);
                return GetDummyHandler(_eventInfo.EventHandlerType);
            }

            var handler = GetHandler(dataContext, _eventInfo, EventHandlerName);
            if (handler == null)
            {
                Trace.TraceError(
                    "EventBinding: no suitable method named '{0}' found in type '{1}' to handle event '{2'}",
                    EventHandlerName,
                    dataContext.GetType(),
                    _eventInfo);
                return GetDummyHandler(_eventInfo.EventHandlerType);
            }

            return handler;
            
        }

        #region Helper methods

        static Delegate GetHandler(object dataContext, EventInfo eventInfo, string eventHandlerName)
        {
            Type dcType = dataContext.GetType();

            var method = dcType.GetMethod(
                eventHandlerName,
                GetParameterTypes(eventInfo.EventHandlerType));
            if (method != null)
            {
                if (method.IsStatic)
                    return Delegate.CreateDelegate(eventInfo.EventHandlerType, method);
                else
                    return Delegate.CreateDelegate(eventInfo.EventHandlerType, dataContext, method);
            }

            return null;
        }

        static Type[] GetParameterTypes(Type delegateType)
        {
            var invokeMethod = delegateType.GetMethod("Invoke");
            return invokeMethod.GetParameters().Select(p => p.ParameterType).ToArray();
        }

        static object GetDataContext(DependencyObject target)
        {
            return target.GetValue(FrameworkElement.DataContextProperty)
                ?? target.GetValue(FrameworkContentElement.DataContextProperty);
        }

        static readonly Dictionary<Type, Delegate> _dummyHandlers = new Dictionary<Type, Delegate>();

        static Delegate GetDummyHandler(Type eventHandlerType)
        {
            Delegate handler;
            if (!_dummyHandlers.TryGetValue(eventHandlerType, out handler))
            {
                handler = CreateDummyHandler(eventHandlerType);
                _dummyHandlers[eventHandlerType] = handler;
            }
            return handler;
        }

        static Delegate CreateDummyHandler(Type eventHandlerType)
        {
            var parameterTypes = GetParameterTypes(eventHandlerType);
            var returnType = eventHandlerType.GetMethod("Invoke").ReturnType;
            var dm = new DynamicMethod("DummyHandler", returnType, parameterTypes);
            var il = dm.GetILGenerator();
            if (returnType != typeof(void))
            {
                if (returnType.IsValueType)
                {
                    var local = il.DeclareLocal(returnType);
                    il.Emit(OpCodes.Ldloca_S, local);
                    il.Emit(OpCodes.Initobj, returnType);
                    il.Emit(OpCodes.Ldloc_0);
                }
                else
                {
                    il.Emit(OpCodes.Ldnull);
                }
            }
            il.Emit(OpCodes.Ret);
            return dm.CreateDelegate(eventHandlerType);
        }

        private void SubscribeToDataContextChanged(DependencyObject targetObj)
        {
            DependencyPropertyDescriptor
                .FromProperty(FrameworkElement.DataContextProperty, targetObj.GetType())
                .AddValueChanged(targetObj, TargetObject_DataContextChanged);
        }

        private void UnsubscribeFromDataContextChanged(DependencyObject targetObj)
        {
            DependencyPropertyDescriptor
                .FromProperty(FrameworkElement.DataContextProperty, targetObj.GetType())
                .RemoveValueChanged(targetObj, TargetObject_DataContextChanged);
        }

        private void TargetObject_DataContextChanged(object sender, EventArgs e)
        {
            DependencyObject targetObj = sender as DependencyObject;
            if (targetObj == null)
                return;

            object dataContext = GetDataContext(targetObj);
            if (dataContext == null)
                return;

            var handler = GetHandler(dataContext, _eventInfo, EventHandlerName);
            if (handler != null)
            {
                _eventInfo.AddEventHandler(targetObj, handler);
            }
            UnsubscribeFromDataContextChanged(targetObj);
        }

        #endregion
    }
```

So this is the kind of things we can do thanks to this new WPF feature. We could also imagine a behavior system similar to what we can do with attached properties, e.g. to execute a standard action when an event occurs. There are lots of possible applications for this, I leave it to you to find them ;)
