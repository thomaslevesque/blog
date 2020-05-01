---
layout: post
title: Hosting an ASP.NET Core 2 application on a Raspberry Pi
date: 2018-04-17T20:57:40.0000000
url: /2018/04/17/hosting-an-asp-net-core-2-application-on-a-raspberry-pi/
tags:
  - .net core
  - asp.net core
  - linux
  - nginx
  - raspberry pi
  - reverse proxy
categories:
  - Uncategorized
---


As you probably know, .NET Core runs on many platforms: Windows, macOS, and many UNIX/Linux variants, whether on x86/x64 architectures or on ARM. This enables a wide range of interesting scenarios... For instance, is a very small machine like a **Raspberry Pi**, which its low performance ARM processor and small amount of RAM (1 GB on my RPi 2 Model B), enough to host an ASP.NET Core web app? Yes it is! At least as long as you don't expect it to handle a very heavy load. So let's see in practice how to deploy an expose an ASP.NET Core web app on a Raspberry Pi.

## Creating the app

Let's start from a basic ASP.NET Core 2.0 MVC app template:

```bash
dotnet new mvc
```

You don't even need to open the project for now, just compile it as is and publish it for the Raspberry Pi:

```bash
dotnet publish -c Release -r linux-arm
```

## Prerequisites

We're going to use a Raspberry Pi running Raspbian, the official Linux distro for Raspberry Pi, which is based on Debian. To run a .NET Core 2.0 app, you'll need version Jessie or higher (I used Raspbian Stretch Lite). ***Update**: as Tomasz mentioned in the comments, you also need a Raspberry Pi 2 or more recent, with an ARMv7 processor; The first RPi has an ARMv6 processor and cannot run .NET Core.*

Even though the app is self-contained and doesn't require .NET Core to be installed on the RPi, you will still need a few low-level dependencies; they are listed [here](https://github.com/dotnet/core/blob/master/samples/RaspberryPiInstructions.md#linux). You can install them using `apt-get`:

```bash
sudo apt-get update
sudo apt-get install curl libunwind8 gettext apt-transport-https
```

## Deploy and run the application

Copy all files from the `bin\Release\netcoreapp2.0\linux-arm\publish` directory to the Raspberry Pi, and make the binary executable (replace MyWebApp with the name of your app):

```bash
chmod 755 ./MyWebApp
```

Run the app:

```bash
./MyWebApp
```

If nothing went wrong, the app should start listening on port 5000. But since it listens only on `localhost`, it's only accessible from the Raspberry Pi itself...

## Exposing the app on the network

