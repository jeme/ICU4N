﻿using ICU4N.Support;
using ICU4N.Support.Text;
using ICU4N.Text;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Collections;

namespace ICU4N.Util
{
    /// <summary>
    /// Light-weight, non-const reader class for a CharsTrie.
    /// Traverses a char-serialized data structure with minimal state,
    /// for mapping strings (16-bit-unit sequences) to non-negative integer values.
    /// <para/>
    /// This class is not intended for public subclassing.
    /// </summary>
    /// <stable>ICU 4.8</stable>
    /// <author>Markus W. Scherer</author>
    public sealed partial class CharsTrie : IEnumerable<CharsTrie.Entry>
#if FEATURE_CLONEABLE
        , ICloneable
#endif
    {
        // ICU4N specific - constructor moved to CharsTrieExtension.tt

        /// <summary>
        /// Clones this trie reader object and its state,
        /// but not the char array which will be shared.
        /// </summary>
        /// <returns>A shallow clone of this trie.</returns>
        /// <stable>ICU 4.8</stable>
        public object Clone()
        {
            return base.MemberwiseClone();  // A shallow copy is just what we need.
        }

        /// <summary>
        /// Resets this trie to its initial state.
        /// </summary>
        /// <returns>This.</returns>
        /// <stable>ICU 4.8</stable>
        public CharsTrie Reset()
        {
            pos_ = root_;
            remainingMatchLength_ = -1;
            return this;
        }

        /// <summary>
        /// CharsTrie state object, for saving a trie's current state
        /// and resetting the trie back to this state later.
        /// </summary>
        /// <stable>ICU 4.8</stable>
        public sealed class State // ICU4N TODO: API De-nest?
        {
            /// <summary>
            /// Constructs an empty <see cref="State"/>.
            /// </summary>
            /// <stable>ICU 4.8</stable>
            public State() { }
            internal ICharSequence Chars { get; set; }
            internal int Root { get; set; }
            internal int Pos { get; set; }
            internal int RemainingMatchLength { get; set; }
        }

        /// <summary>
        /// Saves the state of this trie.
        /// </summary>
        /// <param name="state">The <see cref="State"/> object to hold the trie's state.</param>
        /// <returns>This.</returns>
        /// <see cref="ResetToState(State)"/>
        /// <stable>ICU 4.8</stable>
        public CharsTrie SaveState(State state) /*const*/
        {
            state.Chars = chars_;
            state.Root = root_;
            state.Pos = pos_;
            state.RemainingMatchLength = remainingMatchLength_;
            return this;
        }

        /// <summary>
        /// Resets this trie to the saved state.
        /// </summary>
        /// <param name="state">The State object which holds a saved trie state.</param>
        /// <returns>This.</returns>
        /// <exception cref="ArgumentException">If the state object contains no state,
        /// or the state of a different trie.</exception>
        /// <seealso cref="SaveState(State)"/>
        /// <seealso cref="Reset()"/>
        /// <stable>ICU 4.8</stable>
        public CharsTrie ResetToState(State state)
        {
            if (chars_ == state.Chars && chars_ != null && root_ == state.Root)
            {
                pos_ = state.Pos;
                remainingMatchLength_ = state.RemainingMatchLength;
            }
            else
            {
                throw new ArgumentException("incompatible trie state");
            }
            return this;
        }

        /// <summary>
        /// Determines whether the byte sequence so far matches, whether it has a value,
        /// and whether another input byte can continue a matching byte sequence.
        /// Returns the match/value <see cref="Result"/>.
        /// </summary>
        /// <stable>ICU 4.8</stable>
        public Result Current /*const*/
        {
            get
            {
                int pos = pos_;
                if (pos < 0)
                {
                    return Result.NoMatch;
                }
                else
                {
                    int node;
                    return (remainingMatchLength_ < 0 && (node = chars_[pos]) >= kMinValueLead) ?
                            valueResults_[node >> 15] : Result.NoValue;
                }
            }
        }

        /// <summary>
        /// Traverses the trie from the initial state for this input char.
        /// Equivalent to <c>Reset().Next(inByte)</c>.
        /// </summary>
        /// <param name="inUnit">Input char value. Values below 0 and above 0xffff will never match.</param>
        /// <returns>The match/value <see cref="Result"/>.</returns>
        /// <stable>ICU 4.8</stable>
        public Result First(int inUnit)
        {
            remainingMatchLength_ = -1;
            return NextImpl(root_, inUnit);
        }

        /// <summary>
        /// Traverses the trie from the initial state for the
        /// one or two UTF-16 code units for this input code point.
        /// Equivalent to <c>Reset().NextForCodePoint(cp)</c>.
        /// </summary>
        /// <param name="cp">A Unicode code point 0..0x10ffff.</param>
        /// <returns>The match/value <see cref="Result"/>.</returns>
        /// <stable>ICU 4.8</stable>
        public Result FirstForCodePoint(int cp)
        {
            return cp <= 0xffff ?
                First(cp) :
                (First(UTF16.GetLeadSurrogate(cp)).HasNext() ?
                    Next(UTF16.GetTrailSurrogate(cp)) :
                    Result.NoMatch);
        }

        /// <summary>
        /// Traverses the trie from the current state for this input char.
        /// </summary>
        /// <param name="inUnit">Input char value. Values below 0 and above 0xffff will never match.</param>
        /// <returns>The match/value <see cref="Result"/>.</returns>
        /// <stable>ICU 4.8</stable>
        public Result Next(int inUnit)
        {
            int pos = pos_;
            if (pos < 0)
            {
                return Result.NoMatch;
            }
            int length = remainingMatchLength_;  // Actual remaining match length minus 1.
            if (length >= 0)
            {
                // Remaining part of a linear-match node.
                if (inUnit == chars_[pos++])
                {
                    remainingMatchLength_ = --length;
                    pos_ = pos;
                    int node;
                    return (length < 0 && (node = chars_[pos]) >= kMinValueLead) ?
                            valueResults_[node >> 15] : Result.NoValue;
                }
                else
                {
                    Stop();
                    return Result.NoMatch;
                }
            }
            return NextImpl(pos, inUnit);
        }

        /// <summary>
        /// Traverses the trie from the current state for the
        /// one or two UTF-16 code units for this input code point.
        /// </summary>
        /// <param name="cp">A Unicode code point 0..0x10ffff.</param>
        /// <returns>The match/value <see cref="Result"/>.</returns>
        /// <stable>ICU 4.8</stable>
        public Result NextForCodePoint(int cp)
        {
            return cp <= 0xffff ?
                Next(cp) :
                (Next(UTF16.GetLeadSurrogate(cp)).HasNext() ?
                    Next(UTF16.GetTrailSurrogate(cp)) :
                    Result.NoMatch);
        }

        // ICU4N specific - Next(ICharSequence s, int sIndex, int sLimit) moved to CharsTrieExtension.tt

        /// <summary>
        /// Returns a matching string's value if called immediately after
        /// <see cref="Current"/>/<see cref="First(int)"/>/<see cref="Next(int)"/>
        /// returned <see cref="Result.IntermediateValue"/> or <see cref="Result.FinalValue"/>.
        /// <see cref="GetValue()"/> can be called multiple times.
        /// <para/>
        /// Do not call <see cref="GetValue()"/> after <see cref="Result.NoMatch"/> or <see cref="Result.NoValue"/>!
        /// </summary>
        /// <returns>The value for the string so far.</returns>
        /// <stable>ICU 4.8</stable>
        public int GetValue() /*const*/
        {
            int pos = pos_;
            int leadUnit = chars_[pos++];
            Debug.Assert(leadUnit >= kMinValueLead);
            return (leadUnit & kValueIsFinal) != 0 ?
                ReadValue(chars_, pos, leadUnit & 0x7fff) : ReadNodeValue(chars_, pos, leadUnit);
        }

        /// <summary>
        /// Determines whether all strings reachable from the current state
        /// map to the same value, and if so, returns that value.
        /// </summary>
        /// <returns>The unique value in bits 32..1 with bit 0 set,
        /// if all strings reachable from the current state
        /// map to the same value; otherwise returns 0.</returns>
        /// <stable>ICU 4.8</stable>
        public long GetUniqueValue() /*const*/
        {
            int pos = pos_;
            if (pos < 0)
            {
                return 0;
            }
            // Skip the rest of a pending linear-match node.
            long uniqueValue = FindUniqueValue(chars_, pos + remainingMatchLength_ + 1, 0);
            // Ignore internally used bits 63..33; extend the actual value's sign bit from bit 32.
            return (uniqueValue << 31) >> 31;
        }

        /// <summary>
        /// Finds each char which continues the string from the current state.
        /// That is, each char c for which it would be Next(c)!=<see cref="Result.NoMatch"/> now.
        /// </summary>
        /// <param name="output">Each next char is appended to this object.
        /// (Only uses the output.Append(c) method.)</param>
        /// <returns>The number of chars which continue the string from here.</returns>
        /// <stable>ICU 4.8</stable>
        public int GetNextChars(StringBuilder output) /*const*/
        {
            int pos = pos_;
            if (pos < 0)
            {
                return 0;
            }
            if (remainingMatchLength_ >= 0)
            {
                Append(output, chars_[pos]);  // Next unit of a pending linear-match node.
                return 1;
            }
            int node = chars_[pos++];
            if (node >= kMinValueLead)
            {
                if ((node & kValueIsFinal) != 0)
                {
                    return 0;
                }
                else
                {
                    pos = SkipNodeValue(pos, node);
                    node &= kNodeTypeMask;
                }
            }
            if (node < kMinLinearMatch)
            {
                if (node == 0)
                {
                    node = chars_[pos++];
                }
                GetNextBranchChars(chars_, pos, ++node, output);
                return node;
            }
            else
            {
                // First unit of the linear-match node.
                Append(output, chars_[pos]);
                return 1;
            }
        }

        /// <summary>
        /// Iterates from the current state of this trie.
        /// </summary>
        /// <remarks>
        /// This is equivalent to iterator() in ICU4J.
        /// </remarks>
        /// <returns>A new <see cref="CharsTrie.Enumerator"/>.</returns>
        /// <stable>ICU 4.8</stable>
        public Enumerator GetEnumerator()
        {
            return new Enumerator(chars_, pos_, remainingMatchLength_, 0);
        }

        /// <summary>
        /// Iterates from the current state of this trie.
        /// </summary>
        /// <remarks>
        /// This is equivalent to iterator(int) in ICU4J.
        /// </remarks>
        /// <param name="maxStringLength">If 0, the enumerator returns full strings.
        /// Otherwise, the enumerator returns strings with this maximum length.</param>
        /// <returns>A new <see cref="CharsTrie.Enumerator"/>.</returns>
        /// <stable>ICU 4.8</stable>
        public Enumerator GetEnumerator(int maxStringLength)
        {
            return new Enumerator(chars_, pos_, remainingMatchLength_, maxStringLength);
        }

        IEnumerator<Entry> IEnumerable<Entry>.GetEnumerator()
        {
            return new Enumerator(chars_, pos_, remainingMatchLength_, 0);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        /// <summary>
        /// Iterates from the root of a char-serialized <see cref="BytesTrie"/>.
        /// </summary>
        /// <remarks>
        /// This is equivalent to iterator(ICharSequence, int, int) in ICU4J.
        /// </remarks>
        /// <param name="trieChars"><see cref="string"/> that contains the serialized trie.</param>
        /// <param name="offset">Root offset of the trie in the <see cref="string"/>.</param>
        /// <param name="maxStringLength">If 0, the iterator returns full strings.
        /// Otherwise, the iterator returns strings with this maximum length.</param>
        /// <returns>A new <see cref="CharsTrie.Enumerator"/>.</returns>
        /// <stable>ICU 4.8</stable>
        public static Enumerator GetEnumerator(string trieChars, int offset, int maxStringLength)
        {
            return new Enumerator(trieChars.ToCharSequence(), offset, -1, maxStringLength);
        }

        /// <summary>
        /// Iterates from the root of a char-serialized <see cref="BytesTrie"/>.
        /// </summary>
        /// <remarks>
        /// This is equivalent to iterator(ICharSequence, int, int) in ICU4J.
        /// </remarks>
        /// <param name="trieChars"><see cref="StringBuilder"/> that contains the serialized trie.</param>
        /// <param name="offset">Root offset of the trie in the <see cref="StringBuilder"/>.</param>
        /// <param name="maxStringLength">If 0, the iterator returns full strings.
        /// Otherwise, the iterator returns strings with this maximum length.</param>
        /// <returns>A new <see cref="CharsTrie.Enumerator"/>.</returns>
        /// <stable>ICU 4.8</stable>
        public static Enumerator GetEnumerator(StringBuilder trieChars, int offset, int maxStringLength)
        {
            return new Enumerator(trieChars.ToCharSequence(), offset, -1, maxStringLength);
        }

        /// <summary>
        /// Iterates from the root of a char-serialized <see cref="BytesTrie"/>.
        /// </summary>
        /// <remarks>
        /// This is equivalent to iterator(ICharSequence, int, int) in ICU4J.
        /// </remarks>
        /// <param name="trieChars"><see cref="T:char[]"/> that contains the serialized trie.</param>
        /// <param name="offset">Root offset of the trie in the <see cref="T:char[]"/>.</param>
        /// <param name="maxStringLength">If 0, the iterator returns full strings.
        /// Otherwise, the iterator returns strings with this maximum length.</param>
        /// <returns>A new <see cref="CharsTrie.Enumerator"/>.</returns>
        /// <stable>ICU 4.8</stable>
        public static Enumerator GetEnumerator(char[] trieChars, int offset, int maxStringLength)
        {
            return new Enumerator(trieChars.ToCharSequence(), offset, -1, maxStringLength);
        }

        /// <summary>
        /// Iterates from the root of a char-serialized <see cref="BytesTrie"/>.
        /// </summary>
        /// <remarks>
        /// This is equivalent to iterator(ICharSequence, int, int) in ICU4J.
        /// </remarks>
        /// <param name="trieChars"><see cref="ICharSequence"/> that contains the serialized trie.</param>
        /// <param name="offset">Root offset of the trie in the <see cref="ICharSequence"/>.</param>
        /// <param name="maxStringLength">If 0, the iterator returns full strings.
        /// Otherwise, the iterator returns strings with this maximum length.</param>
        /// <returns>A new <see cref="CharsTrie.Enumerator"/>.</returns>
        /// <stable>ICU 4.8</stable>
        internal static Enumerator GetEnumerator(ICharSequence trieChars, int offset, int maxStringLength)
        {
            return new Enumerator(trieChars, offset, -1, maxStringLength);
        }

        /// <summary>
        /// Return value type for the <see cref="Enumerator"/>.
        /// </summary>
        /// <stable>ICU 4.8</stable>
        public sealed class Entry
        {
            /// <summary>
            /// The string.
            /// </summary>
            /// <stable>ICU 4.8</stable>
            public ICharSequence Chars { get; set; }

            /// <summary>
            /// Gets or Sets the value associated with the string.
            /// </summary>
            /// <stable>ICU 4.8</stable>
            public int Value { get; set; }

            internal Entry()
            {
            }
        }

        /// <summary>
        /// Iterator for all of the (string, value) pairs in a <see cref="CharsTrie"/>.
        /// </summary>
        /// <stable>ICU 4.8</stable>
        public sealed class Enumerator : IEnumerator<Entry>
        {
            private Entry current = null;

            internal Enumerator(ICharSequence trieChars, int offset, int remainingMatchLength, int maxStringLength)
            {
                chars_ = trieChars;
                pos_ = initialPos_ = offset;
                remainingMatchLength_ = initialRemainingMatchLength_ = remainingMatchLength;
                maxLength_ = maxStringLength;
                int length = remainingMatchLength_;  // Actual remaining match length minus 1.
                if (length >= 0)
                {
                    // Pending linear-match node, append remaining bytes to str_.
                    ++length;
                    if (maxLength_ > 0 && length > maxLength_)
                    {
                        length = maxLength_;  // This will leave remainingMatchLength>=0 as a signal.
                    }
                    str_.Append(chars_, pos_, (pos_ + length) - pos_); // ICU4N: Corrected 3rd parameter
                    pos_ += length;
                    remainingMatchLength_ -= length;
                }
            }

            /// <summary>
            /// Resets this iterator to its initial state.
            /// </summary>
            /// <returns>This.</returns>
            /// <stable>ICU 4.8</stable>
            public Enumerator Reset()
            {
                pos_ = initialPos_;
                remainingMatchLength_ = initialRemainingMatchLength_;
                skipValue_ = false;
                int length = remainingMatchLength_ + 1;  // Remaining match length.
                if (maxLength_ > 0 && length > maxLength_)
                {
                    length = maxLength_;
                }
                str_.Length = length;
                pos_ += length;
                remainingMatchLength_ -= length;
                stack_.Clear();
                return this;
            }

            /// <summary>
            /// Returns true if there are more elements.
            /// </summary>
            /// <returns>true if there are more elements.</returns>
            /// <stable>ICU 4.8</stable>
            private bool HasNext() /*const*/ { return pos_ >= 0 || stack_.Count > 0; } // ICU4N TODO: API - make property

            /// <summary>
            /// Finds the next (string, value) pair if there is one.
            /// </summary>
            /// <remarks>
            /// If the string is truncated to the maximum length and does not
            /// have a real value, then the value is set to -1.
            /// In this case, this "not a real value" is indistinguishable from
            /// a real value of -1.
            /// </remarks>
            /// <returns>An <see cref="Entry"/> with the string and value of the next element.</returns>
            /// <stable>ICU 4.8</stable>
            private Entry Next()
            {
                int pos = pos_;
                if (pos < 0)
                {
                    //if (stack_.isEmpty())
                    //{
                    //    throw new NoSuchElementException();
                    //}
                    // Pop the state off the stack and continue with the next outbound edge of
                    // the branch node.
                    long top = stack_[stack_.Count - 1];
                    stack_.Remove(top);
                    int length = (int)top;
                    pos = (int)(top >> 32);
                    str_.Length = (length & 0xffff);
                    length = length.TripleShift(16);
                    if (length > 1)
                    {
                        pos = BranchNext(pos, length);
                        if (pos < 0)
                        {
                            return entry_;  // Reached a final value.
                        }
                    }
                    else
                    {
                        str_.Append(chars_[pos++]);
                    }
                }
                if (remainingMatchLength_ >= 0)
                {
                    // We only get here if we started in a pending linear-match node
                    // with more than maxLength remaining units.
                    return TruncateAndStop();
                }
                for (; ; )
                {
                    int node = chars_[pos++];
                    if (node >= kMinValueLead)
                    {
                        if (skipValue_)
                        {
                            pos = SkipNodeValue(pos, node);
                            node &= kNodeTypeMask;
                            skipValue_ = false;
                        }
                        else
                        {
                            // Deliver value for the string so far.
                            bool isFinal = (node & kValueIsFinal) != 0;
                            if (isFinal)
                            {
                                entry_.Value = ReadValue(chars_, pos, node & 0x7fff);
                            }
                            else
                            {
                                entry_.Value = ReadNodeValue(chars_, pos, node);
                            }
                            if (isFinal || (maxLength_ > 0 && str_.Length == maxLength_))
                            {
                                pos_ = -1;
                            }
                            else
                            {
                                // We cannot skip the value right here because it shares its
                                // lead unit with a match node which we have to evaluate
                                // next time.
                                // Instead, keep pos_ on the node lead unit itself.
                                pos_ = pos - 1;
                                skipValue_ = true;
                            }
                            entry_.Chars = str_.ToCharSequence();
                            return entry_;
                        }
                    }
                    if (maxLength_ > 0 && str_.Length == maxLength_)
                    {
                        return TruncateAndStop();
                    }
                    if (node < kMinLinearMatch)
                    {
                        if (node == 0)
                        {
                            node = chars_[pos++];
                        }
                        pos = BranchNext(pos, node + 1);
                        if (pos < 0)
                        {
                            return entry_;  // Reached a final value.
                        }
                    }
                    else
                    {
                        // Linear-match node, append length units to str_.
                        int length = node - kMinLinearMatch + 1;
                        if (maxLength_ > 0 && str_.Length + length > maxLength_)
                        {
                            str_.Append(chars_, pos, maxLength_ - str_.Length); // ICU4N: (pos + maxLength_ - str_.Length) - pos == (maxLength_ - str_.Length)
                            return TruncateAndStop();
                        }
                        str_.Append(chars_, pos, length); // ICU4N: (pos + length) - pos == length
                        pos += length;
                    }
                }
            }

            // ICU4N specific - Remove() not supported in .NET

            private Entry TruncateAndStop()
            {
                pos_ = -1;
                // We reset entry_.chars every time we return entry_
                // just because the caller might have modified the Entry.
                entry_.Chars = str_.ToCharSequence();
                entry_.Value = -1;  // no real value for str
                return entry_;
            }

            private int BranchNext(int pos, int length)
            {
                while (length > kMaxBranchLinearSubNodeLength)
                {
                    ++pos;  // ignore the comparison unit
                    // Push state for the greater-or-equal edge.
                    // ICU4N: Sign extended operand here is desirable, as that is what was happening in Java
                    stack_.Add(((long)SkipDelta(chars_, pos) << 32) | (uint)((length - (length >> 1)) << 16) | (uint)str_.Length);
                    // Follow the less-than edge.
                    length >>= 1;
                    pos = JumpByDelta(chars_, pos);
                }
                // List of key-value pairs where values are either final values or jump deltas.
                // Read the first (key, value) pair.
                char trieUnit = chars_[pos++];
                int node = chars_[pos++];
                bool isFinal = (node & kValueIsFinal) != 0;
                int value = ReadValue(chars_, pos, node &= 0x7fff);
                pos = SkipValue(pos, node);
                // ICU4N: Sign extended operand here is desirable, as that is what was happening in Java
                stack_.Add(((long)pos << 32) | (uint)((length - 1) << 16) | (uint)str_.Length);
                str_.Append(trieUnit);
                if (isFinal)
                {
                    pos_ = -1;
                    entry_.Chars = str_.ToCharSequence();
                    entry_.Value = value;
                    return -1;
                }
                else
                {
                    return pos + value;
                }
            }

            /// <summary>
            /// Gets the element in the collection at the current position of the enumerator.
            /// </summary>
            /// <stable>ICU 4.8</stable>
            public Entry Current
            {
                get { return current; }
            }

            object IEnumerator.Current
            {
                get { return current; }
            }

            /// <summary>
            /// Finds the next (string, value) pair if there is one.
            /// </summary>
            /// <remarks>
            /// If the string is truncated to the maximum length and does not
            /// have a real value, then the value is set to -1.
            /// In this case, this "not a real value" is indistinguishable from
            /// a real value of -1.
            /// </remarks>
            /// <returns>Returns true if an element has been set to <see cref="Current"/>; otherwise false.</returns>
            /// <stable>ICU 4.8</stable>
            public bool MoveNext()
            {
                if (!HasNext())
                    return false;
                current = Next();
                return (current != null);
            }

            void IEnumerator.Reset() // ICU4N specific - expicit interface declaration for .NET compatibility
            {
                Reset();
            }

            public void Dispose()
            {
                // nothing to do
            }

            private ICharSequence chars_;
            private int pos_;
            private int initialPos_;
            private int remainingMatchLength_;
            private int initialRemainingMatchLength_;
            private bool skipValue_;  // Skip intermediate value which was already delivered.

            private StringBuilder str_ = new StringBuilder();
            private int maxLength_;
            private Entry entry_ = new Entry();

            // The stack stores longs for backtracking to another
            // outbound edge of a branch node.
            // Each long has the offset in chars_ in bits 62..32,
            // the str_.length() from before the node in bits 15..0,
            // and the remaining branch length in bits 31..16.
            // (We could store the remaining branch length minus 1 in bits 30..16 and not use bit 31,
            // but the code looks more confusing that way.)
            private List<long> stack_ = new List<long>();
        }

        private void Stop()
        {
            pos_ = -1;
        }

        // Reads a compact 32-bit integer.
        // pos is already after the leadUnit, and the lead unit has bit 15 reset.
        private static int ReadValue(ICharSequence chars, int pos, int leadUnit)
        {
            int value;
            if (leadUnit < kMinTwoUnitValueLead)
            {
                value = leadUnit;
            }
            else if (leadUnit < kThreeUnitValueLead)
            {
                value = ((leadUnit - kMinTwoUnitValueLead) << 16) | chars[pos];
            }
            else
            {
                value = (chars[pos] << 16) | chars[pos + 1];
            }
            return value;
        }
        private static int SkipValue(int pos, int leadUnit)
        {
            if (leadUnit >= kMinTwoUnitValueLead)
            {
                if (leadUnit < kThreeUnitValueLead)
                {
                    ++pos;
                }
                else
                {
                    pos += 2;
                }
            }
            return pos;
        }
        private static int SkipValue(ICharSequence chars, int pos)
        {
            int leadUnit = chars[pos++];
            return SkipValue(pos, leadUnit & 0x7fff);
        }

        private static int ReadNodeValue(ICharSequence chars, int pos, int leadUnit)
        {
            Debug.Assert(kMinValueLead <= leadUnit && leadUnit < kValueIsFinal);
            int value;
            if (leadUnit < kMinTwoUnitNodeValueLead)
            {
                value = (leadUnit >> 6) - 1;
            }
            else if (leadUnit < kThreeUnitNodeValueLead)
            {
                value = (((leadUnit & 0x7fc0) - kMinTwoUnitNodeValueLead) << 10) | chars[pos];
            }
            else
            {
                value = (chars[pos] << 16) | chars[pos + 1];
            }
            return value;
        }
        private static int SkipNodeValue(int pos, int leadUnit)
        {
            Debug.Assert(kMinValueLead <= leadUnit && leadUnit < kValueIsFinal);
            if (leadUnit >= kMinTwoUnitNodeValueLead)
            {
                if (leadUnit < kThreeUnitNodeValueLead)
                {
                    ++pos;
                }
                else
                {
                    pos += 2;
                }
            }
            return pos;
        }

        private static int JumpByDelta(ICharSequence chars, int pos)
        {
            int delta = chars[pos++];
            if (delta >= kMinTwoUnitDeltaLead)
            {
                if (delta == kThreeUnitDeltaLead)
                {
                    delta = (chars[pos] << 16) | chars[pos + 1];
                    pos += 2;
                }
                else
                {
                    delta = ((delta - kMinTwoUnitDeltaLead) << 16) | chars[pos++];
                }
            }
            return pos + delta;
        }

        private static int SkipDelta(ICharSequence chars, int pos)
        {
            int delta = chars[pos++];
            if (delta >= kMinTwoUnitDeltaLead)
            {
                if (delta == kThreeUnitDeltaLead)
                {
                    pos += 2;
                }
                else
                {
                    ++pos;
                }
            }
            return pos;
        }

        private static Result[] valueResults_ = { Result.IntermediateValue, Result.FinalValue };

        // Handles a branch node for both next(unit) and next(string).
        private Result BranchNext(int pos, int length, int inUnit)
        {
            // Branch according to the current unit.
            if (length == 0)
            {
                length = chars_[pos++];
            }
            ++length;
            // The length of the branch is the number of units to select from.
            // The data structure encodes a binary search.
            while (length > kMaxBranchLinearSubNodeLength)
            {
                if (inUnit < chars_[pos++])
                {
                    length >>= 1;
                    pos = JumpByDelta(chars_, pos);
                }
                else
                {
                    length = length - (length >> 1);
                    pos = SkipDelta(chars_, pos);
                }
            }
            // Drop down to linear search for the last few units.
            // length>=2 because the loop body above sees length>kMaxBranchLinearSubNodeLength>=3
            // and divides length by 2.
            do
            {
                if (inUnit == chars_[pos++])
                {
                    Result result;
                    int node = chars_[pos];
                    if ((node & kValueIsFinal) != 0)
                    {
                        // Leave the final value for getValue() to read.
                        result = Result.FinalValue;
                    }
                    else
                    {
                        // Use the non-final value as the jump delta.
                        ++pos;
                        // int delta=readValue(pos, node);
                        int delta;
                        if (node < kMinTwoUnitValueLead)
                        {
                            delta = node;
                        }
                        else if (node < kThreeUnitValueLead)
                        {
                            delta = ((node - kMinTwoUnitValueLead) << 16) | chars_[pos++];
                        }
                        else
                        {
                            delta = (chars_[pos] << 16) | chars_[pos + 1];
                            pos += 2;
                        }
                        // end readValue()
                        pos += delta;
                        node = chars_[pos];
                        result = node >= kMinValueLead ? valueResults_[node >> 15] : Result.NoValue;
                    }
                    pos_ = pos;
                    return result;
                }
                --length;
                pos = SkipValue(chars_, pos);
            } while (length > 1);
            if (inUnit == chars_[pos++])
            {
                pos_ = pos;
                int node = chars_[pos];
                return node >= kMinValueLead ? valueResults_[node >> 15] : Result.NoValue;
            }
            else
            {
                Stop();
                return Result.NoMatch;
            }
        }

        // Requires remainingLength_<0.
        private Result NextImpl(int pos, int inUnit)
        {
            int node = chars_[pos++];
            for (; ; )
            {
                if (node < kMinLinearMatch)
                {
                    return BranchNext(pos, node, inUnit);
                }
                else if (node < kMinValueLead)
                {
                    // Match the first of length+1 units.
                    int length = node - kMinLinearMatch;  // Actual match length minus 1.
                    if (inUnit == chars_[pos++])
                    {
                        remainingMatchLength_ = --length;
                        pos_ = pos;
                        return (length < 0 && (node = chars_[pos]) >= kMinValueLead) ?
                                valueResults_[node >> 15] : Result.NoValue;
                    }
                    else
                    {
                        // No match.
                        break;
                    }
                }
                else if ((node & kValueIsFinal) != 0)
                {
                    // No further matching units.
                    break;
                }
                else
                {
                    // Skip intermediate value.
                    pos = SkipNodeValue(pos, node);
                    node &= kNodeTypeMask;
                }
            }
            Stop();
            return Result.NoMatch;
        }

        // Helper functions for getUniqueValue().
        // Recursively finds a unique value (or whether there is not a unique one)
        // from a branch.
        // uniqueValue: On input, same as for getUniqueValue()/findUniqueValue().
        // On return, if not 0, then bits 63..33 contain the updated non-negative pos.
        private static long FindUniqueValueFromBranch(ICharSequence chars, int pos, int length,
                                                      long uniqueValue)
        {
            while (length > kMaxBranchLinearSubNodeLength)
            {
                ++pos;  // ignore the comparison unit
                uniqueValue = FindUniqueValueFromBranch(chars, JumpByDelta(chars, pos), length >> 1, uniqueValue);
                if (uniqueValue == 0)
                {
                    return 0;
                }
                length = length - (length >> 1);
                pos = SkipDelta(chars, pos);
            }
            do
            {
                ++pos;  // ignore a comparison unit
                        // handle its value
                int node = chars[pos++];
                bool isFinal = (node & kValueIsFinal) != 0;
                node &= 0x7fff;
                int value = ReadValue(chars, pos, node);
                pos = SkipValue(pos, node);
                if (isFinal)
                {
                    if (uniqueValue != 0)
                    {
                        if (value != (int)(uniqueValue >> 1))
                        {
                            return 0;
                        }
                    }
                    else
                    {
                        uniqueValue = ((long)value << 1) | 1;
                    }
                }
                else
                {
                    uniqueValue = FindUniqueValue(chars, pos + value, uniqueValue);
                    if (uniqueValue == 0)
                    {
                        return 0;
                    }
                }
            } while (--length > 1);
            // ignore the last comparison byte
            return ((long)(pos + 1) << 33) | (uniqueValue & 0x1ffffffffL);
        }
        // Recursively finds a unique value (or whether there is not a unique one)
        // starting from a position on a node lead unit.
        // uniqueValue: If there is one, then bits 32..1 contain the value and bit 0 is set.
        // Otherwise, uniqueValue is 0. Bits 63..33 are ignored.
        private static long FindUniqueValue(ICharSequence chars, int pos, long uniqueValue)
        {
            int node = chars[pos++];
            for (; ; )
            {
                if (node < kMinLinearMatch)
                {
                    if (node == 0)
                    {
                        node = chars[pos++];
                    }
                    uniqueValue = FindUniqueValueFromBranch(chars, pos, node + 1, uniqueValue);
                    if (uniqueValue == 0)
                    {
                        return 0;
                    }
                    pos = (int)(uniqueValue.TripleShift(33));
                    node = chars[pos++];
                }
                else if (node < kMinValueLead)
                {
                    // linear-match node
                    pos += node - kMinLinearMatch + 1;  // Ignore the match units.
                    node = chars[pos++];
                }
                else
                {
                    bool isFinal = (node & kValueIsFinal) != 0;
                    int value;
                    if (isFinal)
                    {
                        value = ReadValue(chars, pos, node & 0x7fff);
                    }
                    else
                    {
                        value = ReadNodeValue(chars, pos, node);
                    }
                    if (uniqueValue != 0)
                    {
                        if (value != (int)(uniqueValue >> 1))
                        {
                            return 0;
                        }
                    }
                    else
                    {
                        uniqueValue = ((long)value << 1) | 1;
                    }
                    if (isFinal)
                    {
                        return uniqueValue;
                    }
                    pos = SkipNodeValue(pos, node);
                    node &= kNodeTypeMask;
                }
            }
        }

        // Helper functions for getNextChars().
        // getNextChars() when pos is on a branch node.
        private static void GetNextBranchChars(ICharSequence chars, int pos, int length, StringBuilder output)
        {
            while (length > kMaxBranchLinearSubNodeLength)
            {
                ++pos;  // ignore the comparison unit
                GetNextBranchChars(chars, JumpByDelta(chars, pos), length >> 1, output);
                length = length - (length >> 1);
                pos = SkipDelta(chars, pos);
            }
            do
            {
                Append(output, chars[pos++]);
                pos = SkipValue(chars, pos);
            } while (--length > 1);
            Append(output, chars[pos]);
        }
        private static void Append(StringBuilder output, int c)
        {
            try
            {
                output.Append((char)c);
            }
            catch (IOException e)
            {
                throw new ICUUncheckedIOException(e);
            }
        }

        // CharsTrie data structure
        //
        // The trie consists of a series of char-serialized nodes for incremental
        // Unicode string/char sequence matching. (char=16-bit unsigned integer)
        // The root node is at the beginning of the trie data.
        //
        // Types of nodes are distinguished by their node lead unit ranges.
        // After each node, except a final-value node, another node follows to
        // encode match values or continue matching further units.
        //
        // Node types:
        //  - Final-value node: Stores a 32-bit integer in a compact, variable-length format.
        //    The value is for the string/char sequence so far.
        //  - Match node, optionally with an intermediate value in a different compact format.
        //    The value, if present, is for the string/char sequence so far.
        //
        //  Aside from the value, which uses the node lead unit's high bits:
        //
        //  - Linear-match node: Matches a number of units.
        //  - Branch node: Branches to other nodes according to the current input unit.
        //    The node unit is the length of the branch (number of units to select from)
        //    minus 1. It is followed by a sub-node:
        //    - If the length is at most kMaxBranchLinearSubNodeLength, then
        //      there are length-1 (key, value) pairs and then one more comparison unit.
        //      If one of the key units matches, then the value is either a final value for
        //      the string so far, or a "jump" delta to the next node.
        //      If the last unit matches, then matching continues with the next node.
        //      (Values have the same encoding as final-value nodes.)
        //    - If the length is greater than kMaxBranchLinearSubNodeLength, then
        //      there is one unit and one "jump" delta.
        //      If the input unit is less than the sub-node unit, then "jump" by delta to
        //      the next sub-node which will have a length of length/2.
        //      (The delta has its own compact encoding.)
        //      Otherwise, skip the "jump" delta to the next sub-node
        //      which will have a length of length-length/2.

        // Match-node lead unit values, after masking off intermediate-value bits:

        // 0000..002f: Branch node. If node!=0 then the length is node+1, otherwise
        // the length is one more than the next unit.

        // For a branch sub-node with at most this many entries, we drop down
        // to a linear search.
        /*package*/
        internal static readonly int kMaxBranchLinearSubNodeLength = 5;

        // 0030..003f: Linear-match node, match 1..16 units and continue reading the next node.
        /*package*/
        internal static readonly int kMinLinearMatch = 0x30;
        /*package*/
        internal static readonly int kMaxLinearMatchLength = 0x10;

        // Match-node lead unit bits 14..6 for the optional intermediate value.
        // If these bits are 0, then there is no intermediate value.
        // Otherwise, see the *NodeValue* constants below.
        /*package*/
        internal static readonly int kMinValueLead = kMinLinearMatch + kMaxLinearMatchLength;  // 0x0040
                                                                                               /*package*/
        internal static readonly int kNodeTypeMask = kMinValueLead - 1;  // 0x003f

        // A final-value node has bit 15 set.
        /*package*/
        internal static readonly int kValueIsFinal = 0x8000;

        // Compact value: After testing and masking off bit 15, use the following thresholds.
        /*package*/
        internal static readonly int kMaxOneUnitValue = 0x3fff;

        /*package*/
        internal static readonly int kMinTwoUnitValueLead = kMaxOneUnitValue + 1;  // 0x4000
                                                                                   /*package*/
        internal static readonly int kThreeUnitValueLead = 0x7fff;

        /*package*/
        internal static readonly int kMaxTwoUnitValue = ((kThreeUnitValueLead - kMinTwoUnitValueLead) << 16) - 1;  // 0x3ffeffff

        // Compact intermediate-value integer, lead unit shared with a branch or linear-match node.
        /*package*/
        internal static readonly int kMaxOneUnitNodeValue = 0xff;
        /*package*/
        internal static readonly int kMinTwoUnitNodeValueLead = kMinValueLead + ((kMaxOneUnitNodeValue + 1) << 6);  // 0x4040
                                                                                                                    /*package*/
        internal static readonly int kThreeUnitNodeValueLead = 0x7fc0;

        /*package*/
        internal static readonly int kMaxTwoUnitNodeValue =
            ((kThreeUnitNodeValueLead - kMinTwoUnitNodeValueLead) << 10) - 1;  // 0xfdffff

        // Compact delta integers.
        /*package*/
        internal static readonly int kMaxOneUnitDelta = 0xfbff;
        /*package*/
        internal static readonly int kMinTwoUnitDeltaLead = kMaxOneUnitDelta + 1;  // 0xfc00
                                                                                   /*package*/
        internal static readonly int kThreeUnitDeltaLead = 0xffff;

        /*package*/
        internal static readonly int kMaxTwoUnitDelta = ((kThreeUnitDeltaLead - kMinTwoUnitDeltaLead) << 16) - 1;  // 0x03feffff

        // Fixed value referencing the CharsTrie words.
        private ICharSequence chars_;
        private int root_;

        // Iterator variables.

        // Pointer to next trie unit to read. NULL if no more matches.
        private int pos_;
        // Remaining length of a linear-match node, minus 1. Negative if not in such a node.
        private int remainingMatchLength_;
    }
}
