---
layout: post
layout: post
title: Automatic factory with Microsoft.Extensions.DependencyInjection and Castle DynamicProxy
date: 2020-09-27
url: /2020/09/27/automatic-factory-with-microsoft-extensions-dependencyinjection-and-castle-dynamicproxy/
tags:
  - Castle.Core
  - Castle
  - DynamicProxy
  - dependency injection
  - .NET Core
  - factory
  - C#
---

## Dependency injection: the good and the bad

Dependency injection (DI) is a great pattern, which can really help make your code cleaner, more decoupled and more testable. There are many DI libraries, like Autofac, Lamar (StructureMap's successor), Castle Windsor, etc., but lately I've mostly been using the one provided by Microsoft in .NET Core : `Microsoft.Extensions.DependencyInjection`. It's not the most full-featured (in fact, it's pretty bare-bones), but I find it sufficient in most cases.

As much as I love dependency injection, in some situations it can become a little unwieldy… For instance, when you have a class that is instantiated explicitly with some data, and also needs service dependencies. Consider this class, for example:

```csharp
  public class FooViewModel
  {
      private readonly IMyService _myService;

      public FooViewModel(int id, string name, IMyService myService)
      {
          Id = id;
          Name = name;
          _myService = myService;
      }

      public int Id { get; }
      public string Name { get; }
  }
```

To create an instance of `FooViewModel`, you need to pass values for `id` and `name`, and an instance of `IMyService`. Which means you must have access to `IMyService`, so you need to inject it into the class that creates an instance of `FooViewModel`, even if it doesn't need it itself. It's OK when there's just one dependency, but if there were more, it could get messy. That's a bit annoying…

## Factory pattern

Fortunately, this is a well known problem, and guess what? It has a well known solution! You typically solve this by introducing a factory. For instance, in this case, since we're creating ViewModels, we can create an `IViewModelFactory` interface like this:

```csharp
public interface IViewModelFactory
{
    FooViewModel CreateFooViewModel(int id, string name);
}
```

Notice that the `CreateFooViewModel` method has the same "data" parameters as the `FooViewModel` constructor, but not the "service" parameters. This way, a class that needs to instantiate a `FooViewModel` just needs to pass the data to `CreateViewModel`, without worrying about the service dependencies.

An implementation of this factory looks like this:

```csharp
public class ViewModelFactory : IViewModelFactory
{
    private readonly IMyService _myService;

    public ViewModelFactory(IMyService myService)
    {
        _myService = myService;
    }

    public FooViewModel CreateFoo(int id, string name)
    {
        return new FooViewModel(id, name, _myService);
    }
}
```

Basically, the factory abstracts the dependencies, so that the rest of the code doesn't need to know about them.

It's a very useful pattern, but what happens when you have dozens of different ViewModels, with many different dependencies? Well, you end up with as many methods as you have ViewModels, and you must inject the dependencies of *all* these ViewModels into the factory. It can quickly get messy in a non-trivial application.

Sure, you could split the big factory into several smaller factories that are in charge of creating just one ViewModel, or a handful of related ones. But then, you'd end up with many factories, which can also be painful to manage.

## Resolving dependencies automatically

Maybe it's time to take a step back and look at the problem from a different perspective…

