using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CabArk
{
    class CFData
    {
        public CFData(byte bReserved=0)
        {
            bReservedBytes = bReserved;
            abData = new byte[8 + 32768];
        }

        public CFData(byte[] source, byte bReserved = 0)
        {
            bReservedBytes = bReserved;
            FromBytes(source);
        }

        public CFData(FileStream src, long pos = -1, byte bReserved = 0, bool bLoadData = false)
        {
            bReservedBytes = bReserved;
            if (pos > -1)
                src.Seek(pos, SeekOrigin.Begin);
            FromStream(src, bLoadData);
        }

        // Calculate the checksum for the cabinet's CFDATA datablock
        // This is mainly a simple XOR of the input splitted into 32-bit blocks:
        // we can optimize computation xoring together raw numeric 64-bit values
        UInt32 checksum(byte[] input, ushort index, ushort ncbytes, UInt32 seed)
        {
            UInt64 csum = seed;

            int loops = ncbytes / 8;
            while (loops-- > 0)
            {
                csum ^= BitConverter.ToUInt64(input, index);
                index += 8;
            }

            int rem = ncbytes % 8;
            if (rem > 0)
            {
                if (rem >= 4)
                {
                    csum ^= BitConverter.ToUInt32(input, index);
                    index += 4;
                }
                // Here it is the trick...
                for (int j = rem % 4; j > 0; )
                    csum ^= ((UInt32)input[index++] << --j * 8);
            }
            return (UInt32)(csum & 0xFFFFFFFF) ^ (UInt32)(csum >> 32);
        }

        // Reads and decodes a CFDATA from source stream
        public int FromStream(FileStream input, bool bLoadData = false)
        {
            abData = new byte[8 + 32768];
            input.Read(abData, 0, 8);
            ulChecksum = BitConverter.ToUInt32(abData, 0);
            usCompressedSize = BitConverter.ToUInt16(abData, 4);
            usOriginalSize = BitConverter.ToUInt16(abData, 6);
            if (usCompressedSize > 32768)
            {
                Array.Resize(ref abData, 8 + usCompressedSize);
            }
            if (bLoadData)
                return input.Read(abData, 8, usCompressedSize);
            else
                return 8;
        }

        // Returns the CFDATA structure raw bytes 
        public byte[] ToBytes()
        {
            BitConverter.GetBytes(usCompressedSize).CopyTo(abData, 4);
            BitConverter.GetBytes(usOriginalSize).CopyTo(abData, 6);

            // CRC is computated over 2 previous WORDs, too!
            ulChecksum = checksum(abData, 4, (ushort)(usCompressedSize + 4), 0);
            BitConverter.GetBytes(ulChecksum).CopyTo(abData, 0);

            return abData;
        }

        // Pick the CFDATA from an array of bytes
        public void FromBytes(byte[] input)
        {
            abData = new byte[input.Length];
            ulChecksum = BitConverter.ToUInt32(input, 0);
            usCompressedSize = BitConverter.ToUInt16(input, 4);
            usOriginalSize = BitConverter.ToUInt16(input, 6);
            if (usCompressedSize != input.Length - 8)
                throw new ApplicationException();
            Array.Copy(input, 8, abData, 8, usCompressedSize);
        }

        // Represent the CFDATA structure as a string 
        override public string ToString()
        {
            return string.Format("CFDATA crc={0:X8} (err={1}), u/c size={2}/{3}", ulChecksum, Check(), usOriginalSize, usCompressedSize);
        }

        // Encodes and writes a CFDATA to dest stream
        public void ToStream(FileStream output)
        {
            ToBytes();
            output.Write(abData, 0, 8 + usCompressedSize);
        }

        // Verifies checksum. Returns 0 (OK), -1 (not present), 1 (BAD)
        public int Check()
        {
            if (ulChecksum == 0)
                return -1;

            ulong crc = checksum(abData, 4, (ushort) (usCompressedSize+4), 0);

            if (crc == ulChecksum)
                return 0;

            return 1;
        }

        public void SetContents(byte[] Data, int Size)
        {
            //if (Size > 32768)
               //throw new ApplicationException("FATAL: got a block greater than 32768 bytes, should never happen!");
            abData = new byte[8 + Size];
            Array.Copy(Data, 0, abData, 8, Size);
            usCompressedSize = (ushort)Size;
        }

        public int Length
        {
            get { return 8 + usCompressedSize + bReservedBytes; }
        }

        // checksum, computated from usCompressedSize field
        public uint ulChecksum;
        // number of raw bytes in this block, eventually compressed
        public ushort usCompressedSize;
        // length of the original, uncompressed block
        public ushort usOriginalSize;
        // Optional reserved area (see in CFHEADER)
        public byte[] abReserved;
        // raw bytes in this block
        public byte[] abData;

        byte bReservedBytes;
    }
}
