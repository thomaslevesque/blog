---
layout: post
title: Automatically inject fakes in test fixture with FakeItEasy
date: 2016-01-17T00:00:00.0000000
url: /2016/01/17/automatically-inject-fakes-in-test-fixture-with-fakeiteasy/
tags:
  - C#
  - dependency injection
  - fakeiteasy
  - mocking
  - unit testing
categories:
  - Unit testing
---


Today I’d like to share a nice feature I discovered recently in [FakeItEasy](http://fakeiteasy.github.io/).

When you write unit tests for a class that takes dependencies, you typically need to create fake/mock dependencies and manually inject them into the SUT (System Under Test), or use a DI container to register the fake dependencies and construct the SUT. This is a bit tedious, and a few months ago I came up with [an auto-mocking Unity extension](http://www.thomaslevesque.com/2015/06/14/create-an-auto-mocking-container-with-unity-and-fakeiteasy/) to make it easier. Now I just realized that FakeItEasy offers an even better solution: just declare the dependencies and SUT as fields or properties in your test fixture, and call `Fake.InitializeFixture` on the fixture to initialize them. Here’s how it looks:

```
    public class BlogManagerTests
    {
        [Fake] public IBlogPostRepository BlogPostRepository { get; set; }
        [Fake] public ISocialNetworkNotifier SocialNetworkNotifier { get; set; }
        [Fake] public ITimeService TimeService { get; set; }

        [UnderTest] public BlogManager BlogManager { get; set; }

        public BlogManagerTests()
        {
            Fake.InitializeFixture(this);
        }

        [Fact]
        public void NewPost_should_add_blog_post_to_repository()
        {
            var post = A.Dummy();

            BlogManager.NewPost(post);

            A.CallTo(() => BlogPostRepository.Add(post)).MustHaveHappened();
        }

        [Fact]
        public void PublishPost_should_update_post_in_repository_and_publish_link_to_social_networks()
        {
            var publishDate = DateTimeOffset.Now;
            A.CallTo(() => TimeService.Now).Returns(publishDate);

            var post = A.Dummy();

            BlogManager.PublishPost(post);

            Assert.Equal(BlogPostStatus.Published, post.Status);
            Assert.Equal(publishDate, post.PublishDate);

            A.CallTo(() => BlogPostRepository.Update(post)).MustHaveHappened();
            A.CallTo(() => SocialNetworkNotifier.PublishLink(post)).MustHaveHappened();
        }
    }
```

The SUT is declared as a property marked with the `[UnderTest]` attribute. Each dependency that you need to manipulate is declared as a property marked with the `[Fake]` attribute. `Fake.InitializeFixture` then initializes the SUT, creating fake dependencies on the fly, and assigns those dependencies to the corresponding properties.

I really like how clean the tests look with this technique; the boilerplate code is reduced to a minimum, all you have to do is configure the dependencies where necessary and get on with your tests.

Two remarks about the code above:

- You can use private fields instead of public properties for the fakes and SUT, but since the fields are never explicitly set in code, it raises a compiler warning (CS0649), so I prefer to use properties.
- The tests in my example use [xUnit](http://xunit.github.io/), so I put the call to `Fake.InitializeFixture` in the fixture constructor, but if you use another test framework like NUnit or MSTests, you would typically put it in the setup method.


Also, note that there are limits to the scenarios supported by this approach:

- only constructor injection is supported, not property injection (i.e. the dependencies must be constructor parameters of the SUT)
- named dependencies are not supported; only the type is taken into account, so you can’t have multiple distinct dependencies with the same type
- dependency collections are not supported (i.e. if your class receives a collection of dependencies, e.g. `IFooService[]`)


