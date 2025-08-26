﻿
using Source.Common.Bitmap;

using System;
using System.Runtime.InteropServices;

namespace Source.Bitmap;

public struct RGBA8888
{
	public byte R;
	public byte G;
	public byte B;
	public byte A;
}

public struct BGRA8888
{
	public byte B;
	public byte G;
	public byte R;
	public byte A;
}

public static class ImageLoader
{
	static readonly ImageFormatInfo[] g_ImageFormatInfo = [
		new( "UNKNOWN",                    0, 0, 0, 0, 0, false ),			// IMAGE_FORMAT_UNKNOWN,
		new( "RGBA8888",                    4, 8, 8, 8, 8, false ),			// IMAGE_FORMAT_RGBA8888,
		new( "ABGR8888",                    4, 8, 8, 8, 8, false ),			// IMAGE_FORMAT_ABGR8888, 
		new( "RGB888",                      3, 8, 8, 8, 0, false ),			// IMAGE_FORMAT_RGB888,
		new( "BGR888",                      3, 8, 8, 8, 0, false ),			// IMAGE_FORMAT_BGR888,
		new( "RGB565",                      2, 5, 6, 5, 0, false ),			// IMAGE_FORMAT_RGB565, 
		new( "I8",                          1, 0, 0, 0, 0, false ),			// IMAGE_FORMAT_I8,
		new( "IA88",                        2, 0, 0, 0, 8, false ),			// IMAGE_FORMAT_IA88
		new( "P8",                          1, 0, 0, 0, 0, false ),			// IMAGE_FORMAT_P8
		new( "A8",                          1, 0, 0, 0, 8, false ),			// IMAGE_FORMAT_A8
		new( "RGB888_BLUESCREEN",           3, 8, 8, 8, 0, false ),			// IMAGE_FORMAT_RGB888_BLUESCREEN
		new( "BGR888_BLUESCREEN",           3, 8, 8, 8, 0, false ),			// IMAGE_FORMAT_BGR888_BLUESCREEN
		new( "ARGB8888",                    4, 8, 8, 8, 8, false ),			// IMAGE_FORMAT_ARGB8888
		new( "BGRA8888",                    4, 8, 8, 8, 8, false ),			// IMAGE_FORMAT_BGRA8888
		new( "DXT1",                        0, 0, 0, 0, 0, true ),			// IMAGE_FORMAT_DXT1
		new( "DXT3",                        0, 0, 0, 0, 8, true ),			// IMAGE_FORMAT_DXT3
		new( "DXT5",                        0, 0, 0, 0, 8, true ),			// IMAGE_FORMAT_DXT5
		new( "BGRX8888",                    4, 8, 8, 8, 0, false ),			// IMAGE_FORMAT_BGRX8888
		new( "BGR565",                      2, 5, 6, 5, 0, false ),			// IMAGE_FORMAT_BGR565
		new( "BGRX5551",                    2, 5, 5, 5, 0, false ),			// IMAGE_FORMAT_BGRX5551
		new( "BGRA4444",                    2, 4, 4, 4, 4, false ),			// IMAGE_FORMAT_BGRA4444
		new( "DXT1_ONEBITALPHA",            0, 0, 0, 0, 0, true ),			// IMAGE_FORMAT_DXT1_ONEBITALPHA
		new( "BGRA5551",                    2, 5, 5, 5, 1, false ),			// IMAGE_FORMAT_BGRA5551
		new( "UV88",                        2, 8, 8, 0, 0, false ),			// IMAGE_FORMAT_UV88
		new( "UVWQ8888",                    4, 8, 8, 8, 8, false ),			// IMAGE_FORMAT_UVWQ8899
		new( "RGBA16161616F",               8, 16, 16, 16, 16, false ),		// IMAGE_FORMAT_RGBA16161616F
		new( "RGBA16161616",                8, 16, 16, 16, 16, false ),		// IMAGE_FORMAT_RGBA16161616
		new( "IMAGE_FORMAT_UVLX8888",       4, 8, 8, 8, 8, false ),			// IMAGE_FORMAT_UVLX8899
		new( "IMAGE_FORMAT_R32F",           4, 32, 0, 0, 0, false ),		// IMAGE_FORMAT_R32F
		new( "IMAGE_FORMAT_RGB323232F", 12, 32, 32, 32, 0, false ),			// IMAGE_FORMAT_RGB323232F
		new( "IMAGE_FORMAT_RGBA32323232F",  16, 32, 32, 32, 32, false ),	// IMAGE_FORMAT_RGBA32323232F

		// Vendor-dependent depth formats used for shadow depth mapping
		new( "NV_DST16",                    2, 16, 0, 0, 0, false ),		// IMAGE_FORMAT_NV_DST16
		new( "NV_DST24",                    4, 24, 0, 0, 0, false ),		// IMAGE_FORMAT_NV_DST24
		new( "NV_INTZ",                 4,  8, 8, 8, 8, false ),			// IMAGE_FORMAT_NV_INTZ
		new( "NV_RAWZ",                 4, 24, 0, 0, 0, false ),			// IMAGE_FORMAT_NV_RAWZ
		new( "ATI_DST16",                   2, 16, 0, 0, 0, false ),		// IMAGE_FORMAT_ATI_DST16
		new( "ATI_DST24",                   4, 24, 0, 0, 0, false ),		// IMAGE_FORMAT_ATI_DST24
		new( "NV_NULL",                 4,  8, 8, 8, 8, false ),			// IMAGE_FORMAT_NV_NULL

		// Vendor-dependent compressed formats typically used for normal map compression
		new( "ATI1N",                       0, 0, 0, 0, 0, true ),			// IMAGE_FORMAT_ATI1N
		new( "ATI2N",                       0, 0, 0, 0, 0, true ),			// IMAGE_FORMAT_ATI2N

		new( "DXT1_RUNTIME",                0, 0, 0, 0, 0, true ),			// IMAGE_FORMAT_DXT1_RUNTIME
		new( "DXT5_RUNTIME",                0, 0, 0, 0, 8, true ),			// IMAGE_FORMAT_DXT5_RUNTIME
	];
	public static ref ImageFormatInfo ImageFormatInfo(this ImageFormat fmt) {
		return ref g_ImageFormatInfo[(int)fmt + 1];
	}
	public static string GetName(this ImageFormat fmt) {
		return ImageFormatInfo(fmt).Name;
	}
	public static int SizeInBytes(this ImageFormat fmt) {
		return ImageFormatInfo(fmt).Bytes;
	}
	public static bool IsTransparent(this ImageFormat fmt) {
		return ImageFormatInfo(fmt).AlphaBits > 0;
	}
	public static bool IsCompressed(this ImageFormat fmt) {
		return ImageFormatInfo(fmt).IsCompressed;
	}
	public static bool IsRuntimeCompressed(this ImageFormat fmt) {
		return fmt == ImageFormat.DXT1_Runtime || fmt == ImageFormat.DXT5_Runtime;
	}
	public static int GetMemRequired(int width, int height, int depth, ImageFormat format, bool mipmap) {
		if (depth <= 0)
			depth = 1;

		if (!mipmap) {
			if (IsCompressed(format)) {
				Dbg.Assert(width < 4 || (width % 4) == 0);
				Dbg.Assert(height < 4 || (height % 4) == 0);
				Dbg.Assert(depth < 4 || (depth % 4) == 0);

				if (width < 4 && width > 0)
					width = 4;
				if (height < 4 && height > 0)
					height = 4;
				if (depth < 4 && depth > 1)
					depth = 4;

				int numBlocks = (width * height) >> 4;
				numBlocks *= depth;
				switch (format) {
					case ImageFormat.DXT1:
					case ImageFormat.DXT1_Runtime:
					case ImageFormat.ATI1N:
						return numBlocks * 8;

					case ImageFormat.DXT3:
					case ImageFormat.DXT5:
					case ImageFormat.DXT5_Runtime:
					case ImageFormat.ATI2N:
						return numBlocks * 16;
				}

				Dbg.Assert(false);
				return 0;
			}

			return width * height * depth * SizeInBytes(format);
		}

		int memSize = 0;
		while (true) {
			memSize += GetMemRequired(width, height, depth, format, false);
			if (width == 1 && height == 1 && depth == 1)
				break;

			width >>= 1;
			height >>= 1;
			depth >>= 1;

			if (width < 1)
				width = 1;
			if (height < 1)
				height = 1;
			if (depth < 1)
				depth = 1;
		}

		return memSize;
	}

