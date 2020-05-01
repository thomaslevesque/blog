---
layout: post
title: '[WPF] Automatically sort a GridView when a column header is clicked'
date: 2009-03-27T01:00:31.0000000
url: /2009/03/27/wpf-automatically-sort-a-gridview-when-a-column-header-is-clicked/
tags:
  - attached property
  - GridView
  - MVVM
  - sort
  - WPF
categories:
  - Code sample
  - WPF
---

It's quite simple, in WPF, to present data in a grid, thanks to the `GridView` class. If you want to sort it, however, it gets a little harder... With the `DataGridView` in Windows Forms, it was "automagic" : when the user clicked a column header, the grid was automatically sorted. To achieve the same behavior in WPF, you need to get your hands dirty... The method recommended by Microsoft is described in [this article](http://msdn.microsoft.com/en-us/library/ms745786.aspx) ; it is based on the `Click` event of the `GridViewColumnHeader` class. In my view, this approach has two major drawbacks : 
- The sorting must be done in code-behind, something we usually want to avoid if the application is designed according to the MVVM pattern. It also makes the code harder to reuse.
- This method assumes that the text of the column header is also the name of the property to use as the sort criteria, which isn't always true, far from it... We could use the `DisplayMemberBinding` of the column, but it's not always set (for instance if a `CellTemplate` is defined instead).

  After spending a long time trying to find a flexible and elegant approach, I came up with an interesting solution. It consists of a class with a few attached properties that can be set in XAML.  This class can be used as follows :  
```xml
    <ListView ItemsSource="{Binding Persons}"
          IsSynchronizedWithCurrentItem="True"
          util:GridViewSort.AutoSort="True">
        <ListView.View>
            <GridView>
                <GridView.Columns>
                    <GridViewColumn Header="Name"
                                    DisplayMemberBinding="{Binding Name}"
                                    util:GridViewSort.PropertyName="Name"/>
                    <GridViewColumn Header="First name"
                                    DisplayMemberBinding="{Binding FirstName}"
                                    util:GridViewSort.PropertyName="FirstName"/>
                    <GridViewColumn Header="Date of birth"
                                    DisplayMemberBinding="{Binding DateOfBirth}"
                                    util:GridViewSort.PropertyName="DateOfBirth"/>
                </GridView.Columns>
            </GridView>
        </ListView.View>
    </ListView>
```
  The `GridViewSort.AutoSort` property enables automatic sorting for the `ListView`. The `GridViewSort.PropertyName` property, defined for each column, indicates the property to use as the sort criteria. There is no extra code to write. A click on a column header triggers the sorting on this column ; if the ListView is already sorted on this column, the sort order is reversed.  In case you need to handle the sorting manually, I also added a `GridViewSort.Command` attached property. When used with the MVVM pattern, this property allows you to bind to a command declared in the ViewModel :  
```xml
    <ListView ItemsSource="{Binding Persons}"
          IsSynchronizedWithCurrentItem="True"
          util:GridViewSort.Command="{Binding SortCommand}">
    ...
```
  The sort command takes as parameter the name of the property to use as the sort criteria.  Note : if both the `Command` and `AutoSort` properties are set, `Command` has priority. `AutoSort` is ignored.  Here is the full code of the `GridViewSort` class :  
