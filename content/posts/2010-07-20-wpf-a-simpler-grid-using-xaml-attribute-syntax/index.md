---
layout: post
title: '[WPF] A simpler Grid using XAML attribute syntax'
date: 2010-07-20T20:51:10.0000000
url: /2010/07/20/wpf-a-simpler-grid-using-xaml-attribute-syntax/
tags:
  - grid
  - WPF
  - XAML
categories:
  - Code sample
  - WPF
---

The `Grid` control is one of the most frequently used containers in WPF. It allows to layout elements easily in rows and columns. Unfortunately the code to declare it, while simple to write, is made quite awkward by the use of the property element syntax:  
```xml

<Grid>
    <Grid.RowDefinitions>
        <RowDefinition Height="Auto"/>
        <RowDefinition Height="5"/>
        <RowDefinition Height="*"/>
    </Grid.RowDefinitions>
    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="60" />
        <ColumnDefinition Width="*" />
    </Grid.ColumnDefinitions>
    
    <Label Content="Name" Grid.Row="0" Grid.Column="0" />
    <TextBox Text="Hello world" Grid.Row="0" Grid.Column="1"/>
    <Rectangle Fill="Black" Grid.Row="1" Grid.ColumnSpan="2"/>
    <Label Content="Image" Grid.Row="2" Grid.Column="0" />
    <Image Source="Resources/Desert.jpg" Grid.Row="2" Grid.Column="1" />
</Grid>
```
  In that example, more than half the code is made of the grid definition ! Even though this syntax offers a great flexibility and a precise control of the layout, in mot cases we just need to define the height of rows and the width of columns... so it would be much simpler if we could declare the grid using the attribute syntax, as follows:  
```xml

<Grid Rows="Auto,5,*" Columns="60,*">
    ...
</Grid>
```
  This article shows how to reach that goal, by creating a `SimpleGrid` class derived from `Grid`.  First of all, our class needs two new properties: `Rows` and `Columns`. These properties define the heights and widths of rows and columns, respectively. These dimensions are not just numbers: values such as `"*"`, `"2*"` ou `"Auto"` are valid dimensions for grid bands. WPF has a specific type to represent these values: the `GridLength` structure. So our new properties will be collections of `GridLength` objects. Here's the signature of the `SimpleGrid` class:  
```csharp

public class SimpleGrid : Grid
{
    public IList<GridLength> Rows { get; set; }
    public IList<GridLength> Columns { get; set; }
}
```
  Since these properties are in charge of defining the grid's rows and columns, they have to modify the `RowDefinitions` and `ColumnDefinitions` properties of the base class. Here's how to implement them to get the desired result :  
```csharp

        private IList<GridLength> _rows;
        public IList<GridLength> Rows
        {
            get { return _rows; }
            set
            {
                _rows = value;
                RowDefinitions.Clear();
                if (_rows == null)
                    return;
                foreach (var length in _rows)
                {
                    RowDefinitions.Add(new RowDefinition { Height = length });
                }
            }
        }

        private IList<GridLength> _columns;
        public IList<GridLength> Columns
        {
            get { return _columns; }
            set
            {
                _columns = value;
                ColumnDefinitions.Clear();
                if (_columns == null)
                    return;
                foreach (var length in _columns)
                {
                    ColumnDefinitions.Add(new ColumnDefinition { Width = length });
                }
            }
        }
```
  At this point, our `SimpleGrid` is already usable... from C# code, which doesn't really help us since we're trying to make the *XAML* code simpler. So we need to find a way to declare the values of these properties in XAML attributes, which isn't obvious since they are collections...  In XAML, all attributes are written in the form of strings. To convert these strings to values of the required type, WPF makes use of converters, which are classes derived from `TypeConverter`, associated with each type which supports conversion to and from other types. For instance, the converter for the `GridLength` structure is the `GridLengthConverter` class, which can convert numbers and strings to `GridLength` objects, and back. The conversion mechanism is described in more detail in [this MSDN article](http://msdn.microsoft.com/en-us/library/aa970913.aspx).  So we need to create a converter and associate it to the type of the `Rows` and `Columns` properties. Since we don't have control over the `IList<T>` type, we'll start by creating a specific `GridLengthCollection` type to be used instead of `IList<GridLength>`, and we'll associate a custom converter with it (`GridLengthCollectionConverter`):  
