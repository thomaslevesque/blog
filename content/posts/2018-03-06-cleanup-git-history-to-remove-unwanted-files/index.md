---
layout: post
title: Cleanup Git history to remove unwanted files
date: 2018-03-06T19:01:59.0000000
url: /2018/03/06/cleanup-git-history-to-remove-unwanted-files/
tags:
  - git
  - version control
categories:
  - Uncategorized
---


I recently had to work with a Git repository whose modifications needed to be ported to another repo. Unfortunately, the repo had been created without a .gitignore file, so a lot of useless files (bin/obj/packages directories...) had been commited. This made the history hard to follow, because each commit had hundreds of modified files.

Fortunately, it's rather easy with Git to cleanup a branch, by recreating the same commits without the files that shouldn't have been there in the first place. Let's see step by step how this can be achieved.

## A word of caution

The operation we need to perform here involves rewriting the history of the branch, so a warning is in order: never rewrite the history of a branch that is published and shared with other people. If someone else bases works on the existing branch and the branch has its history rewritten, it will become much harder to integrate their commits into the rewritten branch.

In my case, it didn't matter, because I didn't need to publish the rewritten branch (I just wanted to examine it locally). But don't do this on a branch your colleagues are currently working on, unless you want them to hate you 😉.

## Create a working branch

Since we're going to make pretty drastic and possibly risky changes on the repo, we'd better be cautious. The easiest way to avoid causing damage to the original branch is, of course, to work on a separate branch. So, assuming the branch we want to cleanup is `master`, let's create a `master2` working branch:

```bash
git checkout -b master2 master
```

## Identify the files to remove

Before we start to cleanup, we need to identify what needs to be cleaned up. In a typical .NET project, it's usually the contents of the `bin` and `obj` directories (wherever they're located) and the `packages` directory (usually at the root of the solution). Which gives us the following patterns to remove:

- `**/bin/**`
- `**/obj/**`
- `packages/**`


## Cleanup the branch: the git filter-branch command

The Git command that will let us remove unwanted files is named [`filter-branch`](https://git-scm.com/docs/git-filter-branch). It's described in the Pro Git book [Pro Git](https://git-scm.com/book/en/v2/Git-Tools-Rewriting-History#_the_nuclear_option_filter_branch) as the "nuclear option", because it's very powerful and possibly destructive, so it should be used with great caution.

This command works by taking each commit in the branch, applying a filter to it, and recommiting it with the changes caused by the filter. There are several kinds of filter, for instance:

- `--msg-filter` : a filter that can rewrite commit messages
- `--tree-filter` : a filter that applies to the working tree (causes each commit to be checked out, so it can take a while on a large repo)
- `--index-filter` : a filter that applies to the index (doesn't require checking out each commit, hence faster)


In our current scenario, `--index-filter` is perfectly adequate, since we only need to filter files based on their path. The `filter-branch` command with this kind of filter can be used as follows:

```bash
git filter-branch --index-filter '<command>'
```

`<command>` is a bash command that will be executed for each commit on the branch. In our case, it will be a call to `git rm` to remove unwanted files from the index:

```bash
git filter-branch --index-filter 'git rm --cached --ignore-unmatch **/bin/** **/obj/** packages/**'
```

The `--cached` parameter means we're working on the index rather than on the working tree; `--ignore-unmatch` tells Git to ignore patterns that don't match any file. By default, the command only applies to the current branch.

If the branch has a long history, this command can take a while to run, so be patient... Once it's done, you should have a branch with the same commits as the original branch, but without the unwanted files.

## More complex cases

In the example above, there were only 3 file patterns to remove, so the command was short enough to be written inline. But if there are many patterns, of if the logic to remove files is more complex, it doesn't really work out... In this case, you can just write a (bash) script that contains the necessary commands, and use this script as the command passed to `git filter-branch --index-filter`.

## Cleanup from a specific commit

In the previous example, we applied `filter-branch` to the whole branch. But it's also possible to apply it only from a given commit, by specifying a revision range:

```bash
git filter-branch --index-filter '<command>' <ref>..HEAD
```

Here `<ref>` is a commit reference (SHA1, branch or tag). Note that the end of the range has to be `HEAD`, i.e. the tip of the current branch: you can't rewrite the beginning or middle of a branch without touching the following commits, since each commit's SHA1 depends on the previous commit.

