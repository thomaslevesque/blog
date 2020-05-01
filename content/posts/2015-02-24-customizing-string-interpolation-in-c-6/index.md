---
layout: post
title: Customizing string interpolation in C# 6
date: 2015-02-24T02:00:00.0000000
url: /2015/02/24/customizing-string-interpolation-in-c-6/
tags:
  - C# 6
  - iformattable
  - roslyn
  - string interpolation
categories:
  - Uncategorized
---


One of the major new features in C# 6 is string interpolation, which allows you to write things like this:

```
string text = $"{p.Name} was born on {p.DateOfBirth:D}";
```

A lesser known aspect of this feature is that an interpolated string can be treated either as a `String`, or as an `IFormattable`, depending on the context. When it is converted to an `IFormattable`, it constructs a `FormattableString` object that implements the interface and exposes:

- the format string with the placeholders (“holes”) replaced by numbers (compatible with `String.Format`)
- the values for the placeholders


The `ToString()` method of this object just calls `String.Format(format, values)`. ``But there is also an overload that accepts an `IFormatProvider`, and this is where things get interesting, because it makes it possible to customize how the values are formatted. It might not be immediately obvious why this is useful, so let me give you a few examples…

### Specifying the culture

During the design of the string interpolation feature, there was a lot of debate on whether to use the current culture or the invariant culture to format the values; there were good arguments on both sides, but eventually it was decided to use the current culture, for consistency with `String.Format` and similar APIs that use [composite formatting](https://msdn.microsoft.com/en-us/library/txafckwd.aspx). Using the current culture makes sense when you’re using string interpolation to build strings to be displayed in the user interface; but there are also scenarios where you want to build strings that will be consumed by an API or protocol (URLs, SQL queries…), and in those cases you usually want to use the invariant culture.

C# 6 provides an easy way to do that, by taking advantage of the conversion to `IFormattable`. You just need to create a method like this:

```
static string Invariant(FormattableString formattable)
{
    return formattable.ToString(CultureInfo.InvariantCulture);
}
```

And you can then use it as follows:

```
string text = Invariant($"{p.Name} was born on {p.DateOfBirth:D}");
```

The values in the interpolated strings will now be formatted with the invariant culture, rather than the default culture.

### Building URLs

Here’s a more advanced example. String interpolation is a convenient way to build URLs, but if you include arbitrary strings in a URL, you need to be careful to URL-encode them. A custom string interpolator can do that for you; you just need to create a custom `IFormatProvider` that will take care of encoding the values. The implementation was not obvious at first, but after some trial and error I came up with this:

```
class UrlFormatProvider : IFormatProvider
{
    private readonly UrlFormatter _formatter = new UrlFormatter();

    public object GetFormat(Type formatType)
    {
        if (formatType == typeof(ICustomFormatter))
            return _formatter;
        return null;
    }

    class UrlFormatter : ICustomFormatter
    {
        public string Format(string format, object arg, IFormatProvider formatProvider)
        {
            if (arg == null)
                return string.Empty;
            if (format == "r")
                return arg.ToString();
            return Uri.EscapeDataString(arg.ToString());
        }
    }
}
```

You can use the formatter like this:

```
static string Url(FormattableString formattable)
{
    return formattable.ToString(new UrlFormatProvider());
}

...

string url = Url($"http://foobar/item/{id}/{name}");
```

It will correctly encode the values of `id` and `name` so that the resulting URL only contains valid characters.

*Aside: Did you notice the if `(format == "r")`? It’s a custom format specifier to indicate that the value should not be encoded (“r” stands for “raw”). To use it you just include it in the format string like this: `{id:r}`. This will prevent the encoding of `id`.*

### Building SQL queries

You can do something similar for SQL queries. Of course, it’s a known bad practice to embed values directly in the query, for security and performance reasons (you should use parameterized queries instead); but for “quick and dirty” developments it can still be useful. And anyway, it’s a good illustration for the feature. When embedding values in a SQL queries, you should:

- enclose strings in single quotes, and escape single quotes inside the strings by doubling them
- format dates according to what the DBMS expects (typically MM/dd/yyyy)
- format numbers using the invariant culture
- replace null values with the `NULL` literal


(there are probably other things to take care of, but these are the most obvious).

We can use the same approach as for URLs and create a `SqlFormatProvider`:

```
class SqlFormatProvider : IFormatProvider
{
    private readonly SqlFormatter _formatter = new SqlFormatter();

    public object GetFormat(Type formatType)
    {
        if (formatType == typeof(ICustomFormatter))
            return _formatter;
        return null;
    }

    class SqlFormatter : ICustomFormatter
    {
        public string Format(string format, object arg, IFormatProvider formatProvider)
        {
            if (arg == null)
                return "NULL";
            if (arg is string)
                return "'" + ((string)arg).Replace("'", "''") + "'";
            if (arg is DateTime)
                return "'" + ((DateTime)arg).ToString("MM/dd/yyyy") + "'";
            if (arg is IFormattable)
                return ((IFormattable)arg).ToString(format, CultureInfo.InvariantCulture);
            return arg.ToString();
        }
    }
}
```

You can then use the formatter like this:

```
static string Sql(FormattableString formattable)
{
    return formattable.ToString(new SqlFormatProvider());
}

...

string sql = Sql($"insert into items(id, name, creationDate) values({id}, {name}, {DateTime.Now})");
```

This will take care of properly formatting the values to produce a valid SQL query.

### Using string interpolation when targeting older versions of .NET

As is often the case for language features that leverage .NET framework types, you can use this feature with older versions of the framework that don’t have the `FormattableString` class; you just have to create the class yourself in the appropriate namespace. Actually, there are two classes to implement: `FormattableString` and `FormattableStringFactory`. [Jon Skeet was apparently in a hurry to try this](https://twitter.com/jonskeet/status/569973023064887296), and he has already [provided an example](https://gist.github.com/jskeet/9d297d0dc013d7a557ee) with the code for these classes:



```
using System;

namespace System.Runtime.CompilerServices
{
    public class FormattableStringFactory
    {
        public static FormattableString Create(string messageFormat, params object[] args)
        {
            return new FormattableString(messageFormat, args);
        }

        public static FormattableString Create(string messageFormat, DateTime bad, params object[] args)
        {
            var realArgs = new object[args.Length + 1];
            realArgs[0] = "Please don't use DateTime";
            Array.Copy(args, 0, realArgs, 1, args.Length);
            return new FormattableString(messageFormat, realArgs);
        }
    }
}

namespace System
{
    public class FormattableString
    {
        private readonly string messageFormat;
        private readonly object[] args;

        public FormattableString(string messageFormat, object[] args)
        {
            this.messageFormat = messageFormat;
            this.args = args;
        }
        public override string ToString()
        {
            return string.Format(messageFormat, args);
        }
    }
}
```

This is the same approach that made it possible to use Linq when targeting .NET 2 ([LinqBridge](http://www.albahari.com/nutshell/linqbridge.aspx)) or [caller info attributes when targeting .NET 4](http://www.thomaslevesque.com/2012/06/13/using-c-5-caller-info-attributes-when-targeting-earlier-versions-of-the-net-framework/) or earlier. Of course, it still requires the C# 6 compiler to work…

### Conclusion

The conversion of interpolated strings to `IFormattable` [had been mentioned previously](https://roslyn.codeplex.com/discussions/570614), but it wasn’t implemented until recently; the [just released CTP 6 of Visual Studio 2015](http://blogs.msdn.com/b/visualstudio/archive/2015/02/23/visual-studio-2015-ctp-6-and-team-foundation-server-2015-ctp-released.aspx) ships with a new version of the compiler that includes this feature, so you can now go ahead and use it. This feature makes string interpolation very flexible, and I’m sure people will come up with many other use cases that I didn’t think of.

You can find the code for the examples above [on GitHub](https://github.com/thomaslevesque/blog-code-samples/tree/master/TestStringInterpolation).

