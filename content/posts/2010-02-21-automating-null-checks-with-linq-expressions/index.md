---
layout: post
title: Automating null checks with Linq expressions
date: 2010-02-21T00:00:00.0000000
url: /2010/02/21/automating-null-checks-with-linq-expressions/
tags:
  - C#
  - expression
  - linq
  - null check
  - proof of concept
categories:
  - Code sample
---

**The problem**  Have you ever written code like the following ?  
```csharp
X xx = GetX();
string name = "Default";
if (xx != null && xx.Foo != null && xx.Foo.Bar != null && xx.Foo.Bar.Baz != null)
{
    name = xx.Foo.Bar.Baz.Name;
}
```
  I bet you have ! You just need to get the value of `xx.Foo.Bar.Baz.Name`, but you have to test *every* intermediate object to ensure that it's not null. It can quickly become annoying if the property you need is nested in a deep object graph....  **A solution**  Linq offers a very interesting feature which can help solve that problem : expressions. C# 3.0 makes it possible to retrieve the abstract syntax tree (AST) of a lambda expression, and perform all kinds of manipulations on it. It is also possible to dynamically generate an AST, compile it to obtain a delegate, and execute it.  How is this related to the problem described above ? Well, Linq makes it possible to analyse the AST for the expression that accesses the `xx.Foo.Bar.Baz.Name` property, and rewrite that AST to insert null checks where needed. So we're going to create a `NullSafeEval` extension method, which takes as a parameter the lambda expression defining how to access a property, and the default value to return if a null object is encountered along the way.  That method will transform the expression `xx.Foo.Bar.Baz.Name` into that :  
```csharp
    (xx == null)
    ? defaultValue
    : (xx.Foo == null)
      ? defaultValue
      : (xx.Foo.Bar == null)
        ? defaultValue
        : (xx.Foo.Bar.Baz == null)
          ? defaultValue
          : xx.Foo.Bar.Baz.Name;
```
  Here's the implementation of the `NullSafeEval` method :  
```csharp
        public static TResult NullSafeEval<TSource, TResult>(this TSource source, Expression<Func<TSource, TResult>> expression, TResult defaultValue)
        {
            var safeExp = Expression.Lambda<Func<TSource, TResult>>(
                NullSafeEvalWrapper(expression.Body, Expression.Constant(defaultValue)),
                expression.Parameters[0]);

            var safeDelegate = safeExp.Compile();
            return safeDelegate(source);
        }

        private static Expression NullSafeEvalWrapper(Expression expr, Expression defaultValue)
        {
            Expression obj;
            Expression safe = expr;

            while (!IsNullSafe(expr, out obj))
            {
                var isNull = Expression.Equal(obj, Expression.Constant(null));

                safe =
                    Expression.Condition
                    (
                        isNull,
                        defaultValue,
                        safe
                    );

                expr = obj;
            }
            return safe;
        }

        private static bool IsNullSafe(Expression expr, out Expression nullableObject)
        {
            nullableObject = null;

            if (expr is MemberExpression || expr is MethodCallExpression)
            {
                Expression obj;
                MemberExpression memberExpr = expr as MemberExpression;
                MethodCallExpression callExpr = expr as MethodCallExpression;

                if (memberExpr != null)
                {
                    // Static fields don't require an instance
                    FieldInfo field = memberExpr.Member as FieldInfo;
                    if (field != null && field.IsStatic)
                        return true;

                    // Static properties don't require an instance
                    PropertyInfo property = memberExpr.Member as PropertyInfo;
                    if (property != null)
                    {
                        MethodInfo getter = property.GetGetMethod();
                        if (getter != null && getter.IsStatic)
                            return true;
                    }
                    obj = memberExpr.Expression;
                }
                else
                {
                    // Static methods don't require an instance
                    if (callExpr.Method.IsStatic)
                        return true;

                    obj = callExpr.Object;
                }

                // Value types can't be null
                if (obj.Type.IsValueType)
                    return true;

                // Instance member access or instance method call is not safe
                nullableObject = obj;
                return false;
            }
            return true;
        }
```
  In short, this code walks up the lambda expression tree, and surrounds each property access or instance method call with a conditional expression *(condition ? value if true : value if false)*.  And here's how we can use this method :  
```csharp
string name = xx.NullSafeEval(x => x.Foo.Bar.Baz.Name, "Default");
```
  Much clearer and concise than our initial code, isn't it ? :)  Note that the proposed implementation handles  not only properties, but also method calls, so we could write something like that :  
```csharp
string name = xx.NullSafeEval(x => x.Foo.GetBar(42).Baz.Name, "Default");
```
  Indexers are not handled yet, but they could be added quite easily ; I will leave it to you to do it if you have the use for it ;)  **Limitations**  Even though that solution can seem very interesting at first sight, please read what follows before you integrate this code into a real world program...  
- First, the proposed code is just a proof of concept, and as such, hasn't been thoroughly tested, so it's probably not very reliable.
- Secondly, keep in mind that dynamic code generation from an expression tree is tough work for the CLR, and will have a big impact on performance. A quick test shows that using the `NullSafeEval` method is about 10000 times slower than accessing the property directly...<br><br>A possible approach to limit that issue would be to cache the delegates generated for each expression, to avoid regenerating them every time. Unfortunately, as far as I know there is no simple and reliable way to compare two Linq expressions, which makes it much harder to implement such a cache.
- Last, you might have noticed that intermediate properties and methods are evaluated several times ; not only this is bad for performance, but more importantly, it could have side effects that are hard to predict, depending on how the properties and methods are implemented.<br><br>A possible workaround would be to rewrite the conditional expression as follows :<br><br>
```csharp
Foo foo = null;
Bar bar = null;
Baz baz = null;
var name =
    (x == null)
    ? defaultValue
    : ((foo = x.Foo) == null)
      ? defaultValue
      : ((bar = foo.Bar) == null)
        ? defaultValue
        : ((baz = bar.Baz) == null)
          ? defaultValue
          : baz.Name;
```
<br><br>Unfortunately, this is not possible in .NET 3.5 : that version only supports simple expressions, so it's not possible to declare variables, assign values to them, or write several distinct instructions. However, in .NET 4.0, support for Linq expressions has been largely improved, and makes it possible to generate that kind of code. I'm currently trying to improve the `NullSafeEval` method to take advantage of the new .NET 4.0 features, but it turns out to be much more difficult than I had anticipated... If I manage to work it out, I'll let you know and post the code !

To conclude, I wouldn't recommend using that technique in real programs, at least not in its current state. However, it gives an interesting insight on the possibilities offered by Linq expressions. If you're new to this, you should know that Linq expressions are used (among other things) :
- To generate SQL queries in ORMs like Linq to SQL or Entity Framework
- To build complex predicates dynamically, like in the [PredicateBuilder](http://www.albahari.com/nutshell/predicatebuilder.aspx) class by Joseph Albahari
- To implement "static reflection", which has generated [a lot of buzz on technical blogs](http://www.google.com/search?tbo=1&amp;tbs=blg:1&amp;q=static+reflection) lately