```csharp

    [TypeConverter(typeof(GridLengthCollectionConverter))]
    public class GridLengthCollection : ReadOnlyCollection<GridLength>
    {
        public GridLengthCollection(IList<GridLength> lengths)
            : base(lengths)
        {
        }
    }
```
  Why is that collection read-only ? That just because allowing to add or remove rows and columns would make the implementation more complex, and it wouldn't bring any benefit for our objective, which is to make it easier to define a `Grid` in XAML. So, let's keep it simple, at least for now... The `ReadOnlyCollection<T>` does exactly what we need, so we just inherit from it, rather than reinventing the wheel.  Notice the use of the `TypeConverter` attribute: that's how we tell the framework which converter should be used with the `GridLengthCollection` type. Now, all we need to do is to implement that converter :  
```csharp

    public class GridLengthCollectionConverter : TypeConverter
    {
        public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType)
        {
            if (sourceType == typeof(string))
                return true;
            return base.CanConvertFrom(context, sourceType);
        }

        public override bool CanConvertTo(ITypeDescriptorContext context, Type destinationType)
        {
            if (destinationType == typeof(string))
                return true;
            return base.CanConvertTo(context, destinationType);
        }

        public override object ConvertFrom(ITypeDescriptorContext context, System.Globalization.CultureInfo culture, object value)
        {
            string s = value as string;
            if (s != null)
                return ParseString(s, culture);
            return base.ConvertFrom(context, culture, value);
        }

        public override object ConvertTo(ITypeDescriptorContext context, CultureInfo culture, object value, Type destinationType)
        {
            if (destinationType == typeof(string) && value is GridLengthCollection)
                return ToString((GridLengthCollection)value, culture);
            return base.ConvertTo(context, culture, value, destinationType);
        }

        private string ToString(GridLengthCollection value, CultureInfo culture)
        {
            var converter = new GridLengthConverter();
            return string.Join(",", value.Select(v => converter.ConvertToString(v)));
        }

        private GridLengthCollection ParseString(string s, CultureInfo culture)
        {
            var converter = new GridLengthConverter();
            var lengths = s.Split(',').Select(p => (GridLength)converter.ConvertFromString(p.Trim()));
            return new GridLengthCollection(lengths.ToArray());
        }
    }
```
  This class can converte a `GridLengthCollection` to and from a string, in which individual dimensions are separated by commas. Notice the use of the `GridLengthConverter`: since there already is a converter for the elements of the collections, we'd better use it rather than try to reimplement the logic to parse a `GridLength`...  Now that all pieces are ready, we can try our new simple grid:  
```xml

<my:SimpleGrid Rows="Auto,5,*" Columns="60,*">
    <Label Content="Name" Grid.Row="0" Grid.Column="0" />
    <TextBox Text="Hello world" Grid.Row="0" Grid.Column="1"/>
    <Rectangle Fill="Black" Grid.Row="1" Grid.ColumnSpan="2"/>
    <Label Content="Image" Grid.Row="2" Grid.Column="0" />
    <Image Source="Resources/Desert.jpg" Grid.Row="2" Grid.Column="1" />
</my:SimpleGrid>
```
  We end up with a much shorter and more readable code than with a normal `Grid`, and the result is the same: mission complete :)  Of course, we could improve this class in a number of ways: implement `Rows` and `Columns` as dependency properties in order to allow binding, handle addition and removal of rows and columns... However, this grid is intended for very simple scenarios, where the grid is defined once and for all, and is not modified at runtime (which is presumably the most frequent use case), so it seems sensible to keep it as simple as possible. For more specific needs, like specifying a minimum/maximum width or a shared sized group, we'll stick to the standard `Grid`.  For reference, here's the final code of the `SimpleGrid` class:  
```csharp

    public class SimpleGrid : Grid
    {
        private GridLengthCollection _rows;
        public GridLengthCollection Rows
        {
            get { return _rows; }
            set
            {
                _rows = value;
                RowDefinitions.Clear();
                if (_rows == null)
                    return;
                foreach (var length in _rows)
                {
                    RowDefinitions.Add(new RowDefinition { Height = length });
                }
            }
        }

        private GridLengthCollection _columns;
        public GridLengthCollection Columns
        {
            get { return _columns; }
            set
            {
                _columns = value;
                if (_columns == null)
                    return;
                ColumnDefinitions.Clear();
                foreach (var length in _columns)
                {
                    ColumnDefinitions.Add(new ColumnDefinition { Width = length });
                }
            }
        }
    }
```

