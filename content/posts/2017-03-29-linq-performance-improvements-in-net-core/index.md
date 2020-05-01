---
layout: post
title: Linq performance improvements in .NET Core
date: 2017-03-28T22:18:21.0000000
url: /2017/03/29/linq-performance-improvements-in-net-core/
tags:
  - .net core
  - C#
  - linq
  - performance
categories:
  - Uncategorized
---


By now, you're probably aware that Microsoft released an open-source and cross-platform version of the .NET platform: [.NET Core](https://github.com/dotnet/corefx). This means you can now build and run .NET apps on Linux or macOS. This is pretty cool in itself, but it doesn't end there: .NET Core also brings a lot of improvements to the Base Class Library.

For instance, Linq has been made faster in .NET Core. I made a little benchmark to compare the performance of some common Linq methods, and the results are quite impressive:
<!--![Performance comparison](perf.png)--><iframe width="700" height="210" frameborder="0" scrolling="no" src="https://onedrive.live.com/embed?cid=D2FB47CF02C0FD46&resid=D2FB47CF02C0FD46%21439375&authkey=AGAkuUFFLgMK5_Q&em=2&wdAllowInteractivity=False&ActiveCell='Sheet1'!A2&Item='Sheet1'!A1%3AG8&wdHideGridlines=True&wdDownloadButton=True"></iframe>
The full code for the benchmark can be found [here](https://github.com/thomaslevesque/TestLinqPerf). As with all microbenchmarks, it has to be taken with a grain of salt, but it gives an idea of the improvements.

Some lines in this table are quite surprising. How can `Select` run 5000 times almost instantly? First, we have to keep in mind that most Linq operators are lazy: they don't actually do anything until you enumerate the result, so doing something like `array.Select(i => i * i)` executes in constant time (it just returns a lazy sequence, without consuming the items in `array`). This is why I included a call to `Count()` in my benchmark, to make sure the result is enumerated.

Despite this, it runs 5000 times in 413µs... This is possible due to an optimization in the .NET Core implementation of `Select` and `Count`. A useful property of `Select` is that it produces a sequence with the same number of items as the source sequence. In .NET Core, `Select` takes advantage of this. If the source is an `ICollection<T>` or an array, it returns a custom enumerable object that keeps track of the number of items. `Count` can then just retrieve this value and return it, which produces a result in constant time. The full .NET Framework implementation, on the other hand, naively enumerates the sequence produced by `Select`, which takes much longer.

It's interesting to note that in this situation, .NET Core will *not* execute the projection specified in `Select`, so it's a breaking change compared to the desktop framework for code that was relying on side effects of this projection. This has been identified as an [issue](https://github.com/dotnet/corefx/pull/14435) which has already been fixed on the master branch, so the next release of .NET Core *will* execute the projection on each item.

`OrderBy` followed by `Count()` also runs almost instantly... did Microsoft invent a `O(1)` sorting algorithm? Unfortunately, no... The explanation is the same as for `Select`: since `OrderBy` preserves the item count, the information is recorded so that it can be used by `Count`, and there is no need to actually sort the input sequence.

OK, so these cases were pretty obvious improvements (which will be rolled back anyway, as mentioned above). What about the `SelectAndToArray` case? In this test, I call `ToArray()` on the result of `Select`, to make sure that the projection is actually performed on each item of the source sequence: no cheating this time. Still, the .NET Core version is 68% faster than the full .NET Framework version. The reason has to do with allocations: since the .NET Core implementation knows how many items are in the result of `Select`, it can directly allocate an array of the correct size. In the .NET Framework, this information is not available, so it starts with a small array, copies items into it until it's full, then allocates a larger array, copies the previous array into it, copies the next items from the sequence until the array is full, and so on. This causes a lot of allocations and copies, hence the degraded performance. A few years ago, I [suggested](http://www.thomaslevesque.com/2014/12/07/optimize-toarray-and-tolist-by-providing-the-number-of-elements/) an optimized version of `ToList` and `ToArray`, where you had to specify the size. The .NET Core implementation basically does the same thing, except that you don't have to pass the size manually, since it's passed along the Linq method chain.

`Where` and `WhereAndToArray` are both about 8% faster on .NET Core 1.1. Looking at the code ([.NET 4.6.2](https://referencesource.microsoft.com/#System.Core/System/Linq/Enumerable.cs,ed14299f42af7eb2), [.NET Core](https://github.com/dotnet/corefx/blob/e5cba5572d5b3634e768e3df3ddb5399fcf969b1/src/System.Linq/src/System/Linq/Where.cs#L208)), I can't see any obvious difference that could explain the better performance, so I suspect it's mostly due to improvements in the runtime. In this case, `ToArray` doesn't know the length of the input sequence, since there is no way to predict how many items `Where` will yield, so it can't use the same optimization as with `Select` and has to build the array the slow way.

We already discussed `OrderBy` + `Count()`, which wasn't a fair comparison since the .NET Core implementation didn't actually sort the sequence. The `OrderByAndToArray` case is more interesting, because the sort can't be skipped. And in this case, the .NET Core implementation is slightly *slower* than the .NET 4.6.2 one. I'm not sure why this is; again, the implementation is very similar, although there has been a bit of refactoring in .NET Core.

So, on the whole, Linq seems generally faster in .NET Core than in .NET 4.6.2, which is very good news. Of course, I only benchmarked a limited numbers of scenarios, but it shows the .NET Core team is working hard to optimize everything they can.

