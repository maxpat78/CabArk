using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CabArk
{
    class CFFolder
    {
        public CFFolder(byte bReserved = 0)
        {
            cfFiles = new List<CFFile>();
            bReservedBytes = bReserved;
        }

        public CFFolder(byte[] source, byte bReserved = 0)
        {
            cfFiles = new List<CFFile>();
            bReservedBytes = bReserved;
            FromBytes(source);
        }

        public CFFolder(FileStream source, long pos = -1, byte bReserved = 0)
        {
            cfFiles = new List<CFFile>();
            bReservedBytes = bReserved;
            if (pos > -1)
                source.Seek(pos, SeekOrigin.Begin);
            FromStream(source);
        }

        // Reads and decodes a CFFOLDER from source stream. Return 0 (OK), 1 (BAD)
        public int FromStream(FileStream input)
        {
            byte[] tmp = new byte[8];
            input.Read(tmp, 0, 8);
            return FromBytes(tmp);
        }

        public int FromBytes(byte[] input)
        {
            uiDataOffset = BitConverter.ToUInt32(input, 0);
            usBlocks = BitConverter.ToUInt16(input, 4);
            usCompression = BitConverter.ToUInt16(input, 6);
            SetCompression(usCompression);
            return 0;
        }

        // Returns the CFFOLDER structure raw bytes 
        public byte[] ToBytes()
        {
            byte[] tmp = new byte[8];
            BitConverter.GetBytes(uiDataOffset).CopyTo(tmp, 0);
            BitConverter.GetBytes(usBlocks).CopyTo(tmp, 4);
            BitConverter.GetBytes(usCompression).CopyTo(tmp, 6);
            return tmp;
        }

        // Writes the CFFOLDER structure to a stream
        public void ToStream(FileStream output)
        {
            byte[] tmp = ToBytes();
            Position = output.Position;
            output.Write(tmp, 0, tmp.Length);
        }

        // Represent the CFFOLDER structure as a string 
        override public string ToString()
        {
            return string.Format("CFFOLDER uiDataOffset={0:X8}h, usBlocks={1}, usCompression={2:X4}h", uiDataOffset, usBlocks, usCompression);
        }

        // Sets compression for the new, empty folder. 
        // Valid values are 0 (uncompressed), 1 ("MS"-ZIP aka Deflate), 3 (LZX)
        // 2 (Quantum) is not supported
        public bool SetCompression(ushort CompressionType)
        {
            if (cfFiles.Count > 0)
                return false;
            if ((CompressionType & 0x00FF) > 3)
                return false;
            if (CompressionType == 2)
                return false;
            if ((CompressionType & 0x00FF) == 3)
                if ((CompressionType >> 8) < 15 || (CompressionType >> 8) > 21)
                    return false;
            
            usCompression = CompressionType;
            
            if (CompressionType == 1)
            {
                compressor = new MSZip();
            }

            if ((CompressionType & 0x00FF) == 3)
            {
                compressor = new LZX((uint)CompressionType >> 8);
            }
            return true;
        }

        public void SetReserved(byte Reserved)
        {
            bReservedBytes = Reserved;
        }

        // Adds a file to the current folder. Returns 0 for success, or
        // 1=not exists, 2=too big (>=4GiB)
        public int AddFile(string PathName, List<string> Strip)
        {
            FileInfo finfo = new FileInfo(PathName);

            if (!finfo.Exists)
                return 1;

            if (finfo.Length > 0xFFFFFFFF)
                return 2;

            CFFile cff = new CFFile();
            cff.uiFileSize = (uint)finfo.Length;
            cff.uiFolderOffset = uiNextItemOffset;
            cff.usDOSAttributes = (ushort)finfo.Attributes;
            uint dt = cff.DateTimeToUInt32(finfo.LastWriteTime);
            cff.usDOSDate = (ushort)((dt & 0xFFFF0000) >> 16);
            cff.usDOSTime = (ushort)(dt & 0x0000FFFF);
            cff.SetItemName(PathName, Strip);

            cfFiles.Add(cff);

            uiNextItemOffset += (uint)finfo.Length;
            return 0;
        }

        public void AddFile(CFFile cff)
        {
            cfFiles.Add(cff);
        }

        public CFFile GetFile(string FileName)
        {
            CFFile cff_found;

            cff_found = cfFiles.Find(
                delegate(CFFile cff)
                {
                    // MS Cabarc searches *ALWAYS* by file name without path, even if preserved!
                    return Win32Wildcards.Match(Path.GetFileName(cff.FileName), FileName);
                });

            return cff_found;
        }

        public bool GetFile(CFFile cff)
        {
            return cfFiles.Contains(cff);
        }

        public List<CFFile> GetFiles(string wildcard=null)
        {
            if (wildcard == null)
                return cfFiles;
            return cfFiles.FindAll(
                delegate(CFFile cff)
                {
                    // MS Cabarc searches *ALWAYS* by file name without path, even if preserved!
                    return Win32Wildcards.Match(Path.GetFileName(cff.FileName), wildcard);
                } );
        }

        public int WriteFolder(FileStream output)
        {
            uiDataOffset = (uint)output.Position;
            byte[] main_buf = new byte[32768];
            byte[] tmp;

            // Cycles through files, filling the tmp buffer upto exactly 32KiB 'til the last chunk
            for (int i=0, ucb=0; i < cfFiles.Count; i++)
            {
                FileStream f = new FileStream(cfFiles[i].FileName, FileMode.Open, FileAccess.Read);

                Console.WriteLine("  -- adding: {0}", cfFiles[i].FileName);

                int req = 32768 - ucb;

                while (true)
                {
                    // Restore the pointer to the main buffer
                    tmp = main_buf;
                    int cb = f.Read(tmp, ucb, req);

                    // If it was the last chunk of the last file...
                    //bool isLast = (cb == 0 && i == cfFiles.Count - 1);
                    // ATTEMPT!
                    bool isLast = (cb < 32768 && i == cfFiles.Count - 1);

                    // ...AND there are no bytes pending, quits!
                    //if (isLast && ucb == 0) break;
                    if (isLast && cb == 0) break;
                    
                    ucb += cb;

                    // If we reached the end of an intermediate file...
                    if (ucb < 32768 && i < cfFiles.Count - 1) break;

                    int ccb;
                    if (usCompression != 0 && compressor != null)
                    {
                        // An uncompressed 32KiB block MUST be represented by some output
                        // We tell the compressor if it is the last chunk
                        tmp = CompressChunk(tmp, ucb, isLast); // "MS"-ZIP or LZX
                        ccb = tmp.Length;
                    }
                    else
                        ccb = ucb;
                    
                    // Makes and saves the CFDATA
                    CFData cfd = new CFData();
                    cfd.SetContents(tmp, ccb);
                    cfd.usOriginalSize = (ushort)ucb;
                    cfd.usCompressedSize = (ushort)ccb;
                    cfd.ToStream(output);

                    usBlocks += 1;
                   
                    ucb = 0;
                    req = 32768;
                }
                f.Close();
            }

            long pos = output.Position;
            output.Position = Position;
            ToStream(output);
            output.Position = pos;

            return 0;
        }

        // Expands and copies a folder's segment (starting from "offset" and long "length" bytes) to a file
        public int Copy(FileStream input, FileStream output, uint offset, uint length)
        {
            byte[] tmp;
            CFData cfd;
            bool bContinuedStream = false;

            uint uiStartChunk = offset / 32768;

            // If it is the last expanded block, cached...
            if (uiStartChunk == uiLastExpandedBlock)
            {
                bContinuedStream = true;
                goto Perform;
            }

            // If we already started the inflater, we have to skip
            // only the blocks between last & next...
            if (compressor != null && compressor.bInited)
            {
                uiStartChunk -= uiLastExpandedBlock;
                uiStartChunk--;
            }
            else
            {
                input.Seek(uiDataOffset, SeekOrigin.Begin);
            }

            // We must synchronize the inflater by expanding all previous chunks...
            while (uiStartChunk-- > 0)
            {
                cfd = new CFData(input, bLoadData:true);
                if (cfd.Check() == 1)
                    throw new ApplicationException("Corrupted data block found!");
                tmp = DecompressChunk(cfd.abData, cfd.usCompressedSize, false);
                uiLastExpandedBlock++;
            }
Perform:
            int iStartOffset = (int)offset % 32768; // Start offset inside 1st chunk
            int ucb = (int)length; // uncompressed bytes to get expanded
            int cb = 0; // uncompressed bytes picked from current chunk

            // How many bytes we picked from last block? The remainder is for next call!
            while (ucb > 0)
            {
                if (!bContinuedStream)
                {
                    cfd = new CFData(input, bLoadData: true);
                    if (cfd.Check() == 1)
                        throw new ApplicationException("Corrupted data block found!");
                    cb = (cfd.usOriginalSize - iStartOffset > ucb) ? ucb : cfd.usOriginalSize - iStartOffset;
                    // The chunk is the last if cb == ucb
                    tmp = DecompressChunk(cfd.abData, cfd.usCompressedSize, (ucb == cb));
                    LastExpandedBlock = tmp;
                    uiLastExpandedBlock++;
                }
                else
                {
                    tmp = LastExpandedBlock;
                    cb = (tmp.Length - iStartOffset > ucb) ? ucb : tmp.Length - iStartOffset;
                    bContinuedStream = false;
                }

                output.Write(tmp, iStartOffset, (ucb == cb) ? ucb : tmp.Length - iStartOffset);
                ucb -= cb;
                iStartOffset = 0;
            }

            return 1;
        }

        public int WriteFiles(FileStream output)
        {
            foreach (CFFile cff in cfFiles)
            {
                cff.ToStream(output);
            }
            return cfFiles.Count;
        }

        public byte[] CompressChunk(byte[] input, int length, bool isLast = false)
        {
            if (length < input.Length)
            {
                byte[] tmp = new byte[length];
                Array.Copy(input, tmp, length);
                input = tmp;
            }

            return compressor.Compress(input, isLast);
        }

        public byte[] DecompressChunk(byte[] input, int length, bool isLast = false)
        {
            byte[] tmp = new byte[length];
            Array.Copy(input, 8, tmp, 0, length);

            return compressor.Decompress(tmp, isLast);
        }

        public int Length
        {
            get { return 8 + bReservedBytes; }
        }

        public int Files
        {
            get { return cfFiles.Count; }
        }

        // Offset of first CFDATA block
        public uint uiDataOffset;
        // Number of CFDATA blocks for this folder stored in this CAB
        public ushort usBlocks;
        // Compression type: 0=none, 1="MS"-ZIP, 2=QUANTUM, 0xNN03=LZX with 2^NN window
        public ushort usCompression;
        // Optional per-CFFOLDER reserved area (see in CFHEADER)
        public byte[] abReserved;

        // Private members
        // Per-CFFOLDER reserved area
        byte bReservedBytes;
        // Position in stream
        long Position = -1;
        // List of folder's files
        List<CFFile> cfFiles;
        // Position of the next file to add inside this folder (=running folder size)
        uint uiNextItemOffset = 0;
        // Index of the last expanded CFDATA block
        uint uiLastExpandedBlock = 0xFFFFFFFF;
        // Uncompressed bytes of such block backed up
        byte[] LastExpandedBlock;
        Compressor compressor = null;
    }
}
