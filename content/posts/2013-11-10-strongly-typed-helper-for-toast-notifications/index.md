---
layout: post
title: Strongly typed helper for toast notifications
date: 2013-11-10T00:00:00.0000000
url: /2013/11/10/strongly-typed-helper-for-toast-notifications/
tags:
  - notification
  - toast
  - windows store
  - winrt
categories:
  - WinRT
---


Windows 8 provides an API for showing toast notifications. Unfortunately, it’s very cumbersome: to define the content of a notification, you must use a predefined template that is provided in the form of an `XmlDocumen`t, and set the value for each field in the XML. There is nothing in the API to let you know which fields the template defines, you need to check the [toast template catalog](http://msdn.microsoft.com/en-us/library/windows/apps/hh761494.aspx) in the documentation. It would be much more convenient to have a strongly typed API…

So I created a simple wrapper around the standard toast API. It can be used like this:

```csharp
var content = new ToastContent.ImageAndText02
{
    Image = "ms-appx:///Images/dotnet.png",
    Title = "Hello world!",
    Text = "Lorem ipsum dolor sit amet, consectetur adipisicing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.",
};
var notifier = ToastNotificationManager.CreateToastNotifier();
notifier.Show(content.CreateNotification());
```

Note that I kept the original names from the toast template catalog, because sufficiently descriptive names would have been too long. I included XML documentation comments on each class to make it easier to choose the correct template.

If you want more flexibility than a strongly typed template can provide, but don’t want to manipulate the template’s XML, you can use the `ToastContent` class directly:

```csharp
var content = new ToastContent(ToastTemplateType.ToastImageAndText02);
content.SetImage(1, "ms-appx:///Images/dotnet.png");
content.SetText(1, "Hello world!");
content.SetText(2, "Lorem ipsum dolor sit amet, consectetur adipisicing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.");
var notifier = ToastNotificationManager.CreateToastNotifier();
notifier.Show(content.CreateNotification());
```


The code is [available on GitHub](https://github.com/thomaslevesque/ToastHelper), along with a demo app. A [NuGet package](https://www.nuget.org/packages/ToastHelper/) is also available.

A point of interest is how I created the template classes: I could have done it manually, but it would have been quite tedious. So instead I extracted the toast templates to an XML file, I added some extra information (property names, description for XML doc comments) in the XML, and created a T4 template to generate the classes automatically from the XML file.