```csharp
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Wpf.Util
{
    public class GridViewSort
    {
        #region Attached properties

        public static ICommand GetCommand(DependencyObject obj)
        {
            return (ICommand)obj.GetValue(CommandProperty);
        }

        public static void SetCommand(DependencyObject obj, ICommand value)
        {
            obj.SetValue(CommandProperty, value);
        }

        // Using a DependencyProperty as the backing store for Command.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty CommandProperty =
            DependencyProperty.RegisterAttached(
                "Command",
                typeof(ICommand),
                typeof(GridViewSort),
                new UIPropertyMetadata(
                    null,
                    (o, e) =>
                    {
                        ItemsControl listView = o as ItemsControl;
                        if (listView != null)
                        {
                            if (!GetAutoSort(listView)) // Don't change click handler if AutoSort enabled
                            {
                                if (e.OldValue != null && e.NewValue == null)
                                {
                                    listView.RemoveHandler(GridViewColumnHeader.ClickEvent, new RoutedEventHandler(ColumnHeader_Click));
                                }
                                if (e.OldValue == null && e.NewValue != null)
                                {
                                    listView.AddHandler(GridViewColumnHeader.ClickEvent, new RoutedEventHandler(ColumnHeader_Click));
                                }
                            }
                        }
                    }
                )
            );

        public static bool GetAutoSort(DependencyObject obj)
        {
            return (bool)obj.GetValue(AutoSortProperty);
        }

        public static void SetAutoSort(DependencyObject obj, bool value)
        {
            obj.SetValue(AutoSortProperty, value);
        }

        // Using a DependencyProperty as the backing store for AutoSort.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty AutoSortProperty =
            DependencyProperty.RegisterAttached(
                "AutoSort",
                typeof(bool),
                typeof(GridViewSort),
                new UIPropertyMetadata(
                    false,
                    (o, e) =>
                    {
                        ListView listView = o as ListView;
                        if (listView != null)
                        {
                            if (GetCommand(listView) == null) // Don't change click handler if a command is set
                            {
                                bool oldValue = (bool)e.OldValue;
                                bool newValue = (bool)e.NewValue;
                                if (oldValue && !newValue)
                                {
                                    listView.RemoveHandler(GridViewColumnHeader.ClickEvent, new RoutedEventHandler(ColumnHeader_Click));
                                }
                                if (!oldValue && newValue)
                                {
                                    listView.AddHandler(GridViewColumnHeader.ClickEvent, new RoutedEventHandler(ColumnHeader_Click));
                                }
                            }
                        }
                    }
                )
            );

        public static string GetPropertyName(DependencyObject obj)
        {
            return (string)obj.GetValue(PropertyNameProperty);
        }

        public static void SetPropertyName(DependencyObject obj, string value)
        {
            obj.SetValue(PropertyNameProperty, value);
        }

        // Using a DependencyProperty as the backing store for PropertyName.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty PropertyNameProperty =
            DependencyProperty.RegisterAttached(
                "PropertyName",
                typeof(string),
                typeof(GridViewSort),
                new UIPropertyMetadata(null)
            );

        #endregion

        #region Column header click event handler

        private static void ColumnHeader_Click(object sender, RoutedEventArgs e)
        {
            GridViewColumnHeader headerClicked = e.OriginalSource as GridViewColumnHeader;
            if (headerClicked != null)
            {
                string propertyName = GetPropertyName(headerClicked.Column);
                if (!string.IsNullOrEmpty(propertyName))
                {
                    ListView listView = GetAncestor<ListView>(headerClicked);
                    if (listView != null)
                    {
                        ICommand command = GetCommand(listView);
                        if (command != null)
                        {
                            if (command.CanExecute(propertyName))
                            {
                                command.Execute(propertyName);
                            }
                        }
                        else if (GetAutoSort(listView))
                        {
                            ApplySort(listView.Items, propertyName);
                        }
                    }
                }
            }
        }

        #endregion

        #region Helper methods

        public static T GetAncestor<T>(DependencyObject reference) where T : DependencyObject
        {
            DependencyObject parent = VisualTreeHelper.GetParent(reference);
            while (!(parent is T))
            {
                parent = VisualTreeHelper.GetParent(parent);
            }
            if (parent != null)
                return (T)parent;
            else
                return null;
        }

        public static void ApplySort(ICollectionView view, string propertyName)
        {
            ListSortDirection direction = ListSortDirection.Ascending;
            if (view.SortDescriptions.Count > 0)
            {
                SortDescription currentSort = view.SortDescriptions[0];
                if (currentSort.PropertyName == propertyName)
                {
                    if (currentSort.Direction == ListSortDirection.Ascending)
                        direction = ListSortDirection.Descending;
                    else
                        direction = ListSortDirection.Ascending;
                }
                view.SortDescriptions.Clear();
            }
            if (!string.IsNullOrEmpty(propertyName))
            {
                view.SortDescriptions.Add(new SortDescription(propertyName, direction));
            }
        }

        #endregion
    }
}
```
  Of course, this class could probably be improved... for instance, we could add an arrow glyph on the sorted column (maybe by using an `Adorner`). Maybe I'll do that someday... meanwhile, please feel free to use it ;)  **Update :** A new version that displays the sort glyph in the sorted column is now available in [this blog post](http://www.thomaslevesque.com/2009/08/04/wpf-automatically-sort-a-gridview-continued/).
