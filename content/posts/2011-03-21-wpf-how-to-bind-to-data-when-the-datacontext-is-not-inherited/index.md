---
layout: post
title: '[WPF] How to bind to data when the DataContext is not inherited'
date: 2011-03-21T00:00:00.0000000
url: /2011/03/21/wpf-how-to-bind-to-data-when-the-datacontext-is-not-inherited/
tags:
  - binding
  - datacontext
  - freezable
  - WPF
categories:
  - Tips and tricks
  - WPF
---

The `DataContext` property in WPF is extremely handy, because it is automatically inherited by all children of the element where you assign it; therefore you don't need to set it again on each element you want to bind. However, in some cases the `DataContext` is not accessible: it happens for elements that are not part of the visual or logical tree. It can be very difficult then to bind a property on those elements...

Let's illustrate with a simple example: we want to display a list of products in a `DataGrid`. In the grid, we want to be able to show or hide the Price column, based on the value of a `ShowPrice` property exposed by the ViewModel. The obvious approach is to bind the `Visibility` of the column to the `ShowPrice` property:

```xml
<DataGridTextColumn Header="Price" Binding="{Binding Price}" IsReadOnly="False"
                    Visibility="{Binding ShowPrice,
                        Converter={StaticResource visibilityConverter}}"/>
```

Unfortunately, changing the value of `ShowPrice` has no effect, and the column is always visible... why? If we look at the Output window in Visual Studio, we notice the following line:

> System.Windows.Data Error: 2 : Cannot find governing FrameworkElement or FrameworkContentElement for target element. BindingExpression:Path=ShowPrice; DataItem=null; target element is 'DataGridTextColumn' (HashCode=32685253); target property is 'Visibility' (type 'Visibility')

The message is rather cryptic, but the meaning is actually quite simple: WPF doesn't know which `FrameworkElement` to use to get the `DataContext`, because the column doesn't belong to the visual or logical tree of the `DataGrid`.

We can try to tweak the binding to get the desired result, for instance by setting the RelativeSource to the `DataGrid` itself:

```xml
<DataGridTextColumn Header="Price" Binding="{Binding Price}" IsReadOnly="False"
                    Visibility="{Binding DataContext.ShowPrice,
                        Converter={StaticResource visibilityConverter},
                        RelativeSource={RelativeSource FindAncestor, AncestorType=DataGrid}}"/>
```

Or we can add a `CheckBox` bound to `ShowPrice`, and try to bind the column visibility to the `IsChecked` property by specifying the element name:

```xml
<DataGridTextColumn Header="Price" Binding="{Binding Price}" IsReadOnly="False"
                    Visibility="{Binding IsChecked,
                        Converter={StaticResource visibilityConverter},
                        ElementName=chkShowPrice}"/>
```

But none of these workarounds seems to work, we always get the same result...

At this point, it seems that the only viable approach would be to change the column visibility in code-behind, which we usually prefer to avoid when using the MVVM pattern... But I'm not going to give up so soon, at least not while there are other options to consider ;)

The solution to our problem is actually quite simple, and takes advantage of the [`Freezable`](http://msdn.microsoft.com/en-us/library/system.windows.freezable.aspx) class. The primary purpose of this class is to define objects that have a modifiable and a read-only state, but the interesting feature in our case is that `Freezable` objects can inherit the `DataContext` even when they're not in the visual or logical tree. I don't know the exact mechanism that enables this behavior, but we're going to take advantage of it to make our binding work...

The idea is to create a class (I called it `BindingProxy` for reasons that should become obvious very soon) that inherits `Freezable` and declares a `Data` dependency property:

```csharp
    public class BindingProxy : Freezable
    {
        #region Overrides of Freezable

        protected override Freezable CreateInstanceCore()
        {
            return new BindingProxy();
        }

        #endregion

        public object Data
        {
            get { return (object)GetValue(DataProperty); }
            set { SetValue(DataProperty, value); }
        }

        // Using a DependencyProperty as the backing store for Data.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty DataProperty =
            DependencyProperty.Register("Data", typeof(object), typeof(BindingProxy), new UIPropertyMetadata(null));
    }
```

We can then declare an instance of this class in the resources of the `DataGrid`, and bind the `Data` property to the current `DataContext`:

```xml
<DataGrid.Resources>
    <local:BindingProxy x:Key="proxy" Data="{Binding}" />
</DataGrid.Resources>
```

The last step is to specify this `BindingProxy` object (easily accessible with `StaticResource`) as the `Source` for the binding:

```xml
<DataGridTextColumn Header="Price" Binding="{Binding Price}" IsReadOnly="False"
                    Visibility="{Binding Data.ShowPrice,
                        Converter={StaticResource visibilityConverter},
                        Source={StaticResource proxy}}"/>
```

Note that the binding path has been prefixed with "Data", since the path is now relative to the `BindingProxy` object.

The binding now works correctly, and the column is properly shown or hidden based on the `ShowPrice` property.
