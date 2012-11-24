using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;

namespace CabArk
{
    abstract class Compressor
    {
        abstract public byte[] Compress(byte[] data, bool isLast);
        abstract public byte[] Decompress(byte[] data, bool isLast);
        //abstract public byte[] GetLastExpandedBlock();

        public bool bInited = false;
        protected byte[] Buffer = new byte[32768 + 6144];
    }

    class MSZip : Compressor
    {
        [StructLayoutAttribute(LayoutKind.Sequential, Pack = 4, Size = 0, CharSet = CharSet.Ansi)]
        internal struct ZStream
        {
            public IntPtr next_in;
            public uint avail_in;
            public uint total_in;

            public IntPtr next_out;
            public uint avail_out;
            public uint total_out;

            [MarshalAs(UnmanagedType.LPStr)]
            string msg;
            uint state;

            uint zalloc;
            uint zfree;
            uint opaque;

            int data_type;
            public uint adler;
            uint reserved;
        }

        [DllImport("zlibwapi.dll", CallingConvention = CallingConvention.Winapi)]
        private static extern int deflateInit2_(ref ZStream sz, int level, int method, int windowbits, int memlevel, int strategy, string vs, int size);

        [DllImport("zlibwapi.dll", CallingConvention = CallingConvention.Winapi)]
        private static extern int deflate(ref ZStream sz, int flush);

        [DllImport("zlibwapi.dll", CallingConvention = CallingConvention.Winapi)]
        private static extern int deflateEnd(ref ZStream sz);

        [DllImport("zlibwapi.dll", CallingConvention = CallingConvention.Winapi)]
        private static extern int inflateInit2_(ref ZStream sz, int windowbits, string vs, int size);

        [DllImport("zlibwapi.dll", CallingConvention = CallingConvention.Winapi)]
        private static extern int inflate(ref ZStream sz, int flush);

        [DllImport("zlibwapi.dll", CallingConvention = CallingConvention.Winapi)]
        private static extern int inflateEnd(ref ZStream sz);


        public override byte[] Compress(byte[] data, bool isLast)
        {
            if (!bInited)
            {
                zstream = new ZStream();

                // memlevel=8 is equivalent to deflateInit_
                // note that with a Python27 folder, DECREASING memlevel gives better results!!!
                // 9 is worse than 8, and 6 is the best (~14KiB better than 8, and 1 KiB better than CabArc 5.2)
                if (deflateInit2_(ref zstream, 5, 8, -15, 8, 0, "1.2.7", Marshal.SizeOf(zstream)) != 0)
                    throw new ApplicationException("Couldn't initialize ZLIB library!");

                bInited = true;
            }

            zstream.next_in = Marshal.UnsafeAddrOfPinnedArrayElement(data, 0);
            zstream.total_in = 0;
            zstream.avail_in = (uint)data.Length;

            zstream.next_out = Marshal.UnsafeAddrOfPinnedArrayElement(Buffer, 0);
            zstream.total_out = 0;
            zstream.avail_out = 32768 + 6144;

            // zlib compress is equivalent to deflate with Z_FINISH (4) + deflateEnd
            // we use deflate with Z_SYNC_FLUSH (2), and never reset
            // Z_FINISH is required (from CabArc) to mark the last block
            deflate(ref zstream, isLast ? 4 : 2);
            //if (deflate(ref zstream, isLast ? 4 : 2) != 0)
            //    throw new ApplicationException("Error deflating data with MS-ZIP!");

            if (isLast)
            {
                Console.WriteLine("DEBUG: isLast == true");
                deflateEnd(ref zstream);
                bInited = false;
            }

            // To be extracted by MS CabArc, a block MUST be <=32780 bytes!
            // But Cabarc emits copied blocks of 32775, so we do the same
            if (zstream.total_out > 32768)
            {
                // Create a Deflate uncompressed block (32775 bytes), copying the raw input
                tmp = new byte[32775];
                // "CK", 01, 0x8000, 0x7FFF, 32KiB raw data 
                tmp[2] = 1; tmp[3] = 0; tmp[4] = 0x80; tmp[5] = 0xFF; tmp[6] = 0x7F;
                Array.Copy(data, 0, tmp, 7, 32768);
            }
            else
            {
                tmp = new byte[zstream.total_out + 2];
                Array.Copy(Buffer, 0, tmp, 2, zstream.total_out);
            }
            tmp[0] = 67; tmp[1] = 75; // CK

            return tmp;
        }

