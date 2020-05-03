---
layout: post
title: '[Entity Framework] Using Include with lambda expressions'
date: 2010-10-03T00:00:00.0000000
url: /2010/10/03/entity-framework-using-include-with-lambda-expressions/
tags:
  - .NET 4.0
  - entity framework
  - expression
  - include
  - lambda
  - linq
categories:
  - C# 4.0
  - Code sample
---

I'm currently working on a project that uses Entity Framework 4. Even though lazy loading is enabled, I often use the [`ObjectQuery.Include`](http://msdn.microsoft.com/en-us/library/bb738708.aspx) method to eagerly load associated entities, in order to avoid database roundtrips when I access them:  
```csharp
var query =
    from ord in db.Orders.Include("OrderDetails")
    where ord.Date >= DateTime.Today
    select ord;
```
  Or if I also want to eagerly load the product:  
```csharp
var query =
    from ord in db.Orders.Include("OrderDetails.Product")
    where ord.Date >= DateTime.Today
    select ord;
```
  However, there's something that really bothers me with this `Include` method: the property path is passed as a string. This approach has two major drawbacks: 
- It's easy to make a mistake when typing the property path, and since it's a string, the compiler doesn't complain. So we get a runtime error, rather than a compilation error.
- We can't take advantage of IDE features like Intellisense and refactoring. If we rename a property in the model, automatic refactoring won't check the content of the string. We have to manually update all calls to `Include` that refer to this property, with the risk of missing some of them in the process...

  It would be much more convenient to use a lambda expression to specify the property path. The principle is well known, and frequently used to avoid using a string to refer to a property.  The trivial case, where the property to include is directly accessible from the source, is pretty easy to handle, and many implementation can be found on the Internet. We just need to use a method that extracts the property name from an expression :  
```csharp
    public static class ObjectQueryExtensions
    {
        public static ObjectQuery<T> Include<T>(this ObjectQuery<T> query, Expression<Func<T, object>> selector)
        {
            string propertyName = GetPropertyName(selector);
            return query.Include(propertyName);
        }

        private static string GetPropertyName<T>(Expression<Func<T, object>> expression)
        {
            MemberExpression memberExpr = expression.Body as MemberExpression;
            if (memberExpr == null)
                throw new ArgumentException("Expression body must be a member expression");
            return memberExpr.Member.Name;
        }
    }
```
  Using that extension method, the code from the first sample can be rewritten as follows:  
