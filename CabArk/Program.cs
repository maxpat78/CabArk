using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CabArk
{
    class Program
    {
        public static bool bRecurse = false;
        public static bool bOverwriteAll = false;
        public static bool bConfirmAll = false;
        public static ushort usCabinetID = 0;
        public static ushort usReservedCabinetSize = 0;

        static void Main(string[] args)
        {
            List<string> toStrip = new List<string>();
            toStrip.Add("*");

            ushort compressionType = 1;

            int lastOpt = -1;

            PrintBanner();

            // Parse opts
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i][0] != '-')
                {
                    lastOpt = i - 1;
                    break;
                }

                if (args[i].Equals("-?"))
                {
                    Help();
                    return;
                }

                if (args[i].Equals("-r"))
                {
                    bRecurse = true;
                }

                if (args[i].Equals("-i"))
                {
                    usCabinetID = Convert.ToUInt16(args[++i]);
                }

                if (args[i].Equals("-s"))
                {
                    usReservedCabinetSize = Convert.ToUInt16(args[++i]);
                }

                if (args[i].Equals("-c"))
                {
                    bConfirmAll = true;
                }

                if (args[i].Equals("-o"))
                {
                    bOverwriteAll = true;
                }

                if (args[i].Equals("-p"))
                {
                    if (toStrip.Count > 0)
                        toStrip.RemoveAt(0);
                }

                if (args[i].Equals("-P"))
                {
                    toStrip.Add(args[++i]);
                }

                if (args[i].Equals("-m")) // Default: MS-ZIP
                {
                    string s = args[++i].ToLower();
                    if (s.StartsWith("none"))
                        compressionType = 0;
                    if (s.StartsWith("lzx:"))
                    {
                        UInt16 dict = Convert.ToUInt16(s.Substring(4));
                        if (dict < 15 || dict > 21)
                        {
                            Console.WriteLine("Bad dictionary size for LZX: it must be in range 15..21!");
                            return;
                        }
                        compressionType = (ushort)((dict << 8) | 3);
                    }
                    if (s.StartsWith("mszip"))
                    {
                        compressionType = 1;
                    }
                }
            }
	    
            if (lastOpt+1 == args.Length)
            {
                Console.WriteLine("You must specify a command");
	            return;
            }

            // Parse args
            for (int i = lastOpt+1; i < args.Length; i++)
            {
                if (args[i].ToLower().Equals("d"))
                {
                    DumpCAB(args[i + 1]);
                }

                if (args[i].ToLower().Equals("l"))
                {
                    ListCAB(args[i + 1]);
                }

                if (args[i].ToLower().Equals("n"))
                {
                    CabWriter cab = new CabWriter(args[i + 1], compressionType);
                    for (int j = i + 2; j < args.Length; j++)
                    {
                        if ((new FileInfo(args[j]).Attributes & FileAttributes.Directory) == FileAttributes.Directory)
                            cab.AddFolder(args[j], bRecurse, toStrip, "*");
                        else
                            cab.AddFile(args[j], toStrip);
                    }
                    cab.Flush();
                }

                // FIXME: in real Cabarc, we must always specify a BASE file name, even if paths are preserved!
                if (args[i].ToLower().Equals("x"))
                {
                    CabReader cab = new CabReader(args[i + 1]);
                    cab.toStrip = toStrip;

                    Console.WriteLine("Extracting file(s) from cabinet '{0}'", args[i + 1]);

                    for (int j = i + 2; j < args.Length - 1; j++)
                    {
                        cab.Extract(args[j]);
                    }
                    if (args[args.Length - 1].EndsWith("\\"))
                        cab.SetTargetFolder(args[args.Length - 1]);
                    else
                        cab.Extract(args[args.Length - 1]);
                    cab.PerformExtraction();
                }
            }

            Console.WriteLine("\nOperation successful");
        }

	    // List CAB contents, MS way
        static void ListCAB(string name)
        {
            FileStream fs = null;

            try
            {
                fs = new FileStream(name, FileMode.Open, FileAccess.Read);
            }
            catch
            {
                Console.WriteLine("Unable to open '{0}' for input", name);
                Environment.Exit(1);
            }
            
            CFHeader cfh = new CFHeader(fs);

            Console.WriteLine("Listing of cabinet file '{0}' (size {1})", name, fs.Length);
            Console.WriteLine("   {0} file(s), {1} folder(s), set ID {2}, cabinet #{3}\n", cfh.usNumFiles, cfh.usNumFolders, cfh.usSetID, cfh.usCabIndex);

            Console.WriteLine("File name                      File size    Date       Time   Attrs");
            Console.WriteLine("-----------------------------  ---------- ---------- -------- -----");

            fs.Seek(cfh.uiFilesOffset, SeekOrigin.Begin);
            for (int i = 0; i < cfh.usNumFiles; i++)
            {
                CFFile cff = new CFFile(fs);
                Console.WriteLine("   {0,-29} {1,8} {2}  {3}", cff.FileName, cff.uiFileSize, cff.GetDateTime(), cff.GetDosAttributes());
            }

            fs.Close();
        }

	    // Show detailed information about CAB structures, for debug purposes
        static void DumpCAB(string name)
        {
            FileStream fs = new FileStream(name, FileMode.Open, FileAccess.Read);

            CFHeader cfh = new CFHeader(fs);

            Console.WriteLine("Cabinet Header");
            Console.WriteLine(cfh);

            fs.Seek(cfh.uiFilesOffset, SeekOrigin.Begin);
            for (int i = 0; i < cfh.usNumFiles; i++)
            {
                Console.Write("@{0:X8}h ", fs.Position);
                CFFile cff = new CFFile(fs);
                Console.WriteLine(cff);
            }

            fs.Seek(cfh.Length, SeekOrigin.Begin);

            Console.WriteLine("Cabinet Folders");

            for (int i = 0; i < cfh.usNumFolders; i++)
            {
                Console.Write("@{0:X8}h ", fs.Position);
                CFFolder cffld = new CFFolder(fs);
                Console.WriteLine(cffld);

                fs.Seek(cffld.uiDataOffset, SeekOrigin.Begin);

                Console.WriteLine("{0} CFDATA blocks", cffld.usBlocks);
                for (int j = 0; j < cffld.usBlocks; j++)
                {
                    Console.Write("@{0:X8}h ", fs.Position);
                    CFData cfd = new CFData(fs, bLoadData:true);
                    Console.WriteLine(cfd);
                }
            }

            fs.Close();
        }

        static void PrintBanner()
        {
            Console.WriteLine("\nA Cabinet tool - Version 0.1");
            Console.WriteLine("Copyright (C)2004-2012, by maxpat78. GNU GPL v2 applies.\nThis free software manages MS Cabinets WITH ABSOLUTELY NO WARRANTY!\n");
        }

        static void Help()
        {
            Console.WriteLine("Usage: CABARK [options] command cabfile [@list] [files] [dest_dir]\n");
            Console.WriteLine("Commands:");
            Console.WriteLine("   L   List contents of cabinet (e.g. cabarc l test.cab)");
            Console.WriteLine("   N   Create new cabinet (e.g. cabarc n test.cab *.c *.h)");
            Console.WriteLine("   X   Extract file(s) from cabinet (e.g. cabarc x test.cab bar*.c)");
            Console.WriteLine("\nOptions:");
            Console.WriteLine("  -c   Confirm files to be operated on");
            Console.WriteLine("  -o   When extracting, overwrite without asking for confirmation");
            Console.WriteLine("  -m   Set compression type [LZX:<15..21>|MSZIP|NONE], default is MSZIP");
            Console.WriteLine("  -p   Preserve path names (absolute paths not allowed)");
            Console.WriteLine("  -P   Strip specified prefix from files when added");
            Console.WriteLine("  -r   Recurse into subdirectories when adding files (see -p also)");
            Console.WriteLine("  -s   Reserve space in cabinet for signing");
            Console.WriteLine("  -i   Set cabinet set ID when creating cabinets (default is 0)");
            Console.WriteLine("  -d   Set diskette size (default is no limit/single CAB)");
        }
    }
}
