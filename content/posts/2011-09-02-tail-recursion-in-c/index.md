---
layout: post
title: Tail recursion in C#
date: 2011-09-01T22:16:52.0000000
url: /2011/09/02/tail-recursion-in-c/
tags:
  - C#
  - tail recursion
  - trampoline
categories:
  - Code sample
---


Regardless of the programming language you're using, there are tasks for which the most natural implementation uses a recursive algorithm (even if it's not always the optimal solution). The trouble with the recursive approach is that it can use a lot of space on the stack: when you reach a certain recursion depth, the memory allocated for the thread stack runs out, and you get a stack overflow error that usually terminates the process (`StackOverflowException` in .NET).

###### **Tail recursion? What's that?**

Some languages, more particularly functional languages, have native support for an optimization technique called [tail recursion](http://en.wikipedia.org/wiki/Tail_recursion). The idea is that if the recursive call is the last instruction in a recursive function, there is no need to keep the current call context on the stack, since we won't have to go back there: we only need to replace the parameters with their new values, and jump back to the beginning of the function. So the recursion is transformed into an iteration, so it can’t cause a stack overflow. This notion being quite new to me, I won’t try to give a full course about tail recursion… much smarter people already took care of it! I suggest you follow the Wikipedia link above, which is a good starting point to understand tail recursion.

Unfortunately, the C# compiler doesn’t support tail recursion, which is a pity, since [the CLR supports it](http://msdn.microsoft.com/en-us/library/system.reflection.emit.opcodes.tailcall.aspx). However, all is not lost! Some people had a very clever idea to work around this issue: a technique called “trampoline” (because it makes the function “bounce”) that allows to easily transform a recursive algorithm into an iterative algorithm. Samuel Jack has a good explanation of this concept [on his blog](http://blog.functionalfun.net/2008/04/bouncing-on-your-tail.html). In the rest of this article, we will see how to apply this technique to a simple algorithm, using the class from Samuel Jack’s article; then I’ll present another implementation of the trampoline, which I find more flexible.

###### **A simple use case in C#**

Let’s see how we can transform a simple recursive algorithm, like the computation of the factorial of a number, into an algorithm that uses tail recursion (incidentally, the factorial can be computed much more efficiently with a non-recursive algorithm, but let’s assume we don’t know that…). Here’s a basic implementation that results directly from the definition:

```

BigInteger Factorial(int n)
{
    if (n < 2)
        return 1;
    return n * Factorial(n - 1);
}
```

(Note the use of `BigInteger`: if we are to make the recursion deep enough to observe the effects of tail recursion, the result will be far beyond the capacity of an int or even a `long`…)

If we call this method with a large value (around 20000 on my machine), we get an error which was quite predictable: `StackOverflowException`. We made so many nested call to the `Factorial` method that we exhausted the capacity of the stack. So we’re going to modify this code so that it can benefit from tail recursion…

As mentioned above, the key requirement for tail recursion is that the method calls itself as the last instruction. It *seems* to be the case here… but it’s not: the last operation is actually the multiplication, which can’t be executed until we know the result of `Factorial(n-1)`. So we need to redesign this method so that it ends with a call to itself, with different arguments. To do that, we can add a new parameter named `product`, which will act as an accumulator:

```

BigInteger Factorial(int n, BigInteger product)
{
    if (n < 2)
        return product;
    return Factorial(n - 1, n * product);
}
```

For the first call, we’ll just have to pass 1 for the initial value of the accumulator.

We now have a method that meets the requirements for tail recursion: the recursive call to `Factorial` really is the last instruction. Now that we have put the algorithm in this form, the final transformation to enable tail recursion using Samuel Jack’s trampoline is trivial:

```

Bounce<int, BigInteger, BigInteger> Factorial(int n, BigInteger product)
{
    if (n < 2)
        return Trampoline.ReturnResult<int, BigInteger, BigInteger>(product);
    return Trampoline.Recurse<int, BigInteger, BigInteger>(n - 1, n * product);
}
```

- Instead of returning the final result directly, we call `Trampoline.ReturnResult` to tell the trampoline that we now have a result
- The recursive call to `Factorial` is replaced with a call to `Trampoline.Recurse`, which tells the trampoline that the method needs to be called again with different parameters


This method can’t be used directly: it returns a `Bounce` object, and we don't really know what to do with this… To execute it, we use the `Trampoline.MakeTrampoline` method, which returns a new function on which tail recursion is applied. We can then use this new function directly:

```

Func<int, BigInteger, BigInteger> fact = Trampoline.MakeTrampoline<int, BigInteger, BigInteger>(Factorial);
BigInteger result = fact(50000, 1);
```

We can now compute the factorial of large numbers, with no risk of causing a stack overflow… Admittedly, it’s not very efficient: as mentioned before, there are better ways of computing a factorial, and furthermore, computations involving `BigInteger`s are much slower than with `int`s or `long`s.

###### **Can we make it better?**

Well, you can guess that I wouldn’t be asking the question unless the answer was yes… The trampoline implementation demonstrated above does its job well enough, but I think it could be made more flexible and easier to use:

- It only works if you have 2 parameters (of course we can adapt it for a different number of parameters, but then we need to create new methods with adequate signatures for each different arity)
- The syntax is quite unwieldy: there are 3 type arguments, and we need to specify them every time because the compiler doesn’t have enough information to infer them automatically
- Having to use `MakeTrampoline` just to create a new function that we can then call isn’t very convenient; it would be more intuitive to have an `Execute` method that returns the result directly


And finally, I think the terminology isn’t very explicit… Names like `Trampoline` and `Bounce` sound like fun, but they don’t really reveal the intent.

So I tried to improve the system to make it more convenient. My solution is based on lambda expressions. There is only one type argument (the return type), and the parameters are passed trough a closure, so there is no need for multiple methods to handle different numbers of parameters. Here’s what the `Factorial` method looks like with my implementation:

```

RecursionResult<BigInteger> Factorial(int n, BigInteger product)
{
    if (n < 2)
        return TailRecursion.Return(product);
    return TailRecursion.Next(() => Factorial(n - 1, n * product));
}
```

It can be used as follows:

```

BigInteger result = TailRecursion.Execute(() => Factorial(50000, 1));
```

It’s more flexible, more concise, and more readable…in my opinion at least![Sourire](wlemoticon-smile.png). The downside is that performance is slightly worse than before (it takes about 20% longer to compute the factorial of 50000), probably because of the delegate creation at each level of recursion.

Here’s the full code for the `TailRecursion` class:

```

public static class TailRecursion
{
    public static T Execute<T>(Func<RecursionResult<T>> func)
    {
        do
        {
            var recursionResult = func();
            if (recursionResult.IsFinalResult)
                return recursionResult.Result;
            func = recursionResult.NextStep;
        } while (true);
    }

    public static RecursionResult<T> Return<T>(T result)
    {
        return new RecursionResult<T>(true, result, null);
    }

    public static RecursionResult<T> Next<T>(Func<RecursionResult<T>> nextStep)
    {
        return new RecursionResult<T>(false, default(T), nextStep);
    }

}

public class RecursionResult<T>
{
    private readonly bool _isFinalResult;
    private readonly T _result;
    private readonly Func<RecursionResult<T>> _nextStep;
    internal RecursionResult(bool isFinalResult, T result, Func<RecursionResult<T>> nextStep)
    {
        _isFinalResult = isFinalResult;
        _result = result;
        _nextStep = nextStep;
    }

    public bool IsFinalResult { get { return _isFinalResult; } }
    public T Result { get { return _result; } }
    public Func<RecursionResult<T>> NextStep { get { return _nextStep; } }
}
```

###### **Is there a better way to accomplish tail recursion in C#?**

Sure! But it gets a little tricky, and it’s not pure C#. As I mentioned before, the CLR supports tail recursion, through the `tail` instruction. Ideally, the C# compiler would automatically generate this instruction for methods that are eligible to tail recursion, but unfortunately it’s not the case, and I don’t think this will ever be supported given the low demand for this feature.

Anyway, we can cheat a little by helping the compiler to do its job: the .NET Framework SDK provides tools named [ildasm](http://msdn.microsoft.com/en-us/library/f7dy01k1.aspx) (IL disassembler) and [ilasm](http://msdn.microsoft.com/en-us/library/496e4ekx.aspx) (IL assembler), which can help to fill the gap between C# and the CLR… Let’s go back to the classical recursive implementation of `Factorial`, which doesn’t yet use tail recursion:

```

static BigInteger Factorial(int n, BigInteger product)
{if (n < 2)	return product;return Factorial(n - 1, n * product);
}
```

If we compile this code and disassemble it with ilasm, we get the following IL code:

```

.method private hidebysig static valuetype [System.Numerics]System.Numerics.BigInteger
        Factorial(int32 n,
                  valuetype [System.Numerics]System.Numerics.BigInteger product) cil managed
{
  // Code size       41 (0x29)
  .maxstack  3
  .locals init (valuetype [System.Numerics]System.Numerics.BigInteger V_0,
           bool V_1)
  IL_0000:  nop
  IL_0001:  ldarg.0
  IL_0002:  ldc.i4.2
  IL_0003:  clt
  IL_0005:  ldc.i4.0
  IL_0006:  ceq
  IL_0008:  stloc.1
  IL_0009:  ldloc.1
  IL_000a:  brtrue.s   IL_0010

  IL_000c:  ldarg.1
  IL_000d:  stloc.0
  IL_000e:  br.s       IL_0027

  IL_0010:  ldarg.0
  IL_0011:  ldc.i4.1
  IL_0012:  sub
  IL_0013:  ldarg.0
  IL_0014:  call       valuetype [System.Numerics]System.Numerics.BigInteger [System.Numerics]System.Numerics.BigInteger::op_Implicit(int32)
  IL_0019:  ldarg.1
  IL_001a:  call       valuetype [System.Numerics]System.Numerics.BigInteger [System.Numerics]System.Numerics.BigInteger::op_Multiply(valuetype [System.Numerics]System.Numerics.BigInteger,
                                                                                                                                      valuetype [System.Numerics]System.Numerics.BigInteger)
  IL_001f:  call       valuetype [System.Numerics]System.Numerics.BigInteger Program::Factorial(int32,
                                                                                                valuetype [System.Numerics]System.Numerics.BigInteger)
  IL_0024:  stloc.0
  IL_0025:  br.s       IL_0027

  IL_0027:  ldloc.0
  IL_0028:  ret
} // end of method Program::Factorial
```

It’s a bit hard on the eye if you’re not used to read IL code, but we can see roughly what’s going on… The recursive call is at offset `IL_001f;` this is where we’re going to fiddle with the generated code to introduce tail recursion. If we look at the documentation for the `tail` instruction, we see that it must immediately precede a `call` instruction, and that the instruction following the `call` must be `ret` (return). Right now, we have several instructions following the recursive call, because the compiler introduced a local variable to store the return value. We just need to modify the code so that it doesn’t use this variable, and add the `tail` instruction in the right place:

```

.method private hidebysig static valuetype [System.Numerics]System.Numerics.BigInteger
        Factorial(int32 n,
                  valuetype [System.Numerics]System.Numerics.BigInteger product) cil managed
{
  // Code size       41 (0x29)
  .maxstack  3
  .locals init (valuetype [System.Numerics]System.Numerics.BigInteger V_0,
           bool V_1)
  IL_0000:  nop
  IL_0001:  ldarg.0
  IL_0002:  ldc.i4.2
  IL_0003:  clt
  IL_0005:  ldc.i4.0
  IL_0006:  ceq
  IL_0008:  stloc.1
  IL_0009:  ldloc.1
  IL_000a:  brtrue.s   IL_0010

  IL_000c:  ldarg.1
  IL_000d:  ret		// Return directly instead of storing the result in V_0
  IL_000e:  nop

  IL_0010:  ldarg.0
  IL_0011:  ldc.i4.1
  IL_0012:  sub
  IL_0013:  ldarg.0
  IL_0014:  call       valuetype [System.Numerics]System.Numerics.BigInteger [System.Numerics]System.Numerics.BigInteger::op_Implicit(int32)
  IL_0019:  ldarg.1
  IL_001a:  call       valuetype [System.Numerics]System.Numerics.BigInteger [System.Numerics]System.Numerics.BigInteger::op_Multiply(valuetype [System.Numerics]System.Numerics.BigInteger,
                                                                                                                                      valuetype [System.Numerics]System.Numerics.BigInteger)
  IL_001f:  tail.
  IL_0020:  call       valuetype [System.Numerics]System.Numerics.BigInteger Program::Factorial(int32,
                                                                                                valuetype [System.Numerics]System.Numerics.BigInteger)
  IL_0025:  ret		// Return directly instead of storing the result in V_0

} // end of method Program::Factorial
```

If we reassemble this code with ilasm, we get a new executable, which runs without issues even for large values which made the old code crash![Sourire](wlemoticon-smile.png). Performance is also pretty good: about 3 times as fast than the version using the `Trampoline` class. If we compare the performance for smaller values (so that the old code doesn’t crash), we can see that it’s also 3 times as fast as the recursive version with no tail recursion.

Of course, this is just a proof of concept… it doesn’t seem very realistic to perform this transformation manually in a “real” project. However, it might be possible to create a tool that rewrites assemblies automatically after the compilation to introduce tail recursion.

