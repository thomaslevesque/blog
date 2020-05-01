---
layout: post
title: 'Google+ shutdown: fixing Google authentication in ASP.NET Core'
date: 2019-01-10T00:00:00.0000000
url: /2019/01/10/google-shutdown-fixing-google-authentication-in-asp-net-core/
tags:
  - asp.net core
  - Authentication
  - google
  - OpenId Connect
categories:
  - ASP.NET Core
---


A few months ago, Google decided to shutdown Google+, due to multiple data leaks. More recently, they announced that [the Google+ APIs will be shutdown](https://developers.google.com/+/api-shutdown) on March 7, 2019, which is pretty soon! In fact, calls to these APIs might start to fail as soon as January 28, which is less than 3 weeks from now. You might think that it doesn't affect you as a developer; but if you're using Google authentication in an ASP.NET Core app, think again! The built-in Google authentication provider (`services.AddAuthentication().AddGoogle(...)`) uses a Google+ API to retrieve information about the signed-in user, which will soon stop working. You can read the details in [this Github thread](https://github.com/aspnet/AspNetCore/issues/6069). *Note that it also affects classic ASP.NET MVC*.

## OK, now I'm listening. How do I fix it?

Fortunately, it's not too difficult to fix. There's already a [pull request](https://github.com/aspnet/AspNetCore/pull/6338) to fix it in ASP.NET Core, and hopefully an update will be released soon. In the meantime, you can either:

- use the workaround [described here](https://github.com/aspnet/AspNetCore/issues/6069#issuecomment-449461197), which basically specifies a different user information endpoint and adjusts the JSON mappings.
- or use the generic OpenID Connect authentication provider instead, which I think is better than the built-in provider anyway, because you can get all the necessary information directly from the ID token, without making an extra API call.


## Using OpenID Connect to authenticate with Google

So, let's see how to change our app to use the OpenID Connect provider instead of the built-in Google provider, and configure it to get the same results as before.

First, let's install the `Microsoft.AspNetCore.Authentication.OpenIdConnect` NuGet package to the project, if it's not already there.

Then, we go to the place where we add the built-in Google provider (the call to `AddGoogle`, usually in the `Startup` class), and remove that call.

Instead, we add the OpenID Connect provider, point it to the Google OpenID Connect authority URL, and set the client id (the same that we were using for the built-in Google provider):

```csharp
services
    .AddAuthentication()
    .AddOpenIdConnect(
        authenticationScheme: "Google",
        displayName: "Google",
        options =>
        {
            options.Authority = "https://accounts.google.com/";
            options.ClientId = configuration["Authentication:Google:ClientId"];
        });
```

We also need to adjust the callback path to be the same as before, so that the redirect URI configured for the Google app still works; and while we're at it, let's also configure the signout paths.

```csharp
options.CallbackPath = "/signin-google";
options.SignedOutCallbackPath = "/signout-callback-google";
options.RemoteSignOutPath = "/signout-google";
```

The default configuration already includes the `openid` and `profile` scopes, but if we want to have access to the user's email address as we did before, we also need to add the `email` scope:

```csharp
options.Scope.Add("email");
```

And that's it! Everything should work as it did before. Here's a [Gist](https://gist.github.com/thomaslevesque/fe2cb14377e30833484f75ac416134bb) that shows the code before and after the change.

## Hey, where's the client secret?

You might have noticed that we didn't specify the client secret. Why is this?

The built-in Google provider is actually just a generic OAuth provider with Google-specific configuration. It uses the authorization code flow, which requires the client secret to exchange the authorization code for an access token, which in turn is used to call the user information endpoint.

But by default the OpenId Connect provider uses the implicit flow. There isn't an authorization code: an `id_token` is provided directly to the `redirect_uri`, and there's no need to call any API, so no secret is needed. If, for some reason, you don't want to use the implicit flow, just change `options.ResponseType` to `code` (the default is `id_token`), and set `options.ClientSecret` as appropriate. You should also set `options.GetClaimsFromUserInfoEndpoint` to true to get the user details (name, email...), since you won't have an `id_token` to get them from.

