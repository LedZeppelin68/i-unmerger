using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Security.Cryptography;
using System.Xml;

namespace i_unmerger
{
    class Program
    {
        static byte[] sync = { 0x00, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0x00 };

        static void Main(string[] args)
        {
            foreach (string arg in args)
            {
                string control_xml_path = arg;

                string working_dir = Path.GetDirectoryName(control_xml_path);

                XmlDocument control_xml = new XmlDocument() { XmlResolver = null };
                control_xml.Load(control_xml_path);
                //control_xml.Load(Path.Combine(working_dir, "control.xml"));

                XmlNode form1_part = control_xml.DocumentElement.SelectSingleNode("partition/form1");
                XmlNode form2_part = control_xml.DocumentElement.SelectSingleNode("partition/form2");
                XmlNode cdda_part = control_xml.DocumentElement.SelectSingleNode("partition/cdda");
                XmlNode map_part = control_xml.DocumentElement.SelectSingleNode("partition/map");

                BinaryReader form1 = null;
                BinaryReader form2 = null;
                BinaryReader cdda = null;
                BinaryReader map = null;

                if (form1_part != null)
                {
                    string part_name = form1_part.Attributes["name"].Value;
                    form1 = new BinaryReader(new FileStream(Path.Combine(working_dir, part_name), FileMode.Open));
                }

                if (form2_part != null)
                {
                    string part_name = form2_part.Attributes["name"].Value;
                    form2 = new BinaryReader(new FileStream(Path.Combine(working_dir, part_name), FileMode.Open));
                }

                if (cdda_part != null)
                {
                    string part_name = cdda_part.Attributes["name"].Value;
                    cdda = new BinaryReader(new FileStream(Path.Combine(working_dir, part_name), FileMode.Open));
                }

                if (map_part != null)
                {
                    string part_name = map_part.Attributes["name"].Value;
                    map = new BinaryReader(new FileStream(Path.Combine(working_dir, part_name), FileMode.Open));
                }

                XmlNodeList games = control_xml.DocumentElement.SelectNodes("game");

                foreach (XmlNode game in games)
                {
                    string game_dir = game.Attributes["name"].Value;
                    game_dir = Path.Combine(working_dir, game_dir);

                    try { Directory.CreateDirectory(game_dir); } catch { };

                    XmlNodeList files = game.SelectNodes("rom");

                    foreach (XmlNode file in files)
                    {
                        string name = file.Attributes["name"].Value;
                        string file_md5 = file.Attributes["md5"].Value;
                        long size = Convert.ToInt64(file.Attributes["size"].Value);
                        long map_offset = Convert.ToInt64(file.Attributes["map"].Value);
                        string file_type = file.Attributes["type"].Value;

                        //BinaryWriter bw = new BinaryWriter(new FileStream(Path.Combine(working_dir, name), FileMode.Create));
                        BinaryWriter bw = new BinaryWriter(new FileStream(Path.Combine(game_dir, name), FileMode.Create));

                        MD5 file_hash = MD5.Create();

                        map.BaseStream.Position = map_offset;

                        switch (file_type)
                        {
                            case "file":
                                {
                                    long file_size = size;

                                    while (bw.BaseStream.Position != size)
                                    {
                                        uint block_offset = map.ReadUInt32();
                                        if (file_size > 2048)
                                        {
                                            if (block_offset == 0xffffffff)
                                            {
                                                bw.Write(new byte[2048]);
                                                file_hash.TransformBlock(new byte[2048], 0, 2048, null, 0);
                                            }
                                            else
                                            {
                                                form1.BaseStream.Seek(block_offset * 2048, SeekOrigin.Begin);
                                                byte[] temp_block = form1.ReadBytes(2048);
                                                bw.Write(temp_block);

                                                file_hash.TransformBlock(temp_block, 0, temp_block.Length, null, 0);
                                            }

                                            file_size -= 2048;
                                        }
                                        else
                                        {
                                            form1.BaseStream.Seek(block_offset * 2048, SeekOrigin.Begin);
                                            byte[] temp_block = form1.ReadBytes((int)file_size);
                                            bw.Write(temp_block);

                                            file_hash.TransformBlock(temp_block, 0, temp_block.Length, null, 0);
                                        }
                                    }
                                }
                                break;
                            case "2048":
                                while (bw.BaseStream.Position != size)
                                {
                                    uint block_offset = map.ReadUInt32();
                                    if (block_offset == 0xffffffff)
                                    {
                                        bw.Write(new byte[2048]);
                                        file_hash.TransformBlock(new byte[2048], 0, 2048, null, 0);
                                    }
                                    else
                                    {
                                        form1.BaseStream.Seek(block_offset * 2048, SeekOrigin.Begin);
                                        byte[] temp_block = form1.ReadBytes(2048);
                                        bw.Write(temp_block);

                                        file_hash.TransformBlock(temp_block, 0, temp_block.Length, null, 0);
                                    }
                                }
                                break;
                            case "2352":
                            case "pcm":
                                while (bw.BaseStream.Position != size)
                                {
                                    int mode_control = map.ReadByte();
                                    byte[] MSF = new byte[3];
                                    //byte[] subheader = map.ReadBytes(8);
                                    uint block_offset = 0;
                                    byte[] temp_block = new byte[2352];
                                    //int edc = mode & 0x10;
                                    //int ecc_form1 = mode & 0x20;

                                    int mode = mode_control & 3;
                                    switch (mode)
                                    {
                                        case 0:
                                            //long audio_size = size;

                                            switch (mode_control)
                                            {
                                                case 0x10:
                                                    int null_samples_count = map.ReadInt32();

                                                    for (int x = 0; x < 4; x++)
                                                    {
                                                        bw.Write(new byte[null_samples_count]);
                                                        //audio_size -= null_samples_count;
                                                        file_hash.TransformBlock(new byte[null_samples_count], 0, null_samples_count, null, 0);
                                                    }
                                                    break;
                                                case 0x20:
                                                    long last_audio_block_offset = map.ReadInt32() * 2352 + 44;
                                                    int last_audio_block_size = map.ReadInt32();
                                                    cdda.BaseStream.Seek(last_audio_block_offset, SeekOrigin.Begin);
                                                    byte[] last_audio_block = cdda.ReadBytes(last_audio_block_size);
                                                    file_hash.TransformBlock(last_audio_block, 0, last_audio_block_size, null, 0);
                                                    bw.Write(last_audio_block);
                                                    break;
                                                default:

                                                    long offset = map.ReadInt32() * 2352 + 44;
                                                    cdda.BaseStream.Seek(offset, SeekOrigin.Begin);
                                                    byte[] temp_audio_block = cdda.ReadBytes(2352);
                                                    file_hash.TransformBlock(temp_audio_block, 0, 2352, null, 0);
                                                    bw.Write(temp_audio_block);
                                                    break;
                                            }
                                            break;
                                        case 1:
                                            MSF = map.ReadBytes(3);
                                            sync.CopyTo(temp_block, 0);
                                            MSF.CopyTo(temp_block, 12);
                                            temp_block[15] = (byte)mode;

                                            block_offset = map.ReadUInt32();
                                            if (block_offset == 0xffffffff)
                                            {
                                                new byte[2048].CopyTo(temp_block, 16);
                                            }
                                            else
                                            {
                                                form1.BaseStream.Seek(block_offset * 2048, SeekOrigin.Begin);
                                                byte[] block2048 = form1.ReadBytes(2048);
                                                block2048.CopyTo(temp_block, 16);
                                            }

                                            CRC.calculate_edc(temp_block, mode);
                                            CRC.calculate_eccp(temp_block);
                                            CRC.calculate_eccq(temp_block);

                                            bw.Write(temp_block);
                                            file_hash.TransformBlock(temp_block, 0, temp_block.Length, null, 0);

                                            break;
                                        case 2:
                                            MSF = map.ReadBytes(3);
                                            byte[] subheader = map.ReadBytes(8);


                                            int edc = mode_control & 0x10;
                                            int ecc_form1 = mode_control & 0x20;


                                            int form = subheader[2] & 0x20;

                                            sync.CopyTo(temp_block, 0);
                                            subheader.CopyTo(temp_block, 16);

                                            switch (form)
                                            {
                                                default:
                                                    block_offset = map.ReadUInt32();
                                                    if (block_offset == 0xffffffff)
                                                    {
                                                        new byte[2048].CopyTo(temp_block, 24);
                                                    }
                                                    else
                                                    {
                                                        form1.BaseStream.Seek(block_offset * 2048, SeekOrigin.Begin);
                                                        byte[] block2048 = form1.ReadBytes(2048);
                                                        block2048.CopyTo(temp_block, 24);
                                                    }
                                                    break;
                                                case 0x20:
                                                    block_offset = map.ReadUInt32();
                                                    if (block_offset == 0xffffffff)
                                                    {
                                                        new byte[2324].CopyTo(temp_block, 24);
                                                    }
                                                    else
                                                    {
                                                        form2.BaseStream.Seek(block_offset * 2324, SeekOrigin.Begin);
                                                        byte[] block2324 = form2.ReadBytes(2324);
                                                        block2324.CopyTo(temp_block, 24);
                                                    }
                                                    break;
                                            }
                                            //EDC
                                            if (ecc_form1 == 0x20)
                                            {
                                                MSF.CopyTo(temp_block, 12);
                                                temp_block[15] = (byte)mode;
                                            }
                                            switch (form)
                                            {
                                                default:
                                                    CRC.calculate_edc(temp_block, mode);
                                                    CRC.calculate_eccp(temp_block);
                                                    CRC.calculate_eccq(temp_block);
                                                    break;
                                                case 0x20:
                                                    if (edc != 0x10)
                                                    {
                                                        CRC.calculate_edc(temp_block, mode);
                                                    }
                                                    break;
                                            }

                                            MSF.CopyTo(temp_block, 12);
                                            temp_block[15] = (byte)(mode);


                                            bw.Write(temp_block);
                                            file_hash.TransformBlock(temp_block, 0, temp_block.Length, null, 0);
                                            break;
                                    }
                                }
                                break;
                        }

                        file_hash.TransformFinalBlock(new byte[0], 0, 0);

                        string calculated_hash = BitConverter.ToString(file_hash.Hash).Replace("-", "").ToLower();

                        if (calculated_hash == file_md5) Console.WriteLine(string.Format("tested: ok: {0}", name)); else Console.WriteLine(string.Format("tested: error: {0}", name));
                    }

                }
                    if (form1_part != null) form1.Dispose();
                    if (form2_part != null) form2.Dispose();
                    if (map_part != null) map.Dispose();


                
            }
        }
    }
}
