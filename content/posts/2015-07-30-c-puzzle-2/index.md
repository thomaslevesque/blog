---
layout: post
title: C# Puzzle 2
date: 2015-07-30T00:00:00.0000000
url: /2015/07/30/c-puzzle-2/
tags:
  - C#
  - puzzle
categories:
  - Puzzles
---


Just another little puzzle based on an issue I had at work…

Consider this piece of code :

```
Console.WriteLine($"x > y is {x > y}");
Console.WriteLine($"!(x <= y) is {!(x <= y)}");
```

How would you declare and initialize `x` and `y` for the program to produce the following, apparently illogical, output?

```
x > y is False
!(x <= y) is True
```

