---
layout: post
title: Using C# 5 caller info attributes when targeting earlier versions of the .NET framework
date: 2012-06-13T00:00:00.0000000
url: /2012/06/13/using-c-5-caller-info-attributes-when-targeting-earlier-versions-of-the-net-framework/
tags:
  - c# 5
  - caller info
categories:
  - Tips and tricks
---

[Caller info attributes](http://msdn.microsoft.com/en-us/library/hh534540%28v=vs.110%29.aspx) are one of the new features of C# 5. They're attributes applied to optional method parameters that enable you to pass caller information implicitly to a method. I'm not sure that description is very clear, so an example will help you understand:  
```csharp
        static void Log(
            string message,
            [CallerMemberName] string memberName = null,
            [CallerFilePath] string filePath = null,
            [CallerLineNumber] int lineNumber = 0)
        {
            Console.WriteLine(
                "[{0:g} - {1} - {2} - line {3}] {4}",
                DateTime.UtcNow,
                memberName,
                filePath,
                lineNumber,
                message);
        }
```
  The method above takes several parameters intended to pass information about the caller: calling member name, source file path and line number. The `Caller*` attributes make the compiler pass the appropriate values automatically, so you don't have to specify the values for these parameters:  
```csharp
        static void Foo()
        {
            Log("Hello world");
            // Equivalent to:
            // Log("Hello world", "Foo", @"C:\x\y\z\Program.cs", 18);
        }
```
  This is of course especially useful for logging methods...  Notice that the `Caller*` attributes are defined in the .NET Framework 4.5. Now, suppose we use Visual Studio 2012 to target an earlier framework version (e.g. 4.0): the caller info attributes don't exist in 4.0, so we can't use them... But wait! What if we could trick the compiler into thinking the attributes exist? Let's define our own attributes, taking care to put them in the namespace where the compiler expects them:  
```csharp
namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
    public class CallerMemberNameAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
    public class CallerFilePathAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
    public class CallerLineNumberAttribute : Attribute
    {
    }
}
```
  If we compile and run the program, we can see that our custom attributes are taken into account by the compiler. So they don't have to be defined in mscorlib.dll like the "real" ones, they just have to be in the right namespace, and the compiler accepts them. This enables us to use this cool feature when targeting .NET 4.0, 3.5 or even 2.0!  Note that a similar trick enabled the creation of extension methods when targeting .NET 2.0 with the C# 3 compiler: you just had to create an `ExtensionAttribute` class in the `System.Runtime.CompilerServices` namespace, and the compiler would pick it up. This is also what enabled [LinqBridge](http://www.albahari.com/nutshell/linqbridge.aspx) to work.  

