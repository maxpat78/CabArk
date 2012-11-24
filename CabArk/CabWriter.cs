using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CabArk
{
    // Class to create a single CAB unit
    class CabWriter
    {
        public CabWriter(string PathName, ushort Compression = 0)
        {
            cfHeader = new CFHeader();
            cfHeader.AddFolder(Compression);

            cfHeader.usSetID = Program.usCabinetID;
            cfHeader.usCabinetReservedBytes = Program.usReservedCabinetSize;

            try
            {
                Cab = new FileStream(PathName, FileMode.Create);
            }
            catch
            {
                Console.WriteLine("Couldn't create cabinet file");
                Environment.Exit(1);
            }
        }

        public int AddFile(string PathName, List<string> Strip)
        {
            return cfHeader.AddFile(PathName, Strip);
        }

        public int AddFolder(string PathName, bool Recursive = false)
        {
            if (Path.IsPathRooted(PathName))
                throw new ApplicationException("Can't specify an absolute pathname!");
            return _AddFolder(PathName, null, Recursive);
        }

        public int AddFolder(string PathName, bool Recursive, List<string> Strip, string Pattern = "*")
        {
            return _AddFolder(PathName, Strip, Recursive, Pattern);
        }

        int _AddFolder(string PathName, List<string> Strip, bool Recursive = false, string Pattern = "*")
        {
            DirectoryInfo di = new DirectoryInfo(PathName);

            if (!di.Exists)
            {
                Console.WriteLine("Can't open {0}", PathName);
                Environment.Exit(1);
            }

            foreach (FileInfo fi in di.EnumerateFiles(Pattern, Recursive? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly))
                cfHeader.AddFile(fi.FullName, Strip);
            return cfHeader.usNumFiles;
        }

        public int Flush()
        {
            // Writes CFHEADER
            cfHeader.ToStream(Cab);
            // Writes CFFOLDER entries
            cfHeader.WriteFolders(Cab);
            cfHeader.uiFilesOffset = (uint)Cab.Position;
            // Writes CFFILE entries
            cfHeader.usNumFiles = cfHeader.WriteFiles(Cab);
            // Writes CFDATA blocks
            cfHeader.usNumFolders = cfHeader.WriteFoldersData(Cab);

            // Writes updated CFHEADER
            cfHeader.uiCabSize = (uint)Cab.Position;
            Cab.Position = 0;
            cfHeader.ToStream(Cab);

            Cab.Close();
            
            return 0;
        }

        // CAB unit header
        CFHeader cfHeader;
        // File object where to store this CAB unit
        FileStream Cab;
    }
}
