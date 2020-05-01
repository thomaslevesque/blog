---
layout: post
title: Easy unit testing of null argument validation (C# 8 edition)
date: 2019-11-19T07:00:39.0000000
url: /2019/11/19/easy-unit-testing-of-null-argument-validation-c-8-edition/
tags:
  - argument validation
  - C#
  - C# 8
  - non-nullable reference types
  - null check
  - unit testing
categories:
  - Uncategorized
---


A few years ago, I blogged about [a way to automate unit testing of null argument validation](https://thomaslevesque.com/2014/11/02/easy-unit-testing-of-null-argument-validation/). Its usage looked like this:

```csharp
[Fact]
public void FullOuterJoin_Throws_If_Argument_Is_Null()
{
    var left = Enumerable.Empty<int>();
    var right = Enumerable.Empty<int>();
    TestHelper.AssertThrowsWhenArgumentNull(
        () => left.FullOuterJoin(right, x => x, y => y, (k, x, y) => 0, 0, 0, null),
        "left", "right", "leftKeySelector", "rightKeySelector", "resultSelector");
}
```

Basically, for each of the specified parameters, the `AssertThrowsWhenArgumentNull` method rewrites the lambda expression by replacing the corresponding argument with `null`, compiles and executes it, and checks that it throws an `ArgumentNullException` with the appropriate parameter name. This method has served me well for many years, as it drastically reduces the amount of code to test argument validation. However, I wasn't completely satisfied with it, because I still had to specify the names of the non-nullable parameters explicitly…

## C# 8 to the rescue

Yesterday, I was working on enabling C# 8 non-nullable reference types on an old library, and I realized that I could take advantage of the nullable metadata to automatically detect which parameters are non-nullable.

Basically, when you compile a library with nullable reference types enabled, method parameters can be annotated with a `[Nullable(x)]` attribute, where `x` is a byte value that indicates the nullability of the parameter (it's actually slightly more complicated than that, see [Jon Skeet's article](https://codeblog.jonskeet.uk/2019/02/10/nullableattribute-and-c-8/) on the subject). Additionally, there can be a `[NullableContext(x)]`  attribute on the method or type that indicates the default nullability for the method or type; if a parameter doesn't have the `[Nullable]` attribute, the default nullability applies.

Using these facts, it's possible to update my old `AssertThrowsWhenArgumentNull` method to make it detect non-nullable parameters automatically. Here's the result:

```csharp
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using FluentAssertions;

static class TestHelper
{
    private const string NullableContextAttributeName = "System.Runtime.CompilerServices.NullableContextAttribute";
    private const string NullableAttributeName = "System.Runtime.CompilerServices.NullableAttribute";

    public static void AssertThrowsWhenArgumentNull(Expression<Action> expr)
    {
        var realCall = expr.Body as MethodCallExpression;
        if (realCall == null)
            throw new ArgumentException("Expression body is not a method call", nameof(expr));
        
        var method = realCall.Method;
        var nullableContextAttribute =
            method.CustomAttributes
            .FirstOrDefault(a => a.AttributeType.FullName == NullableContextAttributeName)
            ??
            method.DeclaringType.GetTypeInfo().CustomAttributes
            .FirstOrDefault(a => a.AttributeType.FullName == NullableContextAttributeName);

        if (nullableContextAttribute is null)
            throw new InvalidOperationException($"The method '{method}' is not in a nullable enable context. Can't determine non-nullable parameters.");

        var defaultNullability = (Nullability)(byte)nullableContextAttribute.ConstructorArguments[0].Value;

        var realArgs = realCall.Arguments;
        var parameters = method.GetParameters();
        var paramIndexes = parameters
            .Select((p, i) => new { p, i })
            .ToDictionary(x => x.p.Name, x => x.i);
        var paramTypes = parameters
            .ToDictionary(p => p.Name, p => p.ParameterType);

        var nonNullableRefParams = parameters
            .Where(p => !p.ParameterType.GetTypeInfo().IsValueType && GetNullability(p, defaultNullability) == Nullability.NotNull);

        foreach (var param in nonNullableRefParams)
        {
            var paramName = param.Name;
            var args = realArgs.ToArray();
            args[paramIndexes[paramName]] = Expression.Constant(null, paramTypes[paramName]);
            var call = Expression.Call(realCall.Object, method, args);
            var lambda = Expression.Lambda<Action>(call);
            var action = lambda.Compile();
            action.ShouldThrow<ArgumentNullException>($"because parameter '{paramName}' is not nullable")
                .And.ParamName.Should().Be(paramName);
        }
    }

    private enum Nullability
    {
        Oblivious = 0,
        NotNull = 1,
        Nullable = 2
    }

    private static Nullability GetNullability(ParameterInfo parameter, Nullability defaultNullability)
    {
        if (parameter.ParameterType.GetTypeInfo().IsValueType)
            return Nullability.NotNull;

        var nullableAttribute = parameter.CustomAttributes
            .FirstOrDefault(a => a.AttributeType.FullName == NullableAttributeName);

        if (nullableAttribute is null)
            return defaultNullability;

        var firstArgument = nullableAttribute.ConstructorArguments.First();
        if (firstArgument.ArgumentType == typeof(byte))
        {
            var value = (byte)firstArgument.Value;
            return (Nullability)value;
        }
        else
        {
            var values = (ReadOnlyCollection<CustomAttributeTypedArgument>)firstArgument.Value;

            // Probably shouldn't happen
            if (values.Count == 0)
                return defaultNullability;

            var value = (byte)values[0].Value;

            return (Nullability)value;
        }
    }
}
```

The unit test is now even simpler, since there's no need to specify the parameters to validate:

```csharp
[Fact]
public void FullOuterJoin_Throws_If_Argument_Is_Null()
{
    var left = Enumerable.Empty<int>();
    var right = Enumerable.Empty<int>();
    TestHelper.AssertThrowsWhenArgumentNull(
        () => left.FullOuterJoin(right, x => x, y => y, (k, x, y) => 0, 0, 0, null));
}
```

It will automatically check that each non-nullable parameter is properly validated.

Happy coding!

