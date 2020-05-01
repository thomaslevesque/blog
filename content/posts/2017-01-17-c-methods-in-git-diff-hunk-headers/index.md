---
layout: post
title: C# methods in git diff hunk headers
date: 2017-01-17T21:31:45.0000000
url: /2017/01/17/c-methods-in-git-diff-hunk-headers/
tags:
  - async
  - C#
  - csharp
  - diff
  - git
  - gitattributes
  - xfuncname
categories:
  - Uncategorized
---


If you use git on the command line, you may have noticed that diff hunks often show the method signature in the hunk header (the line that starts with `@@`), like this:

```diff

diff --git a/Program.cs b/Program.cs
index 655a213..5ae1016 100644
--- a/Program.cs
+++ b/Program.cs
@@ -13,6 +13,7 @@ static void Main(string[] args)
         Console.WriteLine("Hello World!");
         Console.WriteLine("Hello World!");
         Console.WriteLine("Hello World!");
+        Console.WriteLine("blah");
     }
```

This is very useful to know where you are when looking at a diff.

Git has a few built-in regex patterns to detect methods in some languages, including C#; they are defined in [`userdiff.c`](https://github.com/git/git/blob/d7dffce1cebde29a0c4b309a79e4345450bf352a/userdiff.c#L140). But by default, these patterns are not used... you need to tell git which file extensions should be associated with which language. This can be specified in a `.gitattributes` file at the root of your git repository:

```

*.cs    diff=csharp
```

With this done, `git diff` should show an output similar to the sample above.

Are we done yet? Well, almost. See, the patterns for C# were added to git a long time ago, and C# has changed quite a bit since then. Some new keywords that can now be part of a method signature are not recognized by the built-in pattern, e.g. `async` or `partial`. This is quite annoying, because when some code has changed in an async method, the diff hunk header shows the signature of a previous, non-async method, or the line where the class is declared, which is confusing.

My first impulse was to submit a pull request on Github to add the missing keywords; however I soon realized that the [git repository on Github](https://github.com/git/git) is just a mirror and does not accept pull requests... The [contribution process](https://github.com/git/git/blob/master/Documentation/SubmittingPatches) consists of sending a patch to the git mailing list, with a long and annoying checklist of requirements. This process seemed so tedious that I gave it up. I honestly don't know why they use such a difficult and old-fashioned contribution process, it just discourages casual contributors. But that's a bit off-topic, so let's move on and try to solve the problem some other way.

Fortunately, the built-in patterns can be overridden in the git configuration. To define the function name pattern for C#, you need to define the `diff.csharp.xfuncname` setting in your git config file:

```

[[diff "csharp"]]
  xfuncname = ^[ \\t]*(((static|public|internal|private|protected|new|virtual|sealed|override|unsafe|async|partial)[ \\t]+)*[][<>@.~_[:alnum:]]+[ \\t]+[<>@._[:alnum:]]+[ \\t]*\\(.*\\))[ \\t]*$
```

As you can see, it's the same pattern as in `userdiff.c`, with the backslashes escaped and the missing keywords added. With this pattern, `git diff` now shows the correct function signature in async methods:

```diff

diff --git a/Program.cs b/Program.cs
index 655a213..5ae1016 100644
--- a/Program.cs
+++ b/Program.cs
@@ -31,5 +32,6 @@ static async Task FooAsync()
         Console.WriteLine("Hello world");
         Console.WriteLine("Hello world");
         Console.WriteLine("Hello world");
+        await Task.Delay(100);
     }
 }
```

It took me a while to figure it out, so I hope you find it helpful!

