﻿using J2N.Text;
using StringBuffer = System.Text.StringBuilder;

namespace ICU4N.Text
{
    /// <summary>
    /// <see cref="ReplaceableString"/> is an adapter class that implements the
    /// <see cref="IReplaceable"/> API around an ordinary <see cref="StringBuffer"/>.
    /// </summary>
    /// <remarks>
    /// <em>Note:</em> This class does not support attributes and is not
    /// intended for general use.  Most clients will need to implement
    /// <see cref="IReplaceable"/> in their text representation class.
    /// </remarks>
    /// <see cref="IReplaceable"/>
    /// <author>Alan Liu</author>
    /// <stable>ICU 2.0</stable>
    public class ReplaceableString : IReplaceable
    {
        private readonly StringBuffer buf;
        private bool changed; // ICU4N specific
        private int previousLength; // ICU4N specific
        private int previousHashCode; // ICU4N specific
        private string realized; // ICU4N specific

        /// <summary>
        /// Construct a new object with the given initial contents.
        /// </summary>
        /// <param name="str">Initial contents.</param>
        /// <stable>ICU 2.0</stable>
        public ReplaceableString(string str)
        {
            buf = new StringBuffer(str);
            previousLength = buf.Length;
            realized = str;
            previousHashCode = buf.GetHashCode();
        }

        /// <summary>
        /// Construct a new object using <paramref name="buf"/> for internal
        /// storage.  The contents of <paramref name="buf"/> at the time of
        /// construction are used as the initial contents.  <em>Note!
        /// Modifications to <paramref name="buf"/> will modify this object, and
        /// vice versa.</em>
        /// <para/>
        /// Usage Note: If you are making external changes to a <see cref="StringBuffer"/>
        /// that is passed into this constructor, it is recommended to call
        /// <see cref="ReplaceableString.ToString()"/> if the contents of the
        /// <see cref="StringBuffer"/> changed but the length did not change before
        /// calling <see cref="ReplaceableString.Char32At(int)"/>. Since the indexer of the
        /// <see cref="StringBuffer"/> in .NET is slow, the contents are cached internally
        /// so multiple calls to <see cref="ReplaceableString.Char32At(int)"/> in a row are not expensive.
        /// <see cref="ReplaceableString.ToString()"/> forces a reload of the cache.
        /// </summary>
        /// <param name="buf">Object to be used as internal storage.</param>
        /// <stable>ICU 2.0</stable>
        public ReplaceableString(StringBuffer buf)
        {
            this.buf = buf;
        }

        /// <summary>
        /// Construct a new empty object.
        /// </summary>
        /// <stable>ICU 2.0</stable>
        public ReplaceableString()
        {
            buf = new StringBuffer();
        }

        /// <summary>
        /// Return the contents of this object as a <see cref="string"/>.
        /// </summary>
        /// <returns>String contents of this object.</returns>
        /// <stable>ICU 2.0</stable>
        public override string ToString()
        {
            return RealizeString();
        }

        /// <summary>
        /// Return a substring of the given string.
        /// </summary>
        /// <remarks>
        /// Using .NET semantics - that is, the second parameter
        /// is count rather than end.
        /// </remarks>
        /// <stable>ICU 2.0</stable>
        public virtual string Substring(int start, int count) // ICU4N NOTE: Using .NET semantics here - be vigilant about use
        {
            return buf.ToString(start, count);
        }

        /// <summary>
        /// Return the number of characters contained in this object.
        /// <see cref="IReplaceable"/> API.
        /// </summary>
        /// <stable>ICU 2.0</stable>
        public virtual int Length => buf.Length;

        /// <summary>
        /// Gets the character at the given position in this object.
        /// <see cref="IReplaceable"/> API.
        /// </summary>
        /// <param name="index">Offset into the contents, from 0 to <c><see cref="Length"/> - 1</c></param>
        /// <stable>ICU 2.0</stable>
        public virtual char this[int index] => buf[index];

        /// <summary>
        /// Return the 32-bit code point at the given 16-bit offset into
        /// the text.  This assumes the text is stored as 16-bit code units
        /// with surrogate pairs intermixed.  If the offset of a leading or
        /// trailing code unit of a surrogate pair is given, return the
        /// code point of the surrogate pair.
        /// <para/>
        /// Usage Note: If you are making external changes to a <see cref="StringBuffer"/>
        /// that is passed into the <see cref="ReplaceableString"/> constructor,
        /// it is recommended to call <see cref="ReplaceableString.ToString()"/> if
        /// the contents of the <see cref="StringBuffer"/> changed but the length
        /// did not change before calling this method. Since the indexer of the
        /// <see cref="StringBuffer"/> in .NET is slow, the contents are cached internally
        /// so multiple calls to this method in a row are not expensive.
        /// <see cref="ReplaceableString.ToString()"/> forces a reload of the cache.
        /// </summary>
        /// <param name="offset">An integer between 0 and <see cref="Length"/>-1 inclusive.</param>
        /// <returns>32-bit code point of text at given offset.</returns>
        /// <stable>ICU 2.0</stable>
        public virtual int Char32At(int offset)
        {
            // ICU4N: In .NET, the StringBuilder indexer is extremely slow,
            // so we realize (cache) a string whenever a change is detected.
            // GetHashCode() is not a 100% reliable way to determine if the contents
            // of the StringBuilder have changed but more reliable than Length.
            // The Length property is a bit cheaper, so we check that first.

            string realizedString = realized;
            if (realizedString is null || changed || previousLength != buf.Length || previousHashCode != buf.GetHashCode())
                realizedString = RealizeString();

            return UTF16.CharAt(realizedString, offset);
        }

