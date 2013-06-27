﻿#region BSD Licence
/* Copyright (c) 2013, Doxense SARL
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:
	* Redistributions of source code must retain the above copyright
	  notice, this list of conditions and the following disclaimer.
	* Redistributions in binary form must reproduce the above copyright
	  notice, this list of conditions and the following disclaimer in the
	  documentation and/or other materials provided with the distribution.
	* Neither the name of Doxense nor the
	  names of its contributors may be used to endorse or promote products
	  derived from this software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL <COPYRIGHT HOLDER> BE LIABLE FOR ANY
DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */
#endregion

namespace FoundationDB.Client
{
	using FoundationDB.Layers.Tuples;
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using System.Text;

	/// <summary>Factory class for keys</summary>
	public static class FdbKey
	{
		/// <summary>Smallest possible key ('\0')</summary>
		public static readonly Slice MinValue = Slice.FromByte(0);

		/// <summary>Bigest possible key ('\xFF'), excluding the system keys</summary>
		public static readonly Slice MaxValue = Slice.FromByte(255);

		/// <summary>Converts an ASCII string into a binary slice (ony byte per character)</summary>
		/// <param name="value">String that should only contain ASCII characters (0..127)</param>
		/// <returns>Slice with ony byte per character</returns>
		/// <remarks>All non-ASCII chars will be truncated to the lower 8 bits !!</remarks>
		public static Slice Ascii(string value)
		{
			if (string.IsNullOrEmpty(value)) throw new ArgumentException("Key cannot be null or emtpy", "value");

			//TODO: how to handle non-ASCII chars ? (>=128)
			// => we should throw if char >= 128, to force use of unicode string for everything that is not a keyword !

			var bytes = new byte[value.Length];
			for (int i = 0; i < value.Length; i++)
			{
				bytes[i] = (byte)value[i];
			}
			return Slice.Create(bytes);
		}

		/// <summary>Decode an ASCII encoded key into a string</summary>
		/// <param name="key">ASCII bytes</param>
		/// <returns>Decoded string</returns>
		public static string Ascii(Slice key)
		{
			return key.ToAscii();
		}

		/// <summary>Converts a string into an UTF-8 encoded key</summary>
		/// <param name="value">String to convert</param>
		/// <returns>Key that is the UTF-8 representation of the string</returns>
		public static Slice Unicode(string value)
		{
			if (string.IsNullOrEmpty(value)) throw new ArgumentNullException("value");
			return Slice.Create(Encoding.UTF8.GetBytes(value));
		}

		/// <summary>Decode an UTF-8 encoded key into a string</summary>
		/// <param name="key">UTF-8 bytes</param>
		/// <returns>Decoded string</returns>
		public static string Unicode(Slice key)
		{
			return key.ToUnicode();
		}

		public static Slice Binary(byte[] value)
		{
			if (value == null || value.Length == 0) throw new ArgumentException("Key cannot be null or empty", "value");
			return Slice.Create(value);
		}

		public static Slice Binary(byte[] data, int offset, int count)
		{
			if (data == null || count == 0) throw new ArgumentException("Key cannot be null or empty", "value");
			return Slice.Create(data, offset, count);
		}

		/// <summary>Increment the last byte of the slice</summary>
		/// <param name="slice">Slice to increment</param>
		/// <returns>New slice that is guaranteed to be lexicographically higher than <paramref name="slice"/>. If <paramref name="Slice"/> is empty, then the slice [ 0x00 ] is returned.</returns>
		/// <remarks>If the last byte is already equal to 0xFF, it will rollover to 0x00 and the next byte will be incremented.</remarks>
		/// <exception cref="System.ArgumentException">If the Slice is equal to Slice.Nil</exception>
		/// <exception cref="System.OverflowException">If the Slice is equal to [ 0xFF ] (maximum allowed key)</exception>
		/// <example>
		/// FdbKey.Increment(Slice.FromString("ABC")) => "ABD"
		/// FdbKey.Increment(Slice.FromHexa("01 FF")) => { 02 00 }
		/// </example>
		public static Slice Increment(Slice slice)
		{
			if (!slice.HasValue) throw new ArgumentException("Cannot increment null buffer");

			// "" => "\x00" ?
			if (slice.Count == 0) return Slice.FromByte(0);

			// TODO: let say we have Increment({xx yy 00 FF})
			// Should we return {xx yy 01 00}, or should we remove the trailing 0 and return {xx yy 01} instead ?
			// Since {xx yy 00 FF} < {xx yy 01} < {xx yy 01 00}, the smaller one is "closer" to the key than the larger one...

			// copy the bytes from initial slice
			var tmp = slice.GetBytes();

			int p = tmp.Length - 1;
			while (p >= 0)
			{
				byte c = (byte)((tmp[p] + 1) & 0xFF);
				if (c > 0)
				{
					tmp[p] = c;
					break;
				}
				tmp[p] = 0;
				--p;
			}

			if (p < 0) throw new OverflowException("Cannot increment FdbKey because it is already equal to the maximum possible value (0xFF)");
			return new Slice(tmp, 0, tmp.Length);
		}

