using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CabArk
{
    class CFHeader
    {

        public CFHeader()
        {
        }

        public CFHeader(byte[] source)
        {
            FromBytes(source);
        }

        public CFHeader(FileStream src, long pos = -1)
        {
            if (pos > -1)
                src.Seek(pos, SeekOrigin.Begin);
            FromStream(src);
        }

        // Reads and decodes a CFHEADER from source stream. Returns 0 (OK), 1 (BAD)
        public int FromStream(FileStream input)
        {
            byte[] tmp = new byte[36];
            input.Read(tmp, 0, 36);

            if ((tmp[30] & 4) == 4)
            {
                Array.Resize(ref tmp, 40);
                input.Read(tmp, 36, 4);
            }
            return FromBytes(tmp);
        }

        // Pick the CFHEADER from an array of bytes
        public int FromBytes(byte[] input)
        {
            byte[] tmp = input;
            if (tmp[0] != 'M' && tmp[1] != 'S' && tmp[2] != 'C' && tmp[3] != 'F')
                return 1;
            uiCabSize = BitConverter.ToUInt32(tmp, 8);
            uiFilesOffset = BitConverter.ToUInt32(tmp, 16);
            cVerMinor = tmp[24];
            cVerMajor = tmp[25];
            if (cVerMinor != 3 && cVerMajor != 1)
                return 1;
            usNumFolders = BitConverter.ToUInt16(tmp, 26);
            usNumFiles = BitConverter.ToUInt16(tmp, 28);
            usFlags = BitConverter.ToUInt16(tmp, 30);
            usSetID = BitConverter.ToUInt16(tmp, 32);
            usCabIndex = BitConverter.ToUInt16(tmp, 34);

            if ((usFlags & 4) == 4)
            {
                usCabinetReservedBytes = BitConverter.ToUInt16(tmp, 36);
                bFolderReservedBytes = tmp[38];
                bBlockReservedBytes = tmp[39];
            }

            return 0;
        }

        // Represent the CFHEADER structure as a string 
        override public string ToString()
        {
            string s = string.Format("CFHEADER uiCabSize={0}, uiFilesOffset={1:X8}, usNumFolders={2}, usNumFiles={3}, usCabinetReservedBytes={4}, usFlags={5}", uiCabSize, uiFilesOffset, usNumFolders, usNumFiles, usCabinetReservedBytes, usFlags);
            if ((usFlags & 4) == 4)
                s += string.Format(", usCabinetReservedBytes={0}, bFolderReservedBytes={1}, bBlockReservedBytes={2}", usCabinetReservedBytes, bFolderReservedBytes, bBlockReservedBytes);
            return s;

        }

        // Returns the CFHEADER structure raw bytes 
        public byte[] ToBytes()
        {
            byte[] tmp = new byte[36];

            if (usCabinetReservedBytes > 0)
            {
                usFlags |= 4;
                tmp = new byte[40+usCabinetReservedBytes];
            }

            tmp[0] = (byte)'M'; tmp[1] = (byte)'S'; tmp[2] = (byte)'C'; tmp[3] = (byte)'F';


            BitConverter.GetBytes(uiCabSize).CopyTo(tmp, 8);
            BitConverter.GetBytes(uiFilesOffset).CopyTo(tmp, 16);
            BitConverter.GetBytes(cVerMinor).CopyTo(tmp, 24);
            BitConverter.GetBytes(cVerMajor).CopyTo(tmp, 25);
            BitConverter.GetBytes(usNumFolders).CopyTo(tmp, 26);
            BitConverter.GetBytes(usNumFiles).CopyTo(tmp, 28);
            BitConverter.GetBytes(usFlags).CopyTo(tmp, 30);
            BitConverter.GetBytes(usSetID).CopyTo(tmp, 32);
            BitConverter.GetBytes(usCabIndex).CopyTo(tmp, 34);
            if ((usFlags & 4) == 4)
            {
                BitConverter.GetBytes(usCabinetReservedBytes).CopyTo(tmp, 36);
                BitConverter.GetBytes(bFolderReservedBytes).CopyTo(tmp, 38);
                BitConverter.GetBytes(bBlockReservedBytes).CopyTo(tmp, 39);
            }
            return tmp;
        }

        public void ToStream(FileStream output)
        {
            byte[] tmp = ToBytes();
            output.Write(tmp, 0, tmp.Length);
        }

        public void AddFolder(ushort usCompression=0, byte bReserved = 0)
        {
            usNumFolders++;
            if (cfFolders == null)
                cfFolders = new List<CFFolder>();
            cfFolders.Add(new CFFolder(bReserved));
            cfFolders[cfFolders.Count - 1].SetCompression(usCompression);
        }

        public int AddFile(string PathName, List<string> Strip)
        {
            usNumFiles++;
            if (usNumFiles == 65535)
                throw new ApplicationException("FATAL: can't have more than 65535 files on a CAB unit!");
            //if (cfFolders[cfFolders.Count - 1].Files == 65535)
                //cfFolders.Add(new CFFOLDER());
            return cfFolders[cfFolders.Count - 1].AddFile(PathName, Strip);
        }

        public CFFile GetFile(string PathName)
        {
            foreach (CFFolder cfld in cfFolders)
            {
                CFFile cff = cfld.GetFile(PathName);

                if (cff != null)
                    return cff;
            }
            return null;
        }

        public List<CFFile> GetFiles(string wildcard=null)
        {
            List<CFFile> cffl = new List<CFFile>();

            foreach (CFFolder cfld in cfFolders)
            {
                cffl.AddRange(cfld.GetFiles(wildcard));
            }

            return cffl;
        }

        public void ExtractFiles(List<CFFile> FilesList, List<string> Strip, string Dest=null)
        {
            bool bOverwriteAll = Program.bOverwriteAll;

            foreach (CFFile cff in FilesList)
            {
                foreach (CFFolder cfld in cfFolders)
                {
                   if (cfld.GetFile(cff))
                    {
                        if (Dest == null)
                            Dest = "";

                        cff.SetItemName(cff.FileName, Strip);

                        string outPathName = Path.Combine(Dest, cff.ItemName);
                        FileStream output = null;

                        if (Program.bConfirmAll)
                        { 
                                int y = Console.CursorTop;

                                Console.Write("   -- Extract '{0}'? (Yes/No/All/Quit): ", outPathName);
                                ConsoleKeyInfo cki = Console.ReadKey();

                                Console.CursorLeft = 0;
                                Console.CursorTop = y;
                                Console.Write(new String(' ', 80));
                                Console.CursorLeft = 0;
                                Console.CursorTop = y;

                                switch (cki.KeyChar)
                                {
                                    case 'y':
                                        break;
                                    case 'n':
                                        continue;
                                    case 'a':
                                        Program.bConfirmAll = false;
                                        break;
                                    case 'q':
                                        Console.WriteLine("Operation interrupted by user");
                                        Environment.Exit(1);
                                        break;
                                }
                        }

                        try
                        {
                            // Ensures the eventual destination subfolders exist
                            string outDir = Path.GetDirectoryName(outPathName);
                            if (outDir != null)
                            {
                                DirectoryInfo di = new DirectoryInfo(outDir);
                                if (!di.Exists)
                                    di.Create();
                            }

                            // Check if it has to overwrite files
                            if (new FileInfo(outPathName).Exists && !bOverwriteAll)
                            {
                                int y = Console.CursorTop;

                                Console.Write("   -- File '{0}' already exists; overwrite? (Yes/No/All/Quit): ", outPathName);
                                ConsoleKeyInfo cki = Console.ReadKey();

                                Console.CursorLeft = 0;
                                Console.CursorTop = y;
                                Console.Write(new String(' ', 80));
                                Console.CursorLeft = 0;
                                Console.CursorTop = y;

                                switch (cki.KeyChar)
                                {
                                    case 'y':
                                        break;
                                    case 'n':
                                        continue;
                                    case 'a':
                                        bOverwriteAll = true;
                                        break;
                                    case 'q':
                                        Console.WriteLine("Operation interrupted by user");
                                        Environment.Exit(1);
                                        break;
                                }
                            }
                            
                            output = new FileStream(outPathName, FileMode.Create);
                        }
                        catch(DirectoryNotFoundException)
                        {
                            Console.WriteLine("   -- Error opening '{0}' for output", outPathName);
                            Environment.Exit(1);
                        }

                        Console.WriteLine("   extracting: {0}", outPathName);
                        cfld.Copy(CabFile, output, cff.uiFolderOffset, cff.uiFileSize);
                        output.Close();

                        FileInfo fi = new FileInfo(outPathName);
                        fi.LastWriteTime = cff.GetDateTime();
                       // Is the next right?
                        fi.Attributes = (FileAttributes) cff.usDOSAttributes;
                    }
                }
            }
        }

        public int WriteFolders(FileStream output)
        {
            foreach (CFFolder cf in cfFolders)
            {
                cf.ToStream(output);
            }
            return 0;
        }

        public ushort WriteFoldersData(FileStream output)
        {
            ushort numFolders = 0;

            foreach (CFFolder cf in cfFolders)
            {
                cf.WriteFolder(output);
                numFolders++;
            }
            return numFolders;
        }

        public ushort WriteFiles(FileStream output)
        {
            ushort numFiles = 0;
            
            foreach (CFFolder cf in cfFolders)
            {
                numFiles += (ushort) cf.WriteFiles(output);
            }

            return numFiles;
        }

        public void Load(FileStream srcCab)
        {
            CabFile = srcCab;

            cfFolders = new List<CFFolder>();

            for (int i = 0; i < usNumFolders; i++)
            { 
                cfFolders.Add(new CFFolder(CabFile, Length));
            }

            CabFile.Seek(uiFilesOffset, SeekOrigin.Begin);

            for (uint i = 0, lastFolder = 0, lastOffset = 0; i < usNumFiles; i++)
            {
                CFFile cff = new CFFile(CabFile);
                if (cff.uiFolderOffset < lastOffset)
                    lastFolder++;
                lastOffset = cff.uiFolderOffset;
                cfFolders[(int)lastFolder].AddFile(cff);
            }
        }

        public int Length
        {
            get {
                int z = 0;
                if ((usFlags & 4 ) == 4)
                    z = 4;
                return 36 + z + usCabinetReservedBytes;
            }
        }


        // MSCF signature
        public byte[] abSignature = {(byte)'M', (byte)'S', (byte)'C', (byte)'F'};
        // Size of this CAB file
        public uint uiCabSize;
        // Offset of first CFFILE
        public uint uiFilesOffset;
        // Minor version number (actually, 3)
        public byte cVerMinor = 3;
        // Major version number (actually, 1)
        public byte cVerMajor = 1;
        // Number of CFFOLDER items in this CAB
        public ushort usNumFolders;
        // Number of CFFILE items in this CAB
        public ushort usNumFiles;
        // Flags. 1=has previous CAB, 2=has next CAB, 4=optional fields present
        public ushort usFlags;
        // Common identifier for all CAB, if part of a spanned CAB set
        public ushort usSetID;
        // Index of this CAB, if part of a spanned CAB set
        public ushort usCabIndex;

        // Optional fields (if usFlags & 4)
        // Size in bytes of per-CAB reserved area (max 60.000)
        public ushort usCabinetReservedBytes;
        // Size in bytes of per-CFFOLDER reserved area (max 255)
        public byte bFolderReservedBytes;
        // Size in bytes of per-CFDATA reserved area (max 255)
        public byte bBlockReservedBytes;
        // Raw contents of CAB reserved area
        public byte[] abReserved;
        // Following are NUL terminated ASCII strings
        // Name of previous CAB file
        public byte[] szPrevCabinet; // TODO: convert into string all these!
        // Name of previous disk
        public byte[] szPrevDisk;
        // Name of next CAB file
        public byte[] szNextCabinet;
        // Name of next disk
        public byte[] szNextDisk;

        List<CFFolder> cfFolders;
        FileStream CabFile;
    }
}
