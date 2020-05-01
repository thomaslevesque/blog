---
layout: post
title: '[WPF] Article about the Model-View-ViewModel design pattern, by Josh Smith'
date: 2009-02-25T01:30:56.0000000
url: /2009/02/25/wpf-article-about-model-view-viewmodel-design-pattern-by-josh-smith/
tags:
  - article
  - design pattern
  - Josh Smith
  - MVVM
  - WPF
categories:
  - WPF
---

Soon after the release of WPF, people have been talking more and more about "Model-View-ViewModel" (MVVM). This expression refers to a design pattern, drawing its inspiration from the Model-View-Controller (MVC) and Presentation Model (PM) patterns, and created specifically to take advantage of WPF features. This patterns enables an excellent decoupling between data, behavior and presentation, which makes the code easier to understand and maintain, and improves the collaboration between developers and designers. Another benefit of MVVM is the ability to write testable code much more easily.  If you want to know more about this pattern, I urge you to read the excellent article by Josh Smith on this topic, published in the February issue of the MSDN Magazine : [WPF Apps With The Model-View-ViewModel Design Pattern](http://msdn.microsoft.com/en-us/magazine/dd419663.aspx).  Walking through a simple but concrete example, Josh Smith addresses most aspects of the MVVM pattern : 
- Data binding
- Commands
- Validation
- Unit testing
- ...

  Further more, the provided source code makes a good starting point to build a WPF application conforming to the MVVM pattern, and is also a mine of practical examples.
