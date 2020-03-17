using System;
using System.Collections.Generic;
using System.Text;

namespace inpxscan
{
    public class Books
    {
        public int bookid;
        public List<Author> author = new List<Author>();
        public List<string> genre = new List<string>();
        public string title;
        public string series;
        public int? serno;
        public string file;
        public int? size;
        public string libid;
        public int del = 0;
        public string ext;
        public string date;
        public string lang;
        public string keywords;
    }

    public class Author
    {
        public string Last;
        public string First;
        public string Middle;
    }
}