```csharp
var query =
    from ord in db.Orders.Include(o => o.OrderDetails)
    where ord.Date >= DateTime.Today
    select ord;
```
  This code works fine, but only for the simplest cases... In the second example, we also want to eagerly load the `OrderDetail.Product` property, but the code above can't handle that case. Indeed, the expression we would use to include the `Product` property would be something like `o.OrderDetails.Select(od => od.Product)`, but the `GetPropertyName` method can only handle property accesses, not method calls, and it works only for an expression with a single level.  To get the full path of the property to include, we have to walk through the whole expression tree to extract the name of each property. It sounds like a complicated task, but there's a class that can help us with it: [`ExpressionVisitor`](http://msdn.microsoft.com/en-us/library/system.linq.expressions.expressionvisitor.aspx). This class was introduced in .NET 4.0 and implements the Visitor pattern to walk through all nodes in the expression tree. It's just a base class for implementing custom visitors, and it does nothing else than just visiting each node. All we need to do is inherit it, and override some methods to extract the properties from the expression. Here are the methods we need to override:
- `VisitMember` : used to visit a property or field access
- `VisitMethodCall` : used to visit a method call. Even though method calls aren't directly related to what we want to do, we need to change its behavior in the case of Linq operators: the default implementation visits each parameter in their normal order, but for extension method like `Select` or `SelectMany`, we need to visit the first parameter (the `this` parameter) last, so that we retrieve the properties in the correct order.<br><br>Here's a new version of the `Include` method, along with the `ExpressionVisitor` implementation:<br><br>
```csharp
    public static class ObjectQueryExtensions
    {
        public static ObjectQuery<T> Include<T>(this ObjectQuery<T> query, Expression<Func<T, object>> selector)
        {
            string path = new PropertyPathVisitor().GetPropertyPath(selector);
            return query.Include(path);
        }

        class PropertyPathVisitor : ExpressionVisitor
        {
            private Stack<string> _stack;

            public string GetPropertyPath(Expression expression)
            {
                _stack = new Stack<string>();
                Visit(expression);
                return _stack
                    .Aggregate(
                        new StringBuilder(),
                        (sb, name) =>
                            (sb.Length > 0 ? sb.Append(".") : sb).Append(name))
                    .ToString();
            }

            protected override Expression VisitMember(MemberExpression expression)
            {
                if (_stack != null)
                    _stack.Push(expression.Member.Name);
                return base.VisitMember(expression);
            }

            protected override Expression VisitMethodCall(MethodCallExpression expression)
            {
                if (IsLinqOperator(expression.Method))
                {
                    for (int i = 1; i < expression.Arguments.Count; i++)
                    {
                        Visit(expression.Arguments[i]);
                    }
                    Visit(expression.Arguments[0]);
                    return expression;
                }
                return base.VisitMethodCall(expression);
            }

            private static bool IsLinqOperator(MethodInfo method)
            {
                if (method.DeclaringType != typeof(Queryable) && method.DeclaringType != typeof(Enumerable))
                    return false;
                return Attribute.GetCustomAttribute(method, typeof(ExtensionAttribute)) != null;
            }
        }
    }
```
<br><br>I already talked about the `VisitMethodCall` method, so I won't explain it further. The implementation of `VisitMember` is very simple: we just push the member name on a stack. Why a stack ? That's because the expression is not visited in the order one would intuitively expect. For instance, in an expression like `o.OrderDetails.Select(od => od.Product)`, the first visited node is not `o` but the call to `Select`, because what precedes it (`o.OrderDetails`) is actually the first parameter of the static `Select` method... To retrieve the properties in the correct order, we put them on a stack so that we can read them back in reverse order when we need to build the property path.<br><br>The `GetPropertyPath` method probably doesn't need a long explanation: it initializes the stack, visits the expression, and builds the property path from the stack.<br><br>We can now rewrite the code from the second example as follows:<br><br>
```csharp
var query =
    from ord in db.Orders.Include(o => OrderDetails.Select(od => od.Product))
    where ord.Date >= DateTime.Today
    select ord;
```
<br><br>This method also works for more complex cases. Let's add a few new entities to our model: one or more discounts can be applied to each purchased product, and each discount is linked to a sales campaign. If we need to retrieve the associated discounts and campaigns in the query results, we can write something like that:<br><br>
```csharp
var query =
    from ord in db.Orders.Include(o => OrderDetails.Select(od => od.Discounts.Select(d => d.Campaign)))
    where ord.Date >= DateTime.Today
    select ord;
```
<br><br>The result is the same as if we had passed "OrderDetails.Discounts.Campaign" to the standard `Include` method. Since the nested `Select` calls impair the readability, we can also use a different expression, with the same result:<br><br>
```csharp
var query =
    from ord in db.Orders.Include(o => o.OrderDetails
                                        .SelectMany(od => od.Discounts)
                                        .Select(d => d.Campaign))
    where ord.Date >= DateTime.Today
    select ord;
```
To conclude, I just have two remarks regarding this solution:
    - A similar extension method is included in the Entity Framework Feature CTP4 (see [this article](http://romiller.com/2010/07/14/ef-ctp4-tips-tricks-include-with-lambda/) for details). So it is possible that it will eventually be included in the framework (perhaps in a service pack for .NET 4.0 ?).
    - Even though this solution targets Entity Framework 4.0, it should be possible to adapt it for EF 3.5. The `ExpressionVisitor` class is not available in 3.5, but there is another implementation of it in Joseph Albahari's [LINQKit](http://www.albahari.com/nutshell/linqkit.aspx). I didn't try it, but it should work the same way...


