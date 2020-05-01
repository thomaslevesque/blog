---
layout: post
title: 'ASP.NET Core: when environments are not enough, use sub-environments!'
date: 2019-12-20T11:33:00.0000000
url: /2019/12/20/asp-net-core-when-environments-are-not-enough-use-sub-environments/
tags:
  - asp.net core
  - C#
  - configuration
  - environment
  - sub-environment
categories:
  - Uncategorized
---


Out of the box, ASP.NET Core has the concept of "environments", which allows your app to use different settings based on which environment it's running in. For instance, you can have Development/Staging/Production environments, each with its own settings file, and a common settings file shared by all environments:

- `appsettings.json`: global settings
- `appsettings.Development.json`: settings specific to the Development environment
- `appsettings.Staging.json`: settings specific to the Staging environment
- `appsettings.Production.json`: settings specific to the Production environment


With the default configuration, environment-specific settings just override global settings, so you don't have to specify unchanged settings in every environment if they're already specified in the global settings file.

Of course, you can have environments with any name you like; Development/Staging/Production is just a convention.

You can specify which environment to use via the `ASPNETCORE_ENVIRONMENT` environment variable, or via the `--environment` command line switch. When you work in Visual Studio, you typically do this in a launch profile in `Properties/launchSettings.json`.

## Limitations

This feature is quite handy, but sometimes, it's not enough. Even in a given environment, you might need different settings to test different scenarios.

As a concrete example, I develop a solution that consists (among other things) of a web API and an authentication server. The API authenticates users with JWT bearer tokens provided by the authentication server. Most of the time, when I work on the API, I don't need to make changes to the authentication server, and I'm perfectly happy to use the one that's deployed in the development environment in Azure. But when I *do* need to make changes to the authentication server, I have to modify the API settings so that it uses the local auth server instead. And I have to be careful not to commit that change, to avoid breaking the development instance in Azure. It's a minor issue, but it's annoying…

A possible solution would be to create a new "DevelopmentWithLocalAuth" environment, with its own settings file. But the settings would be the same as in the Development environment, with the only change being the auth server URL. I hate to have multiple copies of the same thing, because it's a pain to keep them in sync. What I really want is a way to use the settings of the Development environment, and just override what I need, *without touching the Developement environment settings*.

## Enter "sub-environments"

It's not an actual feature, it's just a name I made up. But the point is that you can easily introduce another "level" of configuration settings that just override some settings of the "parent" environment.

For instance, in my scenario, I want to introduce a `appsettings.Development.LocalAuth.json` file that inherits the settings of the Development environment and just overrides the auth server URL:

```javascript

{
    "Authentication": {
        "Authority": "https://localhost:6001"
    }
}
```

The way to do that is to add the new file as a configuration source when building the host in `Program.cs`:

```csharp

public static IHostBuilder CreateHostBuilder(string[] args) =>
    Host.CreateDefaultBuilder(args)
        .ConfigureAppConfiguration((context, builder) =>
        {
            string subenv = context.Configuration["SubEnvironment"];
            if (!string.IsNullOrEmpty(subenv))
            {
                var env = context.HostingEnvironment;
                builder.AddJsonFile($"appsettings.{env.EnvironmentName}.{subenv}.json", optional: true, reloadOnChange: true);
            }
        })
        .ConfigureWebHostDefaults(webBuilder =>
        {
            webBuilder.UseStartup<Startup>();
        });
```

(This code is for ASP.NET Core 3.0, but the same applies if you use ASP.NET Core 2.0 with `WebHostBuilder` instead of `HostBuilder`.)

The magic happens in the call to `ConfigureAppConfiguration`. It adds a new JSON file whose name depends on the environment and sub-environment. Since this configuration source is added after the existing ones, it will override the settings provided by previous sources.

The name of the sub-environment is retrieved from the host configuration, which itself is based on environment variables starting with `ASPNETCORE_` and command line arguments. So, to specify that you want the "LocalAuth" sub-environment, you need to set the `ASPNETCORE_SUBENVIRONMENT` environment variable to "LocalAuth".

And that's it! With this, you can refine existing environments for specific scenarios.

**Note:** Since the new configuration source is added last, it will override ALL previous configuration sources, not just the default `appsettings.json` files. The default host builder adds user secrets, environment variables, and command line arguments after the JSON files, so those will be overriden as well by the sub-environment settings. This is less than ideal, but probably not a major issue for most scenarios. If it's a concern, the fix is to insert the sub-environment config source after the existing JSON sources, but before the user secrets source. It makes the code a bit more involved, but it's doable:

```csharp

        ...
        .ConfigureAppConfiguration((context, builder) =>
        {
            string subenv = context.Configuration["SubEnvironment"];
            if (!string.IsNullOrEmpty(subenv))
            {
                var env = context.HostingEnvironment;
                var newSource = new JsonConfigurationSource
                {
                    Path = $"appsettings.{env.EnvironmentName}.{subenv}.json",
                    Optional = true,
                    ReloadOnChange = true
                };
                newSource.ResolveFileProvider();

                var lastJsonConfigSource = builder.Sources
                    .OfType<JsonConfigurationSource>()
                    .LastOrDefault(s => !s.Path.Contains("secrets.json"));
                if (lastJsonConfigSource != null)
                {
                    var index = builder.Sources.IndexOf(lastJsonConfigSource);
                    builder.Sources.Insert(index + 1, newSource);
                }
                else
                {
                    builder.Sources.Insert(0, newSource);
                }
            }
        })
        ...
```