There are several ways to fix that. The easiest is to set the `ASPNETCORE_URLS` environment variable to a value like `http://*:5000/`, in order to listen on all addresses. But if you intend to expose the app on the Internet, it might not be a good idea: the Kestrel server used by ASP.NET Core isn't designed to be exposed directly to the outside world, and isn't well protected against attacks. It is strongly recommended to put it behind a reverse proxy, such as [nginx](https://www.nginx.com/). Let's see how to do that.

First, you need to install nginx if it's not already there, using this command:

```bash
sudo apt-get install nginx
```

And start it like this:

```bash
sudo service nginx start
```

Now you need to configure it so that requests arriving to port 80 are passed to your app on port 5000. To do that, open the `/etc/nginx/sites-available/default` file in your favorite editor (I use vim because my RPi has no graphical environment). The default configuration defines only one server, listening on port 80. Under this server, look for the section starting with `location /`: this is the configuration for the root path on this server. Replace it with the following configuration:

```plain
location / {
        proxy_pass http://localhost:5000/;
        proxy_http_version 1.1;
        proxy_set_header Connection keep-alive;
}
```

Be careful to include the final slash in the destination URL.

This configuration is intentionnally minimal, we'll expand it a bit later.

Once you're done editing the file, tell nginx to reload its configuration:

```bash
sudo nginx -s reload
```

From your PC, try to access the app on the Raspberry Pi by entering its IP address in your browser. If you did everything right, you should see the familiar home page from the ASP.NET Core app template!

Note that you'll need to be patient: the first time the home page is loaded, its Razor view is compiled, which can take a while on the RPi's low-end hardware. ASP.NET Core 2.0 doesn't support precompilation of Razor views for self-contained apps; this is fixed in 2.1, which is currently in preview. So for now you have 3 options:

- be patient and endure the delay on first page load
- migrate to ASP.NET Core 2.1 preview, as explained [here](https://blogs.msdn.microsoft.com/webdev/2018/04/12/asp-net-core-2-1-0-preview2-now-available/#migrating)
- make a non self-contained deployment, which requires .NET Core to be installed on the RPi


For this article, I chose the first options to keep things simple.

## Proxy headers

At this point, we could just leave the app alone and call it a day. However, if your app is going to evolve into something more useful, there are a few things that aren't going to work correctly in the current state. The problem is that the app isn't aware that it's behind a reverse proxy; as far as it knows, it's only listening to requests on localhost on port 5000. Which means it cannot know:

- the actual client IP (requests seem to come from localhost)
- the protocol scheme used by the client
- the actual host name specified by the client


For the app to know these things, it has to be told by the reverse proxy. Let's change the nginx configuration so that it adds a few headers to incoming requests. These headers are not standard, but they're widely used by proxy servers.

```plain
    proxy_set_header X-Forwarded-For    $proxy_add_x_forwarded_for;
    proxy_set_header X-Forwarded-Host   $http_host;
    proxy_set_header X-Forwarded-Proto  http;
```

`X-Forwarded-For` contains the client IP address, and optionally the addresses of proxies along the way. `X-Forwarded-Host` contains the host name initially specified by the client, and `X-Forwarded-Proto` contains the original protocol scheme (hard-coded to `http` here since HTTPS is not configured).

(Don't forget to reload the nginx configuration)

We also need to change the ASP.NET Core app so that it takes these headers into account. This can be done easily using the `ForwardedHeaders` middleware; add this code at the start of the `Startup.Configure` method:

```csharp
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.All
});
```

In case you're wondering what a middleware is, [this article](https://www.thomaslevesque.com/2018/03/27/understanding-the-asp-net-core-middleware-pipeline/) might help!

This middleware will read the `X-Forwarded-*` headers from incoming requests, and use them to modify:

- the `Host` and `Scheme` of the request
- the `Connection.RemoteIpAddress`, which contains the client IP.


This way, the app will behave as if the request was received directly from the client.

## Expose the app on a specific path

Our app is now accessible at the URL `http://<ip-address>/`, i.e. at the root of the server. But if we want to host several applications on the Raspberry Pi, it's going to be a problem... We could put each app on a different port, but it's not very convenient. It would be better to have each app on its own path, e.g. with URLs like `http://<ip-address>/MyWebApp/`.

It's pretty easy to do with nginx. Edit the nginx configuration again, and replace `location /` with `location /MyWebApp/` (note the final slash, it's important). Reload the configuration, and try to access the app at its new URL... The home page loads, but the CSS and JS scripts don't: error 404. In addition, links to other pages are now incorrect, and point to `http://<ip-address>/Something` instead of `http://<ip-address>/MyWebApp/Something`. What's going on?

The reason is quite simple: the app isn't aware that it's not served from the root of the server, and generates all its links as if it were... To fix this, we can ask nginx to pass yet another header to our app:

```plain
proxy_set_header X-Forwarded-Path   /MyWebApp;
```

Note that this `X-Forwarded-Path` header is even less standard than the other ones, since I just made it up... So of course, there's no built-in ASP.NET Core middleware that can handle it, and we'll need to do it ourselves. Fortunately it's pretty easy: we just need to use the path specified in the header as the path base. In the `Startup.Configure` method, add this after the `UseForwardHeaders` statement:

```csharp
// Patch path base with forwarded path
app.Use(async (context, next) =>
{
    var forwardedPath = context.Request.Headers["X-Forwarded-Path"].FirstOrDefault();
    if (!string.IsNullOrEmpty(forwardedPath))
    {
        context.Request.PathBase = forwardedPath;
    }

    await next();
});
```

Redeploy and restart the app, reload the nginx configuration, and try again: now it works!

## Run the app as a service

If we want our app to be always running, restarting it manually every time it crashes or when the Raspberry Pi reboots isn't going to be sustainable... What we want is to run it as a service, so that it starts when the system starts, and is automatically restarted if it stops working. To do this, we'll take advantage of **systemd**, which manages services in most Linux distros, including Raspbian.

To create a systemd service, create a `MyWebApp.service` file in the `/lib/systemd/system/` directory, with the following content:

```plain
[Unit]
Description=My ASP.NET Core Web App
After=nginx.service

[Service]
Type=simple
User=pi
WorkingDirectory=/home/pi/apps/MyWebApp
ExecStart=/home/pi/apps/MyWebApp/MyWebApp
Restart=always

[Install]
WantedBy=multi-user.target
```

(replace the name and paths to match your app of course)

Enable the service like this:

```bash
sudo systemctl enable MyWebApp
```

And start it like this (new services aren't started automatically):

```bash
sudo systemctl start MyWebApp
```

And that's it, your app is now monitored by systemd, which will take care of starting or restarting it as needed.

## Conclusion

As you can see, running an ASP.NET Core 2.0 app on a Raspberry Pi is not only possible, but reasonably easy too; you just need a bit of fiddling with headers and reverse proxy settings. You won't host the next Facebook or StackOverflow on your RPi, but it's fine for small utility applications. Just give free rein to your imagination!