	public static int GetMipMapLevelByteOffset(int width, int height, ImageFormat format, int skipMipLevels) {
		int offset = 0;
		while (skipMipLevels > 0) {
			offset += (width * height * SizeInBytes(format));
			if (width == 1 && height == 1)
				break;

			width >>= 1;
			height >>= 1;

			if (width < 1)
				width = 1;
			if (height < 1)
				height = 1;

			skipMipLevels--;
		}

		return offset;
	}
	public static void GetMipMapLevelDimensions(ref int width, ref int height, int skipMipLevels) {
		while (skipMipLevels > 0) {
			if (width == 1 && height == 1)
				break;

			width >>= 1;
			height >>= 1;

			if (width < 1)
				width = 1;
			if (height < 1)
				height = 1;

			skipMipLevels--;
		}
	}

	public static int GetNumMipMapLevels(int width, int height, int depth) {
		if (depth <= 0)
			depth = 1;

		if (width < 1 || height < 1)
			return 0;

		int mipLevels = 1;
		while (true) {
			if (width == 1 && height == 1 && depth == 1)
				break;

			width >>= 1;
			height >>= 1;
			depth >>= 1;

			if (width < 1)
				width = 1;

			if (height < 1)
				height = 1;

			if (depth < 1)
				depth = 1;

			mipLevels++;
		}

		return mipLevels;
	}

