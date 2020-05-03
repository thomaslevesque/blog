---
layout: post
title: '[WPF] Display an animated GIF image'
date: 2011-03-27T00:00:00.0000000
url: /2011/03/27/wpf-display-an-animated-gif-image/
tags:
  - animated
  - gif
  - WPF
categories:
  - WPF
---

***Note:** The code in this article is out of date; the current code is [hosted on GitHub](https://github.com/XamlAnimatedGif/WpfAnimatedGif/).*  WPF is a great technology, but sometimes it seems to be missing some really basic features... A frequently mentioned example is the lack of support for animated GIF images. Actually, the GIF format itself is supported by the imaging API, but the `Image` control only shows the first frame of the animation.  Many solutions to this problem have been proposed on technical forums and blogs, usually variations of the following approaches:  
- Use the `MediaElement` control: unfortunately this control only supports URI like `file://` or `http://`, not the `pack://` URI schema used for WPF resources; this means the image can't be included in the resources, it has to be in a separate file. Furthermore, transparency for GIF images isn't supported in `MediaElement`, which makes the final result quite ugly
- Use the `PictureBox` control from Windows Forms, via a `WindowsFormsHost`: I personnally dislike using WinForms controls in WPF, it really looks like a hack...
- Create a custom control that inherits `Image` and handles the animation. Some solutions take advantage of the `ImageAnimator` class from `System.Drawing` (GDI), others use a WPF animation to change the current frame. It's a rather "clean" approach, but it forces you to use a specific control for GIF images. Also, the solution using `ImageAnimator` turns out not to be very smooth, the animation is quite jerky.

  As you might have guessed, I don't find any of these solutions really satisfying... Furthermore, none of the implementations I've seen of the third approach handles the duration of each frame properly, they only assume that all frames last 100ms (which is almost always true, but *almost*isn't good enough IMHO...). So I kept the best ideas from each approach I've seen, and I came up with my own solution. Here are the goals I set to attain:
- No dependency on Windows Forms or GDI
- Display the animated image in a standard `Image` control
- Use the same XAML code for normal and animated images
- Support for transparency
- Correct handling of frame duration

  To achieve this result, I started from a very simple, even obvious idea: to animate the image, all you have to do is apply an animation to the `Source` property of the `Image` control. WPF provides all the necessary tools to do that; in this case, the [`ObjectAnimationUsingKeyFrames`](http://msdn.microsoft.com/en-us/library/system.windows.media.animation.objectanimationusingkeyframes.aspx) class fits the bill perfectly: it allows to specify at what exact time a given value should be assigned to the property, which makes it easy to take the frame duration into account.  The next problem is to extract the frames from the image: fortunately WPF supports this natively, and the [`BitmapDecoder`](http://msdn.microsoft.com/en-us/library/system.windows.media.imaging.bitmapdecoder.aspx) class provides a `Frames` property to do exactly that. So, no big difficulty so far...  Finally, last obstacle: extract the duration of each frame. It's the part that took me the longest, because I needed to do some research... I first thought I would need to read the file manually and decode the binary data myself. But eventually the solution is quite simple, and takes advantage of the [`BitmapMetadata`](http://msdn.microsoft.com/en-us/library/system.windows.media.imaging.bitmapmetadata.aspx) class. The only difficulty has been to find the "path" of the metadata that contains the delay, but after a few minutes of trial and error, here it is: `/grctlext/Delay`.  The final solution is implemented as an attached property named `AnimatedSource`, that applies to the `Image` control, and can be used instead of `Source`:  
```xml
<Image Stretch="None" my:ImageBehavior.AnimatedSource="/Images/animation.gif" />
```
  This property can also be assigned a normal (not animated) image, it will be displayed normally; therefore this property can be used without worrying about whether the image to display will be animated or not.  So in the end, all the goals have been achieved, and we even get some icing on the cake: this solution also works in the designer (at least in Visual Studio 2010), so the animation is immediately visible when you set the `AnimatedSource` property :)  Without further ado, here's the complete code:  
```csharp
    public static class ImageBehavior
    {
        #region AnimatedSource

        [AttachedPropertyBrowsableForType(typeof(Image))]
        public static ImageSource GetAnimatedSource(Image obj)
        {
            return (ImageSource)obj.GetValue(AnimatedSourceProperty);
        }

        public static void SetAnimatedSource(Image obj, ImageSource value)
        {
            obj.SetValue(AnimatedSourceProperty, value);
        }

        public static readonly DependencyProperty AnimatedSourceProperty =
            DependencyProperty.RegisterAttached(
              "AnimatedSource",
              typeof(ImageSource),
              typeof(ImageBehavior),
              new UIPropertyMetadata(
                null,
                AnimatedSourceChanged));

        private static void AnimatedSourceChanged(DependencyObject o, DependencyPropertyChangedEventArgs e)
        {
            Image imageControl = o as Image;
            if (imageControl == null)
                return;

            var oldValue = e.OldValue as ImageSource;
            var newValue = e.NewValue as ImageSource;
            if (oldValue != null)
            {
                imageControl.BeginAnimation(Image.SourceProperty, null);
            }
            if (newValue != null)
            {
                imageControl.DoWhenLoaded(InitAnimationOrImage);
            }
        }

        private static void InitAnimationOrImage(Image imageControl)
        {
            BitmapSource source = GetAnimatedSource(imageControl) as BitmapSource;
            if (source != null)
            {
                var decoder = GetDecoder(source) as GifBitmapDecoder;
                if (decoder != null && decoder.Frames.Count > 1)
                {
                    var animation = new ObjectAnimationUsingKeyFrames();
                    var totalDuration = TimeSpan.Zero;
                    BitmapSource prevFrame = null;
                    FrameInfo prevInfo = null;
                    foreach (var rawFrame in decoder.Frames)
                    {
                        var info = GetFrameInfo(rawFrame);
                        var frame = MakeFrame(
                            source,
                            rawFrame, info,
                            prevFrame, prevInfo);

                        var keyFrame = new DiscreteObjectKeyFrame(frame, totalDuration);
                        animation.KeyFrames.Add(keyFrame);
                        
                        totalDuration += info.Delay;
                        prevFrame = frame;
                        prevInfo = info;
                    }
                    animation.Duration = totalDuration;
                    animation.RepeatBehavior = RepeatBehavior.Forever;
                    if (animation.KeyFrames.Count > 0)
                        imageControl.Source = (ImageSource)animation.KeyFrames[0].Value;
                    else
                        imageControl.Source = decoder.Frames[0];
                    imageControl.BeginAnimation(Image.SourceProperty, animation);
                    return;
                }
            }
            imageControl.Source = source;
            return;
        }

        private static BitmapDecoder GetDecoder(BitmapSource image)
        {
            BitmapDecoder decoder = null;
            var frame = image as BitmapFrame;
            if (frame != null)
                decoder = frame.Decoder;

            if (decoder == null)
            {
                var bmp = image as BitmapImage;
                if (bmp != null)
                {
                    if (bmp.StreamSource != null)
                    {
                        bmp.StreamSource.Position = 0;
                        decoder = BitmapDecoder.Create(bmp.StreamSource, bmp.CreateOptions, bmp.CacheOption);
                    }
                    else if (bmp.UriSource != null)
                    {
                        Uri uri = bmp.UriSource;
                        if (bmp.BaseUri != null && !uri.IsAbsoluteUri)
                            uri = new Uri(bmp.BaseUri, uri);
                        decoder = BitmapDecoder.Create(uri, bmp.CreateOptions, bmp.CacheOption);
                    }
                }
            }

            return decoder;
        }

        private static BitmapSource MakeFrame(
            BitmapSource fullImage,
            BitmapSource rawFrame, FrameInfo frameInfo,
            BitmapSource previousFrame, FrameInfo previousFrameInfo)
        {
            DrawingVisual visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
            {
                if (previousFrameInfo != null && previousFrame != null &&
                    previousFrameInfo.DisposalMethod == FrameDisposalMethod.Combine)
                {
                    var fullRect = new Rect(0, 0, fullImage.PixelWidth, fullImage.PixelHeight);
                    context.DrawImage(previousFrame, fullRect);
                }

                context.DrawImage(rawFrame, frameInfo.Rect);
            }
            var bitmap = new RenderTargetBitmap(
                fullImage.PixelWidth, fullImage.PixelHeight,
                fullImage.DpiX, fullImage.DpiY,
                PixelFormats.Pbgra32);
            bitmap.Render(visual);
            return bitmap;
        }

        private class FrameInfo
        {
            public TimeSpan Delay { get; set; }
            public FrameDisposalMethod DisposalMethod { get; set; }
            public double Width { get; set; }
            public double Height { get; set; }
            public double Left { get; set; }
            public double Top { get; set; }

            public Rect Rect
            {
                get { return new Rect(Left, Top, Width, Height); }
            }
        }

        private enum FrameDisposalMethod
        {
            Replace = 0,
            Combine = 1,
            RestoreBackground = 2,
            RestorePrevious = 3
        }

        private static FrameInfo GetFrameInfo(BitmapFrame frame)
        {
            var frameInfo = new FrameInfo
            {
                Delay = TimeSpan.FromMilliseconds(100),
                DisposalMethod = FrameDisposalMethod.Replace,
                Width = frame.PixelWidth,
                Height = frame.PixelHeight,
                Left = 0,
                Top = 0
            };

            BitmapMetadata metadata;
            try
            {
                metadata = frame.Metadata as BitmapMetadata;
                if (metadata != null)
                {
                    const string delayQuery = "/grctlext/Delay";
                    const string disposalQuery = "/grctlext/Disposal";
                    const string widthQuery = "/imgdesc/Width";
                    const string heightQuery = "/imgdesc/Height";
                    const string leftQuery = "/imgdesc/Left";
                    const string topQuery = "/imgdesc/Top";

                    var delay = metadata.GetQueryOrNull<ushort>(delayQuery);
                    if (delay.HasValue)
                        frameInfo.Delay = TimeSpan.FromMilliseconds(10 * delay.Value);

                    var disposal = metadata.GetQueryOrNull<byte>(disposalQuery);
                    if (disposal.HasValue)
                        frameInfo.DisposalMethod = (FrameDisposalMethod) disposal.Value;

                    var width = metadata.GetQueryOrNull<ushort>(widthQuery);
                    if (width.HasValue)
                        frameInfo.Width = width.Value;

                    var height = metadata.GetQueryOrNull<ushort>(heightQuery);
                    if (height.HasValue)
                        frameInfo.Height = height.Value;

                    var left = metadata.GetQueryOrNull<ushort>(leftQuery);
                    if (left.HasValue)
                        frameInfo.Left = left.Value;

                    var top = metadata.GetQueryOrNull<ushort>(topQuery);
                    if (top.HasValue)
                        frameInfo.Top = top.Value;
                }
            }
            catch (NotSupportedException)
            {
            }

            return frameInfo;
        }

        private static T? GetQueryOrNull<T>(this BitmapMetadata metadata, string query)
            where T : struct
        {
            if (metadata.ContainsQuery(query))
            {
                object value = metadata.GetQuery(query);
                if (value != null)
                    return (T) value;
            }
            return null;
        }

        #endregion
    }
```
  And here's the `DoWhenLoaded` extension method used in the code above:  
```csharp
public static void DoWhenLoaded<T>(this T element, Action<T> action)
    where T : FrameworkElement
{
    if (element.IsLoaded)
    {
        action(element);
    }
    else
    {
        RoutedEventHandler handler = null;
        handler = (sender, e) =>
        {
            element.Loaded -= handler;
            action(element);
        };
        element.Loaded += handler;
    }
}
```
  Enjoy :)
  
  **Update**: the code that retrieves the frame duration only works on Windows Seven, and on Windows Vista if the [Platform Update](http://support.microsoft.com/kb/971644/en-us) is installed (untested). The default duration (100ms) will be used instead on other versions of Windows. I will update the article if I find a solution that works on all operating systems (I know I could use `System.Drawing.Bitmap`, but I'd rather not depend on this...)  
  
  **Update 2**: as pointed out by Klaus in the comments, the `ImageBehavior` class didn't handle some important attributes of the frames: the diposal method (whether a frame should entirely replace the previous one, or be combined with it), and the frame position (Left/Top/Width/Height). I updated the code to handle these attributes properly. Thank you Klaus!
  
  **Update 3**: a commenter on the French version of my blog pointed out a problem when the AnimatedSource is an image in a resource dictionary; the UriSource wasn't correctly interpreted when it was a relative URI. This problem is now fixed. Thank you, "anonymous"!
  
  **Update 4**: uploaded an [example project](AnimatedGif.zip) to demonstrate the code.
  
  **Update 5**: yet another bug fix, for when you use a `BitmapImage` initialized from a stream. Thanks to Mizutama for spotting this one!
  
  **Update 6**: rather than posting improvements to this blog post, I eventually created [a project on <strike>CodePlex</strike> GitHub](https://github.com/XamlAnimatedGif/WpfAnimatedGif/) where this class will be maintained. You can also install it using NuGet, the package id is [WpfAnimatedGif](https://nuget.org/packages/WpfAnimatedGif). Thanks to Diego Mijelshon for the suggestion!  

