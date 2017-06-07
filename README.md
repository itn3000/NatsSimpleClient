# Simple NATS client library

This is the client library for accessing [NATS](https://nats.io/) server.

# Supported environment

* .NET Standard 1.6
* .NET Framework 4.5.2 or later

# Usage

At first, you must add [NuGet reference](https://www.nuget.org/packages/NatsSimpleClient/) to your project.

## Basic code example

```csharp
// using NatsSimpleClient;
// create connection
var opt = NatsConnectOption.CreateDefault();
// if connection failed, exception will be thrown
using(var c = NatsConnection.Create("127.0.0.1", 4222, opt))
{
    // do subscribe
    // after subscribe, you receive messages from NATS server
    // save return value for unsubscribing.
    var sid = c.Subscribe("subject", null);
    // do publish
    // if you subscribe the subject, you will receive message by WaitMessage
    c.Publish("subject", null, new byte[1]);
    // receive message from server.
    // you will receive messages not only MSG, but '+OK','-ERR','PING'.
    var res = c.WaitMessage();
    bool recvMsg = false;
    while(!recvMsg)
    {
        switch(res.Kind)
        {
            case NatsServerMessageId.Msg:
                // process message
                recvmsg = true;
                break;
            case NatsServerMessageId.Ping:
                c.SendPong();
                break;
            default:
                break;
        }
    }
    // unsubscribe if you do not want to receive message from subject anymore.
    c.Unsubscribe(sid);
}
```

## Detailed example

Detailed examples are [here](./examples)
