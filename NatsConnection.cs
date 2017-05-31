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
            var len = 0;
            while (true)
            {
                var ret = ConsumeMessageOnce(m_Stream, ref m_PublishBuffer, ref len);
                if (ret.id == NatsServerMessageId.Msg)
                {
                    return ret.msg.Data;
                }
            }
            throw new NotImplementedException();
        }
        private void Connect(string host, int port, NatsConnectOption opt)
        {
            m_Option = opt;
            m_Client.ConnectAsync(host, port).Wait();
            //m_Client.Connect(host, port);
            m_SubscribeClient.ConnectAsync(host, port).Wait();
            //m_SubscribeClient.Connect(host, port);
            m_Stream = m_Client.GetStream();
            m_SubscribeStream = m_SubscribeClient.GetStream();
            InitializeConnection(m_Stream, m_PublishBuffer, opt);
            InitializeConnection(m_SubscribeStream, m_PublishBuffer, opt);
            m_ConsumeDataThread = Task.FromResult(0);
            // m_ConsumeDataThread = CreateMessageLoopThread();
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
            m_SubscribeStream.Flush();
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
            var datalen = data != null ? data.Length : 0;
            var str = $"{NatsClientMessageKind.Pub} {subject} {reply} {datalen}";
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
        NatsMessage ParseMessage(Stream stm, string[] args, ref byte[] receivedData, ref int currentReceivedLength)
        {
            var subject = args[0];
            var sid = long.Parse(args[1]);
            string reply = null;
            int size = 0;
            if (args.Length < 4)
            {
                reply = null;
                size = int.Parse(args[2]);
            }
            else
            {
                reply = args[2];
                size = int.Parse(args[3]);
            }
            var copylen = currentReceivedLength < size + 2 ? currentReceivedLength : size + 2;
            currentReceivedLength -= copylen;
            var data = m_BufferPool.Rent(copylen);
            Buffer.BlockCopy(receivedData, 0, data, 0, copylen);
            int currentDataLength = copylen;
            Buffer.BlockCopy(receivedData, copylen, receivedData, 0, receivedData.Length - copylen);
            try
            {
                // read payload
                while (true)
                {
                    // [data]+[CRLF]
                    if (currentDataLength >= size + 2)
                    {
                        var messageData = new byte[size];
                        Buffer.BlockCopy(data, 0, messageData, 0, size);
                        var natsMessage = new NatsMessage()
                        {
                            Reply = reply
                            ,
                            Subject = subject
                            ,
                            Data = messageData
                            ,
                            Sid = sid
                        };
                        Buffer.BlockCopy(data, size + 2, receivedData, 0, currentDataLength - (size + 2));
                        return natsMessage;
                    }
                    else
                    {
                        var bytesread = stm.Read(data, currentDataLength, size + 2 - currentDataLength);
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
        (NatsServerMessageId id, NatsMessage msg, NatsError err) ConsumeMessageOnce(Stream stm, ref byte[] receivedData, ref int currentDataLength)
        {
            var crlfIndex = FindCrlf(new ValueArraySegment<byte>(receivedData, 0, currentDataLength));
            if (crlfIndex < 0)
            {
                AllocateAndRead(stm, true, ref receivedData, ref currentDataLength);
                return (NatsServerMessageId.None, default(NatsMessage), default(NatsError));
            }
            var headerString = Encoding.UTF8.GetString(receivedData.Take(crlfIndex).ToArray());
            var kindAndArg = headerString.Split(m_Space, 2);
            // CRLFを含む行データを削除する
            int remainingDataLength = currentDataLength - crlfIndex - 2;
            Buffer.BlockCopy(receivedData, crlfIndex + 2, receivedData, 0, remainingDataLength);
            currentDataLength = remainingDataLength;
            switch (kindAndArg[0])
            {
                case NatsServerMessageKind.Msg:
                    var msg = ParseMessage(stm, kindAndArg[1].Split(' '), ref receivedData, ref currentDataLength);
                    return (NatsServerMessageId.Msg, msg, default(NatsError));
                case NatsServerMessageKind.Err:
                    return (NatsServerMessageId.Err, default(NatsMessage), new NatsError() { ErrorString = kindAndArg[1] });
                case NatsServerMessageKind.Ok:
                    return (NatsServerMessageId.Ok, default(NatsMessage), default(NatsError));
                case NatsBothMessageKind.Ping:
                    return (NatsServerMessageId.Ping, default(NatsMessage), default(NatsError));
                default:
                    break;
            }
            // message consumed
            return (NatsServerMessageId.None, default(NatsMessage), default(NatsError));
        }
        // bool ConsumeMessage(Stream stm, ref byte[] receivedData, ref int currentDataLength)
        // {
        //     var crlfIndex = FindCrlf(new ValueArraySegment<byte>(receivedData, 0, currentDataLength));
        //     if (crlfIndex < 0)
        //     {
        //         AllocateAndRead(stm, true, ref receivedData, ref currentDataLength);
        //         return false;
        //     }
        //     var headerString = Encoding.UTF8.GetString(receivedData.Take(crlfIndex).ToArray());
        //     var kindAndArg = headerString.Split(m_Space, 2);
        //     // CRLFを含む行データを削除する
        //     int remainingDataLength = currentDataLength - crlfIndex - 2;
        //     Buffer.BlockCopy(receivedData, crlfIndex + 2, receivedData, 0, remainingDataLength);
        //     currentDataLength = remainingDataLength;
        //     switch (kindAndArg[0])
        //     {
        //         case NatsServerMessageKind.Msg:
        //             var msg = ParseMessage(stm, kindAndArg[1].Split(' '), ref receivedData, ref currentDataLength);
        //             byte[] replyBuffer = null;
        //             if (OnMessage != null)
        //             {
        //                 replyBuffer = OnMessage(msg);
        //             }
        //             if (!string.IsNullOrEmpty(msg.Reply))
        //             {
        //                 WritePublishMessage(stm, msg.Reply, null, replyBuffer, ref receivedData, ref currentDataLength);
        //             }
        //             break;
        //         case NatsServerMessageKind.Err:
        //             OnServerError(new NatsError()
        //             {
        //                 ErrorString = kindAndArg[1]
        //             });
        //             break;
        //         case NatsServerMessageKind.Ok:
        //             break;
        //         case NatsBothMessageKind.Ping:
        //             break;
        //         default:
        //             break;
        //     }
        //     // message consumed
        //     return true;
        // }

        // void MessageLoop()
        // {
        //     Console.WriteLine($"thread started");
        //     while (!m_CancelToken.IsCancellationRequested)
        //     {
        //         try
        //         {
        //             ConsumeMessage(m_SubscribeStream, ref m_SubscribeBuffer, ref m_CurrentReceivedLength);
        //             // var bytesread = m_SubscribeStream.Read(m_ReceiveBuffer, 0, m_ReceiveBuffer.Length);
        //             // messageBytes.AddRange(m_ReceiveBuffer.Take(bytesread));
        //             // ConsumeMessage(m_SubscribeStream, ref messageBytes, ref ctx);
        //             // var bytesread = m_Stream.Read(m_ReceiveBuffer, 0, m_ReceiveBuffer.Length);
        //         }
        //         catch (Exception e)
        //         {
        //             Console.WriteLine($"{DateTime.Now}: error in reading message:{e}");
        //         }
        //     }
        //     Console.WriteLine($"thread end");
        // }

        // async Task CreateMessageLoopThread()
        // {
        //     await Task.Run(() =>
        //     {
        //         MessageLoop();
        //     }).ConfigureAwait(false);
        // }

        public (NatsServerMessageId id, NatsMessage msg, NatsError err) WaitMessage()
        {
            return ConsumeMessageOnce(m_SubscribeStream, ref m_SubscribeBuffer, ref m_CurrentReceivedLength);
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