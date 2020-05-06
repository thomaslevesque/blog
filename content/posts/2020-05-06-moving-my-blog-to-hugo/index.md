---
layout: post
title: Moving my blog to Hugo
date: 2020-05-06
url: /2020/05/06/moving-my-blog-to-hugo/
draft: true
tags:
  - hugo
  - blog
  - meta
  - wordpress
  - github pages
  - cloudflare
  - static site generator
categories:
  - Uncategorized
---

If you're a regular reader of my blog, you probably noticed that the design has changed. In fact, it's not just the design, it's just about everything!

My blog used to be hosted on WordPress. It did the job, but honestly, I didn't really like WordPress. It's slow, bloated, and the editing and publishing experience is a bit of a mess (or at least, it's not a good fit for the way I like to work).

See, a few years ago, I started writing my articles in Markdown, which is perfect for writing technical articles, especially when you have blocks of code (and most of my posts have code in them). There are a few plugins to use Markdown in WordPress, but it's not the same as native support. My solution was to run the Markdown through a custom converter, and paste the resulting HTML into Wordpress, which was quite tedious.

So I set out to move away from WordPress, and I started looking at static site generators. [Jekyll](https://jekyllrb.com/) looked like a promising candidate, because it's natively supported by GitHub pages, but I don't like the way it forces you to organize your content. I prefer to have one folder per post, with the images next to the markdown, but Jekyll doesn't support this. Also, it requires me to install Ruby to test locally, and installing Ruby on Windows is a mess. I looked at a few others (Gatsby, Hexo...), but I eventually settled on [Hugo](https://gohugo.io/). Installing it is very easy using Chocolatey, it's blazing fast, reasonably easy to customize, and supports the content organization that I want.

I chose Github Pages for hosting, because it's free, and I like the Git-based workflow. I just had an issue with HTTPS, because it doesn't generate a SSL certificate for both the apex domain and the www subdomain. I solved this by putting the site behind [Cloudflare](https://www.cloudflare.com/), which has very nice free offer. I also setup a Github Actions workflow to regenerate the site when I push changes.

Regarding comments, I considered using Disqus, but it's infamous for its privacy issues. Most of the rival solutions are not free, and Facebook Comments is out of the question. I considered not having a comment system at all to avoid the hassle, but then I discovered [Utterances](https://utteranc.es/), which uses Github issues to enable comments on static pages. It's incredibly easy to setup, and integrates nicely with the site. The fact that you need a Github account to comment can be seen as a constraint, but I think of it as an advantage, as it dramatically reduces comment spam.

Overall, I'm very happy with my new setup. Publishing posts is much less tedious: I write markdown, commit and push to Github, and I'm done (now I'll have no excuse for not writing more often!). The site is now much faster, thanks to the static content and Cloudflare's caching. It's also more secure, since nothing runs server-side. And finally, I no longer need my shared hosting plan, and GitHub Pages is free, so I'm saving a few euros per month; the only remaining cost is the domain name.