How did we get in this mess in the first place? DI containers are supposed to be good at creating instances of classes with dependencies (that's their job, after all!), but as soon as you introduce "data" parameters in addition to service dependencies, it all breaks down, and you need to do things manually. What we really need is to let the container resolve the service dependencies, and provide the rest ourselves.

Fortunately, there's a relatively little-known class in `Microsoft.Extensions.DependencyInjection` that can help us: `ActivatorUtilities`. This static class has all the smarts to construct an object by resolving service dependencies from the container (a.k.a. service provider), and accepts additional arguments for non-service inputs. For instance, we could use it to construct a `FooViewModel` like this:

```csharp
ActivatorUtilities.CreateInstance(
    serviceProvider,
    typeof(FooViewModel),
    123,      // id
    "test");  // name
```

This way, we don't need to worry about the dependencies: `ActivatorUtilities` looks at the constructor parameters, resolves what it can from the service provider, and takes the rest from the explicitly passed arguments.

Of course, in this form, it's not very convenient, and it's not strongly-typed, so we'll need to wrap this in a factory to make it usable. But the factory code is going to be pretty tedious to write… What if we could automate this?

## Implementing the factory automatically

Castle DynamicProxy is a component of the venerable [Castle Core library](https://github.com/castleproject/Core). To put it simply, it makes it easy to implement types dynamically, optionally delegating to another object, and using interceptors to control the behavior of methods and properties. This makes it very well suited for implementing cross-cutting concerns, and things like mocking libraries (FakeItEasy, Moq and NSubstitute all rely on Castle DynamicProxy).

But how can it help with our factory problem? Well, we're going to use it to automatically implement our `IViewModelFactory` interface!

First, we need to write an interceptor. As the name implies, it will intercept all calls to the proxy object create by Castle DynamicProxy. Here it goes:

```csharp
class FactoryInterceptor : IInterceptor
{
    private readonly IServiceProvider _serviceProvider;

    public FactoryInterceptor(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public void Intercept(IInvocation invocation)
    {
        invocation.ReturnValue =
            ActivatorUtilities.CreateInstance(
                _serviceProvider,
                invocation.Method.ReturnType,
                invocation.Arguments);
    }
}
```

The `IInvocation` object represents the call. We can examine the method and arguments, and specify the return value. Here we just use the method's return type as the type to create, and we use `ActivatorUtilities` to create an instance.

Now, we just need to create the actual proxy object for `IViewModelFactory`:

```csharp
var generator = new ProxyGenerator();
var interceptor = new FactoryInterceptor(serviceProvider);
var factory = generator.
    CreateInterfaceProxyWithoutTarget<IViewModelFactory>(
        interceptor);
```

And that's it, we have our factory! We no longer need to implement the factory's method ourselves, we just need to declare them. We just need to ensure that the methods in the factory interface match the constructors of the ViewModels, or it will fail at runtime.

## Wrapping it up

OK, we're almost done. Let's just package this solution in a handy extension method that we can use to register a factory:

```csharp
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAutoFactory<TFactory>(this IServiceCollection services)
        where TFactory : class
    {
        services.AddSingleton<TFactory>(CreateFactory<TFactory>);
        return services;
    }

    private static TFactory CreateFactory<TFactory>(IServiceProvider serviceProvider)
        where TFactory : class
    {
        var generator = new ProxyGenerator();
        return generator.CreateInterfaceProxyWithoutTarget<TFactory>(
            new FactoryInterceptor(serviceProvider));
    }

    private class FactoryInterceptor : IInterceptor
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ConcurrentDictionary<MethodInfo, ObjectFactory> _factories;

        public FactoryInterceptor(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
            _factories = new ConcurrentDictionary<MethodInfo, ObjectFactory>();
        }

        public void Intercept(IInvocation invocation)
        {
            var factory = _factories.GetOrAdd(invocation.Method, CreateFactory);
            invocation.ReturnValue = factory(_serviceProvider, invocation.Arguments);
        }

        private ObjectFactory CreateFactory(MethodInfo method)
        {
            return ActivatorUtilities.CreateFactory(
                method.ReturnType,
                method.GetParameters().Select(p => p.ParameterType).ToArray());
        }
    }
}
```

*Note that I included an optimization in the interceptor: instead of calling `ActivatorUtilities.CreateInstance` for every call, I used `ActivatorUtilities.CreateFactory` to create and cache a reusable delegate for each method.*

Using this method, we can register our factory like this:

```csharp
public void ConfigureServices(IServiceCollection services)
{
    ...
    services.AddAutoFactory<IViewModelFactory>();
    ...
}
```

And that's it, we can now inject our auto-implemented factory anywhere it's needed!

**Note:** This solution has an important limitation: the factory interface methods must declare *concrete* return types, not interfaces or abstract classes. Otherwise `ActivatorUtilities` won't know which concrete type to create.