﻿using ICU4N.Support.IO;
using ICU4N.Text;
using System;
using System.Diagnostics;

namespace ICU4N.Impl
{
    /// <summary>
    /// Trie implementation which stores data in char, 16 bits.
    /// </summary>
    /// <author>synwee</author>
    /// <seealso cref="Trie"/>
    /// <since>release 2.1, Jan 01 2002</since>

    // note that i need to handle the block calculations later, since chartrie
    // in icu4c uses the same index array.
    public class CharTrie : Trie
    {
        // public constructors ---------------------------------------------

        /// <summary>
        /// Creates a new Trie with the settings for the trie data.
        /// <para/>
        /// Unserialize the 32-bit-aligned input buffer and use the data for the trie.
        /// </summary>
        /// <param name="bytes">Data of an ICU data file, containing the trie.</param>
        /// <param name="dataManipulate">Object which provides methods to parse the char data.</param>
        public CharTrie(ByteBuffer bytes, IDataManipulate dataManipulate) // ICU4N TODO: API - make internal and make overload that accepts byte[]
            : base(bytes, dataManipulate)
        {
            if (!IsCharTrie)
            {
                throw new ArgumentException(
                                   "Data given does not belong to a char trie.");
            }
        }

        /// <summary>
        /// Make a dummy CharTrie.
        /// </summary>
        /// <remarks>
        /// A dummy trie is an empty runtime trie, used when a real data trie cannot
        /// be loaded.
        /// <para/>
        /// The trie always returns the initialValue,
        /// or the leadUnitValue for lead surrogate code points.
        /// The Latin-1 part is always set up to be linear.
        /// </remarks>
        /// <param name="initialValue">The initial value that is set for all code points.</param>
        /// <param name="leadUnitValue">The value for lead surrogate code _units_ that do not have associated supplementary data.</param>
        /// <param name="dataManipulate">Object which provides methods to parse the char data.</param>
        public CharTrie(int initialValue, int leadUnitValue, IDataManipulate dataManipulate)
                : base(new char[BMP_INDEX_LENGTH + SURROGATE_BLOCK_COUNT], HEADER_OPTIONS_LATIN1_IS_LINEAR_MASK_, dataManipulate)
        {
            int dataLength, latin1Length, i, limit;
            char block;

            /* calculate the actual size of the dummy trie data */

            /* max(Latin-1, block 0) */
            dataLength = latin1Length = INDEX_STAGE_1_SHIFT_ <= 8 ? 256 : DATA_BLOCK_LENGTH;
            if (leadUnitValue != initialValue)
            {
                dataLength += DATA_BLOCK_LENGTH;
            }
            m_data_ = new char[dataLength];
            m_dataLength_ = dataLength;

            m_initialValue_ = (char)initialValue;

            /* fill the index and data arrays */

            /* indexes are preset to 0 (block 0) */

            /* Latin-1 data */
            for (i = 0; i < latin1Length; ++i)
            {
                m_data_[i] = (char)initialValue;
            }

            if (leadUnitValue != initialValue)
            {
                /* indexes for lead surrogate code units to the block after Latin-1 */
                block = (char)(latin1Length >> INDEX_STAGE_2_SHIFT_);
                i = 0xd800 >> INDEX_STAGE_1_SHIFT_;
                limit = 0xdc00 >> INDEX_STAGE_1_SHIFT_;
                for (; i < limit; ++i)
                {
                    m_index_[i] = block;
                }

                /* data for lead surrogate code units */
                limit = latin1Length + DATA_BLOCK_LENGTH;
                for (i = latin1Length; i < limit; ++i)
                {
                    m_data_[i] = (char)leadUnitValue;
                }
            }
        }

        // public methods --------------------------------------------------

        /// <summary>
        /// Gets the value associated with the codepoint.
        /// <para/>
        /// If no value is associated with the codepoint, a default value will be
        /// returned.
        /// </summary>
        /// <param name="ch">Codepoint.</param>
        /// <returns>Offset to data.</returns>
        public char GetCodePointValue(int ch)
        {
            int offset;

            // fastpath for U+0000..U+D7FF
            if (0 <= ch && ch < UTF16.LEAD_SURROGATE_MIN_VALUE)
            {
                // copy of getRawOffset()
                offset = (m_index_[ch >> INDEX_STAGE_1_SHIFT_] << INDEX_STAGE_2_SHIFT_)
                        + (ch & INDEX_STAGE_3_MASK_);
                return m_data_[offset];
            }

            // handle U+D800..U+10FFFF
            offset = GetCodePointOffset(ch);

            // return -1 if there is an error, in this case we return the default
            // value: m_initialValue_
            return (offset >= 0) ? m_data_[offset] : m_initialValue_;
        }

        /// <summary>
        /// Gets the value to the data which this lead surrogate character points
        /// to.
        /// <para/>
        /// Returned data may contain folding offset information for the next
        /// trailing surrogate character.
        /// <para/>
        /// This method does not guarantee correct results for trail surrogates.
        /// </summary>
        /// <param name="ch">Lead surrogate character.</param>
        /// <returns>Data value.</returns>
        public char GetLeadValue(char ch)
        {
            return m_data_[GetLeadOffset(ch)];
        }

