---
layout: post
title: Scaling out ASP.NET Core SignalR using Azure Service Bus
date: 2019-03-18T06:16:15.0000000
url: /2019/03/18/scaling-out-asp-net-core-signalr-using-azure-service-bus/
tags:
  - asp.net core
  - Azure
  - scale-out
  - Service Bus
  - SignalR
categories:
  - ASP.NET Core
  - Azure
---


[ASP.NET Core SignalR](https://docs.microsoft.com/en-us/aspnet/core/signalr/introduction) is a super easy way to establish two-way communication between an ASP.NET Core app and its clients, using WebSockets, Server-Sent Events, or long polling, depending on the client's capabilities. For instance, it can be used to send a notification to all connected clients. However, if you scale out your application to multiple server instances, it no longer works out of the box: only the clients connected to the instance that sent the notification will receive it. Microsoft has two documented solutions to this problem:

- [Using Redis as a backplane](https://docs.microsoft.com/en-us/aspnet/core/signalr/redis-backplane) for sharing information between server instances.
- [Using Azure SignalR Service](https://docs.microsoft.com/en-us/azure/azure-signalr/signalr-overview?toc=/aspnet/core/toc.json&amp;bc=/aspnet/core/breadcrumb/toc.json), which is basically "SignalR As A Service".


Derek Comartin did a good job explaining these solutions ([Redis](https://codeopinion.com/practical-asp-net-core-signalr-scaling/), [Azure SignalR Service](https://codeopinion.com/practical-asp-net-core-signalr-azure/)), so I won't go into the details. Both are perfectly viable, however they're relatively expensive. A Redis Cache resource in Azure starts at about 14€/month for the smallest size, and Azure SignalR Service starts at about 40€/month for a single unit (I'm entirely dismissing the free plan, which is too limited to use beyond development scenarios). Sure, it's not *that* expensive, but why pay more when you can pay less?

What I want to talk about in this post is a third option that will probably be cheaper in many scenarios: using [Azure Service Bus](https://docs.microsoft.com/en-us/azure/service-bus-messaging/service-bus-messaging-overview) to dispatch SignalR messages between server instances. In fact, [this approach was supported in classic ASP.NET](https://docs.microsoft.com/en-us/aspnet/signalr/overview/performance/scaleout-with-windows-azure-service-bus), but it hasn't been ported to ASP.NET Core.

Here's an overview of how one could manually implement the Azure Service Bus approach:

- When an instance of the application wants to send a SignalR message to all clients, it sends it:

    - via its own SignalR hub or hub context (only clients connected to this instance will receive it)
    - and to an Azure Service Bus topic, for distribution to other instances.


```csharp

// Pseudo code...

private readonly IHubContext<ChatHub, IChatClient> _hubContext;
private readonly IServiceBusPublisher _serviceBusPublisher;

public async Task SendMessageToAllAsync(string text)
{
    // Send the message to clients connected to the current instance
    await _hubContext.Clients.All.ReceiveMessageAsync(text);

    // Notify other instances to send the same message
    await _serviceBusPublisher.PublishMessageAsync(new SendToAllMessage(text));
}
```
- Each instance of the application runs a [hosted service](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services) that subscribes to the topic and processes the messages

    - When a message is received, it's sent to the relevant clients via the hub context, unless it's from the current instance.


```csharp

// Very simplified pseudo code...

// Subscribe to the topic
var subscriptionClient = new SubscriptionClient(connectionString, topicName, subscriptionName);
subscriptionClient.RegisterMessageHandler(OnMessageReceived, OnError);

...

private async Task OnMessageReceived(Message sbMessage, CancellationToken cancellationToken)
{
    SendToAllMessage message = DeserializeServiceBusMessage(sbMessage);

    if (message.SenderInstanceId == MyInstanceId)
        return; // ignore message from self

    // Send the message to clients connected to the current instance
    await _hubContext.Clients.All.ReceiveMessageAsync(message.Text);
}
```


I'm not showing the full details of how to implement this solution, because to be honest, it kind of sucks. It works, but it's a bit ugly: the fact that it's using a service bus to share messages with other server instances is too visible, you can't just ignore it. Every time you send a message via SignalR, you also have to explicitly send one to the service bus. It would be better to hide that ugliness behind an abstraction, or even better, make it completely invisible...

If you have used the Redis or Azure SignalR Service approaches before, you might have noticed how simple they are to use. Basically, in your `Startup.ConfigureServices` method, just append `AddRedis(...)` or `AddAzureSignalR(...)` after `services.AddSignalR()`, and you're done: you can use SignalR as usual, the details of how it handles scale-out are completely abstracted away. Wouldn't it be nice to be able to do the same for Azure Service Bus? I thought so too, so I made a library that does exactly that: [AspNetCore.SignalR.AzureServiceBus](https://github.com/thomaslevesque/AspNetCore.SignalR.AzureServiceBus). To use it, reference the [NuGet package](https://www.nuget.org/packages/AspNetCore.SignalR.AzureServiceBus/1.0.0-alpha.1), and just add this in your `Startup.ConfigureServices` method:

```csharp

services.AddSignalR()
        .AddAzureServiceBus(options =>
        {
            options.ConnectionString = "(your service bus connection string)";
            options.TopicName = "(your topic name)";
        });
```

**Disclaimer:** The library is still in alpha status, probably not ready for production use. I'm not aware of any issue, but it hasn't been battle tested yet. Use at your own risk, and please report any issues you find!

