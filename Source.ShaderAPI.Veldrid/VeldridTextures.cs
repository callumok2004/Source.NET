using NeoVeldrid;

using Source.Common.Bitmap;
using Source.Common.MaterialSystem;
using Source.Common.ShaderAPI;

namespace Source.ShaderAPI.Veldrid;

[Flags]
public enum InternalTextureFlags
{
	None = 0,
	IsAllocated = 0x0001,
	IsLockable = 0x0002,
	IsVertexTexture = 0x0004,
	IsDepthStencil = 0x0008,
}

public class InternalTextureInfo
{
	public VdTexture[] Copies = [];
	public string DebugName = "";

	public int Width;
	public int Height;
	public int Depth;
	public int Levels;
	public int Count;
	public int CountIndex;
	public ImageFormat Format;
	public VdPixelFormat PixelFormat;
	public CreateTextureFlags CreationFlags;
	public InternalTextureFlags Flags;
	public byte NumCopies;
	public byte CurrentCopy;
	public nuint SizeBytes;
	public int SizeTexels;

	public byte[]? Shadow;

	public SamplerShadowSettings Sampler = SamplerShadowSettings.Default;

	public bool IsCubeMap => (CreationFlags & CreateTextureFlags.Cubemap) != 0;

	public VdTexture? Current => Copies.Length == 0 ? null : Copies[CurrentCopy];

	public ImageFormat GetImageFormat() => Format;
	public int GetLevelCount() => Levels;

	public void Dispose() {
		foreach (VdTexture texture in Copies)
			texture.Dispose();
		Copies = [];
		Flags = InternalTextureFlags.None;
	}
}

public sealed class VeldridSamplerCache : IDisposable
{
	readonly GraphicsDevice device;
	readonly Dictionary<SamplerDescription, VdSampler> samplers = [];

	public VeldridSamplerCache(GraphicsDevice device) {
		this.device = device;
	}

	public VdSampler Get(in SamplerDescription description) {
		if (samplers.TryGetValue(description, out VdSampler? existing))
			return existing;

		SamplerDescription copy = description;
		VdSampler created = device.ResourceFactory.CreateSampler(ref copy);
		samplers.Add(description, created);
		return created;
	}

	public void Dispose() {
		foreach (VdSampler sampler in samplers.Values)
			sampler.Dispose();
		samplers.Clear();
	}
}

public struct SamplerShadowSettings
{
	public VdSamplerAddressMode AddressU;
	public VdSamplerAddressMode AddressV;
	public VdSamplerAddressMode AddressW;
	public VdSamplerFilter Filter;
	public uint MaximumAnisotropy;

	public static SamplerShadowSettings Default => new() {
		AddressU = VdSamplerAddressMode.Wrap,
		AddressV = VdSamplerAddressMode.Wrap,
		AddressW = VdSamplerAddressMode.Wrap,
		Filter = VdSamplerFilter.MinLinear_MagLinear_MipLinear,
		MaximumAnisotropy = 0,
	};

	public readonly SamplerDescription ToDescription() => new() {
		AddressModeU = AddressU,
		AddressModeV = AddressV,
		AddressModeW = AddressW,
		Filter = MaximumAnisotropy > 0 ? VdSamplerFilter.Anisotropic : Filter,
		MaximumAnisotropy = MaximumAnisotropy,
		ComparisonKind = null,
		MinimumLod = 0,
		MaximumLod = uint.MaxValue,
		LodBias = 0,
		BorderColor = SamplerBorderColor.TransparentBlack,
	};
}
