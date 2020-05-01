---
layout: post
title: Easy unit testing of null argument validation
date: 2014-11-02T15:24:06.0000000
url: /2014/11/02/easy-unit-testing-of-null-argument-validation/
tags:
  - argument validation
  - C#
  - nunit
  - unit testing
categories:
  - Uncategorized
---


When unit testing a method, one of the things to test is argument validation : for instance, ensure that the method throws a `ArgumentNullException` when a null argument is passed for a parameter that isn’t allowed to be null. Writing this kind of test is very easy, but it’s also a tedious and repetitive task, especially if the method has many parameters… So I wrote a method that automates part of this task: it tries to pass null for each of the specified arguments, and asserts that the method throws an `ArgumentNullException`. Here’s an example that tests a `FullOuterJoin` extension method:

```

[Test]
public void FullOuterJoin_Throws_If_Argument_Null()
{
    var left = Enumerable.Empty<int>();
    var right = Enumerable.Empty<int>();
    TestHelper.AssertThrowsWhenArgumentNull(
        () => left.FullOuterJoin(right, x => x, y => y, (k, x, y) => 0, 0, 0, null),
        "left", "right", "leftKeySelector", "rightKeySelector", "resultSelector");
}
```

The first parameter is a lambda expression that represents how to call the method. In this lambda, you should only pass valid arguments. The following parameters are the names of the parameters that are not allowed to be null. For each of the specified names, `AssertThrowsWhenArgumentNull` will replace the corresponding argument with null in the provided lambda, compile and invoke the lambda, and assert that the method throws a `ArgumentNullException`.

Using this method, instead of writing a test for each of the arguments that are not allowed to be null, you only need one test.

Here’s the code for the `TestHelper.AssertThrowsWhenArgumentNull` method (you can also find it on [Gist](https://gist.github.com/thomaslevesque/c4cb9f537316b122f5b9)):

```

using System;
using System.Linq;
using System.Linq.Expressions;
using NUnit.Framework;

namespace MyLibrary.Tests
{
    static class TestHelper
    {
        public static void AssertThrowsWhenArgumentNull(Expression<TestDelegate> expr, params string[] paramNames)
        {
            var realCall = expr.Body as MethodCallExpression;
            if (realCall == null)
                throw new ArgumentException("Expression body is not a method call", "expr");

            var realArgs = realCall.Arguments;
            var paramIndexes = realCall.Method.GetParameters()
                .Select((p, i) => new { p, i })
                .ToDictionary(x => x.p.Name, x => x.i);
            var paramTypes = realCall.Method.GetParameters()
                .ToDictionary(p => p.Name, p => p.ParameterType);
            
            

            foreach (var paramName in paramNames)
            {
                var args = realArgs.ToArray();
                args[paramIndexes[paramName]] = Expression.Constant(null, paramTypes[paramName]);
                var call = Expression.Call(realCall.Method, args);
                var lambda = Expression.Lambda<TestDelegate>(call);
                var action = lambda.Compile();
                var ex = Assert.Throws<ArgumentNullException>(action, "Expected ArgumentNullException for parameter '{0}', but none was thrown.", paramName);
                Assert.AreEqual(paramName, ex.ParamName);
            }
        }

    }
}
```

Note that it is written for NUnit, but can easily be adapted to other unit test frameworks.

I used this method in my [Linq.Extras](https://github.com/thomaslevesque/Linq.Extras) library, which provides many additional extension methods for working with sequences and collections (including the `FullOuterJoin` method mentioned above).

