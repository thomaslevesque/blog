---
layout: post
title: '[WPF] Declare global hotkeys in XAML with NHotkey'
date: 2014-02-05T00:00:00.0000000
url: /2014/02/05/wpf-declare-global-hotkeys-in-xaml-with-nhotkey/
tags:
  - global
  - hotkey
  - windows forms
  - WPF
  - XAML
categories:
  - Libraries
---


A common requirement for desktop applications is to handle system-wide hotkeys, in order to intercept keyboard shortcuts even when they don’t have focus. Unfortunately, there is no built-in feature in the .NET framework to do it.

Of course, this is not a new issue, and there are quite a few open-source libraries that address it (e.g. [VirtualInput](https://github.com/SaqibS/VirtualInput)). Most of them rely on a global system hook, which allow them to intercept *all* keystrokes, even the ones you’re not interested in. I used some of those libraries before, but I’m not really happy with them:

- they’re often tied to a specific UI framework (usually Windows Forms), which makes them a bit awkward to use in another UI framework (like WPF)
- I don’t really like the approach of intercepting all keystrokes. It usually means that you end up with a big method with lots of `if/else if` to decide what to do based on which keys were pressed.


A better option, in my opinion, is to listen only to the keys you’re interested in, and declare what to do for each of those. The approach used in WPF for key bindings is quite elegant:

```xml
<Window.InputBindings>
    <KeyBinding Gesture="Ctrl+Alt+Add" Command="{Binding IncrementCommand}" />
    <KeyBinding Gesture="Ctrl+Alt+Subtract" Command="{Binding DecrementCommand}" />
</Window.InputBindings>
```

But of course, key bindings are not global, they require that your app has focus… What if we could change that?

[NHotkey](https://github.com/thomaslevesque/NHotkey) is a very simple hotkey library that enables global key bindings. All you have to do is set an attached property to true on your key bindings:

```xml
<Window.InputBindings>
    <KeyBinding Gesture="Ctrl+Alt+Add" Command="{Binding IncrementCommand}"
                HotkeyManager.RegisterGlobalHotkey="True" />
    <KeyBinding Gesture="Ctrl+Alt+Subtract" Command="{Binding DecrementCommand}"
                HotkeyManager.RegisterGlobalHotkey="True" />
</Window.InputBindings>
```

And that’s it; the commands defined in the key bindings will now be invoked even if your app doesn’t have focus!

You can also use NHotkey from code:

```csharp
HotkeyManager.Current.AddOrReplace("Increment", Key.Add, ModifierKeys.Control | ModifierKeys.Alt, OnIncrement);
HotkeyManager.Current.AddOrReplace("Decrement", Key.Subtract, ModifierKeys.Control | ModifierKeys.Alt, OnDecrement);
```

The library takes advantage of the `RegisterHotkey` function. Because it also supports Windows Forms, it is split into 3 parts, so that you don’t need to reference the WinForms assembly from a WPF app or vice versa:

- The core library, which handles the hotkey registration itself, independently of any specific UI framework. This library is not directly usable, but is used by the other two.
- The WinForms-specific API, which uses the `Keys` enumeration from `System.Windows.Forms`
- The WPF-specific API, which uses the `Key` and `ModifierKeys` enumerations from `System.Windows.Input`, and supports global key bindings in XAML.


If you install the library from Nuget, add either the [NHotkey.Wpf](http://www.nuget.org/packages/NHotkey.Wpf/) or the [NHotkey.WindowsForms](http://www.nuget.org/packages/NHotkey.WindowsForms/) package; the core package will be added automatically.

