using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CabArk
{
    class CabReader
    {
        public CabReader(string PathName)
        {
            try
            {
                Cab = new FileStream(PathName, FileMode.Open, FileAccess.Read);
            }
            catch
            {
                Console.WriteLine("Unable to open '{0}' for input", PathName);
                Environment.Exit(1);
            }
            cfHeader = new CFHeader(Cab);
            cfHeader.Load(Cab);
        }

        public void SetTargetFolder(string PathName)
        {
            TargetFolder = PathName;
        }

        static int CFFile_Compare(CFFile a, CFFile b)
        {
            return a.uiFolderOffset.CompareTo(b.uiFolderOffset);
        }

        public int Extract(string FileName)
        {
            noItemsPushed = false;
            FilesToExtract.AddRange(cfHeader.GetFiles(FileName));
            FilesToExtract.Sort(CFFile_Compare);
            return FilesToExtract.Count;
        }

        public void PerformExtraction()
        {
            if (FilesToExtract.Count == 0 && noItemsPushed)
                FilesToExtract = cfHeader.GetFiles();

            cfHeader.ExtractFiles(FilesToExtract, toStrip, TargetFolder);
        }

        
        public List<string> toStrip;

        // CAB unit header
        CFHeader cfHeader;
        // File object where to retrieve this CAB unit
        FileStream Cab;
        List<CFFile> FilesToExtract = new List<CFFile>();
        string TargetFolder = "";
        bool noItemsPushed = true;
    }
}