        /// <summary>
        /// Copies characters from this object into the destination
        /// character array.  The first character to be copied is at index
        /// The first character to be copied is at index
        /// <paramref name="sourceIndex"/>; the last character to be copied is at
        /// index <paramref name="count"/>-<paramref name="sourceIndex"/>.
        /// The characters are copied into the subarray of <paramref name="destination"/>
        /// starting at index <paramref name="destinationIndex"/> and ending at index
        /// <paramref name="destinationIndex"/>+<paramref name="count"/>.
        /// </summary>
        /// <remarks>
        /// NOTE: This is roughly equivalent to GetChars(int srcStart, int srcLimit, char dst[], int dstStart)
        /// in ICU4J with one important difference - the final parameter is the total
        /// count of characters to be copied (srcLimit - srcStart). This is to 
        /// make the function compatible with the <see cref="string.CopyTo(int, char[], int, int)"/> implementation in .NET.
        /// </remarks>
        /// <param name="sourceIndex">The index of the first character in this instance to copy.</param>
        /// <param name="destination">An array of Unicode characters to which characters in this instance are copied.</param>
        /// <param name="destinationIndex">The index in <paramref name="destination"/> at which the copy operation begins.</param>
        /// <param name="count">The number of characters in this instance to copy to <paramref name="destination"/>.</param>
        /// <stable>ICU 2.0</stable>
        public virtual void CopyTo(int sourceIndex, char[] destination, int destinationIndex, int count)
        {
            if (count > 0)
            {
                buf.CopyTo(sourceIndex, destination, destinationIndex, count);
            }
        }

        /// <summary>
        /// Replace zero or more characters with new characters.
        /// <see cref="IReplaceable"/> API.
        /// </summary>
        /// <param name="startIndex">The beginning index, inclusive; <c>0 &lt;= <paramref name="startIndex"/></c>.</param>
        /// <param name="count">The number of characters to replace; <c>0 &lt;= <paramref name="count"/></c>.</param>
        /// <param name="text">The text to replace characters beginning at <paramref name="startIndex"/>.</param>
        /// <stable>ICU 2.0</stable>
        public virtual void Replace(int startIndex, int count, string text) // ICU4N: Made 2nd parameter into count rather than limit
        {
            buf.Replace(startIndex, count, text);
            changed = true;
        }

        /// <summary>
        /// Replace a substring of this object with the given text.
        /// </summary>
        /// <param name="startIndex">The beginning index, inclusive; <c>0 &lt;= <paramref name="startIndex"/></c>.</param>
        /// <param name="count">The number of characters to replace; <c>0 &lt;= <paramref name="count"/></c>.</param>
        /// <param name="chars">The text to replace characters <paramref name="startIndex"/> to <c><paramref name="count"/> - 1</c></param>
        /// <param name="charsStart">The beginning index into <paramref name="chars"/>, inclusive; <c>0 &lt;= <paramref name="startIndex"/></c>.</param>
        /// <param name="charsLen">The number of characters of <paramref name="chars"/>.</param>
        /// <stable>ICU 2.0</stable>
        public virtual void Replace(int startIndex, int count, char[] chars,
                        int charsStart, int charsLen) // ICU4N: Made 2nd parameter into count rather than limit
        {
            buf.Delete(startIndex, count);
            buf.Insert(startIndex, chars, charsStart, charsLen);
            changed = true;
        }

        /// <summary>
        /// Copy a substring of this object, retaining attribute (out-of-band)
        /// information.  This method is used to duplicate or reorder substrings.
        /// The destination index must not overlap the source range.
        /// </summary>
        /// <param name="startIndex">The beginning index, inclusive; <c>0 &lt;= <paramref name="startIndex"/></c>.</param>
        /// <param name="length">The length; <c><paramref name="startIndex"/> + <paramref name="length"/> &lt;= <see cref="Length"/></c>.</param>
        /// <param name="destinationIndex">The destination index. <paramref name="length"/> characters from
        /// <paramref name="startIndex"/> will be copied to <paramref name="destinationIndex"/>.
        /// Implementations of this method may assume that <c><paramref name="destinationIndex"/> &lt;= <paramref name="startIndex"/> ||
        /// <paramref name="destinationIndex"/> &gt;= <paramref name="startIndex"/> + <paramref name="length"/></c>.
        /// </param>
        /// <stable>ICU 2.0</stable>
        public virtual void Copy(int startIndex, int length, int destinationIndex)
        {
            if (0 == length && startIndex >= 0 && startIndex + length <= buf.Length)
            {
                return;
            }
            char[] text = new char[length]; // ICU4N: Corrected length
            //getChars(start, limit, text, 0);
            CopyTo(startIndex, text, 0, length); // ICU4N: Corrected 4th parameter
            Replace(destinationIndex, destinationIndex - destinationIndex, text, 0, length); // ICU4N: Corrected 2nd and 5th Replace parameters
            changed = true;
        }

        /// <summary>
        /// Implements <see cref="IReplaceable"/>
        /// </summary>
        /// <stable>ICU 2.0</stable>
        public virtual bool HasMetaData => false;

        /// <summary>
        /// Caches the string from the internal <see cref="StringBuffer"/>
        /// so when we call <see cref="UTF16.CharAt(string, int)"/>, we can
        /// use a <see cref="string"/> which is much faster to index over.
        /// </summary>
        /// <returns>The realized string.</returns>
        private string RealizeString() // ICU4N specific
        {
            realized = buf.ToString();
            changed = false;
            previousLength = buf.Length;
            previousHashCode = buf.GetHashCode();
            return realized;
        }
    }
}
