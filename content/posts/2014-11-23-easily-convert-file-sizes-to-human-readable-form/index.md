---
layout: post
title: Easily convert file sizes to human-readable form
date: 2014-11-23T00:00:00.0000000
url: /2014/11/23/easily-convert-file-sizes-to-human-readable-form/
tags:
  - byte
  - C#
  - size
categories:
  - Libraries
---


If you write an application that has anything to do with file management, you will probably need to display the size of the files. But if a file has a size of 123456789 bytes, it doesn’t mean that you should just display this value to the user, because it’s hard to read, and the user usually doesn’t need 1-byte precision. Instead, you will write something like 118 MB.

This should be a no-brainer, but there are actually a number of different ways to display byte sizes… For instance, there are several co-existing conventions for units and prefixes:

- The [SI](http://en.wikipedia.org/wiki/International_System_of_Units) (International System of Units) convention uses decimal multiples, based on powers of 10: 1 kilobyte is 1000 bytes, 1 megabyte is 1000 kilobytes, etc. The prefixes are the one from the metric system (k, M, G, etc.).
- The [IEC convention](http://en.wikipedia.org/wiki/Binary_prefix#IEC_prefixes) uses binary multiples, based on powers of 2: 1 kibibyte is 1024 bytes, 1 mebibyte is 1024 kibibytes, etc. The prefixes are Ki, Mi, Gi etc., to avoid confusion with the metric system.
- But neither of these conventions is commonly used: the customary convention is to use binary mutiples (1024), but decimal prefixes (K, M, G, etc.).


Depending on the context, you might want to use either of these conventions. I’ve never seen the SI convention used anywhere; some apps (I’ve seen it in VirtualBox for instance) use the IEC convention; most apps and operating systems use the customary convention. You can read this Wikipedia article if you want more details: [Binary prefix](http://en.wikipedia.org/wiki/Binary_prefix).

OK, so let’s chose the customary convention for now. Now you have to decide which scale to use: do you want to write 0.11 GB, 118 MB, 120564 KB, or 123456789 B? Typically, the scale is chosen so that the displayed value is between 1 and 1024.

A few more things you might have to consider:

- Do you want to display integer values, or include a few decimal places?
- Is there a minimum unit to use (for instance, Windows never uses bytes: a 1 byte file is displayed as 1 KB)?
- How should the value be rounded?
- How do you want to format the value?
- for values less than 1KB, do you want to use the word “bytes”, or just the symbol “B”?


### OK, enough of this! What’s your point?

So as you can see, displaying a byte size in human-readable form isn’t as straightforward as you might have expected… I’ve had to write code to do it in a number of apps, and I eventually got tired of doing it again over and over, so I wrote a library that attempts to cover all use cases. I called it [HumanBytes](https://github.com/thomaslevesque/HumanBytes), for reasons that should be obvious… It is also available as a [NuGet package](https://www.nuget.org/packages/HumanBytes).

Its usage is quite simple. It’s based on a class named `ByteSizeFormatter`, which has a few properties to control how the value is rendered:

```csharp
var formatter = new ByteSizeFormatter
{
    Convention = ByteSizeConvention.Binary,
    DecimalPlaces = 1,
    NumberFormat = "#,##0.###",
    MinUnit = ByteSizeUnit.Kilobyte,
    MaxUnit = ByteSizeUnit.Gigabyte,
    RoundingRule = ByteSizeRounding.Closest,
    UseFullWordForBytes = true,
};

var f = new FileInfo("TheFile.jpg");
Console.WriteLine("The size of '{0}' is {1}", f, formatter.Format(f.Length));
```

In most cases, though, you will just want to use the default settings. You can do that easily with the `Bytes` extension method:

```csharp
var f = new FileInfo("TheFile.jpg");
Console.WriteLine("The size of '{0}' is {1}", f, f.Length.Bytes());
```

This method returns an instance of the `ByteSize` structure, whose `ToString` method formats the value using the default formatter. You can change the default formatter settings globally through the `ByteSizeFormatter.Default` static property.

### A note on localization

Not all languages use the same symbol for “byte”, and obviously the word “byte” itself is different across languages. Currently the library only supports English and French; if you want your language to be supported as well, please fork, add your translation, and make a pull request. There are only 3 terms to translate, so it shouldn’t take long ![Winking smile](wlEmoticon-winkingsmile.png).

