using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.IO.Compression;

class BNDLRepacker {

    private const int header1_size=9;
    private const int header2_size = 18;

    private Dictionary<uint, string> type2id = new Dictionary<uint, string> {
        { 1,"Texture"},
        { 2,"Material"},
        { 5,"Model"},
        { 21,"Script-"},
        { 81,"LODGroup"},
        { 96,"UnusedColsn"},
        { 128,"RPMSound" },
        { 129,"EffectSound" },
        { 176,"SkeletonDefm" },
        { 178,"Skeleton" },
        { 179,"Animation" },
        { 262,"Synthesis" },
        { 1281,"SpoilerAnim" }
    };

    private byte[] bndl_byte;

    private string root_path;

    private uint car_id;

    private byte[] ZlibCompress(byte[] input) {
        byte[] adler32(byte[] data) {
            uint s1 = 1, s2 = 0;
            foreach(byte b in data) {
                s1 = (s1 + b) % 65521;
                s2 = (s2 + s1) % 65521;
            }
            return BitConverter.GetBytes(s2 * 0x10000 + s1).Reverse().ToArray();
        }
        using MemoryStream uncompressed_stream = new(input);
        using MemoryStream compressed_stream = new();
        using DeflateStream compressor = new(compressed_stream, CompressionLevel.Optimal); 
        uncompressed_stream.CopyTo(compressor);
        compressor.Close();
        var result = new byte[] { 0x78, 0xDA }.Concat(compressed_stream.ToArray()).Concat(adler32(input));
        return result.ToArray();
    }

    private byte[] ZlibUncompress(byte[] input) {
        using MemoryStream compressed_stream = new(input);
        compressed_stream.Position = 2;
        using MemoryStream uncompressed_stream = new();
        using DeflateStream deflateStream = new(compressed_stream, CompressionMode.Decompress);
        deflateStream.CopyTo(uncompressed_stream);
        return uncompressed_stream.ToArray();
    }

    private void ExtractFile(string detail_path, string name_no_ex, bool unzip, long index, long length) {
        string output_path = root_path + detail_path;
        if(!Directory.Exists(output_path)) Directory.CreateDirectory(output_path);
        if (detail_path != "") output_path += "\\";
        byte[] file = new byte[length];
        Array.Copy(bndl_byte, index, file, 0, length);
        if (unzip) file = ZlibUncompress(file);
        File.WriteAllBytes(output_path + name_no_ex + ".dat", file);
    }

    public void UnpackBNDL(string input_path) {
        bndl_byte = File.ReadAllBytes(input_path);
        root_path = Path.GetDirectoryName(input_path) + "\\" + Path.GetFileNameWithoutExtension(input_path) + "\\";
        if (Directory.Exists(root_path + "raw")) Directory.Delete(root_path + "raw", true);
        //读取BNDL文件头
        MemoryStream ms = new(bndl_byte,false);
        BinaryReader br = new(ms);
        uint[] header1 = new uint[header1_size];
        for(int i = 0; i < header1_size; i++) {
            header1[i] = br.ReadUInt32();
        }
        ExtractFile("raw", "header", false, 0, header1[4]);
        //获取每个文件的信息并解压
        br.BaseStream.Position = header1[4];
        int flg_count = 0;
        int type20_count = 0;
        int type21_carid_count = 0;
        for (int i = 0; i < header1[3]; i++) {
            uint[] header2 = new uint[header2_size];
            for (int j = 0; j < header2_size; j++) {
                header2[j] = (j == 2 || j == 3) ? br.ReadUInt32() % 0x10000000 : br.ReadUInt32();
            }
            if (header2[15] == 262) car_id = header2[0];
            //文件分区id=B3FE48A
            if (header2[0] == 0xB3FE48A) flg_count++;
            type2id.TryGetValue(header2[15], out string type);
            string detail_path = "raw\\" + (flg_count < 1 ? 1 : flg_count) + "_block\\" + header2[15] + "_" + type;
            string file_name;
            if (header2[15] == 20) {
                type20_count++;
                file_name = type20_count.ToString();
            }
            else if (header2[15] == 21 && header2[0] == car_id) {
                type21_carid_count++;
                file_name = type21_carid_count.ToString();
            }
            else {
                file_name = header2[0].ToString();
            }
            //压缩信息
            ExtractFile(detail_path, file_name + "_a", false, header1[4] + header2_size * 4 * i, header2_size * 4);
            //短文件体
            if (header2[6] > 0) {
                ExtractFile(detail_path, file_name + "_b", true, header1[5] + header2[10], header2[6]);
            }
            //长文件体
            if (header2[7] > 0) {
                ExtractFile(detail_path, file_name + "_c", true, header1[6] + header2[11], header2[7]);
            }
        }
        br.Dispose();
        ms.Dispose();
        Console.WriteLine("unpack done");
    }

