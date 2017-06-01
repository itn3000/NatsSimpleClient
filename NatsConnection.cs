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
    struct NatsNone
    {

    }
    struct NatsResponse
    {
        public readonly NatsMessage Msg;
        public readonly NatsError Error;
        public readonly NatsServerMessageId Kind;
        public NatsResponse(NatsMessage msg)
        {
            Msg = msg;
            Error = default(NatsError);
            Kind = NatsServerMessageId.Msg;
        }
        public NatsResponse(NatsError err)
        {
            Msg = default(NatsMessage);
            Error = err;
            Kind = NatsServerMessageId.Err;
        }
        private NatsResponse(NatsServerMessageId kind)
        {
            Msg = default(NatsMessage);
            Error = default(NatsError);
            Kind = kind;
        }
        public NatsResponse(NatsOk ok)
            : this(NatsServerMessageId.Ok)
        {

        }
        public NatsResponse(NatsPing ping)
            : this(NatsServerMessageId.Ping)
        {
        }
        public NatsResponse(NatsNone none)
            : this(NatsServerMessageId.None)
        {
        }
    }
    class NatsConnection : IDisposable
    {
        public static NatsConnection Create(string host, int port, NatsConnectOption opt, bool manualFlush = false)
        {
            var ret = new NatsConnection(manualFlush);
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
        Stream m_Stream;
        NatsConnectOption m_Option;
        long m_CurrentSid = 1;
        static readonly byte[] CrLfBytes = new byte[] { 0x0d, 0x0a };
        Task m_ConsumeDataThread;
        ServerInfo m_ServerInfo;
        CancellationTokenSource m_CancelToken = new CancellationTokenSource();
        ArrayPool<byte> m_BufferPool = System.Buffers.ArrayPool<byte>.Shared;
        int m_CurrentReceivedLength = 0;
        byte[] m_PublishBuffer = new byte[4096];
        bool m_ManualFlush = false;

        private NatsConnection(bool manualFlush)
        {
            m_Client = new TcpClient();
            m_ManualFlush = manualFlush;
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
            stm.Flush();
        }
        private void Connect(string host, int port, NatsConnectOption opt)
        {
            m_Option = opt;
            m_Client.ConnectAsync(host, port).Wait();
            m_Stream = new BufferedStream(m_Client.GetStream());
            InitializeConnection(m_Stream, m_PublishBuffer, opt);
            m_ConsumeDataThread = Task.FromResult(0);
        }

        public long Subscribe(string subject, string queue)
        {
            var qname = !string.IsNullOrEmpty(queue) ? $" {queue} " : " ";
            var sid = Interlocked.Increment(ref m_CurrentSid);
            var str = $"{NatsClientMessageKind.Sub} {subject}{qname}{sid}";
            var msg = System.Text.Encoding.UTF8.GetBytes(str);
            var buf = m_BufferPool.Rent(msg.Length + CrLfBytes.Length);
            Buffer.BlockCopy(msg, 0, buf, 0, msg.Length);
            Buffer.BlockCopy(CrLfBytes, 0, buf, msg.Length, 2);
            m_Stream.Write(buf, 0, msg.Length + 2);
            if (m_Option.verbose)
            {
                AllocateAndRead(m_Stream, m_Client, true, ref m_SubscribeBuffer, ref m_CurrentReceivedLength);
                // Console.WriteLine($"sub result:{bytesread},{Encoding.UTF8.GetString(m_ReceiveBuffer, 0, bytesread)}");
            }
            if(!m_ManualFlush)
            {
                m_Stream.Flush();
            }
            return sid;
        }
        public void Publish(string subject, string replyTo, byte[] data)
        {
            WritePublishMessage(m_Stream, subject, replyTo, data, ref m_SubscribeBuffer, ref m_CurrentReceivedLength);
        }
        public async Task PublishAsync(string subject, string replyTo, byte[] data)
        {
            (m_SubscribeBuffer, m_CurrentReceivedLength) = await WritePublishMessageAsync(m_Stream, subject, replyTo, data, m_SubscribeBuffer, m_CurrentReceivedLength);
        }
        void AllocateAndRead(Stream stm, TcpClient client, bool fromPool, ref byte[] buffer, ref int dataLength)
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
            if (client.Client.Poll(1000 * 1000, SelectMode.SelectRead))
            {
                var bytesread = stm.Read(buffer, dataLength, buffer.Length - dataLength);
                dataLength += bytesread;
            }
            else
            {
                throw new NatsTimeoutException();
            }
        }
        async Task<(byte[] receiveData, int receiveDataLength)> WritePublishMessageAsync(Stream stm, string subject, string replyTo, byte[] data, byte[] receiveData, int receivedLength)
        {
            var reply = replyTo ?? "";
            var datalen = data != null ? data.Length : 0;
            var str = $"{NatsClientMessageKind.Pub} {subject} {reply} {datalen}";
            var msg = System.Text.Encoding.UTF8.GetBytes(str);
            stm.Write(msg, 0, msg.Length);
            stm.Write(CrLfBytes, 0, CrLfBytes.Length);
            if (datalen != 0)
            {
                stm.Write(data, 0, data.Length);
            }
            await stm.WriteAsync(CrLfBytes, 0, CrLfBytes.Length).ConfigureAwait(false);
            if (!m_ManualFlush)
            {
                await m_Stream.FlushAsync().ConfigureAwait(false);
            }
            if (m_Option.verbose)
            {
                var bytesread = await stm.ReadAsync(receiveData, receivedLength, receiveData.Length - receivedLength).ConfigureAwait(false);
                receivedLength += bytesread;
            }
            return (receiveData, receivedLength);
        }
        void WritePublishMessage(Stream stm, string subject, string replyTo, byte[] data, ref byte[] receiveData, ref int receivedLength)
        {
            var reply = replyTo ?? "";
            var datalen = data != null ? data.Length : 0;
            var str = $"{NatsClientMessageKind.Pub} {subject} {reply} {datalen}";
            var msg = System.Text.Encoding.UTF8.GetBytes(str);
            stm.Write(msg, 0, msg.Length);
            stm.Write(CrLfBytes, 0, CrLfBytes.Length);
            if (datalen != 0)
            {
                stm.Write(data, 0, data.Length);
            }
            stm.Write(CrLfBytes, 0, CrLfBytes.Length);
            if (!m_ManualFlush)
            {
                m_Stream.Flush();
            }
            if (m_Option.verbose)
            {
                var bytesread = stm.Read(receiveData, receivedLength, receiveData.Length - receivedLength);
                receivedLength += bytesread;
            }
        }
        public void Unsubscribe(long sid)
        {
            var msg = Encoding.UTF8.GetBytes($"{NatsClientMessageKind.Unsub} {sid}");
            m_Stream.Write(msg, 0, msg.Length);
            m_Stream.Write(CrLfBytes, 0, CrLfBytes.Length);
            if(!m_ManualFlush)
            {
                m_Stream.Flush();
            }
            // m_SubscribeStream.Flush();
        }
        public void Unsubscribe(long sid, int afterMessageNum)
        {
            var msg = Encoding.UTF8.GetBytes($"{NatsClientMessageKind.Unsub} {sid} {afterMessageNum}");
            m_Stream.Write(msg, 0, msg.Length);
            m_Stream.Write(CrLfBytes, 0, CrLfBytes.Length);
            if(!m_ManualFlush)
            {
                m_Stream.Flush();
            }
        }
        public void Flush()
        {
            m_Stream.Flush();
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
        NatsResponse ConsumeMessageOnce(Stream stm, TcpClient client, ref byte[] receivedData, ref int currentDataLength)
        {
            var crlfIndex = FindCrlf(new ValueArraySegment<byte>(receivedData, 0, currentDataLength));
            if (crlfIndex < 0)
            {
                AllocateAndRead(stm, client, true, ref receivedData, ref currentDataLength);
                return new NatsResponse(new NatsNone());
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
                    return new NatsResponse(msg);
                case NatsServerMessageKind.Err:
                    return new NatsResponse(new NatsError() { ErrorString = kindAndArg[1] });
                case NatsServerMessageKind.Ok:
                    return new NatsResponse(new NatsOk());
                case NatsBothMessageKind.Ping:
                    return new NatsResponse(new NatsPing());
                default:
                    break;
            }
            // message consumed
            return new NatsResponse(new NatsNone());
        }

        public NatsResponse WaitMessage()
        {
            return ConsumeMessageOnce(m_Stream, m_Client, ref m_SubscribeBuffer, ref m_CurrentReceivedLength);
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
                            if (m_Stream != null)
                            {
                                m_Stream.Dispose();
                                m_Stream = null;
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