        /// <summary>
        /// Get the value associated with the BMP code point.
        /// Lead surrogate code points are treated as normal code points, with
        /// unfolded values that may differ from <see cref="GetLeadValue(char)"/> results.
        /// </summary>
        /// <param name="ch">The input BMP code point.</param>
        /// <returns>Trie data value associated with the BMP codepoint.</returns>
        public char GetBMPValue(char ch)
        {
            return m_data_[GetBMPOffset(ch)];
        }

        /// <summary>
        /// Get the value associated with a pair of surrogates.
        /// </summary>
        /// <param name="lead">A lead surrogate.</param>
        /// <param name="trail">A trail surrogate.</param>
        public char GetSurrogateValue(char lead, char trail)
        {
            int offset = GetSurrogateOffset(lead, trail);
            if (offset > 0)
            {
                return m_data_[offset];
            }
            return m_initialValue_;
        }

        /// <summary>
        /// Get a value from a folding offset (from the value of a lead surrogate)
        /// and a trail surrogate.
        /// </summary>
        /// <param name="leadvalue">Value associated with the lead surrogate which contains the folding offset.</param>
        /// <param name="trail">Trail surrogate.</param>
        /// <returns>Trie data value associated with the trail character.</returns>
        public char GetTrailValue(int leadvalue, char trail)
        {
            if (m_dataManipulate_ == null)
            {
                throw new ArgumentNullException(
                                 "The field DataManipulate in this Trie is null");
            }
            int offset = m_dataManipulate_.GetFoldingOffset(leadvalue);
            if (offset > 0)
            {
                return m_data_[GetRawOffset(offset,
                                            (char)(trail & SURROGATE_MASK_))];
            }
            return m_initialValue_;
        }

        /// <summary>
        /// Gets the latin 1 fast path value.
        /// <para/>
        /// Note this only works if latin 1 characters have their own linear array.
        /// </summary>
        /// <param name="ch">Latin 1 characters.</param>
        /// <returns>Value associated with latin character.</returns>
        public char GetLatin1LinearValue(char ch)
        {
            return m_data_[INDEX_STAGE_3_MASK_ + 1 + m_dataOffset_ + ch];
        }

        /// <summary>
        /// Checks if the argument Trie has the same data as this Trie.
        /// </summary>
        /// <param name="other">Trie to check.</param>
        /// <returns>true if the argument Trie has the same data as this Trie, false otherwise.</returns>
        //CLOVER:OFF
        public override bool Equals(object other)
        {
            bool result = base.Equals(other);
            if (result && other is CharTrie)
            {
                CharTrie othertrie = (CharTrie)other;
                return m_initialValue_ == othertrie.m_initialValue_;
            }
            return false;
        }

        public override int GetHashCode()
        {
            Debug.Assert(false, "hashCode not designed");
            return 42;
        }
        //CLOVER:ON

        // protected methods -----------------------------------------------

        /// <summary>
        /// Parses the byte buffer and stores its trie content into a index and
        /// data array.
        /// </summary>
        /// <param name="bytes">Buffer containing trie data.</param>
        protected override sealed void Unserialize(ByteBuffer bytes) // ICU4N TODO: API - make internal and add overload that accepts byte[]
        {
            int indexDataLength = m_dataOffset_ + m_dataLength_;
            m_index_ = ICUBinary.GetChars(bytes, indexDataLength, 0);
            m_data_ = m_index_;
            m_initialValue_ = m_data_[m_dataOffset_];
        }

        /// <summary>
        ///  Gets the offset to the data which the surrogate pair points to.
        /// </summary>
        /// <param name="lead">Lead surrogate.</param>
        /// <param name="trail">Trailing surrogate.</param>
        /// <returns>Offset to data.</returns>
        protected override sealed int GetSurrogateOffset(char lead, char trail)
        {
            if (m_dataManipulate_ == null)
            {
                throw new ArgumentNullException(
                                 "The field DataManipulate in this Trie is null");
            }

            // get fold position for the next trail surrogate
            int offset = m_dataManipulate_.GetFoldingOffset(GetLeadValue(lead));

            // get the real data from the folded lead/trail units
            if (offset > 0)
            {
                return GetRawOffset(offset, (char)(trail & SURROGATE_MASK_));
            }

            // return -1 if there is an error, in this case we return the default
            // value: m_initialValue_
            return -1;
        }

        /// <summary>
        /// Gets the value at the argument index.
        /// For use internally in <see cref="TrieEnumerator"/>.
        /// <para/>
        /// NOTE: This was named GetValue(int) in icu4j.
        /// </summary>
        /// <param name="index">Value at index will be retrieved.</param>
        /// <returns>32 bit value.</returns>
        /// <seealso cref="TrieEnumerator"/>
        protected internal override sealed int this[int index]
        {
            get { return m_data_[index]; }
        }

        /// <summary>
        /// Gets the default initial (32 bit) value.
        /// </summary>
        protected internal override sealed int InitialValue
        {
            get { return m_initialValue_; }
        }

        // private data members --------------------------------------------

        /// <summary>
        /// Default value
        /// </summary>
        private char m_initialValue_;
        /// <summary>
        /// Array of char data
        /// </summary>
        private char[] m_data_;
    }
}