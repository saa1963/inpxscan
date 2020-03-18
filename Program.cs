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
        private static Dictionary<string, int> d_authors = new Dictionary<string, int>();
        private static Dictionary<string, string> d_genres = new Dictionary<string, string>();
        private static Dictionary<string, int> d_series = new Dictionary<string, int>();
        private static int new_genre = 9999;
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
            rc = raw.sqlite3_exec(db, "BEGIN");
            InsertGenres();
            sqlite3_stmt stmt = null;
            string cSql = "INSERT INTO Books " +
                "(LibID, Title, SeqNumber, UpdateDate, Lang, Folder, FileName, Ext, BookSize, IsDeleted, KeyWords, " +
                "SearchTitle, SearchLang, SearchFolder, SearchFileName, SearchExt, SearchKeyWords, SearchAnnotation, InsideNo) " +
                "VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)";
            if (raw.sqlite3_prepare_v2(db, cSql, out stmt) != raw.SQLITE_OK)
            {
                throw new Exception(raw.sqlite3_errmsg(db).utf8_to_string());
            }
            int booksCount = FillList(args[0], stmt);
            Console.WriteLine($"Книг - {booksCount}");
            Console.WriteLine($"Необработанных жанров - {9999 - new_genre}");
            raw.sqlite3_finalize(stmt);
            rc = raw.sqlite3_exec(db, "COMMIT");

            raw.sqlite3_close_v2(db);

            Console.WriteLine("Загрузка окончена.");
            Console.ReadKey();
        }

        private static int FillList(string fname, sqlite3_stmt stmt)
        {
            int booksCount = 0;
            int insideno = 0;
            using (var zipArchive = ZipFile.OpenRead(fname))
            {
                foreach (var entry in zipArchive.Entries)
                {
                    if (entry.Name.Contains(".inp"))
                    {
                        Console.WriteLine($"Обрабатывается файл {entry.Name}");
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
                                    o.bookid = InsertBook(o, entry.Name, insideno++, stmt);
                                    booksCount++;
                                    InsertGenreList(o.bookid, o.genre);
                                    InsertAuthorList(o.bookid, o.author);
                                    if (!string.IsNullOrWhiteSpace(o.series))
                                    {
                                        int seriesid;
                                        if (!d_series.ContainsKey(o.series.ToUpper()))
                                        {
                                            seriesid = InsertSeries(o.series);
                                            d_series.Add(o.series.ToUpper(), seriesid);
                                        }
                                        else
                                        {
                                            seriesid = d_series[o.series.ToUpper()];
                                        }
                                        UpdateBook(o.bookid, seriesid);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            return booksCount;
        }

        private static void UpdateBook(int bookid, int seriesid)
        {
            sqlite3_stmt stmt = null;
            string cSql = "UPDATE Books Set SeriesID = ? WHERE bookid = ?";
            if (raw.sqlite3_prepare_v2(db, cSql, out stmt) != raw.SQLITE_OK)
            {
                throw new Exception(raw.sqlite3_errmsg(db).utf8_to_string());
            }
            raw.sqlite3_bind_int(stmt, 1, seriesid);
            raw.sqlite3_bind_int(stmt, 2, bookid);
            if (raw.sqlite3_step(stmt) != raw.SQLITE_DONE) throw new Exception(raw.sqlite3_errmsg(db).utf8_to_string());
            raw.sqlite3_finalize(stmt);
        }

        private static int InsertSeries(string series)
        {
            sqlite3_stmt stmt = null;
            string cSql = "INSERT INTO Series (SeriesTitle, SearchSeriesTitle) VALUES (?,?)";
            if (raw.sqlite3_prepare_v2(db, cSql, out stmt) != raw.SQLITE_OK)
            {
                throw new Exception(raw.sqlite3_errmsg(db).utf8_to_string());
            }
            raw.sqlite3_bind_text(stmt, 1, series);
            raw.sqlite3_bind_text(stmt, 2, series.ToUpper());
            if (raw.sqlite3_step(stmt) != raw.SQLITE_DONE) throw new Exception(raw.sqlite3_errmsg(db).utf8_to_string());
            raw.sqlite3_reset(stmt);
            if (raw.sqlite3_prepare_v2(db, "select seq from sqlite_sequence where name='Series'", out stmt) != raw.SQLITE_OK)
            {
                throw new Exception(raw.sqlite3_errmsg(db).utf8_to_string());
            }
            if (raw.sqlite3_step(stmt) != raw.SQLITE_ROW)
                throw new Exception(raw.sqlite3_errmsg(db).utf8_to_string());
            var rt = raw.sqlite3_column_int(stmt, 0);
            raw.sqlite3_finalize(stmt);
            return rt;
        }

        private static void InsertAuthorList(int bookid, HashSet<Author> author)
        {
            sqlite3_stmt stmt = null;
            string cSql = "INSERT INTO Author_List (AuthorID, BookID) VALUES (?,?)";
            if (raw.sqlite3_prepare_v2(db, cSql, out stmt) != raw.SQLITE_OK)
            {
                throw new Exception(raw.sqlite3_errmsg(db).utf8_to_string());
            }
            foreach (var a in author)
            {
                if (!d_authors.ContainsKey(a.SearchName))
                {
                    int authorid = InsertAuthor(a.Last, a.First, a.Middle, a.SearchName);
                    raw.sqlite3_bind_int(stmt, 1, authorid);
                    d_authors.Add(a.SearchName, authorid);
                }
                else
                    raw.sqlite3_bind_int(stmt, 1, d_authors[a.SearchName]);
                raw.sqlite3_bind_int(stmt, 2, bookid);
                if (raw.sqlite3_step(stmt) != raw.SQLITE_DONE)
                    throw new Exception(raw.sqlite3_errmsg(db).utf8_to_string());
                raw.sqlite3_reset(stmt);
            }
            raw.sqlite3_finalize(stmt);
        }

        private static int InsertAuthor(string last, string first, string middle, string searchName)
        {
            sqlite3_stmt stmt = null;
            string cSql = "INSERT INTO Authors (LastName, FirstName, MiddleName, SearchName) VALUES (?,?,?,?)";
            if (raw.sqlite3_prepare_v2(db, cSql, out stmt) != raw.SQLITE_OK)
            {
                throw new Exception(raw.sqlite3_errmsg(db).utf8_to_string());
            }
            raw.sqlite3_bind_text(stmt, 1, last);
            raw.sqlite3_bind_text(stmt, 2, first);
            raw.sqlite3_bind_text(stmt, 3, middle);
            raw.sqlite3_bind_text(stmt, 4, searchName);
            if (raw.sqlite3_step(stmt) != raw.SQLITE_DONE) throw new Exception(raw.sqlite3_errmsg(db).utf8_to_string());
            raw.sqlite3_reset(stmt);
            if (raw.sqlite3_prepare_v2(db, "select seq from sqlite_sequence where name='Authors'", out stmt) != raw.SQLITE_OK)
            {
                throw new Exception(raw.sqlite3_errmsg(db).utf8_to_string());
            }
            if (raw.sqlite3_step(stmt) != raw.SQLITE_ROW)
                throw new Exception(raw.sqlite3_errmsg(db).utf8_to_string());
            var rt = raw.sqlite3_column_int(stmt, 0);
            raw.sqlite3_finalize(stmt);
            return rt;
        }

        private static void InsertGenreList(int bookid, List<string> genre)
        {
            sqlite3_stmt stmt = null;
            string cSql = "INSERT INTO Genre_List (GenreCode, BookID) VALUES (?,?)";
            if (raw.sqlite3_prepare_v2(db, cSql, out stmt) != raw.SQLITE_OK)
            {
                throw new Exception(raw.sqlite3_errmsg(db).utf8_to_string());
            }
            foreach (var g in genre)
            {
                if (!d_genres.ContainsKey(g))
                {
                    InsertGenre(new_genre--.ToString(), "", g, "");
                    raw.sqlite3_bind_text(stmt, 1, (new_genre + 1).ToString());
                    d_genres.Add(g, (new_genre + 1).ToString());
                }
                else
                    raw.sqlite3_bind_text(stmt, 1, d_genres[g]);
                raw.sqlite3_bind_int(stmt, 2, bookid);
                if (raw.sqlite3_step(stmt) != raw.SQLITE_DONE)
                    throw new Exception(raw.sqlite3_errmsg(db).utf8_to_string());
                raw.sqlite3_reset(stmt);
            }
            raw.sqlite3_finalize(stmt);
        }

        private static void InsertGenre(string GenreCode, string ParentCode, string FB2Code, string GenreAlias)
        {
            sqlite3_stmt stmt = null;
            string cSql = "INSERT INTO Genres (GenreCode, ParentCode, FB2Code, GenreAlias) VALUES (?,?,?,?)";
            if (raw.sqlite3_prepare_v2(db, cSql, out stmt) != raw.SQLITE_OK)
            {
                throw new Exception(raw.sqlite3_errmsg(db).utf8_to_string());
            }
            raw.sqlite3_bind_text(stmt, 1, GenreCode);
            raw.sqlite3_bind_text(stmt, 2, ParentCode);
            raw.sqlite3_bind_text(stmt, 3, FB2Code);
            raw.sqlite3_bind_text(stmt, 4, GenreAlias);
            if (raw.sqlite3_step(stmt) != raw.SQLITE_DONE) throw new Exception(raw.sqlite3_errmsg(db).utf8_to_string());
            raw.sqlite3_finalize(stmt);
        }

        private static int InsertBook(Books o, string folder, int insideno, sqlite3_stmt stmt)
        {
            raw.sqlite3_bind_text(stmt, 1, o.libid);
            raw.sqlite3_bind_text(stmt, 2, o.title);
            if (o.serno.HasValue)
                raw.sqlite3_bind_int(stmt, 3, o.serno.Value);
            else
                raw.sqlite3_bind_null(stmt, 3);
            raw.sqlite3_bind_text(stmt, 4, o.date);
            raw.sqlite3_bind_text(stmt, 5, o.lang);
            folder = Path.GetFileNameWithoutExtension(folder) + ".zip";
            raw.sqlite3_bind_text(stmt, 6, folder);
            raw.sqlite3_bind_text(stmt, 7, o.file);
            raw.sqlite3_bind_text(stmt, 8, "." + o.ext);
            if (o.size.HasValue)
                raw.sqlite3_bind_int(stmt, 9, o.size.Value);
            else
                raw.sqlite3_bind_null(stmt, 9);
            raw.sqlite3_bind_int(stmt, 10, o.del);
            raw.sqlite3_bind_text(stmt, 11, o.keywords);
            raw.sqlite3_bind_text(stmt, 12, o.title.ToUpper());
            raw.sqlite3_bind_text(stmt, 13, o.lang.ToUpper());
            raw.sqlite3_bind_text(stmt, 14, folder.ToUpper());
            raw.sqlite3_bind_text(stmt, 15, o.file.ToUpper());
            raw.sqlite3_bind_text(stmt, 16, "." + o.ext.ToUpper());
            raw.sqlite3_bind_text(stmt, 17, o.keywords.ToUpper());
            raw.sqlite3_bind_text(stmt, 18, "");
            raw.sqlite3_bind_int(stmt, 19, insideno);

            if (raw.sqlite3_step(stmt) != raw.SQLITE_DONE) 
                throw new Exception(raw.sqlite3_errmsg(db).utf8_to_string());
            raw.sqlite3_reset(stmt);
            
            if (raw.sqlite3_prepare_v2(db, "select seq from sqlite_sequence where name='Books'", out stmt) != raw.SQLITE_OK)
            {
                throw new Exception(raw.sqlite3_errmsg(db).utf8_to_string());
            }
            if (raw.sqlite3_step(stmt) != raw.SQLITE_ROW)
                throw new Exception(raw.sqlite3_errmsg(db).utf8_to_string());
            var rt = raw.sqlite3_column_int(stmt, 0);
            raw.sqlite3_reset(stmt);
            return rt;
        }

        private static void InsertGenres()
        {
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

                            InsertGenre(GenreCode, ParentCode, FB2Code, GenreAlias);
                            
                            if (!string.IsNullOrWhiteSpace(FB2Code))
                            {
                                if (!d_genres.ContainsKey(FB2Code))
                                {
                                    d_genres.Add(FB2Code, GenreCode);
                                }
                            }
                        }
                        
                    }
                }
            }
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
