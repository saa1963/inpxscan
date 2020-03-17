using SQLitePCL;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;

namespace inpxscan
{
    class Program
    {
        private static sqlite3 db;
        /// <summary>
        /// 
        /// </summary>
        /// <param name="args">
        /// args[0] - .inpx file
        /// args[1] - directory for hlc2 files
        /// </param>
        static void Main(string[] args)
        {
            int rc;
            if (args.Length == 0)
            {
                Console.WriteLine("Неверное число параметров. inpxscan <.inpx file>");
                Console.ReadKey();
                return;
            }
            string scriptText;
            using (var scriptStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("inpxscan.CreateCollectionDB_SQLite.sql"))
            {
                using (var sr = new StreamReader(scriptStream, Encoding.UTF8))
                {
                    scriptText = sr.ReadToEnd();
                }
            }

            string newbaseName = Path.Combine(args[1], "tinyopdscore_" + DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss-ff") + ".hlc2");
            raw.SetProvider(new SQLite3Provider_e_sqlite3());
            rc = raw.sqlite3_open(newbaseName, out db);
            if (rc != raw.SQLITE_OK)
            {
                throw new Exception(raw.sqlite3_errmsg(db).utf8_to_string());
            }
            raw.sqlite3_create_collation(db, "MHL_SYSTEM", null, mhl_system_collation);
            raw.sqlite3_create_collation(db, "MHL_SYSTEM_NOCASE", null, mhl_system_nocase_collation);
            raw.sqlite3_create_function(db, "MHL_UPPER", 1, null, mhl_upper);
            raw.sqlite3_create_function(db, "MHL_LOWER", 1, null, mhl_lower);
            rc = raw.sqlite3_exec(db, scriptText);
            if (rc != raw.SQLITE_OK)
            {
                throw new Exception(raw.sqlite3_errmsg(db).utf8_to_string());
            }

            InsertGenres();
            List<Books> lst = FillList(args[0]);
            
            raw.sqlite3_close_v2(db);

            Console.WriteLine("Data had been loaded");
            Console.ReadKey();
        }

        private static List<Books> FillList(string fname)
        {
            var rt = new List<Books>();
            using (var zipArchive = ZipFile.OpenRead(fname))
            {
                foreach (var entry in zipArchive.Entries)
                {
                    if (entry.Name.Contains(".inp"))
                    {
                        using (var stream = entry.Open())
                        {
                            using (var sr = new StreamReader(stream, Encoding.UTF8))
                            {
                                string s;
                                while ((s = sr.ReadLine()) != null)
                                {
                                    var o = new Books();
                                    var mas = s.Split('\x04');
                                    var authors = mas[0].Split(':');
                                    foreach (var a in authors)
                                    {
                                        if (!string.IsNullOrWhiteSpace(a))
                                        {
                                            var names = a.Split(',');
                                            var nameL = names.Length;
                                            var oa = new Author();
                                            if (nameL > 2)
                                            {
                                                oa.Middle = names[2] ?? "";
                                            }
                                            if (nameL > 1)
                                            {
                                                oa.First = names[1] ?? "";
                                            }
                                            if (nameL > 0)
                                            {
                                                oa.Last = names[0] ?? "";
                                            }
                                            o.author.Add(oa);
                                        }
                                    }
                                    var genres = mas[1].Split(':');
                                    foreach (var g in genres)
                                    {
                                        if (!string.IsNullOrWhiteSpace(g))
                                            o.genre.Add(g);
                                    }
                                    o.title = mas[2];
                                    o.series = mas[3];
                                    if (int.TryParse(mas[4], out int serno))
                                        o.serno = serno;
                                    o.file = mas[5];
                                    if (int.TryParse(mas[6], out int size))
                                        o.size = size;
                                    o.libid = mas[7];
                                    if (int.TryParse(mas[8], out int del))
                                        o.del = del;
                                    else
                                        o.del = 0;
                                    o.ext = mas[9];
                                    o.date = mas[10];
                                    o.lang = mas[11];
                                    o.keywords = mas[12];
                                    o.bookid = InsertBook(o);
                                    rt.Add(o);
                                }
                            }
                        }
                    }
                }
            }
            return rt;
        }

        private static int InsertBook(Books o)
        {
            sqlite3_stmt stmt = null;
            string cSql = "INSERT INTO Books " +
                "(LibID, Title, SeqNumber, UpdateDate, Lang, Folder, FileName, Ext, BookSize, IsDeleted, KeyWords) " +
                "VALUES (?,?,?,?,?,?,?,?,?,?,?,?)";
            if (raw.sqlite3_prepare_v2(db, cSql, out stmt) != raw.SQLITE_OK)
            {
                throw new Exception(raw.sqlite3_errmsg(db).utf8_to_string());
            }
            raw.sqlite3_bind_text(stmt, 1, o.libid);
            raw.sqlite3_bind_text(stmt, 2, o.title);
            if (o.serno.HasValue)
                raw.sqlite3_bind_int(stmt, 3, o.serno.Value);
            else
                raw.sqlite3_bind_null(stmt, 3);
            raw.sqlite3_bind_text(stmt, 4, o.date);
            raw.sqlite3_bind_text(stmt, 5, o.lang);
            raw.sqlite3_bind_text(stmt, 6, o.fol);
            raw.sqlite3_bind_text(stmt, 3, FB2Code);
            raw.sqlite3_bind_text(stmt, 4, GenreAlias);
            if (raw.sqlite3_step(stmt) != raw.SQLITE_DONE) throw new Exception(raw.sqlite3_errmsg(db).utf8_to_string());
            raw.sqlite3_finalize(stmt);
        }

        private static void InsertGenres()
        {
            sqlite3_stmt stmt = null;
            string cSql = "INSERT INTO Genres (GenreCode, ParentCode, FB2Code, GenreAlias) VALUES (?,?,?,?)";
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("inpxscan.genres_fb2.glst"))
            {
                using (var sr = new StreamReader(stream, Encoding.UTF8))
                {
                    string s;
                    int ind, ind1, ind3;
                    string GenreCode, ParentCode, FB2Code, GenreAlias;
                    while ((s = sr.ReadLine()) != null)
                    {
                        if (string.IsNullOrEmpty(s)) continue;
                        if (s[0] == '#') continue;
                        if ((ind = s.IndexOf(' ')) > 0)
                        {
                            GenreCode = s.Substring(0, ind);
                            ind1 = GenreCode.LastIndexOf('.');
                            ParentCode = GenreCode.Substring(0, ind1);
                            if (ParentCode == "0")
                            {
                                FB2Code = "";
                                GenreAlias = s.Substring(ind + 1);
                            }
                            else
                            {
                                ind3 = s.IndexOf(';');
                                if (ind3 < 0)
                                {
                                    FB2Code = "";
                                    GenreAlias = s.Substring(ind + 1);
                                }
                                else
                                {
                                    var mas = s.Substring(ind + 1).Split(';');
                                    FB2Code = mas[0];
                                    GenreAlias = mas[1];
                                }
                            }

                            if (raw.sqlite3_prepare_v2(db, cSql, out stmt) != raw.SQLITE_OK)
                            {
                                throw new Exception(raw.sqlite3_errmsg(db).utf8_to_string());
                            }
                            raw.sqlite3_bind_text(stmt, 1, GenreCode);
                            raw.sqlite3_bind_text(stmt, 2, ParentCode);
                            raw.sqlite3_bind_text(stmt, 3, FB2Code);
                            raw.sqlite3_bind_text(stmt, 4, GenreAlias);
                            if (raw.sqlite3_step(stmt) != raw.SQLITE_DONE) throw new Exception(raw.sqlite3_errmsg(db).utf8_to_string());
                            raw.sqlite3_reset(stmt);
                        }
                        
                    }
                }
            }
            raw.sqlite3_finalize(stmt);
        }

        private static void mhl_lower(sqlite3_context ctx, object user_data, sqlite3_value[] args)
        {
            var s = raw.sqlite3_value_text(args[0]).utf8_to_string().ToLower();
            raw.sqlite3_result_text(ctx, s);
            throw new NotImplementedException();
        }

        private static void mhl_upper(sqlite3_context ctx, object user_data, sqlite3_value[] args)
        {
            var s = raw.sqlite3_value_text(args[0]).utf8_to_string().ToUpper();
            raw.sqlite3_result_text(ctx, s);
        }

        private static int mhl_system_nocase_collation(object user_data, string s1, string s2)
        {
            return String.Compare(s1, s2);
        }

        private static int mhl_system_collation(object user_data, string s1, string s2)
        {
            return String.Compare(s1, s2);
        }
    }
}
