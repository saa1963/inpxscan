using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace inpxscan
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("Неверное число параметров. inpxscan <.inpx file>");
                Console.ReadKey();
                return;
            }
            using (var zipArchive = ZipFile.OpenRead(args[0]))
            {
                foreach (var entry in zipArchive.Entries)
                {
                    using (var stream = entry.Open())
                    {
                        using (var sr = new StreamReader(stream, Encoding.UTF8))
                        {
                            string s;
                            while ((s = sr.ReadLine()) != null)
                            {
                                var mas = s.Split('\x04');
                                var author = mas[0];
                                var genre = mas[1];
                                var title = mas[2];
                                var series = mas[3];
                                var serno = mas[4];
                                var file = mas[5];
                                var size = mas[6];
                                var libid = mas[7];
                                var del = mas[8];
                                var ext = mas[9];
                                var date = mas[10];
                                var lang = mas[11];
                                var keywords = mas[12];
                            }
                        }
                    }
                }
            }
            Console.ReadKey();
        }
    }
}
