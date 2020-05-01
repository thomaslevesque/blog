---
layout: post
title: Asynchronous initialization in ASP.NET Core with custom middleware
date: 2018-07-20T15:46:31.0000000
url: /2018/07/20/asynchronous-initialization-in-asp-net-core-with-custom-middleware/
tags:
  - asp.net core
  - async
  - C#
  - initialization
  - middleware
categories:
  - ASP.NET Core
---


***Update:** I no longer recommend the approach described in this post. I propose a better solution here: [Asynchronous initialization in ASP.NET Core, revisited](https://www.thomaslevesque.com/2018/09/25/asynchronous-initialization-in-asp-net-core-revisited/).*

Sometimes you need to perform some initialization steps when your web application starts. However, putting such code in the `Startup.Configure` method is generally not a good idea, because:

- There's no current scope in the `Configure` method, so you can't use services registered with "scoped" lifetime (this would throw an `InvalidOperationException`: *Cannot resolve scoped service 'MyApp.IMyService' from root provider*).
- If the initialization code is asynchronous, you can't await it, because the `Configure` method can't be asynchronous. You could use `.Wait` to block until it's done, but it's ugly.


## Async initialization middleware

A simple way to do it involves writing a custom [middleware](https://www.thomaslevesque.com/2018/03/27/understanding-the-asp-net-core-middleware-pipeline/) that ensures initialization is complete before processing a request. This middleware starts the initialization process when the app starts, and upon receiving a request, will wait until the initialization is done before passing the request to the next middleware. A basic implementation could look like this:

```csharp

public class AsyncInitializationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger _logger;
    private Task _initializationTask;

    public AsyncInitializationMiddleware(RequestDelegate next, IApplicationLifetime lifetime, ILogger<AsyncInitializationMiddleware> logger)
    {
        _next = next;
        _logger = logger;

        // Start initialization when the app starts
        var startRegistration = default(CancellationTokenRegistration);
        startRegistration = lifetime.ApplicationStarted.Register(() =>
        {
            _initializationTask = InitializeAsync(lifetime.ApplicationStopping);
            startRegistration.Dispose();
        });
    }

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Initialization starting");

            // Do async initialization here
            await Task.Delay(2000);

            _logger.LogInformation("Initialization complete");
        }
        catch(Exception ex)
        {
            _logger.LogError(ex, "Initialization failed");
            throw;
        }
    }

    public async Task Invoke(HttpContext context)
    {
        // Take a copy to avoid race conditions
        var initializationTask = _initializationTask;
        if (initializationTask != null)
        {
            // Wait until initialization is complete before passing the request to next middleware
            await initializationTask;

            // Clear the task so that we don't await it again later.
            _initializationTask = null;
        }

        // Pass the request to the next middleware
        await _next(context);
    }
}
```

We can then add this middleware to the pipeline in the `Startup.Configure` method. It should be added early in the pipeline, before any other middleware that would need the initialization to be complete.

```csharp

public void Configure(IApplicationBuilder app, IHostingEnvironment env)
{
    if (env.IsDevelopment())
    {
        app.UseDeveloperExceptionPage();
    }

    app.UseMiddleware<AsyncInitializationMiddleware>();

    app.UseMvc();
}
```

## Dependencies

At this point, our initialization middleware doesn't depend on any service. If it has transient or singleton dependencies, they can just be injected into the middleware constructor as usual, and used from the `InitializeAsync` method.

However, if the dependencies are scoped, we're in trouble: the middleware is instantiated directly from the root provider, not from a scope, so it can't take scoped dependencies in its constructor.

Depending on scoped dependencies for initialization code doesn't make a lot of sense anyway, since by definition scoped dependencies only exist in the context of a request. But if for some reason you need to do it anyway, the solution is to perform initialization in the middleware's `Invoke` method, injecting the dependencies as method parameters. This approach has at least two drawbacks:

- Initialization won't start until a request is received, so the first requests will have a delayed response time; this can be an issue if the initialization takes a long time.
- You need to take special care to ensure thread safety: the initialization code must run only once, even if several requests arrive before initialization is done.


Writing thread-safe code is hard and error-prone, so avoid getting in this situation if possible, e.g. by refactoring your services so that your initialization middleware doesn't depend on any scoped service.

