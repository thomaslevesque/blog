---
layout: post
title: What's new in FakeItEasy 3.0.0?
date: 2017-02-20T20:32:30.0000000
url: /2017/02/20/whats-new-in-fakeiteasy-3-0-0/
tags:
  - fakeiteasy
  - mocking
  - unit testing
categories:
  - Uncategorized
---


[FakeItEasy](https://fakeiteasy.github.io/) is a popular mocking framework for .NET, with an very intuitive and easy-to-use API. For about one year, I've been a maintainer of FakeItEasy, along with [Adam Ralph](https://github.com/adamralph/) and [Blair Conrad](https://github.com/blairconrad/). It's been a real pleasure working with them and I had a lot of fun!

**Today I'm glad to announce that we're releasing [FakeItEasy 3.0.0](https://github.com/FakeItEasy/FakeItEasy/releases/tag/3.0.0), which supports .NET Core and introduces a few useful features.**

Let's see what's new!

## .NET Core support

In addition to .NET 4+, FakeItEasy now supports .NET Standard 1.6, so you can use it in .NET Core projects.

Note that due to limitations in .NET Standard 1.x, there are some minor differences with the .NET 4 version:

- Fakes are not binary serializable;
- Self-initializing fakes are not supported (i.e. `fakeService = A.Fake<IService>(options => options.Wrapping(realService).RecordedBy(recorder)`).


Huge thanks to the people who made .NET Core support possible:

- [Jonathon Rossi](https://github.com/jonorossi), who maintains [Castle.Core](https://github.com/castleproject/Core). FakeItEasy relies heavily on Castle.Core, so it couldn't have supported .NET Core if Castle.Core didn't.
- [Jeremy Meng](https://github.com/jeremymeng) from Microsoft, who did most of the heavy lifting to make both FakeItEasy 3.0.0 and Castle.Core 4.0.0 work on .NET Core.


## Analyzer

### VB.NET support

The [FakeItEasy analyzer](http://fakeiteasy.readthedocs.io/en/stable/analyzer/), which warns you of incorrect usage of the library, now supports VB.NET as well as C#.

## New or improved features

### Better syntax for configuring successive calls to the same member

When you configure calls on a fake, it creates rules that are "stacked" on each other, which means you can override a previously configured rule. Combined with the ability to specify the number of times a rule must apply, this lets you say things like "return 42 twice, then throw an exception". Until now, to do that you had to configure the calls in reverse order, which wasn't very intuitive and meant you had to repeat the call specification:

```csharp

A.CallTo(() => foo.Bar()).Throws(new Exception("oops"));
A.CallTo(() => foo.Bar()).Returns(42).Twice();
```

FakeItEasy 3.0.0 introduces a new syntax to make this easier:

```csharp

A.CallTo(() => foo.Bar()).Returns(42).Twice()
    .Then.Throws(new Exception("oops"));
```

Note that if you don't specify how many times the rule must apply, it will apply forever until explicitly overridden. Hence, you can only use `Then` after `Once()`, `Twice()` or `NumberOfTimes(...)`.

This is a breaking change at the API level, as the shape of the configuration interfaces has changed, but unless you manipulate those interfaces explicitly, you shouldn't be affected.

### Automatic support for cancellation

When a method accepts a `CancellationToken`, it should usually throw an exception when it's called with a token that is already canceled. Previously this behavior had to be configured manually. In FakeItEasy 3.0.0, fake methods will now throw an `OperationCanceledException` by default when called with a canceled token. Asynchronous methods will return a canceled task.

This is technically a breaking change, but most users are unlikely to be affected.

### Throw asynchronously

FakeItEasy lets you configure a method to throw an exception with `Throws`. But for async methods, there are actually two ways to "throw":

- throw an exception synchronously, before actually returning a task (this is what `Throws` does)
- return a failed task (which had to be done manually until now)


In some cases the difference can be important to the caller, if it doesn't directly await the async method. FakeItEasy 3.0.0 introduces a `ThrowsAsync` method to configure a method to return a failed task:

```csharp

A.CallTo(() => foo.BarAsync()).ThrowsAsync(new Exception("foo"));
```

### Configure property setters on unnatural fakes

Unnatural fakes (i.e. `Fake<T>`) now have a `CallsToSet` method, which does the same as `A.CallToSet` on natural fakes:

```csharp

var fake = new Fake<IFoo>();
fake.CallsToSet(foo => foo.Bar).To(0).Throws(new Exception("The value of Bar can't be 0"));
```

### Better API for specifying additional attributes

The syntax to specify additional attributes on fakes was a bit unwieldy; you had to create a collection of `CustomAttributeBuilder`s, which themselves had to be created by specifying the constructor and argument values. The `WithAdditionalAttributes` method has been retired in FakeItEasy 3.0.0 and replaced with a simpler `WithAttributes` that accepts expressions:

```csharp

var foo = A.Fake<IFoo>(x => x.WithAttributes(() => new FooAttribute()));
```

This is a breaking change.

## Other notable changes

### Deprecation of self-initializing fakes

Self-initializing fakes are a feature that lets you record the results of calls to a real object, and replay them on a fake object. This feature was used by *very* few people, and didn't seem to be a good fit in the core FakeItEasy library, so it will be removed in a future version. We're considering providing the same functionality as a separate package.

### Bug fixes

- Faking a type multiple times and applying different attributes to the fakes now correctly generates different fake types. ([#436](https://github.com/FakeItEasy/FakeItEasy/issues/436))
- All non-void members attempt to return a Dummy by default, even after being reconfigured by `Invokes` or `DoesNothing` ([#830](https://github.com/FakeItEasy/FakeItEasy/issues/830))


* * *

The full list of changes for this release is available in the [release notes](https://github.com/FakeItEasy/FakeItEasy/releases/tag/3.0.0).

Other contributors to this release include:

- Artem Zinenko - [@ar7z1](https://github.com/ar7z1)
- Christian Merat - [@cmerat](https://github.com/cmerat)
- [@thunderbird55](https://github.com/thunderbird55)


A big thanks to them!