        public override byte[] Decompress(byte[] data, bool isLast)
        {
            // Detect uncompressed, copied block
            if (data.Length == 32775 && data[2] == 1)
            {
                tmp = new byte[32768];
                Array.Copy(data, 7, tmp, 0, 32768);
                return tmp;
            }

            if (!bInited)
            {
                zstream = new ZStream();

                if (inflateInit2_(ref zstream, -15, "1.2.7", Marshal.SizeOf(zstream)) != 0)
                    throw new ApplicationException("Couldn't initialize ZLIB library!");

                bInited = true;
            }

            zstream.next_in = Marshal.UnsafeAddrOfPinnedArrayElement(data, 2); // Discard CK marker
            zstream.total_in = 0;
            zstream.avail_in = (uint)data.Length - 2;

            zstream.next_out = Marshal.UnsafeAddrOfPinnedArrayElement(Buffer, 0);
            zstream.total_out = 0;
            zstream.avail_out = 32768 + 6144;

            //int err = inflate(ref zstream, isLast ? 4 : 2); // Z_FINISH : Z_SYNC_FLUSH
            int err = inflate(ref zstream, 2); // Z_FINISH : Z_SYNC_FLUSH

            if (err != 0 && err != 1) // Z_OK || Z_STREAM_END
            {
                Console.WriteLine("Error inflating MS-ZIP data!");
                Environment.Exit(1);
            }
            tmp = new byte[zstream.total_out];
            Array.Copy(Buffer, tmp, zstream.total_out);

            // Technically speaking, the last chunk is at FOLDER'S END!
            //if (isLast)
            //{
            //    inflateEnd(ref zstream);
            //    bInited = false;
            //}

            return tmp;
        }

        //public override byte[] GetLastExpandedBlock()
        //{
        //    return tmp;
        //}
        
        ZStream zstream;
        byte[] tmp;
    }

    class LZX : Compressor
    {
        [DllImport("mscompression.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr lzx_cab_compress_start(uint numDictBits);

        [DllImport("mscompression.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern uint lzx_cab_compress_block(IntPtr in_bytes, int in_len, IntPtr out_bytes, int out_len, IntPtr state);

        [DllImport("mscompression.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void lzx_cab_compress_end(IntPtr state);

        [DllImport("mscompression.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern uint lzx_cab_decompress(IntPtr in_bytes, int in_len, IntPtr out_bytes, int out_len, uint numDictBits);

        public LZX(uint WindowSize=15)
        {
            numDictBits = WindowSize;
        }

        public override byte[] Compress(byte[] data, bool isLast)
        {
            if (!bInited)
            {
                lzx_cab_state = lzx_cab_compress_start(numDictBits);
                bInited = true;
            }

            // This call is known to raise access violation exceptions!
            uint cb = 0;

            try
            {
                cb = lzx_cab_compress_block(Marshal.UnsafeAddrOfPinnedArrayElement(data, 0), data.Length,
                    Marshal.UnsafeAddrOfPinnedArrayElement(Buffer, 0), Buffer.Length,
                    lzx_cab_state);
            }
            catch (AccessViolationException)
            {
                Console.WriteLine("FATAL: Unrecoverable Access Violation Exception from LZX compressor!");
                Environment.Exit(1);
            }

            tmp = new byte[cb];
            Array.Copy(Buffer, 0, tmp, 0, cb);

            if (isLast)
            {
                lzx_cab_compress_end(lzx_cab_state);
                bInited = false;
            }

            return tmp;
        }

        public override byte[] Decompress(byte[] data, bool isLast)
        {
            uint cb = 0;

            try
            {
                cb = lzx_cab_decompress(Marshal.UnsafeAddrOfPinnedArrayElement(data, 0), data.Length,
                    Marshal.UnsafeAddrOfPinnedArrayElement(Buffer, 0), Buffer.Length, numDictBits);

                if (cb == 0)
                    throw new ApplicationException();
            }
            catch
            {
                Console.WriteLine("FATAL: Unrecoverable error in LZX decompressor!");
                Environment.Exit(1);
            }

            tmp = new byte[cb];
            Array.Copy(Buffer, 0, tmp, 0, cb);

            return tmp;
        }

        //public override byte[] GetLastExpandedBlock()
        //{
        //    return tmp;
        //}

        byte[] tmp;
        IntPtr lzx_cab_state;
        uint numDictBits;
    }
}
