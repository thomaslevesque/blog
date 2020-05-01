---
layout: post
title: '[WinRT] Toggle selection of a list item on long press'
date: 2013-11-21T21:41:59.0000000
url: /2013/11/21/winrt-toggle-selection-of-a-list-item-on-long-press/
tags:
  - gesture
  - GridView
  - hold
  - ListView
  - long press
  - selection
  - winrt
categories:
  - WinRT
---


As you probably know, the standard way to select or deselect an item in a WinRT list control is to slide it up or down a little. Although I rather like this gesture, it’s not very intuitive for users unfamiliar with Modern UI. And it gets even more confusing, because my previous statement wasn’t perfectly accurate: in fact, you have to slide the item *perpendicularly to the panning direction*. In a `GridView`, which (by default) pans horizontally, that means up or down; but in a `ListView`, which pans vertically, you have to slide the item left or right. If an application uses both kinds of lists, it becomes *very* confusing for the user.

Sure, in the default style, there is visual hint (a discrete “slide down” animation with a gray tick symbol) when the user presses and holds an item, but it’s not always enough for everyone to understand. Many people (e.g. Android users) are used to do a “long press” gesture (known as “Hold” in Modern UI terminology) to select items. So, in order to make your app easier to use for a larger number of people, you might want to enable selection by long press.

A simple way to do it is to create an attached property which, when set to `true`, subscribes to the `Holding` event of an item, and toggles the `IsSelected` property when the event occurs. Here’s a possible implementation:

```
using Windows.UI.Input;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;

namespace TestSelectOnHold
{
    public static class SelectorItemEx
    {
        public static bool GetToggleSelectedOnHold(SelectorItem item)
        {
            return (bool)item.GetValue(ToggleSelectedOnHoldProperty);
        }

        public static void SetToggleSelectedOnHold(SelectorItem item, bool value)
        {
            item.SetValue(ToggleSelectedOnHoldProperty, value);
        }

        public static readonly DependencyProperty ToggleSelectedOnHoldProperty =
            DependencyProperty.RegisterAttached(
              "ToggleSelectedOnHold",
              typeof(bool),
              typeof(SelectorItemEx),
              new PropertyMetadata(
                false,
                ToggleSelectedOnHoldChanged));

        private static void ToggleSelectedOnHoldChanged(DependencyObject o, DependencyPropertyChangedEventArgs e)
        {
            var item = o as SelectorItem;
            if (item == null)
                return;

            var oldValue = (bool)e.OldValue;
            var newValue = (bool)e.NewValue;

            if (oldValue && !newValue)
            {
                item.Holding -= Item_Holding;
            }
            else if (newValue && !oldValue)
            {
                item.Holding += Item_Holding;
            }
        }

        private static void Item_Holding(object sender, HoldingRoutedEventArgs e)
        {
            var item = sender as SelectorItem;
            if (item == null)
                return;

            if (e.HoldingState == HoldingState.Started)
                item.IsSelected = !item.IsSelected;
        }
    }
}
```

You can then set this property in the `ItemContainerStyle` of the list control

```
<GridView.ItemContainerStyle>
    <Style TargetType="GridViewItem">
        ...
        <Setter Property="local:SelectorItemEx.ToggleSelectedOnHold" Value="False" />
    </Style>
</GridView.ItemContainerStyle>
```

And you’re done : the user can now select items by holding them. The standard gesture still works, of course, so users who know it can still use it.

Note that this feature could also have been implemented as a full-fledged `Behavior`. There are two reasons why I didn’t choose this approach:

- Behaviors are not natively supported in WinRT (though they can be added as a [Nuget package](http://www.nuget.org/packages/Windows.UI.Interactivity/))
- Behaviors don’t play well with styles, because `Interaction.Behaviors` is a collection, and you can’t add items to a collection from a style. A possible workaround would be to create an `IsEnabled` attached property that would add the behavior to the item when set to true, but then we would end up with a solution almost identical to the one described above, only more complex…


