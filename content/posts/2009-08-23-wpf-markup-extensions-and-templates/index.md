---
layout: post
title: '[WPF] Markup extensions and templates'
date: 2009-08-23T00:00:00.0000000
url: /2009/08/23/wpf-markup-extensions-and-templates/
tags:
  - markup extension
  - template
  - WPF
  - XAML
categories:
  - Code sample
  - WPF
---

*Note : This post follows the one about a [a markup extension that can update its target](/2009/07/28/wpf-a-markup-extension-that-can-update-its-target/), and reuses the same code.*  You may have noticed that using a custom markup extension in a template sometimes lead to unexpected results... In this post I'll explain what the problem is, and how to create a markup extensions that behaves correctly in a template.  **The problem**  Let's take the example from the previous post : a markup extension which gives the state of network connectivity, and updates its target when the network is connected or disconnected :  
```xml
<CheckBox IsChecked="{my:NetworkAvailable}" Content="Network is available" />
```
  Now let's put the same `CheckBox` in a `ControlTemplate` :  
```xml
<ControlTemplate x:Key="test">
  <CheckBox IsChecked="{my:NetworkAvailable}" Content="Network is available" />
</ControlTemplate>
```
  And let's create a control which uses this template :  
```xml
<Control Template="{StaticResource test}" />
```
  If we disconnect from the network, we notice that the `CheckBox` is not automatically updated by the `NetworkAvailableExtension`, whereas it was working fine when we used it outside the template...  **Explanation and solution**  The markup expression is evaluated when it is encountered by the XAML parser : in that case, when the template is parsed. But at this time, the `CheckBox` control is not created yet, so the `ProvideValue` method can't access it... When a markup extension is evaluated inside a template, the `TargetObject` is actually an instance of `System.Windows.SharedDp`, an internal WPF class.  For the markup extension to be able to access its target, it has to be evaluated when the template is applied : we need to defer its evaluation until this time. It's actually pretty simple, we just need to return the markup extension itself from `ProvideValue` : this way, it will be evaluated again when the actual target control is created.  To check if the extension is evaluated for the template or for a "real" control, we just need to test whether the type of the `TargetObject` is `System.Windows.SharedDp`. So the code of the `ProvideValue` method becomes :  
```csharp
        public sealed override object ProvideValue(IServiceProvider serviceProvider)
        {
            IProvideValueTarget target = serviceProvider.GetService(typeof(IProvideValueTarget)) as IProvideValueTarget;
            if (target != null)
            {
                if (target.TargetObject.GetType().FullName == "System.Windows.SharedDp")
                    return this;
                _targetObject = target.TargetObject;
                _targetProperty = target.TargetProperty;
            }

            return ProvideValueInternal(serviceProvider);
        }
```
  Cool, it's now fixed, the `CheckBox` is updated when the network connectivity changes :)  **Last, but not least**  OK, we have a solution that apparently works fine, but let's not count our chickens before they're hatched... What if we now want to use our `ControlTemplate` on several controls ?  
```xml
<Control Template="{StaticResource test}" />
<Control Template="{StaticResource test}" />
```
  Now let's run the application and unplug the network cable : the second `CheckBox` is updated, but the first one is not...  The reason for this is simple : there are two `CheckBox` controls, but only one instance of `NetworkAvailableExtension`, shared between all instances of the template. Now, `NetworkAvailableExtension` can only reference one target object, so only the last one for which `ProvideValue` has been called is kept...  So we need to keep track of not one target object, but a collection of target objects, which will all be update by the `UpdateValue` method. Here's the final code of the `UpdatableMarkupExtension` base class :  
```csharp
    public abstract class UpdatableMarkupExtension : MarkupExtension
    {
        private List<object> _targetObjects = new List<object>();
        private object _targetProperty;

        protected IEnumerable<object> TargetObjects
        {
            get { return _targetObjects; }
        }

        protected object TargetProperty
        {
            get { return _targetProperty; }
        }

        public sealed override object ProvideValue(IServiceProvider serviceProvider)
        {
            // Retrieve target information
            IProvideValueTarget target = serviceProvider.GetService(typeof(IProvideValueTarget)) as IProvideValueTarget;

            if (target != null && target.TargetObject != null)
            {
                // In a template the TargetObject is a SharedDp (internal WPF class)
                // In that case, the markup extension itself is returned to be re-evaluated later
                if (target.TargetObject.GetType().FullName == "System.Windows.SharedDp")
                    return this;

                // Save target information for later updates
                _targetObjects.Add(target.TargetObject);
                _targetProperty = target.TargetProperty;
            }

            // Delegate the work to the derived class
            return ProvideValueInternal(serviceProvider);
        }

        protected virtual void UpdateValue(object value)
        {
            if (_targetObjects.Count > 0)
            {
                // Update the target property of each target object
                foreach (var target in _targetObjects)
                {
                    if (_targetProperty is DependencyProperty)
                    {
                        DependencyObject obj = target as DependencyObject;
                        DependencyProperty prop = _targetProperty as DependencyProperty;

                        Action updateAction = () => obj.SetValue(prop, value);

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
                        prop.SetValue(target, value, null);
                    }
                }
            }
        }

        protected abstract object ProvideValueInternal(IServiceProvider serviceProvider);
    }
```
  The `UpdatableMarkupExtension` is now fully functional... until proved otherwise ;). This class makes a good starting point for any markup extension that needs to update its target, without having to worry about the low-level aspects of tracking and updating target objects.

