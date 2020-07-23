---
layout: post
title: "ASP.NET Core 3, IIS and empty HTTP headers"
date: 2020-07-23
url: /2020/07/23/aspnet-core-iis-and-empty-http-headers/
tags:
  - ASP.NET Core
  - IIS
  - headers
  - HTTP
  - WOPI
---



HTTP headers are key/value pairs sent at the beginning of a request or response. According to the grammar in [RFC 7230](https://tools.ietf.org/html/rfc7230#section-3.2), a field *could* have an empty value. In practice, it probably doesn't make much sense: semantically, a header with an empty value or the absence of that header are equivalent.

However, some client or server implementations actually require that a given header is present, even if it's empty. For instance, the [validation tests](https://wopi.readthedocs.io/en/latest/build_test_ship/validator.html) for [WOPI](https://wopi.readthedocs.io/en/latest/) (an HTTP-based protocol used to integrate Office for the Web with an application) require that the `X-WOPI-Lock` header is included in the response in certain situations, even if it's empty (even though the spec says it can be omitted).

I had a working WOPI host implementation, made with ASP.NET Core 2.1 and hosted in Azure App Service. All the relevant validation tests passed. But after upgrading it to ASP.NET Core 3.1, some lock-related tests started failing because the `X-WOPI-Lock` header was missing. The code emitting this header had not changed, and when I tested the app locally on Kestrel, I could see that the header was present, with an empty value. But when the app was deployed on Azure, the header was missing!

I eventually tracked this down to [this pull request](https://github.com/dotnet/aspnetcore/pull/12486), which explicitly omits empty headers when the app is hosted in-process in IIS. I couldn't see it when testing on my machine because I was using Kestrel, but Azure App Service uses IIS to host applications. In my opinion, this change is a mistake. An empty header is valid according to the HTTP specs, so ASP.NET Core shouldn't remove it.

Anyway, there's an easy workaround to prevent empty headers from being removed: instead of setting the header to an empty string, set it to a string containing only whitespace:

```csharp
Response.Headers["X-WOPI-Lock"] = " ";
```

The header will not be removed from the response, but its value will be trimmed, so the response will actually contain an empty header, which is what we want. This workaround is a bit brittle and feels like a hack, but it works!