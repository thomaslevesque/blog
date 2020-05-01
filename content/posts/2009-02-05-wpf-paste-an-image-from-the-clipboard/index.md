---
layout: post
title: '[WPF] Paste an image from the clipboard (bug in Clipboard.GetImage)'
date: 2009-02-05T00:26:28.0000000
url: /2009/02/05/wpf-paste-an-image-from-the-clipboard/
tags:
  - bug
  - clipboard
  - image
  - workaround
  - WPF
categories:
  - Code sample
  - WPF
---


Oops... 2 months already since my previous (and first) post... I really have to get on a more regular schedule ;)

If you've ever tried to use the `Clipboard.GetImage` method in WPF, you probably had an unpleasant surprise... In fact, this method returns an `InteropBitmap` which, in some cases (most cases actually), can't be displayed in an `Image` control : no exception is thrown, the image size is correct, but the image either appears empty or unrecognizable.

However, if we save that image to a stream and re-read it from the stream, we get a perfectly usable image... So this could be an acceptable workaround, but I think its pretty bad for performance, because the image gets decoded, re-encoded, and re-decoded. It is also possible to use the `Clipboard` class from Windows Forms, which works fine, and convert the `System.Drawing.Image` to a `System.Windows.Media.ImageSource`, but I don't like the idea of referencing the Windows Forms assembly in a WPF app... So I decided to manually retrieve the image from the clipboard and handle the decoding myself.

If we look at the image formats available from the clipboard (`Clipboard.GetDataObject().GetFormats()`), we can see that they depend on the origin of the image (screenshot, copy from Paint...). The only format that is always available is `DeviceIndependentBitmap` (DIB). So I tried to retrieve the `MemoryStream` for this format and decode it into a `BitmapSource` :

```csharp

        private ImageSource ImageFromClipboardDib()
        {
            MemoryStream ms = Clipboard.GetData("DeviceIndependentBitmap") as MemoryStream;
            BitmapImage bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.StreamSource = ms;
            bmp.EndInit();
            return bmp;
        }
```

Unfortunately, this code throws a nasty `NotSupportedException` : « No imaging component suitable to complete this operation was found ». In other words, it doesn't know how to decode the contents of the stream... That's quite surprising, because DIB is a very common format. So I had a look at the structure of a DIB in MSDN documentation. Basically, a « classical » bitmap file (.bmp) is made of the following sections :

- File header (`BITMAPFILEHEADER` structure)
- Bitmap header (`BITMAPINFO` structure)
- Palette (array of RGBQUAD)
- Raw pixel data


If we observe the content of the DIB from the clipboard, we can see that it has the same structure, without the `BITMAPFILEHEADER` part... so the trick is just to add that header at the beginning of the buffer, and use this complete buffer to decode the image. Doesn't seem so hard, does it ? Well, the trouble is that we have to fill in some of the header fields... for instance, we must provide the location at which the actual image data begins, so we must know the total size of the headers and palette. These values can be read or calculated from the content of the image. The following code performs that task and returns an ImageSource from the clipboard :

```csharp

        private ImageSource ImageFromClipboardDib()
        {
            MemoryStream ms = Clipboard.GetData("DeviceIndependentBitmap") as MemoryStream;
            if (ms != null)
            {
                byte[] dibBuffer = new byte[ms.Length];
                ms.Read(dibBuffer, 0, dibBuffer.Length);

                BITMAPINFOHEADER infoHeader =
                    BinaryStructConverter.FromByteArray<BITMAPINFOHEADER>(dibBuffer);

                int fileHeaderSize = Marshal.SizeOf(typeof(BITMAPFILEHEADER));
                int infoHeaderSize = infoHeader.biSize;
                int fileSize = fileHeaderSize + infoHeader.biSize + infoHeader.biSizeImage;

                BITMAPFILEHEADER fileHeader = new BITMAPFILEHEADER();
                fileHeader.bfType = BITMAPFILEHEADER.BM;
                fileHeader.bfSize = fileSize;
                fileHeader.bfReserved1 = 0;
                fileHeader.bfReserved2 = 0;
                fileHeader.bfOffBits = fileHeaderSize + infoHeaderSize + infoHeader.biClrUsed * 4;

                byte[] fileHeaderBytes =
                    BinaryStructConverter.ToByteArray<BITMAPFILEHEADER>(fileHeader);

                MemoryStream msBitmap = new MemoryStream();
                msBitmap.Write(fileHeaderBytes, 0, fileHeaderSize);
                msBitmap.Write(dibBuffer, 0, dibBuffer.Length);
                msBitmap.Seek(0, SeekOrigin.Begin);

                return BitmapFrame.Create(msBitmap);
            }
            return null;
        }
```

Definition of the `BITMAPFILEHEADER` and `BITMAPINFOHEADER` structures :

```csharp

        [StructLayout(LayoutKind.Sequential, Pack = 2)]
        private struct BITMAPFILEHEADER
        {
            public static readonly short BM = 0x4d42; // BM

            public short bfType;
            public int bfSize;
            public short bfReserved1;
            public short bfReserved2;
            public int bfOffBits;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFOHEADER
        {
            public int biSize;
            public int biWidth;
            public int biHeight;
            public short biPlanes;
            public short biBitCount;
            public int biCompression;
            public int biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public int biClrUsed;
            public int biClrImportant;
        }
```

Utility class to convert structures to binary :

```csharp

    public static class BinaryStructConverter
    {
        public static T FromByteArray<T>(byte[] bytes) where T : struct
        {
            IntPtr ptr = IntPtr.Zero;
            try
            {
                int size = Marshal.SizeOf(typeof(T));
                ptr = Marshal.AllocHGlobal(size);
                Marshal.Copy(bytes, 0, ptr, size);
                object obj = Marshal.PtrToStructure(ptr, typeof(T));
                return (T)obj;
            }
            finally
            {
                if (ptr != IntPtr.Zero)
                    Marshal.FreeHGlobal(ptr);
            }
        }

        public static byte[] ToByteArray<T>(T obj) where T : struct
        {
            IntPtr ptr = IntPtr.Zero;
            try
            {
                int size = Marshal.SizeOf(typeof(T));
                ptr = Marshal.AllocHGlobal(size);
                Marshal.StructureToPtr(obj, ptr, true);
                byte[] bytes = new byte[size];
                Marshal.Copy(ptr, bytes, 0, size);
                return bytes;
            }
            finally
            {
                if (ptr != IntPtr.Zero)
                    Marshal.FreeHGlobal(ptr);
            }
        }
    }
```

The image returned by that code can be safely used in an `Image` control.

That goes to show that, even with a state-of-the-art technology like WPF, we still have to get our hands dirty sometimes ;). Let's hope Microsoft will fix this in a later version...