	const int GL_RGBA32F = 0x8814;
	const int GL_RGB32F = 0x8815;
	const int GL_RGBA16F = 0x881A;
	const int GL_COMPRESSED_RED_RGTC1 = 0x8DBB;
	const int GL_COMPRESSED_RG_RGTC2 = 0x8DBD;
	const int GL_R8 = 0x8229;
	const int GL_RG8 = 0x822B;
	const int GL_R32F = 0x822E;
	const int GL_RGB565 = 0x8D62;
	const int GL_COMPRESSED_RGBA_S3TC_DXT1_EXT = 0x83F1;
	const int GL_COMPRESSED_RGBA_S3TC_DXT3_EXT = 0x83F2;
	const int GL_COMPRESSED_RGBA_S3TC_DXT5_EXT = 0x83F3;
	const int GL_RGB8 = 0x8051;
	const int GL_RGBA4 = 0x8056;
	const int GL_RGB5_A1 = 0x8057;
	const int GL_RGBA8 = 0x8058;
	const int GL_BGRA8_EXT = 0x93A1;
	const int GL_RGBA16 = 0x805B;
	const int GL_DEPTH_COMPONENT16 = 0x81A5;
	const int GL_DEPTH_COMPONENT24 = 0x81A6;
	const int GL_DEPTH_COMPONENT32 = 0x81A7;

