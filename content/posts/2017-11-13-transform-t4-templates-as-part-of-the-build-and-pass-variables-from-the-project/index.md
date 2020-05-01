---
layout: post
title: Transform T4 templates as part of the build, and pass variables from the project
date: 2017-11-12T22:05:47.0000000
url: /2017/11/13/transform-t4-templates-as-part-of-the-build-and-pass-variables-from-the-project/
tags:
  - build
  - code generation
  - msbuild
  - T4
  - Visual Studio
categories:
  - Uncategorized
---


[T4 (Text Template Transformation Toolkit)](https://docs.microsoft.com/en-us/visualstudio/modeling/code-generation-and-t4-text-templates) is a great tool to generate code at design time; you can, for instance, create POCO classes from database tables, generate repetitive code, etc. In Visual Studio, T4 files (.tt extension) are associated with the `TextTemplatingFileGenerator` custom tool, which transforms the template to generate an output file every time you save the template. But sometimes it's not enough, and you want to ensure that the template's output is regenerated before build. It's pretty easy to set this up, but there are a few gotchas to be aware of.

## Transforming templates at build time

If your project is a classic csproj or vbproj (i.e. not a .NET Core SDK-style project), things are actually quite simple and well documented on [this page](https://docs.microsoft.com/en-us/visualstudio/modeling/code-generation-in-a-build-process).

Unload your project, and open it in the editor. Add the following `PropertyGroup` near the beginning of the file:

```xml


    
    15.0
    $(MSBuildExtensionsPath32)\Microsoft\VisualStudio\v$(VisualStudioVersion)
    
    true
    
    true
    
    false
```

And add the following `Import` at the end, after the import of `Microsoft.CSharp.targets` or `Microsoft.VisualBasic.targets`:

```xml

```

Reload your project, and you're done. Building the project should now transform the templates and regenerate their output.

## SDK-style projects

If you're using the new project format that comes with the .NET Core SDK (sometimes informally called "SDK-style project"), the approach described above will need a small change to work. This is because the default targets file (`Sdk.targets` in the .NET Core SDK) is now imported implicitly at the very end of the project, so you can't import the text templating targets after the default targets. This causes the `BuildDependsOn` variable, which is modified by the T4 targets, to be overwritten, so the `TransformAll` target doesn't run before the `Build` target.

Fortunately, there's a workaround: you can import the default targets file explicitly, and import the text templating targets after that:

```xml

```

Note that it will cause a MSBuild warning in the build output (MSB4011) because `Sdk.targets` is imported twice; you can safely ignore this warning.

## Passing MSBuild variables to templates

At some point, the code generation logic might become too complex to remain entirely in the T4 template file. You might want to extract some of it into a helper assembly, and reference this assembly from the template, like this:

```

<#@ assembly name="../CodeGenHelper/bin/Release/net462/CodeGenHelper.dll" #>
```

Of course, specifying the path like this isn't very very convenient... For instance, if you're currently in `Debug` configuration, the `Release` version of CodeGenHelper.dll might be out of date. Fortunately, Visual Studio's `TextTemplatingFileGenerator` custom tool recognizes MSBuild variables from the project, so you can do this instead:

```

<#@ assembly name="$(SolutionDir)/CodeGenHelper/bin/$(Configuration)/net462/CodeGenHelper.dll" #>
```

The `$(SolutionDir)` and `$(Configuration)` variables will be expanded to their actual values. If you save the template, the template will be transformed using the CodeGenHelper.dll assembly. Nice!

However, there's a catch... if you configured the project to transform templates on build as described above, the build will now fail, with an error like this:


> System.IO.FileNotFoundException: Could not find a part of the path 'C:\Path\To\The\Project\$(SolutionDir)\CodeGenHelper\bin\$(Configuration)\net462\CodeGenHelper.dll'.


Notice the `$(SolutionDir)` and `$(Configuration)` variables in the path? They were not expanded! This is because the MSBuild target that transforms the templates and the `TextTemplatingFileGenerator` custom tool don't use the same text transformation engine. And unfortunately, the one used by MSBuild doesn't recognize MSBuild properties out of the box... Ironic, isn't it?

All is not lost, though. All you have to do is explicitly specify the variables you want to pass as T4 parameters. Edit your project file again, and create a new `ItemGroup` with the following items:

```xml


    
        $(SolutionDir)
        False
    
    
        $(Configuration)
        False
    
```

The `Include` attribute is the name of the parameter as it will be passed to the text transformation engine. The `Value` element is, well, the value. And the `Visible` element prevents the `T4ParameterValues` item from appearing under the project in the solution explorer.

With this change, the build should now successfully transform the templates again.

So, just keep in mind that the `TextTemplatingFileGenerator` custom tool and the MSBuild text transformation target have different mechanisms for passing variables:

- `TextTemplatingFileGenerator` supports *only* MSBuild variables from the project
- MSBuild supports *only* `T4ParameterValues`


So if you use variables in your template and you want to be able to transform it when you save the template in Visual Studio *and* when you build the project, the variables have to be defined both as MSBuild variables and as `T4ParameterValues`.

