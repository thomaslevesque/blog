---
layout: post
title: Create an auto-mocking container with Unity and FakeItEasy
date: 2015-06-14T20:00:47.0000000
url: /2015/06/14/create-an-auto-mocking-container-with-unity-and-fakeiteasy/
tags:
  - C#
  - dependency injection
  - fakeiteasy
  - mocking
  - unit testing
  - unity
categories:
  - Unit testing
---


Unit testing can be tedious sometimes, especially when testing classes that have complex dependencies. Fortunately, some tools make it somewhat easier. I’ve been using [FakeItEasy](https://github.com/FakeItEasy/FakeItEasy) a lot recently; it’s a very easy to use mocking framework for .NET. It has a very lean and simple API based on generics and lambda expressions, and is a real pleasure to work with. It came as a breath of fresh air compared to the old RhinoMocks I had been using before.

But as nice as FakeItEasy is, the process of registering all the fake dependencies for the class you are testing is still a bit tedious. Wouldn’t it be nice if the IoC container could automatically create the fakes on demand ? So this code:

```

var container = new UnityContainer();

// Setup dependencies
var fooProvider = A.Fake<IFooProvider>();
container.RegisterInstance(fooProvider);
var barService = A.Fake<IBarService>();
container.RegisterInstance(barService);
var bazManager = A.Fake<IBazManager>();
container.RegisterInstance(bazManager);

var sut = container.Resolve<SystemUnderTest>();
```

Could be reduced to this:

```

var container = new UnityContainer();

// This will cause the container to provide fakes for all dependencies
container.AddNewExtension<AutoFakeExtension>();

var sut = container.Resolve<SystemUnderTest>();
```

Well, it’s actually pretty easy to do with Unity. Unity is usually not considered the “cool kid” in the small world of IoC containers, but it’s well supported, easy to use, and extensible. I came up with the following extension to enable the above scenario:

```

public class AutoFakeExtension : UnityContainerExtension
{
    protected override void Initialize()
    {
        Context.Strategies.AddNew<AutoFakeBuilderStrategy>(UnityBuildStage.PreCreation);
    }
    
    private class AutoFakeBuilderStrategy : BuilderStrategy
    {
        private static readonly MethodInfo _fakeGenericDefinition;
    
        static AutoFakeBuilderStrategy()
        {
            _fakeGenericDefinition = typeof(A).GetMethod("Fake", Type.EmptyTypes);
        }
        
        public override void PreBuildUp(IBuilderContext context)
        {
            if (context.Existing == null)
            {
                var type = context.BuildKey.Type;
                if (type.IsInterface || type.IsAbstract)
                {
                    var fakeMethod = _fakeGenericDefinition.MakeGenericMethod(type);
                    var fake = fakeMethod.Invoke(null, new object[0]);
                    context.PersistentPolicies.Set<ILifetimePolicy>(new ContainerControlledLifetimeManager(), context.BuildKey);
                    context.Existing = fake;
                    context.BuildComplete = true;
                }
            }
            base.PreBuildUp(context);
        }
    }
}
```

A few comments on this code:

- The logic is a bit crude (it generates fakes only for interfaces and abstract classes), but can easily be adjusted if necessary.
- The ugly reflection hack is due to the fact that FakeItEasy doesn’t have an non-generic overload of `A.Fake` that accepts a `Type` as a parameter. Well, nobody’s perfect…
- The lifetime is set to “container controlled”, because if you need to configure method calls, you will need to access the same instance that is injected into the system under test:


```

var fooProvider = container.Resolve<IFooProvider>();
A.CallTo(() => fooProvider.GetFoo(42)).Returns(new Foo { Id = 42, Name = “test” });
```

Note that if you register a dependency explicitly, it will take precedence and no fake will be created. So you can use this extension and still be able to manually specify a dependency:

```

var container = new UnityContainer();
container.AddNewExtension<AutoFakeExtension>();
container.RegisterType<IFooProvider, TestFooProvider>();
var sut = container.Resolve<SystemUnderTest>();
```

Of course, this extension could easily be modified to use a different mocking framework. I guess the same principle could be applied to other IoC containers as well, as long as they have suitable extension points.

### What about AutoFixture?

Before you ask: yes, I know about [AutoFixture](https://github.com/AutoFixture/AutoFixture). It’s a pretty good library, and I actually tried to use it as well, with some success. The resulting code is very similar to the examples above. The main reason why I didn’t keep using it is that AutoFixture is not really an IoC container (though it does some of the things an IoC container does), and I prefer to use the same IoC mechanism in my unit tests and in the actual application. Also, I’m not very comfortable with the way it handles properties when creating an instance of the SUT. By default, it sets all public writable properties to dummy instances of their type; this is fine if those properties are used for dependency injection, but IMO it doesn’t make sense for other properties. I know I can suppress this behavior for specific properties, but I have to do it manually on a case by case basis; it doesn’t take IoC container specific attributes like `[Dependency]` into account. So eventually I found it easier to use my custom Unity extension.

