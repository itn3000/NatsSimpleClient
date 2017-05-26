namespace nats_simple_client
{
    using System;
    using System.Net;
    using System.Net.Sockets;
    using System.Text;
    using Jil;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Collections.Concurrent;
    using System.IO;
    using System.Buffers;
    struct NatsOk
    {

    }
    struct NatsError
    {
        public string ErrorString;
    }
    struct NatsPing
    {

    }
    struct NatsPong
    {

    }
    class NatsConnection : IDisposable
    {
        struct ConsumerContext
        {
            public string Subject;
            public string Reply;
            public int Size;
            public long Sid;
            public List<byte> Content;
            public bool IsInContent;
        }
        public static NatsConnection Create(string host, int port, NatsConnectOption opt)
        {
            var ret = new NatsConnection();
            try
            {
                ret.Connect(host, port, opt);
                return ret;
            }
            catch
            {
                ret.Dispose();
                throw;
            }
        }
        ConcurrentQueue<byte> m_EventQueue = new ConcurrentQueue<byte>();
        byte[] m_SubscribeBuffer = new byte[4096];
        List<byte> m_MessageBuffer = new List<byte>();
        TcpClient m_Client;
        NetworkStream m_Stream;
        TcpClient m_SubscribeClient;
        Stream m_SubscribeStream;
        NatsConnectOption m_Option;
        long m_CurrentSid = 1;
        static readonly byte[] CrLfBytes = new byte[] { 0x0d, 0x0a };
        Task m_ConsumeDataThread;
        ServerInfo m_ServerInfo;
        CancellationTokenSource m_CancelToken = new CancellationTokenSource();
        ArrayPool<byte> m_BufferPool = System.Buffers.ArrayPool<byte>.Shared;
        int m_CurrentReceivedLength = 0;
        byte[] m_PublishBuffer = new byte[4096];

        private NatsConnection()
        {
            m_Client = new TcpClient();
            m_SubscribeClient = new TcpClient();
        }
        void InitializeConnection(Stream stm, byte[] buf, NatsConnectOption opt)
        {
            var bytesread = stm.Read(buf, 0, buf.Length);
            var response = Encoding.UTF8.GetString(buf, 0, bytesread);
            if (response.StartsWith("INFO "))
            {
                var svrInfo = JSON.Deserialize<ServerInfo>(response.Substring(5));
                m_ServerInfo = svrInfo;
            }
            else if (response.StartsWith("-ERR "))
            {
                throw new InvalidOperationException($"error response from server:{response.Substring(5)}");
            }
            else
            {
                throw new InvalidOperationException($"unknown connect response:{response}");
            }
            var connectmsg = Encoding.UTF8.GetBytes($"{NatsClientMessageKind.Connect} {JSON.Serialize(opt)}");
            stm.Write(connectmsg, 0, connectmsg.Length);
            stm.Write(CrLfBytes, 0, CrLfBytes.Length);
        }
        public byte[] Request(string subject, string replyto, byte[] data)
        {
            var sid = Interlocked.Increment(ref m_CurrentSid);
            var msg = Encoding.UTF8.GetBytes($"SUB {replyto} {sid}");
            m_Stream.Write(msg, 0, msg.Length);
            m_Stream.Write(CrLfBytes, 0, CrLfBytes.Length);
            int offset = 0;
            if (m_Option.verbose)
            {
                // consume +OK
                AllocateAndRead(m_Stream, true, ref m_PublishBuffer, ref offset);
            }
            msg = Encoding.UTF8.GetBytes($"UNSUB {sid} 1");
            m_Stream.Write(msg, 0, msg.Length);
            m_Stream.Write(CrLfBytes, 0, CrLfBytes.Length);
            if (m_Option.verbose)
            {
                // consume +OK
                AllocateAndRead(m_Stream, true, ref m_PublishBuffer, ref offset);
            }
            msg = Encoding.UTF8.GetBytes($"PUB {subject} {replyto} {data.Length}");
            m_Stream.Write(msg, 0, msg.Length);
            m_Stream.Write(CrLfBytes, 0, CrLfBytes.Length);
            m_Stream.Write(data, 0, data.Length);
            m_Stream.Write(CrLfBytes, 0, CrLfBytes.Length);
            if (m_Option.verbose)
            {
                // consume +OK
                AllocateAndRead(m_Stream, true, ref m_PublishBuffer, ref offset);
            }
            var bytesread = m_Stream.Read(m_PublishBuffer, 0, m_PublishBuffer.Length);
            return m_PublishBuffer.Take(bytesread).ToArray();
        }
        private void Connect(string host, int port, NatsConnectOption opt)
        {
            m_Option = opt;
            m_Client.ConnectAsync(host, port).Wait();
            //m_Client.Connect(host, port);
            m_SubscribeClient.ConnectAsync(host, port).Wait();
            //m_SubscribeClient.Connect(host, port);
            m_Stream = m_Client.GetStream();
            m_SubscribeStream = new BufferedStream(m_SubscribeClient.GetStream());
            InitializeConnection(m_Stream, m_PublishBuffer, opt);
            InitializeConnection(m_SubscribeStream, m_PublishBuffer, opt);
            // m_ConsumeDataThread = Task.FromResult(0);
            m_ConsumeDataThread = CreateMessageLoopThread();
            // m_ConsumeDataThread.Start();
        }

        public long Subscribe(string subject, string queue)
        {
            var qname = !string.IsNullOrEmpty(queue) ? $" {queue} " : " ";
            var sid = Interlocked.Increment(ref m_CurrentSid);
            var str = $"{NatsClientMessageKind.Sub} {subject}{qname}{sid}";
            var msg = System.Text.Encoding.UTF8.GetBytes(str);
            // using (var stm = m_SubscribeClient.GetStream())
            // {
            //     stm.Write(msg, 0, msg.Length);
            //     stm.Write(CrLfBytes, 0, CrLfBytes.Length);
            // }
            m_SubscribeStream.Write(msg, 0, msg.Length);
            m_SubscribeStream.Write(CrLfBytes, 0, CrLfBytes.Length);
            // m_Stream.Write(CrLfBytes, 0, CrLfBytes.Length);
            if (m_Option.verbose)
            {
                AllocateAndRead(m_SubscribeStream, true, ref m_SubscribeBuffer, ref m_CurrentReceivedLength);
                // Console.WriteLine($"sub result:{bytesread},{Encoding.UTF8.GetString(m_ReceiveBuffer, 0, bytesread)}");
            }
            return sid;
        }
        public void Publish(string subject, string replyTo, byte[] data)
        {
            int offset = 0;
            WritePublishMessage(m_Stream, subject, replyTo, data, ref m_PublishBuffer, ref offset);
        }
        void AllocateAndRead(Stream stm, bool fromPool, ref byte[] buffer, ref int dataLength)
        {
            if (buffer.Length * 9 / 10 < dataLength)
            {
                byte[] tmp = null;
                if (fromPool)
                {
                    tmp = m_BufferPool.Rent(buffer.Length * 2);
                }
                else
                {
                    tmp = new byte[buffer.Length * 2];
                }
                Buffer.BlockCopy(buffer, 0, tmp, 0, dataLength);
                if (fromPool)
                {
                    m_BufferPool.Return(buffer);
                }
                buffer = tmp;
            }
            var bytesread = stm.Read(buffer, dataLength, buffer.Length - dataLength);
            dataLength += bytesread;
        }
        void WritePublishMessage(Stream stm, string subject, string replyTo, byte[] data, ref byte[] receiveData, ref int receivedLength)
        {
            var reply = replyTo != null ? replyTo : "";
            var str = $"{NatsClientMessageKind.Pub} {subject} {reply} {data.Length}";
            var msg = System.Text.Encoding.UTF8.GetBytes(str);
            stm.Write(msg, 0, msg.Length);
            stm.Write(CrLfBytes, 0, CrLfBytes.Length);
            if (data != null && data.Length != 0)
            {
                stm.Write(data, 0, data.Length);
            }
            stm.Write(CrLfBytes, 0, CrLfBytes.Length);
            // m_Stream.Flush();
            if (m_Option.verbose)
            {
                var bytesread = stm.Read(receiveData, receivedLength, receiveData.Length - receivedLength);
                receivedLength += bytesread;
            }
        }
        public void Unsubscribe(long sid)
        {
            var msg = Encoding.UTF8.GetBytes("{NatsClientMessageKind.Unsub} {sid}");
            m_SubscribeStream.Write(msg, 0, msg.Length);
            m_SubscribeStream.Write(CrLfBytes, 0, CrLfBytes.Length);
            // m_SubscribeStream.Flush();
        }

        public event Action<NatsError> OnServerError;
        public event Func<NatsMessage, byte[]> OnMessage;
        public event Action<NatsOk> OnOk;
        event Action<NatsPing> OnPing;
        event Action<NatsPong> OnPong;
        public event Action<string[]> OnUnknownMessage;

        // void ProcessHeader(string[] msgHeaderStringArray, ref long sid, ref string subject, ref string replyTo, ref bool isInContent, ref int size, ref List<byte> content)
        // {
        //     // found crlf
        //     // var msgHeaderStringArray = Encoding.UTF8.GetString(messageBytes.Take(i - 1).ToArray()).Split(' ');
        //     // if (msgHeaderStringArray.Length < 4)
        //     // {
        //     //     throw new InvalidOperationException($"unknown message format:{string.Join(" ", msgHeaderStringArray)}");
        //     // }
        //     var kind = msgHeaderStringArray[0];
        //     switch (kind)
        //     {
        //         case NatsServerMessageKind.Msg:
        //             subject = msgHeaderStringArray[1];
        //             sid = long.Parse(msgHeaderStringArray[2]);
        //             if (msgHeaderStringArray.Length < 5)
        //             {
        //                 replyTo = null;
        //                 size = int.Parse(msgHeaderStringArray[3]);
        //             }
        //             else
        //             {
        //                 replyTo = msgHeaderStringArray[3];
        //                 size = int.Parse(msgHeaderStringArray[4]);
        //             }
        //             isInContent = true;
        //             break;
        //         case NatsServerMessageKind.Err:
        //             var errmsg = string.Join(" ", msgHeaderStringArray.Skip(1));
        //             if (OnServerError != null)
        //             {
        //                 OnServerError(new NatsError()
        //                 {
        //                     ErrorString = errmsg
        //                 });
        //             }
        //             isInContent = false;
        //             break;
        //         default:
        //             if (OnUnknownMessage != null)
        //             {
        //                 OnUnknownMessage(msgHeaderStringArray);
        //             }
        //             isInContent = false;
        //             break;
        //     }
        // }
        // bool ConsumeData(int bytesread, ref List<byte> messageBytes, ref long sid, ref string subject, ref string replyTo, ref bool isInContent, ref int size, ref List<byte> content)
        // {
        //     if (!isInContent)
        //     {
        //         bool foundCrLf = false;
        //         int CrLfIndex = 0;
        //         for (int i = 0; i < messageBytes.Count - 1; i++)
        //         {
        //             if (messageBytes[i] == 0x0d && messageBytes[i + 1] == 0x0a)
        //             {
        //                 var msgHeaderStringArray = Encoding.UTF8.GetString(messageBytes.Take(i - 1).ToArray()).Split(' ');
        //                 ProcessHeader(msgHeaderStringArray, ref sid, ref subject, ref replyTo, ref isInContent, ref size, ref content);
        //                 foundCrLf = true;
        //                 CrLfIndex = i;
        //             }
        //         }
        //         if (foundCrLf)
        //         {
        //             // remove consumed bytes
        //             messageBytes = messageBytes.Skip(CrLfIndex + 1).ToList();
        //         }
        //     }
        //     if (isInContent)
        //     {
        //         content.AddRange(messageBytes.Take(bytesread < size ? bytesread : bytesread - size));

        //         // content + crlf
        //         if (content.Count >= size + 2)
        //         {
        //             OnMessage(new NatsMessage()
        //             {
        //                 Data = content
        //                 ,
        //                 Sid = sid
        //                 ,
        //                 Subject = subject
        //                 ,
        //                 Reply = replyTo
        //             });
        //             isInContent = false;
        //         }
        //     }
        // }

        int FindCrlf(byte[] data)
        {
            for (int i = 0; i < data.Length - 1; i++)
            {
                if (data[i] == 0x0d && data[i + 1] == 0x0a)
                {
                    return i;
                }
            }
            return -1;
        }
        int FindCrlf(ValueArraySegment<byte> data)
        {
            for (int i = 0; i < data.Length - 1; i++)
            {
                if (data[i] == 0x0d && data[i + 1] == 0x0a)
                {
                    return i;
                }
            }
            return -1;
        }
        NatsMessage ParseMessage(Stream stm, string[] args, ref byte[] receivedData, ref int currentReceivedLength, ref ConsumerContext ctx)
        {
            ctx.Subject = args[0];
            ctx.Sid = long.Parse(args[1]);
            if (args.Length < 4)
            {
                ctx.Reply = null;
                ctx.Size = int.Parse(args[2]);
            }
            else
            {
                ctx.Reply = args[2];
                ctx.Size = int.Parse(args[3]);
            }
            var data = m_BufferPool.Rent(ctx.Size);
            Buffer.BlockCopy(receivedData, 0, data, 0, currentReceivedLength);
            int currentDataLength = currentReceivedLength;
            try
            {
                // read payload
                while (true)
                {
                    // [data]+[CRLF]
                    if (currentDataLength >= ctx.Size + 2)
                    {
                        var messageData = new byte[ctx.Size];
                        Buffer.BlockCopy(data, 0, messageData, 0, ctx.Size);
                        var natsMessage = new NatsMessage()
                        {
                            Reply = ctx.Reply
                            ,
                            Subject = ctx.Subject
                            ,
                            Data = messageData
                            ,
                            Sid = ctx.Sid
                        };
                        Buffer.BlockCopy(data, ctx.Size + 2, receivedData, 0, currentDataLength - (ctx.Size + 2));
                        currentReceivedLength = currentDataLength - (ctx.Size + 2);
                        return natsMessage;
                    }
                    else
                    {
                        var bytesread = stm.Read(data, currentDataLength, data.Length - currentDataLength);
                        currentDataLength += bytesread;
                    }
                }

            }
            finally
            {
                m_BufferPool.Return(data);
            }
        }
        static readonly char[] m_Space = new char[] { ' ' };
        bool ConsumeMessage(Stream stm, ref byte[] receivedData, ref int currentDataLength, ref ConsumerContext ctx)
        {
            var crlfIndex = FindCrlf(new ValueArraySegment<byte>(receivedData, 0, currentDataLength));
            if (crlfIndex < 0)
            {
                AllocateAndRead(stm, true, ref receivedData, ref currentDataLength);
                // var bytesread = stm.Read(receivedData, currentDataLength, receivedData.Length - currentDataLength);
                // currentDataLength += bytesread;
                // // if buffer usage exceed 90%, try to re-allocate buffer.
                // if (receivedData.Length * 9 / 10 < m_CurrentReceivedLength)
                // {
                //     var tmp = m_BufferPool.Rent(receivedData.Length * 2);
                //     Buffer.BlockCopy(receivedData, 0, tmp, 0, m_CurrentReceivedLength);
                //     m_BufferPool.Return(receivedData);
                //     receivedData = tmp;
                // }
                return false;
            }
            var headerString = Encoding.UTF8.GetString(receivedData.Take(crlfIndex).ToArray());
            var kindAndArg = headerString.Split(m_Space, 2);
            // CRLFを含む行データを削除する
            int remainingDataLength = receivedData.Length - crlfIndex - 2;
            Buffer.BlockCopy(receivedData, crlfIndex + 1, receivedData, 0, remainingDataLength);
            currentDataLength = remainingDataLength;
            switch (kindAndArg[0])
            {
                case NatsServerMessageKind.Msg:
                    var msg = ParseMessage(stm, kindAndArg[1].Split(' '), ref receivedData, ref currentDataLength, ref ctx);
                    byte[] replyBuffer = null;
                    if (OnMessage != null)
                    {
                        replyBuffer = OnMessage(msg);
                    }
                    if (!string.IsNullOrEmpty(msg.Reply))
                    {
                        WritePublishMessage(stm, msg.Reply, null, replyBuffer, ref receivedData, ref currentDataLength);
                    }
                    break;
                case NatsServerMessageKind.Err:
                    OnServerError(new NatsError()
                    {
                        ErrorString = kindAndArg[1]
                    });
                    break;
                case NatsServerMessageKind.Ok:
                    break;
                case NatsBothMessageKind.Ping:
                    break;
                default:
                    break;
            }
            // message consumed
            return true;
        }

        void MessageLoop()
        {
            ConsumerContext ctx = new ConsumerContext();
            var content = new List<byte>();
            Console.WriteLine($"thread started");
            while (!m_CancelToken.IsCancellationRequested)
            {
                try
                {
                    ConsumeMessage(m_SubscribeStream, ref m_SubscribeBuffer, ref m_CurrentReceivedLength, ref ctx);
                    // var bytesread = m_SubscribeStream.Read(m_ReceiveBuffer, 0, m_ReceiveBuffer.Length);
                    // messageBytes.AddRange(m_ReceiveBuffer.Take(bytesread));
                    // ConsumeMessage(m_SubscribeStream, ref messageBytes, ref ctx);
                    // var bytesread = m_Stream.Read(m_ReceiveBuffer, 0, m_ReceiveBuffer.Length);
                }
                catch (Exception e)
                {
                    Console.WriteLine($"{DateTime.Now}: error in reading message:{e}");
                }
            }
            Console.WriteLine($"thread end");
        }

        async Task CreateMessageLoopThread()
        {
            await Task.Run(() =>
            {
                MessageLoop();
            }).ConfigureAwait(false);
        }

        public async Task WaitMessage()
        {
            await Task.FromResult(0);
            // using (var stm = m_SubscribeClient.GetStream())
            // {
            //     var bytesread = await stm.ReadAsync(m_ReceiveBuffer, 0, m_ReceiveBuffer.Length, m_CancelToken.Token).ConfigureAwait(false);
            //     Console.WriteLine($"{string.Join(":", m_ReceiveBuffer.Take(bytesread))}");
            // }
            // var bytesread = await m_SubscribeStream.ReadAsync(m_ReceiveBuffer, 0, m_ReceiveBuffer.Length, m_CancelToken.Token).ConfigureAwait(false);
            // Console.WriteLine($"{string.Join(":", m_ReceiveBuffer.Take(bytesread))}");
        }

        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    if (m_CancelToken != null)
                    {
                        m_CancelToken.Cancel();
                        try
                        {
                            Console.WriteLine($"waiting");
                            if (m_Stream != null)
                            {
                                m_Stream.Dispose();
                                m_Stream = null;
                                m_SubscribeStream.Dispose();
                                m_SubscribeStream = null;
                            }
                            m_ConsumeDataThread.Wait();
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine($"disposing task error:{e}");
                        }
                        m_CancelToken.Dispose();
                        m_CancelToken = null;
                    }
                    if (m_Stream != null)
                    {
                        m_Stream.Dispose();
                        m_Stream = null;
                    }
                    if (m_Client != null)
                    {
                        m_Client.Dispose();
                        m_Client = null;
                    }
                    if (m_SubscribeClient != null)
                    {
                        m_SubscribeClient.Dispose();
                        m_SubscribeClient = null;
                    }
                }

                // TODO: free unmanaged resources (unmanaged objects) and override a finalizer below.
                // TODO: set large fields to null.

                disposedValue = true;
            }
        }

        // TODO: override a finalizer only if Dispose(bool disposing) above has code to free unmanaged resources.
        // ~NatsConnection() {
        //   // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
        //   Dispose(false);
        // }

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
            // TODO: uncomment the following line if the finalizer is overridden above.
            // GC.SuppressFinalize(this);
        }
        #endregion
    }
}