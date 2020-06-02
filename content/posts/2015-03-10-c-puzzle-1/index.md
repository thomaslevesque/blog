---
layout: post
title: C# Puzzle 1
date: 2015-03-10T00:00:00.0000000
url: /2015/03/10/c-puzzle-1/
tags:
  - C#
  - puzzle
categories:
  - Puzzles
---


I love to solve C# puzzles; I think it’s a great way to gain a deep understanding of the language. And besides, it’s fun!

I just came up with this one:

```csharp
static void Test(out int x, out int y)
{
    x = 42;
    y = 123;
    Console.WriteLine (x == y);
}
```

What do you think this code prints? Can you be sure? Post your answer in the comments!

I’ll try to post more puzzles in the future if I can come up with others.

