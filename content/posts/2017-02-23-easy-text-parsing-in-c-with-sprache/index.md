---
layout: post
title: Easy text parsing in C# with Sprache
date: 2017-02-23T00:00:00.0000000
url: /2017/02/23/easy-text-parsing-in-c-with-sprache/
tags:
  - parser
  - parser combinator
  - parsing
  - sprache
categories:
  - Uncategorized
---


A few days ago, I discovered a little gem: [Sprache](https://github.com/sprache/sprache). The name means "language" in German. It's a very elegant and easy to use library to create text parsers, using [parser combinators](https://en.wikipedia.org/wiki/Parser_combinator), which are a very common technique in functional programming. The theorical concept may seem a bit scary, but as you'll see in a minute, Sprache makes it very simple.

## Text parsing

Parsing text is a common task, but it can be tedious and error-prone. There are plenty of ways to do it:

- manual parsing based on `Split`, `IndexOf`, `Substring` etc.
- regular expressions
- hand-built parser that scans the string for tokens
- full blown parser generated with ANTLR or a similar tool
- and probably many others...


None of these options is very appealing. For simple cases, splitting the string or using a regex can be enough, but it doesn't scale to more complex grammars. Building a real parser by hand for non-trivial grammars is, well, non-trivial. ANTLR requires Java, a bit of knowledge, and it relies on code generation, which complicates the build process.

Fortunately, [Sprache](https://github.com/sprache/sprache) offers a very nice alternative. It provides many predefined parsers and combinators that you can use to define a grammar. Let's walk through an example: parsing the challenge in the `WWW-Authenticate` header of an HTTP response (I recently had to write a parser by hand for this recently, and I wish I had known Sprache then).

## The grammar

The `WWW-Authenticate` header is sent by an HTTP server as part of a 401 (Unauthorized) response to indicate how you should authenticate:

```
# Basic challenge
WWW-Authenticate: Basic realm="FooCorp"

# OAuth 2.0 challenge after sending an expired token
WWW-Authenticate: Bearer realm="FooCorp", error=invalid_token, error_description="The access token has expired"
```

What we want to parse is the "challenge", i.e. the value of the header. So, we have an authentication scheme (`Basic`, `Bearer`), followed by one or more parameters (name-value pairs). This looks simple enough, we could probably just split by `','` then by `'='` to get the values... but the double quotes complicate things, since quoted strings could contain the `','` or `'='` characters. Also, the double quotes are optional if the parameter value is a single token, so we can't rely on the fact they will (or won't) be there. If we want to parse this reliably, we're going to have to look at the specs.

The `WWW-Authenticate` header is described in detail in [RFC-2617](https://tools.ietf.org/html/rfc2617). The grammar looks like this, in what the RFC calls "augmented Backus-Naur Form" (see [RFC 2616 §2.1](https://tools.ietf.org/html/rfc2616#section-2.1)):

```
# from RFC-2617 (HTTP Basic and Digest authentication)

challenge      = auth-scheme 1*SP 1#auth-param
auth-scheme    = token
auth-param     = token "=" ( token | quoted-string )

# from RFC2616 (HTTP/1.1)

token          = 1*<any CHAR except CTLs or separators>
separators     = "(" | ")" | "<" | ">" | "@"
               | "," | ";" | ":" | "\" | <">
               | "/" | "[" | "]" | "?" | "="
               | "{" | "}" | SP | HT
quoted-string  = ( <"> *(qdtext | quoted-pair ) <"> )
qdtext         = <any TEXT except <">>
quoted-pair    = "\" CHAR
```

So, we have a few grammar rules, let's see how we can encode them in C# code with Sprache, and use them to parse a challenge.

## Parsing tokens

Let's start with the most simple parts of the grammar: tokens. A token is declared as one or more of any characters that are not control chars or separators.

We'll define our rules in a `Grammar` class. Let's start by defining some character classes:

```csharp
static class Grammar
{
    private static readonly Parser<char> SeparatorChar =
        Parse.Chars("()<>@,;:\\\"/[]?={} \t");

    private static readonly Parser<char> ControlChar =
        Parse.Char(Char.IsControl, "Control character");

}
```

- Each rule is declared as a `Parser<T>`; since these rules match single characters, they are of type `Parser<char>`.
- The `Parse` class from Sprache exposes parser primitives and combinators.
- `Parse.Chars` matches any character from the specified string, we use it to specify the list of separator characters.
- The overload of `Parse.Char` that we use here takes a predicate that will be called to check if the character matches, and a description of the character class. Here we just use `System.Char.IsControl` as the predicate to match control characters.


Now, let's define a `TokenChar` character class to match characters that can be part of a token. As per the RFC, this can be any character not in the previous classes:

```csharp
    private static readonly Parser<char> TokenChar =
        Parse.AnyChar
            .Except(SeparatorChar)
            .Except(ControlChar);
```

- `Parse.AnyChar`, as the name implies, matches any character.
- `Except` specifies exceptions to the rule.


Finally, a token is a sequence of one or more of these characters:

```csharp
    private static readonly Parser<string> Token =
        TokenChar.AtLeastOnce().Text();
```

- A token is a string, so the rule for a token is of type `Parser<string>`.
- `AtLeastOnce()` means one or more repetitions, and since `TokenChar` is a `Parser<char>`, it returns a `Parser<IEnumerable<char>>`.
- `Text()` combines the sequence of characters into a string, returning a `Parser<string>`.


We're now able to parse a token. But it's just a small step, and we still have a lot to do...

## Parsing quoted strings

The grammar defines a quoted string as a sequence of:

- an opening double quote
- any number of either
    - a "qdtext", which is any character except a double quote
    - a "quoted pair", which is any character preceded by a backslash (this is used to escape double quotes inside a string)
- a closing double quote


Let's write the rules for "qdtext" and "quoted pair":

```csharp
    private static readonly Parser<char> DoubleQuote = Parse.Char('"');
    private static readonly Parser<char> Backslash = Parse.Char('\\');

    private static readonly Parser<char> QdText =
        Parse.AnyChar.Except(DoubleQuote);

    private static readonly Parser<char> QuotedPair =
        from _ in Backslash
        from c in Parse.AnyChar
        select c;
```

The `QdText` rule doesn't require much explanation, but `QuotedPair` is more interesting... As you can see, it looks like a Linq query: this is Sprache's way of specifying a sequence. This particular query means: *match a backslash (named `_` because we ignore it) followed by any character named `c`, and return just `c`* (quoted pairs are not escape sequences in the same sense as in C, Java or C#, so `"\n"` isn't interpreted as "new line" but just as `"n"`).

We can now write the rule for a quoted string:

```csharp
    private static readonly Parser<string> QuotedString =
        from open in DoubleQuote
        from text in QuotedPair.Or(QdText).Many().Text()
        from close in DoubleQuote
        select text;
```

- the `Or` method indicates a choice between two parsers. `QuotedPair.Or(QdText)` will try to match a quoted pair, and if that fails, it will try to match a `QdText` instead.
- `Many()` indicates any number of repetition
- `Text()` combines the characters into a string


We now have all the basic building blocks, so we can move on to higher level rules.

### Parsing challenge parameters

A challenge is made of an auth scheme followed by one or more parameters. The auth scheme is trivial (it's just a token), so let's start by parsing the parameters.

Although there isn't a named rule for it in the grammar, let's define a rule for parameter values. The value can be either a token or a quoted string:

```csharp
    private static readonly Parser<string> ParameterValue =
        Token.Or(QuotedString);
```

Since a parameter is a composite element (name and value), let's define a class to represent it:

```csharp
class Parameter
{
    public Parameter(string name, string value)
    {
        Name = name;
        Value = value;
    }
    
    public string Name { get; }
    public string Value { get; }
}
```

The `T` in `Parser<T>` isn't restricted to characters and strings, it can be any type. So the rule for parsing parameters will be of type `Parser<Parameter>`:

```csharp
    private static readonly Parser<char> EqualSign = Parse.Char('=');

    private static readonly Parser<Parameter> Parameter =
        from name in Token
        from _ in EqualSign
        from value in ParameterValue
        select new Parameter(name, value);
```

Here we match a token (the parameter name), followed by the `'='` sign, followed by a parameter value, and we combine the name and value into a `Parameter` instance.

Now let's parse a sequence of one or more parameters. Parameters are separated by commas (`','`), with optional leading and trailing whitespace (look for "#rule" in [RFC 2616 §2.1](https://tools.ietf.org/html/rfc2616#section-2.1)). The grammar for lists allows several commas without items in between, e.g. "item1 ,, item2,item3, ,item4", so the rule for the delimiter can be written like this:

```csharp
    private static readonly Parser<char> Comma = Parse.Char(',');

    private static readonly Parser<char> ListDelimiter =
        from leading in Parse.WhiteSpace.Many()
        from c in Comma
        from trailing in Parse.WhiteSpace.Or(Comma).Many()
        select c;
```

We just match the first comma, the rest can be any number of commas or whitespace characters. We return the comma because we have to return something, but we won't actually use it.

We could now match the sequence of parameters like this:

```csharp
    private static readonly Parser<Parameter[]> Parameters =
        from first in Parameter.Once()
        from others in (
            from _ in ListDelimiter
            from p in Parameter
            select p).Many()
        select first.Concat(others).ToArray();
```

But it's not very straightforward... fortunately Sprache provides an easier option with the `DelimitedBy` method:

```csharp
    private static readonly Parser<Parameter[]> Parameters =
        from p in Parameter.DelimitedBy(ListDelimiter)
        select p.ToArray();
```

## Parsing the challenge

We're almost done. We now have everything we need to parse the whole challenge. Let's define a class to represent it first:

```csharp
class Challenge
{
    public Challenge(string scheme, Parameter[] parameters)
    {
        Scheme = scheme;
        Parameters = parameters;
    }
    public string Scheme { get; }
    public Parameter[] Parameters { get; }
}
```

And finally we can write the top-level rule:

```csharp
    public static readonly Parser<Challenge> Challenge =
        from scheme in Token
        from _ in Parse.WhiteSpace.AtLeastOnce()
        from parameters in Parameters
        select new Challenge(scheme, parameters);
```

Note that I made this rule public, unlike the others: it's the only one we need to expose.

## Using the parser

Our parser is done, now we just have to use it, which is pretty straightforward:

```csharp
void ParseAndPrintChallenge(string input)
{
    var challenge = Grammar.Challenge.Parse(input);
    Console.WriteLine($"Scheme: {challenge.Scheme}");
    Console.WriteLine($"Parameters:");
    foreach (var p in challenge.Parameters)
    {
        Console.WriteLine($"- {p.Name} = {p.Value}");
    }
}
```

With the OAuth 2.0 challenge example from earlier, this produces the following output:

```
Scheme: Bearer
Parameters:
- realm = FooCorp
- error = invalid_token
- error_description = The access token has expired
```

If there's a syntax error in the input text, the `Parse` will throw a `ParseException` with a message describing where and why the parsing failed. For instance, if I remove the space between "Bearer" and "realm", I get the following error:


> Parsing failure: unexpected '='; expected whitespace (Line 1, Column 12); recently consumed: earerrealm


You can find the full code for this article [here](https://gist.github.com/thomaslevesque/d8ee28be1cf383a3f8aaf39cee776f92).

## Conclusion

As you can see, Sprache makes it very simple to parse complex text. The code isn't particularly short, but it's completely declarative; there are no loops, no conditionals, no temporary variables, no state... This makes it very easy to understand, and it can easily be compared with the actual grammar definition to check its correctness. It also provides pretty good feedback in case of error, which is hard to accomplish with a hand-built parser.

