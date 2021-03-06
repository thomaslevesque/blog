---
layout: post
title: Making a WPF app using a SDK-style project with MSBuildSdkExtras
date: 2018-11-23T00:00:00.0000000
url: /2018/11/23/making-a-wpf-app-using-a-sdk-style-project-with-msbuildsdkextras/
tags:
  - .net core
  - .net core sdk
  - sdk-style project
  - WPF
categories:
  - WPF
---


Ever since the first stable release of the .NET Core SDK, we've enjoyed a better C# project format, often called "SDK-style" because you specify a SDK to use in the project file. It's still a .csproj XML file, it's still based on MSBuild, but it's much more lightweight and much easier to edit by hand. Personally, I love it and use it everywhere I can.

However, out of the box, it's only usable for some project types: ASP.NET Core apps, console applications, and simple class libraries. If you want to write a WPF Windows application, for instance, you're stuck with the old, bloated project format. This will change with .NET Core 3.0, but it's not there yet.

## MSBuildSdkExtras

Fortunately, Oren Novotny created a pretty cool project named [MSBuildSdkExtras](https://github.com/onovotny/MSBuildSdkExtras). This is basically an extension of the .NET Core SDK that adds missing MSBuild targets and properties to enable building project types that are not supported out of the box. It presents itself as an alternative SDK, i.e. instead of specifying `Sdk="Microsoft.NET.Sdk"` in the root element of your project file, you write `Sdk="MSBuild.Sdk.Extras/1.6.61"`. The SDK will be automatically resolved from NuGet (note that you need VS2017 15.6 or higher for this to work). Alternatively, you can just specify `Sdk="MSBuild.Sdk.Extras"`, and specify the SDK version in a `global.json` file in the solution root folder, like this:

```js
{
    "msbuild-sdks": {
        "MSBuild.Sdk.Extras": "1.6.61"
    }
}
```

This approach is useful to share the SDK version between multiple projects.

## Our first SDK-style WPF project

Let's see how to create a WPF project with the SDK project format. Follow the usual steps to create a new WPF application in Visual Studio. Once it's done, unload the project and edit the csproj file; replace the whole content with this:

```xml
<Project Sdk="MSBuild.Sdk.Extras/1.6.61">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net471</TargetFramework>
    <ExtrasEnableWpfProjectSetup>true</ExtrasEnableWpfProjectSetup>
  </PropertyGroup>
</Project>
```

Reload your project, and remove the `Properties/AssemblyInfo.cs` file. That's it, you can now build and run as usual, but now with a much more concise project file!

A few things to note:

- `ExtrasEnableWpfProjectSetup` is a MSBuildSdkExtras property to opt in to WPF support (which isn't enabled by default). Basically, it includes WPF file types with the appropriate build action (e.g. `ApplicationDefinition` for the `App.xaml` file, `Page` for other XAML files, etc.) and sets up appropriate tasks to handle XAML compilation.
- The `Properties/AssemblyInfo.cs` file is redundant, because a file with the same attributes is automatically generated by the SDK. You can control how the attributes are generated by setting the properties listed [on this page](https://docs.microsoft.com/en-us/dotnet/core/tools/csproj#assemblyinfo-properties). If you prefer to keep your own assembly info file, you can set `GenerateAssemblyInfo` to false in the project file.


## Limitations

While it's very convenient to be able to use this project format for WPF apps, there are a few limitations to be aware of:

- Even though we're using the .NET Core SDK project format, we need WPF-specific MSBuild tasks that are not available in the .NET Core SDK. So you can't use `dotnet build` to build the project, you *have* to use MSBuild (or Visual Studio, which uses MSBuild).
- This project format for WPF projects isn't fully supported in Visual Studio; it will build and run just fine, but some features won't work correctly, e.g. Visual Studio won't offer the appropriate item templates when you add a new item to the project.


But even with these limitations, MSBuildSdkExtras gives us a taste of what we'll be able to do in .NET Core 3.0!

