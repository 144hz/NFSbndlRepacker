using System;
using System.IO;

internal static class Program
{
	private static void Main(string[] args)
	{
        if(args.Length == 0) {
            Console.WriteLine("Drag BNDL file or the unpacked folder into the exe");
            return;
        }
        string path = args[0];
        BNDLRepacker p = new();
        if (path.EndsWith(".BNDL")) {
            if (!File.Exists(path)) {
                Console.WriteLine("BNDL not exist");
            }
            else {
                p.UnpackBNDL(path);
            }
        }
        else {
            if(!Directory.Exists(path)) {
                Console.WriteLine("Folder not exist");
            }
            else {
                p.RepackBNDL(path);
            }
        }
        Console.ReadKey();
    }
}
