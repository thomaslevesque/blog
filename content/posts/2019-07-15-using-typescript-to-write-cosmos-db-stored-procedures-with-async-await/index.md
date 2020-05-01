---
layout: post
title: Using TypeScript to write Cosmos DB stored procedures with async/await
date: 2019-07-15T06:00:23.0000000
url: /2019/07/15/using-typescript-to-write-cosmos-db-stored-procedures-with-async-await/
tags:
  - Azure
  - Azure Cosmos DB
  - Cosmos DB
  - server-side
  - stored procedure
  - TypeScript
categories:
  - Uncategorized
---


***Disclaimer**: I am by no mean a TypeScript expert. In fact, I know very little about JS, npm, gulp, etc. So it's entirely possible I said something really stupid in this article, or maybe I missed a much simpler way of doing things. Don't hesitate to let me know in the comments!*

[Azure Cosmos DB](https://docs.microsoft.com/en-us/azure/cosmos-db/introduction) (formerly known as Azure Document DB) is a NoSQL, multi-model, globally-distributed database hosted in Azure. If you come from relational SQL databases, it's a very different world. Some things are great, for instance modeling data is much easier than in a relational database, and performance is excellent. Other things can be disconcerting, such as the lack of support for [ACID](https://en.wikipedia.org/wiki/ACID_%28computer_science%29). From the client's perspective, there are no transactions: you can't update multiple documents atomically. Of course, there's a workaround: you can write stored procedures and triggers, which execute in the context of a transaction. So, when you really, *really* need multiple updates to be made atomically, you write a stored procedure to do the job.

## The bad news

Unfortunately, on Cosmos DB, stored procedures are written… in Javascript 😢 (I know, plenty of folks love Javascript, but I don't. Sue me!). All APIs for database operations are asynchronous (which is a good thing), but these APIs are based on callbacks, not on promises, so even though ECMAScript 2017 is supported, you can't use `async`/`await` with them. This fact is enough to turn any non-trivial task (i.e. code that involves branches such as ifs or loops) into a nightmare, at least for a C# developer like me… I typically spend a full day to write and debug a stored procedure that should have taken less than an hour with async/await.

## Promise-based wrapper

Of course, I wouldn't be writing this post if there wasn't a way to make things better. Cosmos DB Product Manager [Andrew Liu](https://twitter.com/aliuy8) was kind enough to show me how to write a wrapper around the callback-based API to enable the use of promises and `async`/`await`. Basically, it's just a few functions that you can add to your stored procedures:

```javascript

function setFoo() {
    async function main() {
        let { feed, options } = await queryDocuments("SELECT * from c");
        for (let doc of feed) {
            doc.foo = "bar";
            await replaceDocument(doc);
        }
    }

    main().catch(err => getContext().abort(err));
}

function queryDocuments(sqlQuery, options) {
    return new Promise((resolve, reject) => {
        let isAccepted = __.queryDocuments(__.getSelfLink(), sqlQuery, options, (err, feed, opts) => {
            if (err) reject(err);
            else resolve({ feed, options: opts });
        });
        if (!isAccepted) reject(new Error(429, "queryDocuments was not accepted."));
    });
}

function replaceDocument(doc, options) {
    return new Promise((resolve, reject) => {
        let isAccepted = __.replaceDocument(doc._self, doc, (err, result, opts) => {
            if (err) reject(err);
            else resolve({ result, options: opts });
        });
        if (!isAccepted) reject(new Error(429, "replaceDocument was not accepted."));
    });
}

// and so on for other APIs...
```

Note that the stored procedure's entry point (`setFoo` in this example) cannot be async (if it returns a promise, Cosmos DB won't wait for it to complete), so you need to write another async function (`main`), call it from the stored procedure's entry point, and catch the error that could be thrown. Note the use of `getContext().abort(err)`, which aborts and rolls back the current transaction; without this, the exception would be swallowed.

I'm not going to show the equivalent code using the callback-based API here, because honestly, it makes my head hurt just thinking about it. But trust me on this: it's not pretty, and much harder to understand.

## Using TypeScript

The code shown above is pretty straightforward, once you have the wrapper functions. However, there are at least two issues with it:

- This is still Javascript, which is weakly typed, so it's easy to make mistakes that won't be caught until runtime.
- Cosmos DB stored procedures and triggers must consist of a single self-contained file; no `import` or `require` allowed. Which means you can't share the wrapper functions across multiple stored procedures, you have to include them in each stored procedure. This is annoying...


First, let's see how we can write our stored procedure in TypeScript and reduce the boilerplate code.

Let's start by installing TypeScript. Create a `package.json` file with the `npm init` command (it will prompt you for a few details, you can leave everything empty), and run the `npm install typescript` command. We'll also need the TypeScript definitions of the Cosmos DB server-side APIs. For this, we'll install a npm package named [`@types/documentdb-server`](https://www.npmjs.com/package/@types/documentdb-server) which contains the definitions: `npm install @types/documentdb-server`.

We also need a `tsconfig.json` file:

```javascript

{
    "exclude": [
        "node_modules"
    ],
    "compilerOptions": {
        "target": "es2017",
        "strict": true,
    }
}
```

Now, let's create a few helpers to use in our stored procedures. I put them all in a `CosmosServerScriptHelpers` folder. The most important piece is the `AsyncCosmosContext` class, which is basically a strongly-typed, promise-based wrapper for [the `__` object](https://azure.github.io/azure-cosmosdb-js-server/-__object.html). It implements the following interface:

```javascript

export interface IAsyncCosmosContext {

    readonly request: IRequest;
    readonly response: IResponse;

    // Basic query and CRUD methods
    queryDocuments(sqlQuery: any, options?: IFeedOptions): Promise<IFeedResult>;
    readDocument(link: string, options?: IReadOptions): Promise<any>;
    createDocument(doc: any, options?: ICreateOptions): Promise<any>;
    replaceDocument(doc: any, options?: IReplaceOptions): Promise<any>;
    deleteDocument(doc: any, options?: IDeleteOptions): Promise<any>;

    // Helper methods
    readDocumentById(id: string, options?: IReadOptions): Promise<any>;
    readDocumentByIdIfExists(id: string, options?: IReadOptions): Promise<any>;
    deleteDocumentById(id: string, options?: IDeleteOptions): Promise<any>
    queryFirstDocument(sqlQuery: any, options?: IFeedOptions): Promise<any>;
    createOrReplaceDocument(doc: any, options?: ICreateOrReplaceOptions): Promise<any>;
}
```

I'm not showing the whole code in this article because it would be too long, but you can see the implementation and auxiliary types in the GitHub repo here: [https://github.com/thomaslevesque/TypeScriptCosmosDBStoredProceduresArticle](https://github.com/thomaslevesque/TypeScriptCosmosDBStoredProceduresArticle).

So, how can we use this? Let's look at our previous example again, and see how we can rewrite it in TypeScript using our wrappers:

```javascript

import {IAsyncCosmosContext} from "CosmosServerScriptHelpers/IAsyncCosmosContext";
import {AsyncCosmosContext} from "CosmosServerScriptHelpers/AsyncCosmosContext";

function setFoo() {
    async function main(context: IAsyncCosmosContext) {
        let { feed, options } = await context.queryDocuments("SELECT * from c");
        for (let doc of feed) {
            doc.foo = "bar";
            await replaceDocument(doc);
        }
    }

    main(new AsyncCosmosContext()).catch(err => getContext().abort(err));
}
```

It looks remarkably similar to the previous version, with just the following changes:

- We no longer have the wrapper functions in the same file, instead we just import them via the `AsyncCosmosContext` class.
- We pass an instance of `AsyncCosmosContext` to the `main` function.


This looks pretty good already, but what's bugging me is having to explicitly create the context and do the `.catch(...)`. So let's create another helper to encapsulate this:

```javascript

import {IAsyncCosmosContext} from "./IAsyncCosmosContext";
import {AsyncCosmosContext} from "./AsyncCosmosContext";

export class AsyncHelper {
    /**
     * Executes the specified async function and returns its result as the response body of the stored procedure.
     * @param func The async function to execute, which returns an object.
     */
    public static executeAndReturn(func: (context: IAsyncCosmosContext) => Promise<any>) {
        this.executeCore(func, true);
    }

    /**
     * Executes the specified async function, but doesn't write anything to the response body of the stored procedure.
     * @param func The async function to execute, which returns nothing.
     */
    public static execute(func: (context: IAsyncCosmosContext) => Promise<void>) {
        this.executeCore(func, false);
    }

    private static executeCore(func: (context: IAsyncCosmosContext) => Promise<any>, setBody: boolean) {
        func(new AsyncCosmosContext())
            .then(result => {
                if (setBody) {
                    __.response.setBody(result);
                }
            })
            .catch(err => {
                // @ts-ignore
                getContext().abort(err);
            });
    }
}
```

Using this helper, our stored procedure now looks like this:

```javascript

import {AsyncHelper} from "CosmosServerScriptHelpers/AsyncHelper";

function setFoo() 
{
    AsyncHelper.execute(async context => {
        let result = await context.queryDocuments("SELECT * from c");
        for (let doc of result.feed) {
            doc.foo = "bar";
            await context.replaceDocument(doc);
        }
    });
}
```

This reduces the boilerplate code to a minimum. I'm pretty happy with it, so let's leave it alone.

## Generate the actual JS stored procedure files

OK, now comes the tricky part... We have a bunch of TypeScript files that `import` each other. But Cosmos DB wants a single, self-contained JavaScript file, with the first function as the entry point of the stored procedure. By default, compiling the TypeScript files to JavaScript will just generate one JS file for each TS file. The `--outFile` compiler option outputs everything to a single file, but it doesn't really work for us, because it still emits some module related code that won't work in Cosmos DB. What we need, for each stored procedure, is a file that only contains:

- the stored procedure function itself
- all the helper code, without any `import` or `require`.


Since it doesn't seem possible to get the desired result using just the TypeScript compiler, the solution I found was to use a Gulp pipeline to concatenate the output files and remove the extraneous `export`s and `import`s. Here's my `gulpfile.js`:

```javascript

const gulp = require("gulp");
const ts = require("gulp-typescript");
const path = require("path");
const flatmap = require("gulp-flatmap");
const replace = require('gulp-replace');
const concat = require('gulp-concat');

gulp.task("build-cosmos-server-scripts", function() {
    const sharedScripts = "CosmosServerScriptHelpers/*.ts";
    const tsServerSideScripts = "StoredProcedures/**/*.ts";

    return gulp.src(tsServerSideScripts)
        .pipe(flatmap((stream, file) =>
        {
            let outFile = path.join(path.dirname(file.relative), path.basename(file.relative, ".ts") + ".js");
            let tsProject = ts.createProject("tsconfig.json");
            return stream
                .pipe(gulp.src(sharedScripts))
                .pipe(tsProject())
                .pipe(replace(/^\s*import .+;\s*$/gm, ""))
                .pipe(replace(/^\s*export .+;\s*$/gm, ""))
                .pipe(replace(/^\s*export /gm, ""))
                .pipe(concat(outFile))
                .pipe(gulp.dest("StoredProcedures"));
        }));
});

gulp.task("default", gulp.series("build-cosmos-server-scripts"));
```

Note that this script requires a few additional npm packages: `gulp`, `gulp-concat`, `gulp-replace`, `gulp-flatmap`, and `gulp-typescript`.

Now you can just run `gulp` and it will produce the appropriate JS file for each TS stored procedure.

To be honest, this solution feels a bit hacky, but it's the best I've been able to come up with. If you know of a better approach, please let me know!

## Wrapping up

The out-of-the-box experience for writing Cosmos DB server-side code is not great (to put it mildly), but with just a bit of work, it can be made much better. You can have strong-typing thanks to TypeScript and the type definitions, and you can use `async`/`await` to make the code simpler. Note that this approach is also valid for triggers.

Hopefully, a future Cosmos DB update will introduce a proper promise-based API, and maybe even TypeScript support. In the meantime, feel free to use the solution in this post!

The full code for this article is here: [https://github.com/thomaslevesque/TypeScriptCosmosDBStoredProceduresArticle](https://github.com/thomaslevesque/TypeScriptCosmosDBStoredProceduresArticle).

