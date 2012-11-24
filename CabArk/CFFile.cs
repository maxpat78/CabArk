using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CabArk
{
    class CFFile
    {
        public CFFile()
        {
        }

        public CFFile(byte[] source)
        {
            FromBytes(source);
        }

        public CFFile(FileStream src, long pos = -1)
        {
            if (pos > -1)
                src.Seek(pos, SeekOrigin.Begin);
            FromStream(src);
        }

        // Reads and decodes a CFFOLDER from source stream. Return 0 (OK), 1 (BAD)
        public int FromStream(FileStream source)
        {
            byte[] tmp = new byte[17];
            source.Read(tmp, 0, 16);
            FromFixedBytes(tmp);

            // Load filename (max 255 bytes)
            tmp[16] = (byte) ' ';
            int i = 0;
            while (tmp[16] != 0 && i++ < 255)
            {
                source.Read(tmp, 16, 1);
                if (tmp[16] == 0) break;
                FileName += (char) tmp[16];
            }

            return 0;
        }

        void FromFixedBytes(byte[] input)
        {
            byte[] tmp = input;
            uiFileSize = BitConverter.ToUInt32(tmp, 0);
            uiFolderOffset = BitConverter.ToUInt32(tmp, 4);
            usFolderSpan = BitConverter.ToUInt16(tmp, 8);
            usDOSDate = BitConverter.ToUInt16(tmp, 10);
            usDOSTime = BitConverter.ToUInt16(tmp, 12);
            usDOSAttributes = BitConverter.ToUInt16(tmp, 14);
        }

        public void FromBytes(byte[] input)
        {
            byte[] tmp = input;
            FromFixedBytes(tmp);

            // Load filename (max 255 bytes)
            int i = 0;
            while (tmp[16+i] != 0 && i++ < 255)
            {
                if (tmp[16+i] == 0) break;
                FileName += (char)tmp[16+i];
            }
        }

        public byte[] ToBytes()
        {
            byte[] tmp = new byte[Length];
            BitConverter.GetBytes(uiFileSize).CopyTo(tmp, 0);
            BitConverter.GetBytes(uiFolderOffset).CopyTo(tmp, 4);
            BitConverter.GetBytes(usFolderSpan).CopyTo(tmp, 8);
            BitConverter.GetBytes(usDOSDate).CopyTo(tmp, 10);
            BitConverter.GetBytes(usDOSTime).CopyTo(tmp, 12);
            BitConverter.GetBytes(usDOSAttributes).CopyTo(tmp, 14);
            Array.Copy(Encoding.ASCII.GetBytes(ItemName), 0, tmp, 16, ItemName.Length);
            return tmp;
        }

        public void ToStream(FileStream dest)
        {
            byte[] tmp = ToBytes();
            dest.Write(tmp, 0, tmp.Length);
        }

        // Represent the CFFILE structure as a string 
        override public string ToString()
        {
            return string.Format("CFFILE={0}, uiFileSize={1}, uiFolderOffset={2}, usFolderSpan={3}", FileName, uiFileSize, uiFolderOffset, usFolderSpan);
        }

        public void SetItemName(string PathName, List<string> Strip)
        {
            FileName = PathName;

            if (Strip.Count == 0)
                ItemName = FileName;
            else if (Strip[0] == "*")
                ItemName = Path.GetFileName(FileName);
            else
                ItemName = FileName;

            foreach(string s in Strip)
            {
                if (ItemName.Contains(s))
                    ItemName = ItemName.Remove(ItemName.IndexOf(s), s.Length);
            }

            if (Path.IsPathRooted(ItemName))
            {
                ItemName = ItemName.Substring(Path.GetPathRoot(ItemName).Length);
            }

            if (ItemName.Length > 255)
                throw new ApplicationException(string.Format("FATAL: item name '{0}' is longer than 255 bytes!", ItemName));
        }

        public uint DateTimeToUInt32(DateTime dt)
        {
            ushort dDate = (ushort)((dt.Year - 1980) << 9 | dt.Month << 5 | dt.Day);
            ushort dTime = (ushort)(dt.Hour << 11 | dt.Minute << 5 | dt.Second >> 1);
            return (uint)(dDate << 16 | dTime);
        }

        public DateTime GetDateTime()
        {
            return new DateTime(1980 + (usDOSDate >> 9), (usDOSDate >> 5) & 0xF, usDOSDate & 0x1F, (usDOSTime >> 11), (usDOSTime >> 5) & 0x3F, (usDOSTime & 0x1F) << 1);
        }

        public string GetDosAttributes()
        {
            string s = "";
            if ((usDOSAttributes & 0x01) == 1)
                s += "r";
            else
                s += "-";
            if ((usDOSAttributes & 0x20) == 0x20)
                s += "a";
            else
                s += "-";
            if ((usDOSAttributes & 0x04) == 0x04)
                s += "s";
            else
                s += "-";
            if ((usDOSAttributes & 0x02) == 0x02)
                s += "h";
            else
                s += "-";

            return s;
        }

        public int Length
        {
            get { return 16 + ItemName.Length + 1; }
        }

        // File size
        public uint uiFileSize;
        // Offset of this file relative to the uncompressed folder stream
        public uint uiFolderOffset;
        // 0xFFFD=is continuation of a previous folder, 0xFFFE=continues, 0xFFFF=has prev&next
        public ushort usFolderSpan;
        // Date, Time and File Attributes in MS-DOS format
        public ushort usDOSDate;
        public ushort usDOSTime;
        public ushort usDOSAttributes;
        // Filename ASCIIZ (UTF-8 if flag in CFHEADER), max 255 bytes
        public string ItemName;
        // Source pathname from which to load the file
        public string FileName;
    }
}
