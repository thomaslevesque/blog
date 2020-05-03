---
layout: post
title: '[VS2010] Binding support in InputBindings'
date: 2009-10-26T00:00:00.0000000
url: /2009/10/26/vs2010-binding-support-in-inputbindings/
tags:
  - .NET 4.0
  - binding
  - InputBinding
  - KeyBinding
  - MVVM
  - Visual Studio
  - Visual Studio 2010
  - WPF
  - WPF 4.0
  - XAML
categories:
  - WPF
---

**THE feature that was missing from WPF !**  Visual Studio 2010 beta 2 has been released last week, and it brings to WPF a long awaited feature : support for bindings in `InputBindings`.  As a reminder, the issue in previous releases was that the `Command` property of the `InputBinding` class wasn't a `DependencyProperty`, so it wasn't possible to bind it. Furthermore, `InputBindings` didn't inherit the parent DataContext, which made it difficult to provide alternative implementations...  Until now, in order to bind the `Command` of a `KeyBinding` or `MouseBinding` to a property of the `DataContext`, we had to resort to clumsy workarounds... I had eventually came up with an acceptable solution, described in [this post](/2009/03/17/wpf-using-inputbindings-with-the-mvvm-pattern/), but I wasn't really satisfied with it (it used reflection on private members, and had annoying limitations).  More recently, I found a better solution in the [MVVM toolkit](http://www.codeplex.com/wpf/Release/ProjectReleases.aspx?ReleaseId=14962) : a `CommandReference` class, inherited from `Freezable`, allows to put a reference to a ViewModel command in the page or control resources, so that it can be used later with `StaticResource`. It's much cleaner than my previous solution, but still not very straightforward...  WPF 4.0 solves that problem once and for all : the `InputBinding` class now inherits from `Freezable`, which allows it to inherit the `DataContext`, and the `Command`, `CommandParameter` and `CommandTarget` properties are now dependency properties. So, at last, we can forget about the clumsy workarounds described above, and go straight to the point :  
```xml
    <Window.InputBindings>
        <KeyBinding Key="F5"
                    Command="{Binding RefreshCommand}" />
    </Window.InputBindings>
```
  This new feature should make it much easier to develop MVVM applications !  **Help 3**  Other than that, I would like to say a few words about the new offline help system that comes with Visual Studio 2010, called "Help 3". It's quite a big change compared to previous versions... First, it's not a standalone application anymore, but a locally hosted web application, so you can access the documentation with your favorite web browser. On the whole, it's better than the previous system... much faster and more responsive than the old Document Explorer included in previous Visual Studio releases.  However, the new system misses the feature that was the most useful to me : the index ! Now there's only the hierarchical view, and a search textbox. IMHO, the index was the most convenient way of looking up something in the doc, it made it very easy to access a class or member directly, even without knowing its namespace... why on earth did they remove it ? Worse still : the search results don't show the namespace, only the class or member name. For instance, if you search "button class", in the results there is no way to see the difference between `System.Windows.Forms.Button`, `System.Windows.Controls.Button` and `System.Web.UI.WebControls` ! You have to click each link and see where it leads... In Document Explorer, the Index Results pane showed this information clearly.  So, eventually I have mixed feelings about this new help system, because I will have to change the way I use the documentation. But except for this annoying detail,  I must concede that it's objectively a big improvement over the old system...

