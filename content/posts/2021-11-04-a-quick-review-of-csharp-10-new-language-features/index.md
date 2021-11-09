---
layout: post
title: "A quick review of C# 10 new language features"
date: 2021-11-04
url: /2021/11/04/a-quick-review-of-csharp-10-new-language-features/
tags:
  - C#
  - C# 10
  - language
  - features
---

.NET 6.0 and C# 10 are just around the corner, so now is a good time to review some of the most interesting new language features!

## Record structs

📄 [Proposal](https://github.com/dotnet/csharplang/blob/main/proposals/csharp-10.0/record-structs.md)

Records were introduced in C# 9 as a simple way to define data types with value equality semantics, for instance:

```csharp
public record Money(decimal Amount, string CurrencyCode);
```

An annoying limitation was that records were always reference types, but in some scenarios it would have been better to use value types. C# 10 fixes this by allowing the declaration of record structs:

```csharp
public readonly record struct Money(decimal Amount, string CurrencyCode);
```

Note that this also supports the `readonly` struct modifier if the record is immutable.

This is one of the most useful features of C# 10 in my opinion, as it makes it extremely easy to define immutable custom structs, without having to manually implement `Equals` and `GetHashCode`.

## Static abstract members in interfaces

📄 [Proposal](https://github.com/dotnet/csharplang/issues/4436)

OK, I know what you're thinking. Static members in interfaces? Static abstract members? What does that even mean? Yes, that's weird, but it'll make more sense after you see the use cases!

(Note that interfaces can already have static methods since C# 8 and the introduction of [Default Interface Methods](https://docs.microsoft.com/en-us/dotnet/csharp/whats-new/csharp-8#default-interface-methods). These methods are typically used by default implementations of interface methods.)

Many types have static methods such as `Parse` or `Create`, or operators such as `+` or `-` (which are actually static methods). However, there's currently no way to use those members in generic code, because they can't be represented in interfaces or base classes. The goal of this feature is to be able to abstract these members in interfaces so that they can be used in generic code.

The most obvious example is numbers: if you want to write a method that computes the sum of several numbers, it has to specify which type of number it's working on (`int`, `decimal`...). You can't write a generic method that works for all types of number, because there's no way to express the fact that the generic type argument must have an addition operator. In C# 10, it becomes possible to declare this at the interface level:

```csharp
public interface IAddable<T> where T : IAddable<T>
{
    static abstract T Zero { get; }
    static abstract T operator +(T t1, T t2);
}
```

Assuming this interface is implemented by all numeric types, this makes it possible to write a method like this, that can compute the sum of numbers of any type:

```csharp
public static T Sum<T>(params T[] numbers) where T : IAddable<T>
{
    T sum = T.Zero;
    foreach (T number in numbers)
    {
        sum += number;
    }
    return sum;
}
```

.NET 6.0 actually introduces several interfaces such a `INumber<T>` and makes the built-in numeric types implement them.

Another example is parsing. .NET 6.0 also introduces an `IParseable<T>` interface, implemented by numeric types, `DateTime`, `Guid`, etc., which makes it possible to write generic code that parses values of any compatible type.

**Note:** this is a preview feature, and still will be when .NET 6.0 is released. To use it, you will need to:

- enable preview features by setting the `EnablePreviewFeatures` property to true in your project:
  ```xml
  <PropertyGroup>
    <EnablePreviewFeatures>true</EnablePreviewFeatures>
  </PropertyGroup>
  ```
- reference the [System.Runtime.Experimental package](https://www.nuget.org/packages/system.runtime.experimental):
  ```xml
  <ItemGroup>
    <PackageReference Include="System.Runtime.Experimental" Version="6.0.0-preview.7.21377.19" />
  </ItemGroup>
  ```

## Caller argument expression attribute

📄 [Proposal](https://github.com/dotnet/csharplang/blob/main/proposals/csharp-10.0/caller-argument-expression.md)

This is something I've been wanting for a looong time! The new `[CallerArgumentExpression]` attribute lets you capture the expression passed to a method as a string. It's similar to the `[CallerMemberName]` attribute, and is used like this:

```csharp
public static void LogExpression<T>(T value, [CallerArgumentExpression("value")] string expression = null)
{
    Console.WriteLine($"{expression}: {value}");
}

...
var person = new Person("Thomas", "Levesque");
LogExpression(person.FirstName); // Outputs "person.FirstName: Thomas"
```

The `expression` parameter is optional, and if it isn't specified, the compiler will automatically pass the expression used as the `value` parameter, as a string. In other words, the call to `LogExpression` in the code above is equivalent to doing this:

```csharp
LogExpression(person.FirstName, "person.FirstName");
```

A common use case is a method that checks that an argument isn't null:

```csharp
public static void EnsureArgumentIsNotNull<T>(T value, [CallerArgumentExpression("value")] string expression = null)
{
    if (value is null)
        throw new ArgumentNullException(expression);
}

public static void Foo(string name)
{
    EnsureArgumentIsNotNull(name); // if name is null, throws ArgumentNullException: "Value cannot be null. (Parameter 'name')"
    ...
}
```

Until now, you had to manually pass the parameter name to `EnsureArgumentIsNotNull`.

Note that you won't actually need to create such a method in .NET 6.0: it introduces a new `ArgumentNullException.ThrowIfNull` method that does exactly the same thing.

## Lambda improvements

📄 [Proposal](https://github.com/dotnet/csharplang/blob/main/proposals/csharp-10.0/lambda-improvements.md)

C# 10 brings a few improvements to lambda expressions:

### Apply attributes to lambda expression

Currently, only named methods (including local functions) can have attributes; in C# 10, lambda expression can have them as well:

```csharp
Func<int, bool> isEven = [Pure] n => n % 2 == 0;
```

### Automatically infer a "natural" type for a lambda

Historically, C# lambda expressions didn't have an intrinsic type. The expression `(int n) => n % 2 == 0` didn't have a type on its own; the type was determined based on what it was assigned to, e.g.:

```csharp
Func<int, bool> isEven = (int n) => n % 2 == 0;
```

Now, the compiler will try to automatically infer a "natural" delegate type for a lambda, making it possible to use `var` to make the code more concise and readable:

```csharp
// isEven is implicitly of type Func<int, bool>
var isEven = (int n) => n % 2 == 0;
```

The inferred type will always be a variant of the `System.Func<...>` or `System.Action<...>` delegate types, depending on the parameters and return type. Note that the compiler will only be able to infer a type if it has all the information it needs, especially the parameter types; it can't infer a type for `n => n % 2 == 0`, since it doesn't know the type of `n`.

### Explicitly specify the return type for a lambda

C# 10 makes it possible to specify a return type for a lambda. This wasn't necessary before, since a lambda had to be assigned to a specific delegate type anyway, but with natural delegate type inference, there might be cases where you want the return type to be different from what is automatically inferred. For instance

```csharp
var oneTwoThreeArray = () => new[]{1, 2, 3}; // inferred type is Func<int[]>
var oneTwoThreeList = IList<int> () => new[]{1, 2, 3}; // same body, but inferred type is now Func<IList<int>>
```

## Extended property patterns

📄 [Proposal](https://github.com/dotnet/csharplang/blob/main/proposals/csharp-10.0/extended-property-patterns.md)

Pattern matching was introduced in C# 7.0, and has been improved in every major language version since then. C# 10 is no exception, and makes it possible to use more concise property patterns. For instance, before C# 10, if you wanted to check nested properties in a pattern, you had to write something like this:

```csharp
if (person is { LastName: { Length: > 30} })
    Console.WriteLine("What a long name!");
```

This was a little more verbose than we'd like. With extended property patterns, you can now write it like this:

```csharp
if (person is { LastName.Length: > 30 })
    Console.WriteLine("What a long name!");
```

## File-scoped namespace

📄 [Proposal](https://github.com/dotnet/csharplang/blob/main/proposals/csharp-10.0/file-scoped-namespaces.md)

In a typical C# source file, most of the code is indented because it's inside a namespace declaration. It's been like this for so long we don't even notice it anymore, but it's a waste of horizontal space! C# 10 fixes this by letting you declare a namespace for the whole file, without the need for braces:

```csharp
namespace MyApp;
```

Note that it only works for files that contain a single namespace (which is probably the case for 99.9% of C# files).

## Global usings and implicit usings

📄 [Proposal](https://github.com/dotnet/csharplang/blob/main/proposals/csharp-10.0/GlobalUsingDirective.md)

You know how every source file in your projects begins with a wall of `using` directives, which are frequently the same across many files? Well, good news, it's now possible to write these directives once and for all, for all files in the project! Just prefix the using directives you want to apply everywhere with `global`:

```csharp
global using System.Linq.Expressions;
global using System.Reflection;
```

This can be done either in a separate file (e.g. `GlobalUsings.cs`), or in an existing file (e.g. `Program.cs`).

Another interesting option (which is actually an SDK feature rather than a language feature) is "implicit usings". Basically, depending on the type of project, the SDK will implicitly include global usings for the most commonly used namespaces, such as `System`, `System.Linq` (for all projects), `Microsoft.AspNetCore.Http`, `Microsoft.Extensions.Logging` (for ASP.NET Core projects), etc. This feature is opt-in, you need to enable it in your project by setting the `ImplicitUsings` property to `enable`:

```xml
<PropertyGroup>
  <ImplicitUsings>enable</ImplicitUsings>
</PropertyGroup>
```

Note that you can also add global usings manually in your project file, like this:

```xml
<ItemGroup>
  <Using Include="System.Linq.Expressions" />
  <Using Include="System.Reflection" />
</ItemGroup>
```

## Parameterless constructors and field initializers in structs

📄 [Proposal](https://github.com/dotnet/csharplang/blob/main/proposals/csharp-10.0/parameterless-struct-constructors.md)

This one has been on the radar for a _very_ long time (I think it was already proposed for C# 6), but was always pushed back due to various design issues.

Until now, it was not legal to explicitly declare a default (parameterless) constructor for a struct, so `new MyStruct()` was just equivalent to `default(MyStruct)`. Field initializers were also not allowed in structs.

With C# 10, it becomes possible to write a struct like this:

```csharp
struct MyStruct
{
    public int Value { get; set; } = 42;
}
```

Or like this:

```csharp
struct MyStruct
{
    public MyStruct()
    {
        Value = 42;
    }

    public int Value { get; set; }
}
```

And it does exactly what you would expect: `new MyStruct()` creates an instance of the struct and invokes the parameterless constructor and field initializers.

Keep in mind that it doesn't affect the `default` keyword: `default(MyStruct)` still creates an uninitialized instance of the struct, with all fields set to their default values. **It does not invoke the parameterless constructor**, so `default(MyStruct)` is no longer equivalent to `new MyStruct()`.

Also note that the parameterless constructor must be `public`.

## Mix declarations and variables in deconstruction

📄 [Proposal](https://github.com/dotnet/csharplang/issues/125)

Currently, when deconstructing a tuple or record (or any type with an appropriate `Deconstruct` method), we can to assign the result either to new variables or existing ones, but not a mix of both. C# 10 relaxes this constraint to allow this.

```csharp
var p = new Point(10, 20);
(int x1, int y1) = p; // OK

int x2;
int y2;
(x2, y2) = p; // OK

int x3;
(x3, int y3) = p; // Was illegal, but is allowed in C# 10
```

## Other features

There are a few other new features in C# 10, but I won't present them in detail here, because they're either anecdotal or pretty obscure for the average developer. Here's a quick rundown.

### Sealed record ToString

📄 [Proposal](https://github.com/dotnet/csharplang/issues/4174)

It's now possible to make the `ToString` method `sealed` in non-sealed records. It's a small thing, but it was my first contribution to the C# compiler!

### Improved definite assignment

📄 [Proposal](https://github.com/dotnet/csharplang/blob/main/proposals/csharp-10.0/improved-definite-assignment.md)

The definite assignment analysis rules have been improved to cover a few cases where the compiler was unable to detect that a variable was definitely assigned. In all likelihood, you will never notice this feature, but it might prevent you from hitting some weird definite assignment errors in the future.

### AsyncMethodBuilder override

📄 [Proposal](https://github.com/dotnet/csharplang/blob/main/proposals/csharp-10.0/async-method-builders.md)

Async method builder is a mechanism for defining custom "task-like" types that can be returned from `async` methods. This feature allows overriding which builder is used on a per-method basis. That's pretty advanced stuff, and most of us will probably never need to use this.

### Enhanced #line directive

📄 [Proposal](https://github.com/dotnet/csharplang/blob/main/proposals/csharp-10.0/enhanced-line-directives.md)

The `#line` directive is typically used in generated code (e.g. code generated from Razor files) to specify the original file name and line number that will be reported in compiler diagnostics and debug information. In C# 10, this directive has been extended to also specify the location on the line. You probably won't need this, unless you're writing advanced DSLs or code generators.

### Constant interpolated strings

📄 [Proposal](https://github.com/dotnet/csharplang/blob/main/proposals/csharp-10.0/constant_interpolated_strings.md)

It's now possible to use string interpolation to define constants, if the interpolated string only references other string constants:

```csharp
const string Name = "World";
const string Hello = $"Hello {Name}";
```

Nice, but probably not a game-changer...

### Interpolated string improvements

📄 [Proposal](https://github.com/dotnet/csharplang/blob/main/proposals/csharp-10.0/improved-interpolated-strings.md)

This feature improves the code emitted by the compiler for interpolated strings.

Currently, the compiler just emits a call to `String.Format`, which isn't ideal in terms of performance and memory allocation. This feature introduces the concept of interpolated string handlers, which are objects designed to efficiently build strings from interpolated strings. I won't go into the details here, because you won't really need to know about it except in very specific scenarios.

### Incremental source generators

📄 [Proposal](https://github.com/chsienki/roslyn/blob/main/docs/features/incremental-generators.md)

This is a new kind of source generator that breaks the source generation process into granular steps, for better performance. To be honest, it looks pretty complex, and I haven't looked at it in detail yet. Maybe in a future article!
