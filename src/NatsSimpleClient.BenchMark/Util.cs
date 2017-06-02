namespace NatsSimpleClient.BenchMark
{
    static class Util
    {
        public static void i32tob(int i, byte[] buf)
        {
            buf[0] = (byte)(i & 0xff);
            buf[1] = (byte)((i >> 8) & 0xff);
            buf[2] = (byte)((i >> 16) & 0xff);
            buf[3] = (byte)((i >> 24) & 0xff);
        }
        public static int btoi32(byte[] b)
        {
            return (int)(b[0])
                + ((int)b[1] << 8)
                + ((int)b[2] << 16)
                + ((int)b[3] << 24)
                ;
        }
        
    }
    
}