	// An uncomfortable amount of this is guessing...
	// The various forms of RGBA rearranged needs transmutating - we'll figure that out later as well.
	public static int GetGLImageFormat(ImageFormat format) => format switch {
		// Uncompressed color formats
		ImageFormat.RGBA8888 => GL_RGBA8,
		ImageFormat.ABGR8888 => GL_RGBA8,
		ImageFormat.RGB888 => GL_RGBA8,
		ImageFormat.BGR888 => GL_RGBA8,
		ImageFormat.RGB565 => GL_RGB565,
		ImageFormat.BGR565 => GL_RGB565,
		ImageFormat.I8 => GL_R8,
		ImageFormat.IA88 => GL_RG8,
		ImageFormat.A8 => GL_RGBA8,
		ImageFormat.RGB888_Bluescreen => GL_RGB8,
		ImageFormat.BGR888_Bluescreen => GL_RGB8,
		ImageFormat.ARGB8888 => GL_RGBA8,
		ImageFormat.BGRA8888 => GL_BGRA8_EXT,
		ImageFormat.BGRX8888 => GL_RGBA8,
		ImageFormat.BGRX5551 => GL_RGBA8,
		ImageFormat.BGRA4444 => GL_RGBA4,
		ImageFormat.BGRA5551 => GL_RGB5_A1,
		ImageFormat.RGBA16161616 => GL_RGBA16,
		ImageFormat.RGBA16161616F => GL_RGBA16F,
		ImageFormat.R32F => GL_R32F,
		ImageFormat.RGB323232F => GL_RGB32F,
		ImageFormat.RGBA32323232F => GL_RGBA32F,

		// Compressed DXT formats
		ImageFormat.DXT1 => GL_COMPRESSED_RGBA_S3TC_DXT1_EXT,
		ImageFormat.DXT3 => GL_COMPRESSED_RGBA_S3TC_DXT3_EXT,
		ImageFormat.DXT5 => GL_COMPRESSED_RGBA_S3TC_DXT5_EXT,
		ImageFormat.DXT1_OneBitAlpha => GL_COMPRESSED_RGBA_S3TC_DXT1_EXT,
		ImageFormat.DXT1_Runtime => GL_COMPRESSED_RGBA_S3TC_DXT1_EXT,
		ImageFormat.DXT5_Runtime => GL_COMPRESSED_RGBA_S3TC_DXT5_EXT,

		// ATI compressed normal maps
		ImageFormat.ATI2N => GL_COMPRESSED_RG_RGTC2,
		ImageFormat.ATI1N => GL_COMPRESSED_RED_RGTC1,

		// Depth-stencil
		ImageFormat.NV_DST16 => GL_DEPTH_COMPONENT16,
		ImageFormat.NV_DST24 => GL_DEPTH_COMPONENT24,
		ImageFormat.NV_IntZ => GL_DEPTH_COMPONENT24,
		ImageFormat.NV_RawZ => GL_DEPTH_COMPONENT32,
		ImageFormat.ATI_DST16 => GL_DEPTH_COMPONENT16,
		ImageFormat.ATI_DST24 => GL_DEPTH_COMPONENT24,
		ImageFormat.NV_NULL => 0,      // dummy, no storage

		_ => throw new NotSupportedException($"GetGLImageFormat: unexpected format '{format}'"),
	};

	public static bool ConvertImageFormat(Span<byte> srcData, ImageFormat srcFormat, Span<byte> dstData, ImageFormat dstFormat, int width, int height) {
		switch (srcFormat) {
			case ImageFormat.DXT5:
				switch (dstFormat) {
					case ImageFormat.RGBA8888:
						ConvertFromDXT5(srcData, MemoryMarshal.Cast<byte, RGBA8888>(dstData), width, height);
						return true;
				}
				break;
		}

		AssertMsg(false, $"No good way to convert {srcFormat} to {dstFormat}, expect issues\n");
		return false;
	}

	private static void ConvertFromDXT5<T>(Span<byte> srcData, Span<T> span, int width, int height) {
		int realWidth = 0;
		int realHeight = 0;

		if (width < 4 || height < 4)
			Assert(false);

		Assert((width % 4) == 0);
		Assert((height % 4) == 0);

		int xblocks = width >> 2, yblocks = height >> 2;
		BGRA8888 col0, col1, col2, col3;

		throw new Exception("I don't care enough to implement ConvertFromDXT5 right now will do it later");
	}
}