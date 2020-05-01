---
layout: post
title: 'StringTemplate: another approach to string interpolation'
date: 2014-11-17T00:00:00.0000000
url: /2014/11/17/stringtemplate-another-approach-to-string-interpolation/
tags:
  - C# 6
  - localization
  - NString
  - string interpolation
  - StringTemplate
categories:
  - Libraries
---


With the upcoming version 6 of C#, there’s a lot of talk on CodePlex and elsewhere about string interpolation. Not very surprising, since it’s one of the major features of that release… In case you were living under a rock during the last few months and you haven’t heard about it, string interpolation is a way to insert C# expressions inside a string, so that they’re evaluated at runtime and replaced with their values. Basically, you write something like this:

```
string text = $"{p.Name} was born on {p.DateOfBirth:D}";
```

And the compiler transforms it to this:

```
string text = String.Format("{0} was born on {1:D}", p.Name, p.DateOfBirth);
```

**Note**: the syntax shown above is the one from the [latest design notes about this feature](http://roslyn.codeplex.com/discussions/570292). It might still change before the final release, and the current preview build of VS2015 uses a different syntax: `“\{p.Name} was born on \{p.DateOfBirth:D}”.`

I really *love* this feature. It’s going to be extremely convenient for things like logging, generating URLs or queries, etc. I will probably use it a lot, especially since Microsoft has listened to community feedback and included a way to customize how the embedded expressions are evaluated (see the part about `IFormattable` in the [design notes](http://roslyn.codeplex.com/discussions/570292)).

But there’s one thing that bothers me, though: since interpolated strings are interpreted by the compiler, they *have* to be hard-coded ; you can’t extract them to resources for localization. This means that this feature cannot be used for localization, and we’re stuck with old-fashioned numeric placeholders in localized strings.

Or are we really?

For a few years now, I’ve been using a [custom string interpolation engine](https://github.com/thomaslevesque/NString#stringtemplate) that can be used like `String.Format`, but with named placeholders instead of numeric ones. It takes a format string, and an object with properties that match the placeholder names:

```
string text = StringTemplate.Format("{Name} was born on {DateOfBirth:D}", new { p.Name, p.DateOfBirth });
```

Obviously, if you already have an object with the properties you want to include in the string, you can just pass that object directly:

```
string text = StringTemplate.Format("{Name} was born on {DateOfBirth:D}", p);
```

The result is exactly what you would expect: the placeholders are replaced with the values of the corresponding properties.

In which ways is it better than `String.Format`?

- It’s much more readable: a named placeholder tells you immediately which value will go there
- It’s less error-prone: you don’t need to pay attention to the order of the values to be formatted
- When you extract the format strings to resources for localization, the translator sees a name in the placeholder, not a number. This gives more context to the string, and makes it easier to understand what the final string will look like.


Note that you can use the same format specifiers as in `String.Format`. The `StringTemplate` class parses your format string into one compatible with `String.Format`, extracts the property values into an array, and calls `String.Format`.

Of course, parsing the string and extracting the property values with reflection every time would be very inefficient, so there are a some optimizations:

- each distinct format string is only parsed once, and the result of the parsing is added to a cache, to be reused every time.
- for each property used in a format string, a getter delegate is generated and cached, to avoid using reflection every time.


This means that the first time you use a given format string, there will be the overhead of parsing and generating the delegates, but subsequent usages of the same format string will be much faster.

The `StringTemplate` class is part of a library called [NString](https://github.com/thomaslevesque/NString), which also contains a few extension methods to make string manipulations easier. The library is a PCL that can be used with all .NET flavors except Silverlight 5. A NuGet package is available [here](https://www.nuget.org/packages/NString/).