    private uint[] GetFileList(string path, bool file_dic) {
        string[] files = file_dic ? Directory.GetFiles(path) : Directory.GetDirectories(path);
        uint[] files_int = new uint[files.Length];
        for (int i = 0; i < files.Length; i++) {
            string fname = Path.GetFileNameWithoutExtension(files[i]);
            uint file_int = 0;
            try {
                if (file_dic) file_int = fname.Split('_')[1] == "a" ? Convert.ToUInt32(fname.Split('_')[0]) : 0;
                else file_int = Convert.ToUInt32(fname.Split('_')[0]);
                files_int[i] = Array.IndexOf(files_int, file_int) < 0 ? file_int : 0;
            }
            catch (Exception) { }
        }
        Array.Sort(files_int);
        return files_int;
    }

    private void LoadDataSetHeader(MemoryStream ms, ref List<byte> rebuild_data, string path, bool bool_type) {
        byte[] data = File.ReadAllBytes(path);
        byte[] data2 = ZlibCompress(data);
        //内容起始位置
        ms.Position = bool_type ? 10 * 4 : 11 * 4;
        ms.Write(BitConverter.GetBytes(rebuild_data.Count), 0, 4);
        //压缩内容体积
        ms.Position = bool_type ? 6 * 4 : 7 * 4;
        ms.Write(BitConverter.GetBytes(data2.Length), 0, 4);
        //原始内容体积
        ms.Position = bool_type ? 2 * 4 : 3 * 4;
        ms.Write(BitConverter.GetBytes(data.Length + 0x40000000), 0, 4);
        rebuild_data.AddRange(data2);
    }

    public void RepackBNDL(string input_path) {
        root_path = input_path + "\\";
        int file_count = 0;
        //三组重建文件
        List<byte> rebuild_a = new();
        List<byte> rebuild_b = new();
        List<byte> rebuild_c = new();
        //获取分块文件夹
        uint[] folders_block = GetFileList(root_path + "raw", false);
        foreach (uint folder_block in folders_block) {
            if (folder_block == 0) continue;
            string block_folder = root_path + "raw\\" + folder_block + "_block\\";
            //获取分类文件夹
            uint[] folders_type = GetFileList(block_folder, false);
            //依次读取文件夹内文件
            foreach (uint folder_type in folders_type) {
                if (folder_type == 0) continue;
                type2id.TryGetValue(folder_type, out string type);
                string data_folder = block_folder + folder_type + "_" + type + "\\";
                uint[] files_uint = GetFileList(data_folder, true);
                foreach (uint file_uint in files_uint) {
                    if (file_uint == 0) continue;
                    file_count++;
                    //读取压缩信息
                    byte[] f_a = File.ReadAllBytes(data_folder + file_uint + "_a.dat");
                    MemoryStream ms = new MemoryStream(f_a);
                    //读取短文件体
                    if (File.Exists(data_folder + file_uint + "_b.dat"))
                        LoadDataSetHeader(ms, ref rebuild_b, data_folder + file_uint + "_b.dat", true);
                    //读取长文件体
                    if (File.Exists(data_folder + file_uint + "_c.dat"))
                        LoadDataSetHeader(ms, ref rebuild_c, data_folder + file_uint + "_c.dat", false);
                    ms.Dispose();
                    rebuild_a.AddRange(f_a);
                }
            }
        }
        //写入BNDL体积数据
        byte[] bndl_header = File.ReadAllBytes(root_path + "raw\\header.dat");
        MemoryStream ms2 = new(bndl_header);
        ms2.Position = 3 * 4;
        ms2.Write(BitConverter.GetBytes(file_count), 0, 4);
        int size = bndl_header.Length;
        ms2.Position = 4 * 4;
        ms2.Write(BitConverter.GetBytes(size), 0, 4);
        size += rebuild_a.Count;
        ms2.Position = 5 * 4;
        ms2.Write(BitConverter.GetBytes(size), 0, 4);
        size += rebuild_b.Count;
        ms2.Position = 6 * 4;
        ms2.Write(BitConverter.GetBytes(size), 0, 4);
        size += rebuild_c.Count;
        ms2.Position = 2 * 4;
        ms2.Write(BitConverter.GetBytes(size), 0, 4);
        ms2.Position = 7 * 4;
        ms2.Write(BitConverter.GetBytes(size), 0, 4);
        ms2.Position = 8 * 4;
        ms2.Write(BitConverter.GetBytes(size), 0, 4);
        ms2.Dispose();
        //输出文件
        List<byte> rebuild_file = new(bndl_header);
        rebuild_file.AddRange(rebuild_a);
        rebuild_file.AddRange(rebuild_b);
        rebuild_file.AddRange(rebuild_c);
        string output_path = root_path + "build\\";
        Directory.CreateDirectory(output_path);
        File.WriteAllBytes(output_path + Path.GetFileName(input_path) + ".BNDL", rebuild_file.ToArray());
        Console.WriteLine("repack done");
    }
}
