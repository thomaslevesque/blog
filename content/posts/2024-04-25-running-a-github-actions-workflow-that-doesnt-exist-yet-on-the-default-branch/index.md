---
layout: post
title: "Running a GitHub Actions workflow that doesn't exist yet on the default branch"
date: 2024-04-25
url: /2024/04/25/running-a-github-actions-workflow-that-doesnt-exist-yet-on-the-default-branch/
tags:
  - GitHub Actions
  - CI
---

Writing this mostly for future me in case I need it again. Hopefully it can help someone else too!

Sometimes you need to run a GitHub Actions workflow, but it's not on the default branch yet, because it's still a work in progress and hasn't been merged yet. Typically it's just because you want to test it.

If it has a `pull_request` trigger, no problem, it will just run automatically when you open the pull request that adds it (if you have the appropriate permissions on the repo). But if it's a manually run workflow like (`workflow_dispatch` trigger), it won't appear in the Actions tab until it's merged on the default branch or has run at least once. But how can you run it for the first time before it's merged?

It's actually reasonably easy, but not exactly straightforward.

First, you need to temporarily add the `pull_request` trigger to the workflow, and create a PR with the workflow. This will cause it to run (and fail, if it needs inputs) and to appear on the Actions tab. Once it's visible, you can remove the `pull_request` trigger.

At this point, you can see the workflow in the Actions tab, but you still don't have the option to run it manually from the GitHub UI.

The solution is to use the [GitHub CLI](https://cli.github.com/). Install it, authenticate if necessary (`gh auth login`, then follow the instructions), and run this command:

```bash
gh workflow run your-workflow.yml \
  --ref your-branch \
  -f input1=value1 \
  -f input2=value2 \
  ...
```

If all goes well, it should show the following output:

```plain
✓ Created workflow_dispatch event for your-workflow.yml at your-branch
```

You can then go to the Actions tab on GitHub to see the logs.