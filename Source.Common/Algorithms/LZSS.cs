﻿// C# implementation of Valve's implementation of LZSS. Complete with C++ memory safety hell

using System.Runtime.CompilerServices;

namespace Source.Common.Algorithms;

public struct lzss_header_t
{
	public uint id;
	public uint actualSize;    // always little endian
};
public unsafe struct lzss_node_t
{
	byte* pData;
	lzss_node_t* pPrev;
	lzss_node_t* pNext;
	public fixed byte empty[4];
};

public unsafe struct lzss_list_t
{
	lzss_node_t* pStart;
	lzss_node_t* pEnd;
};
public unsafe class CLZSS
{
	public const uint LZSS_ID = 'S' << 24 | 'S' << 16 | 'Z' << 8 | 'L';
	public const uint SNAPPY_ID = 'P' << 24 | 'A' << 16 | 'N' << 8 | 'S';

	private lzss_list_t* m_pHashTable;
	private lzss_node_t* m_pHashTarget;
	private int m_nWindowSize;

	public CLZSS(int nWindowSize = 4096) {
		m_nWindowSize = nWindowSize;
	}

	public static uint WordSwap(uint value) {
		ushort temp = BitConverter.ToUInt16(BitConverter.GetBytes(value), 0);
		temp = (ushort)(temp >> 8 | temp << 8);
		return Unsafe.As<ushort, uint>(ref temp);
	}
	public static uint DWordSwap(uint value) {
		uint temp = BitConverter.ToUInt32(BitConverter.GetBytes(value), 0);
		temp = temp >> 24 | (temp & 0x00FF0000) >> 8 | (temp & 0x0000FF00) << 8 | temp << 24;
		return Unsafe.As<uint, uint>(ref temp);
	}


	public const int LZSS_LOOKSHIFT = 4;
	public const int LZSS_LOOKAHEAD = 1 << LZSS_LOOKSHIFT;

	//-----------------------------------------------------------------------------
	// Returns true if buffer is compressed.
	//-----------------------------------------------------------------------------
	public static bool IsCompressed(byte* pInput) {
		lzss_header_t* pHeader = (lzss_header_t*)pInput;

		//printf("LOL: %x __ %x\n", pHeader->id, LZSS_ID);
		if (pHeader != null && pHeader->id == LZSS_ID) {
			return true;
		}

		// unrecognized
		return false;
	}

	//-----------------------------------------------------------------------------
	// Returns uncompressed size of compressed input buffer. Used for allocating output
	// buffer for decompression. Returns 0 if input buffer is not compressed.
	//-----------------------------------------------------------------------------
	public static uint GetActualSize(byte* pPtr) {
		lzss_header_t* pHeader = (lzss_header_t*)pPtr;
		if (pHeader != null && pHeader->id == LZSS_ID) {
			return pHeader->actualSize;
		}

		// unrecognized
		return 0;
	}
	public static uint GetActualSize(Span<byte> pInput) {
		fixed (byte* pPtr = pInput) {
			lzss_header_t* pHeader = (lzss_header_t*)pPtr;
			if (pHeader != null && pHeader->id == LZSS_ID) {
				return pHeader->actualSize;
			}

			// unrecognized
			return 0;
		}
	}


	//-----------------------------------------------------------------------------
	// Uncompress a buffer, Returns the uncompressed size. Caller must provide an
	// adequate sized output buffer or memory corruption will occur.
	//-----------------------------------------------------------------------------
	public static uint Uncompress(Span<byte> input, Span<byte> output) {
		uint totalBytes = 0;
		int cmdByte = 0;
		int getCmdByte = 0;

		uint actualSize = GetActualSize(input);
		if (actualSize == 0) {
			return 0;
		}

		int inputIndex = sizeof(lzss_header_t);
		int outputIndex = 0;

		while (true) {
			if (getCmdByte == 0) {
				cmdByte = input[inputIndex++];
			}
			getCmdByte = getCmdByte + 1 & 0x07;

			if ((cmdByte & 0x01) != 0) {
				int position = input[inputIndex++] << LZSS_LOOKSHIFT;
				position |= input[inputIndex] >> LZSS_LOOKSHIFT;
				int count = (input[inputIndex++] & 0x0F) + 1;
				if (count == 1) {
					break;
				}

				int sourceIndex = outputIndex - position - 1;
				for (int i = 0; i < count; i++) {
					output[outputIndex++] = output[sourceIndex++];
				}
				totalBytes += (uint)count;
			}
			else {
				output[outputIndex++] = input[inputIndex++];
				totalBytes++;
			}
			cmdByte >>= 1;
		}

		return totalBytes == actualSize ? totalBytes : 0;
	}

}