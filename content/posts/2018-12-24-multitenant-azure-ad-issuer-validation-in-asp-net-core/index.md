---
layout: post
title: Multitenant Azure AD issuer validation in ASP.NET Core
date: 2018-12-24T00:00:00.0000000
url: /2018/12/24/multitenant-azure-ad-issuer-validation-in-asp-net-core/
tags:
  - asp.net core
  - Authentication
  - Azure
  - Azure Active Directory
  - Azure AD
  - C#
  - JWT
  - Multitenant
  - OpenId Connect
categories:
  - ASP.NET Core
---

***Update 2021/09/19:** If you're using the newer Microsoft.Identity.Web library, you don't have anything to do to handle this, as it's already handled by the library. This article only applies if you're using the generic OpenID Connect provider. Thanks to Ohad Schneider for mentioning this!*

<hr/>

If you use Azure AD authentication and want to allow users from any tenant to connect to your ASP.NET Core application, you need to configure the Azure AD app as multi-tenant, and use a "wildcard" tenant id such as `organizations` or `common` in the authority URL:

```csharp
openIdConnectOptions.Authority = "https://login.microsoftonline.com/organizations/v2.0";
```

The problem when you do that is that with the default configuration, the token validation will fail because the issuer in the token won't match the issuer specified in the [OpenID metadata](https://login.microsoftonline.com/organizations/v2.0/.well-known/openid-configuration). This is because the issuer from the metadata includes a placeholder for the tenant id:

```plain
https://login.microsoftonline.com/{tenantid}/v2.0
```

But the `iss` claim in the token contains the URL for the actual tenant, e.g.:

```plain
https://login.microsoftonline.com/64c5f641-7e94-4d21-ae5c-9747994e4211/v2.0
```

A workaround that is often suggested is to disable issuer validation in the token validation parameters:

```csharp
openIdConnectOptions.TokenValidationParameters.ValidateIssuer = false;
```

However, if you do that the issuer won't be validated at all. Admittedly, it's not much of a problem, since the token signature will prove the issuer identity anyway, but it still bothers me...

Fortunately, you can control *how* the issuer is validated, by specifying the `TokenValidator` property:

```csharp
options.TokenValidationParameters.IssuerValidator = ValidateIssuerWithPlaceholder;
```

Where `ValidateIssuerWithPlaceholder` is the method that validates the issuer. In that method, we need to check if the issuer from the token matches the issuer with a placeholder from the metadata. To do this, we just replace the `{tenantid}` placeholder with the value of the token's `tid` claim (which contains the tenant id), and check that the result matches the token's issuer:

```csharp
private static string ValidateIssuerWithPlaceholder(string issuer, SecurityToken token, TokenValidationParameters parameters)
{
    // Accepts any issuer of the form "https://login.microsoftonline.com/{tenantid}/v2.0",
    // where tenantid is the tid from the token.

    if (token is JwtSecurityToken jwt)
    {
        if (jwt.Payload.TryGetValue("tid", out var value) &&
            value is string tokenTenantId)
        {
            var validIssuers = (parameters.ValidIssuers ?? Enumerable.Empty<string>())
                .Append(parameters.ValidIssuer)
                .Where(i => !string.IsNullOrEmpty(i));

            if (validIssuers.Any(i => i.Replace("{tenantid}", tokenTenantId) == issuer))
                return issuer;
        }
    }

    // Recreate the exception that is thrown by default
    // when issuer validation fails
    var validIssuer = parameters.ValidIssuer ?? "null";
    var validIssuers = parameters.ValidIssuers == null
        ? "null"
        : !parameters.ValidIssuers.Any()
            ? "empty"
            : string.Join(", ", parameters.ValidIssuers);
    string errorMessage = FormattableString.Invariant(
        $"IDX10205: Issuer validation failed. Issuer: '{issuer}'. Did not match: validationParameters.ValidIssuer: '{validIssuer}' or validationParameters.ValidIssuers: '{validIssuers}'.");

    throw new SecurityTokenInvalidIssuerException(errorMessage)
    {
        InvalidIssuer = issuer
    };
}
```

With this in place, you're now able to fully validate tokens from any Azure AD tenant without skipping issuer validation.

Happy coding, and merry Christmas!

