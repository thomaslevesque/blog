---
layout: post
title: '[WPF] Prevent the user from pasting an image in a RichTextBox'
date: 2015-09-05T00:00:00.0000000
url: /2015/09/05/wpf-prevent-the-user-from-pasting-an-image-in-a-richtextbox/
tags:
  - C#
  - clipboard
  - image
  - richtextbox
  - WPF
categories:
  - WPF
---


WPF’s RichTextBox control is quite powerful, and very handy if you need to accept rich text input. However, one of its features can become an issue: the user can paste an image. Depending on what you intend to do with the text entered by the user, you might not want that.

When I googled for a way to prevent that, the only solutions I found suggested to intercept the Ctrl-V keystroke, and swallow the event if the clipboard contains an image. There are several issues with this approach:

- it doesn’t prevent the user from pasting using the context menu
- it won’t work if the command’s shortcut has been changed
- it doesn’t prevent the user from inserting an image using drag and drop


Since I wasn’t satisfied with this solution, I used the [.NET Framework Reference Source website](http://referencesource.microsoft.com/) to look for a way to intercept the paste operation itself. I followed the code from the `ApplicationCommands.Paste` property, and eventually found the [DataObject.Pasting](https://msdn.microsoft.com/en-us/library/system.windows.dataobject.pasting.aspx)`` attached event. It’s not a place where I had thought to look, but when you think about it, it actually makes sense. This event can be used to intercept a paste or drag and drop operation, and lets the hander do a few things:

- cancel the operation completely
- change which data format will be pasted from the clipboard
- replace the `DataObject` used in the paste operation


In my case, I just wanted to prevent an image from being pasted or drag and dropped, so I just cancelled the operation when the `FormatToApply` was `"Bitmap"`, as shown below.

XAML:

```
<RichTextBox DataObject.Pasting="RichTextBox1_Pasting" ... />
```

Code-behind:

```
private void RichTextBox1_Pasting(object sender, DataObjectPastingEventArgs e)
{
    if (e.FormatToApply == "Bitmap")
    {
        e.CancelCommand();
    }
}
```

Of course, it’s also possible to handle this in a smarter way. For instance, if the `DataObject` contains several formats, we could create a new `DataObject` with only the acceptable formats. This way the user is still able to paste *something*, if not the image.

