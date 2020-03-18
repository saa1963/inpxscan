using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace inpxscan
{
    class AuthorsComparer : EqualityComparer<Author>
    {
        public override bool Equals(Author b1, Author b2)
        {
            if (b1 == null && b2 == null)
                return true;
            else if (b1 == null || b2 == null)
                return false;

            return string.Equals(b1.SearchName, b2.SearchName);
        }

        public override int GetHashCode(Author bx)
        {
            return bx.SearchName.GetHashCode();
        }
    }
    public class Books
    {
        public int bookid;
        public HashSet<Author> author = new HashSet<Author>(new AuthorsComparer());
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
        public string SearchName
        {
            get => ((Last ?? "").Trim().ToUpper() + " " + (First ?? "").Trim().ToUpper() + " " + (Middle ?? "").Trim().ToUpper()).Trim();
        }
    }
}
