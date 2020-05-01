---
layout: post
title: An easy and secure way to store a password using Data Protection API
date: 2013-05-20T22:59:50.0000000
url: /2013/05/21/an-easy-and-secure-way-to-store-a-password-using-data-protection-api/
tags:
  - cryptography
  - dpapi
  - password
  - protect
categories:
  - Code sample
---

If you're writing a client application that needs to store user credentials, it's usually not a good idea to store the password as plain text, for obvious security reasons. So you need to encrypt it, but as soon as you start to think about encryption, it raises all kinds of issues... Which algorithm should you use? Which encryption key? Obviously you will need the key to decrypt the password, so it needs to be either in the executable or in the configuration. But then it will be pretty easy to find...  Well, the good news is that you don't really need to solve this problem, because Windows already solved it for you! The solution is called [Data Protection API](http://msdn.microsoft.com/en-us/library/ms995355.aspx), and enables you to protect data without having to worry about an encryption key. The documentation is lengthy and boring, but actually it's pretty easy to use from .NET, because the framework provides a [`ProtectedData`](http://msdn.microsoft.com/en-us/library/system.security.cryptography.protecteddata.aspx) class that wraps the low-level API calls for you.  This class has two methods, with pretty self-explanatory names: `Protect` and `Unprotect`:  
```csharp

public static byte[] Protect(byte[] userData, byte[] optionalEntropy, DataProtectionScope scope);
public static byte[] Unprotect(byte[] encryptedData, byte[] optionalEntropy, DataProtectionScope scope);
```
  The `userData` parameter is the plain, unencrypted binary data. The `scope` is a value that indicates whether to protect the data for the current user (only that user will be able to decrypt it) or for the local machine (any user on the same machine will be able to decrypt it). What about the `optionalEntropy` parameter? Well, I'm not an expert in cryptography, but as far as I understand, it's a kind of "salt": according to the documentation, it is used to "increase the complexity of the encryption". Obviously, you'll need to provide the same entropy to decrypt the data later. As the name implies, this parameter is optional, so you can just pass null if you don't want to use it.  So, this API is quite simple, but not directly usable for our goal: the input and output of `Protect` are byte arrays, but we want to encrypt a password, which is a string; also, it's usually more convenient to store a string than a byte array. To get a byte array from the password string, it's pretty easy: we just need to use a text encoding, like UTF-8. But we can't use the same approach to get a string from the encrypted binary data, because it will probably not contain printable text; instead we can encode the result in Base64, which gives a clean text representation of binary data. So, basically we're going to do this:  
```
                      clear text
(encode to UTF8)   => clear bytes
(Protect)          => encrypted bytes
(encode to base64) => encrypted text
```
  And for decryption, we just need to reverse the steps:  
```
                        encrypted text
(decode from base64) => encrypted bytes
(Unprotect)          => clear bytes
(decode from UTF8)   => clear text
```
  I omitted the entropy in the description above; in most cases it will probably be more convenient to have it as a string, too, so we can just encode the string to UTF-8 to get the corresponding bytes.  Eventually, we can wrap all this in two simple extension methods:  
```csharp

public static class DataProtectionExtensions
{
    public static string Protect(
        this string clearText,
        string optionalEntropy = null,
        DataProtectionScope scope = DataProtectionScope.CurrentUser)
    {
        if (clearText == null)
            throw new ArgumentNullException("clearText");
        byte[] clearBytes = Encoding.UTF8.GetBytes(clearText);
        byte[] entropyBytes = string.IsNullOrEmpty(optionalEntropy)
            ? null
            : Encoding.UTF8.GetBytes(optionalEntropy);
        byte[] encryptedBytes = ProtectedData.Protect(clearBytes, entropyBytes, scope);
        return Convert.ToBase64String(encryptedBytes);
    }
    
    public static string Unprotect(
        this string encryptedText,
        string optionalEntropy = null,
        DataProtectionScope scope = DataProtectionScope.CurrentUser)
    {
        if (encryptedText == null)
            throw new ArgumentNullException("encryptedText");
        byte[] encryptedBytes = Convert.FromBase64String(encryptedText);
        byte[] entropyBytes = string.IsNullOrEmpty(optionalEntropy)
            ? null
            : Encoding.UTF8.GetBytes(optionalEntropy);
        byte[] clearBytes = ProtectedData.Unprotect(encryptedBytes, entropyBytes, scope);
        return Encoding.UTF8.GetString(clearBytes);
    }
}
```
  Encryption example:  
```csharp

string encryptedPassword = password.Protect();
```
  Decryption example:  
```csharp

try
{
    string password = encryptedPassword.Unprotect();
}
catch(CryptographicException)
{
    // Possible causes:
    // - the entropy is not the one used for encryption
    // - the data was encrypted by another user (for scope == CurrentUser)
    // - the data was encrypted on another machine (for scope == LocalMachine)
    // In this case, the stored password is not usable; just prompt the user to enter it again.
}
```
  What I love with this technique is that it Just Works™: you don't need to worry about how the data is encrypted, where the key is stored, or anything, Windows takes care of everything.  The code above works on the full .NET framework, but the Data Protection API is also available:  
- for Windows Phone: same methods, but without the scope parameter
- for Windows Store apps, using the [DataProtectionProvider](http://msdn.microsoft.com/en-us/library/windows/apps/windows.security.cryptography.dataprotection.dataprotectionprovider) class. The API is quite different (async methods, no entropy) and a bit more complex to use, but it achieves the same result. [Here's a WinRT version of the extension methods above](https://gist.github.com/thomaslevesque/5652991).


