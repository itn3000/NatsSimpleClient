namespace nats_simple_client
{
    using System.IO;
    using System.Text;
    static class ValueArraySegmentExtension
    {
        public static int Read(this Stream stm, ref ValueArraySegment<byte> buf)
        {
            return stm.Read(buf.Array, buf.Offset, buf.Length);
        }
        public static void Write(this Stream stm, ref ValueArraySegment<byte> buf)
        {
            stm.Write(buf.Array, buf.Offset, buf.Length);
        }
        public static string GetString(this Encoding enc, ref ValueArraySegment<byte> data)
        {
            return enc.GetString(data.Array, data.Offset, data.Length);
        }
    }
}