		/// <summary>Split a buffer containing multiple contiguous segments into an array of segments</summary>
		/// <param name="buffer">Buffer containing all the segments</param>
		/// <param name="start">Offset of the start of the first segment</param>
		/// <param name="endOffsets">Array containing, for each segment, the offset of the following segment</param>
		/// <returns>Array of segments</returns>
		/// <example>SplitIntoSegments("HelloWorld", 0, [5, 10]) => [{"Hello"}, {"World"}]</example>
		internal static Slice[] SplitIntoSegments(byte[] buffer, int start, List<int> endOffsets)
		{
			var result = new Slice[endOffsets.Count];
			int i = 0;
			int p = start;
			foreach (var end in endOffsets)
			{
				result[i++] = new Slice(buffer, p, end - p);
				p = end;
			}

			return result;
		}

		/// <summary>Split a range of indexes into several batches</summary>
		/// <param name="offset">Offset from which to start counting</param>
		/// <param name="count">Total number of values that will be returned</param>
		/// <param name="batchSize">Maximum size of each batch</param>
		/// <returns>Collection of B batches each containing at most <paramref name="batchSize"/> contiguous indices, counting from <paramref name="offset"/> to (<paramref name="offset"/> + <paramref name="count"/> - 1)</returns>
		/// <example>Batched(0, 100, 20) => [ {0..19}, {20..39}, {40..59}, {60..79}, {80..99} ]</example>
		public static IEnumerable<IEnumerable<int>> Batched(int offset, int count, int batchSize)
		{
			while (count > 0)
			{
				int chunk = Math.Min(count, batchSize);
				yield return Enumerable.Range(offset, chunk);
				offset += chunk;
				count -= chunk;
			}
		}

		private sealed class BatchIterator : IEnumerable<IEnumerable<KeyValuePair<int, int>>>
		{
			private readonly object m_lock = new object();
			private int m_cursor;
			private int m_remaining;

			private readonly int m_offset;
			private readonly int m_count;
			private readonly int m_workers;
			private readonly int m_batchSize;

			public BatchIterator(int offset, int count, int workers, int batchSize)
			{
				m_offset = offset;
				m_count = count;
				m_workers = workers;
				m_batchSize = batchSize;

				m_cursor = offset;
				m_remaining = count;
			}

			private KeyValuePair<int, int> GetChunk()
			{
				if (m_remaining == 0) return default(KeyValuePair<int, int>);

				lock (m_lock)
				{
					int cursor = m_cursor;
					int size = Math.Min(m_remaining, m_batchSize);

					m_cursor += size;
					m_remaining -= size;

					return new KeyValuePair<int, int>(cursor, size);
				}
			}


			public IEnumerator<IEnumerable<KeyValuePair<int, int>>> GetEnumerator()
			{
				for (int k = 0; k < m_workers; k++)
				{
					if (m_remaining == 0) yield break;
					yield return WorkerIterator(k);
				}
			}

			private IEnumerable<KeyValuePair<int, int>> WorkerIterator(int k)
			{
				while (true)
				{
					var chunk = GetChunk();
					if (chunk.Value == 0) break;
					yield return chunk;
				}
			}

			System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
			{
				return this.GetEnumerator();
			}
		}

		public static IEnumerable<IEnumerable<KeyValuePair<int, int>>> Batched(int offset, int count, int workers, int batchSize)
		{
			return new BatchIterator(offset, count, workers, batchSize);
		}

	}

}