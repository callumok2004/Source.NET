using NeoVeldrid;

using Source.Common.Bitmap;

namespace Source.ShaderAPI.Veldrid;

public static class VeldridTextureFormat
{
	public static bool TryMap(ImageFormat format, bool srgb, out VdPixelFormat pixelFormat) {
		switch (format) {
			case ImageFormat.RGBA8888:
				pixelFormat = srgb ? VdPixelFormat.R8_G8_B8_A8_UNorm_SRgb : VdPixelFormat.R8_G8_B8_A8_UNorm;
				return true;
			case ImageFormat.BGRA8888:
			case ImageFormat.BGRX8888:
				pixelFormat = srgb ? VdPixelFormat.B8_G8_R8_A8_UNorm_SRgb : VdPixelFormat.B8_G8_R8_A8_UNorm;
				return true;
			case ImageFormat.A8:
				pixelFormat = VdPixelFormat.R8_UNorm;
				return true;
			case ImageFormat.I8:
				pixelFormat = VdPixelFormat.R8_UNorm;
				return true;
			case ImageFormat.IA88:
				pixelFormat = VdPixelFormat.R8_G8_UNorm;
				return true;
			case ImageFormat.DXT1:
			case ImageFormat.DXT1_OneBitAlpha:
				pixelFormat = srgb ? VdPixelFormat.BC1_Rgba_UNorm_SRgb : VdPixelFormat.BC1_Rgba_UNorm;
				return true;
			case ImageFormat.DXT3:
				pixelFormat = srgb ? VdPixelFormat.BC2_UNorm_SRgb : VdPixelFormat.BC2_UNorm;
				return true;
			case ImageFormat.DXT5:
				pixelFormat = srgb ? VdPixelFormat.BC3_UNorm_SRgb : VdPixelFormat.BC3_UNorm;
				return true;
			case ImageFormat.ATI1N:
				pixelFormat = VdPixelFormat.BC4_UNorm;
				return true;
			case ImageFormat.ATI2N:
				pixelFormat = VdPixelFormat.BC5_UNorm;
				return true;
			case ImageFormat.RGBA16161616F:
				pixelFormat = VdPixelFormat.R16_G16_B16_A16_Float;
				return true;
			case ImageFormat.RGBA16161616:
				pixelFormat = VdPixelFormat.R16_G16_B16_A16_UNorm;
				return true;
			case ImageFormat.R32F:
				pixelFormat = VdPixelFormat.R32_Float;
				return true;
			case ImageFormat.RGBA32323232F:
				pixelFormat = VdPixelFormat.R32_G32_B32_A32_Float;
				return true;
			case ImageFormat.NV_DST24:
			case ImageFormat.ATI_DST24:
				pixelFormat = VdPixelFormat.D24_UNorm_S8_UInt;
				return true;
			case ImageFormat.NV_DST16:
			case ImageFormat.ATI_DST16:
				pixelFormat = VdPixelFormat.R16_UNorm;
				return true;
			default:
				pixelFormat = srgb ? VdPixelFormat.R8_G8_B8_A8_UNorm_SRgb : VdPixelFormat.R8_G8_B8_A8_UNorm;
				return false;
		}
	}

	public static ImageFormat NearestSupported(ImageFormat format) => TryMap(format, false, out _) ? format : ImageFormat.RGBA8888;

	public static bool IsDepthFormat(VdPixelFormat format) => format switch {
		VdPixelFormat.D24_UNorm_S8_UInt or VdPixelFormat.D32_Float_S8_UInt or VdPixelFormat.R16_UNorm or VdPixelFormat.R32_Float => false,
		_ => false,
	};

	public static bool RequiresDepthStencilUsage(VdPixelFormat format) => format
		is VdPixelFormat.D24_UNorm_S8_UInt
		or VdPixelFormat.D32_Float_S8_UInt;

	public static bool IsCompressed(ImageFormat format) => format switch {
		ImageFormat.DXT1 or ImageFormat.DXT1_OneBitAlpha or ImageFormat.DXT3 or ImageFormat.DXT5
			or ImageFormat.ATI1N or ImageFormat.ATI2N => true,
		_ => false,
	};
}
