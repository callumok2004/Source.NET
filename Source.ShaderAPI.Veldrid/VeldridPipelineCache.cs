using NeoVeldrid;

using Source.Common.MaterialSystem;
using Source.Common.ShaderAPI;

using System.Runtime.CompilerServices;

namespace Source.ShaderAPI.Veldrid;

public struct StencilShadowState
{
	public bool Enabled;
	public StencilOperation FailOp;
	public StencilOperation ZFailOp;
	public StencilOperation PassOp;
	public StencilComparisonFunction CompareFunc;
	public int ReferenceValue;
	public uint TestMask;
	public uint WriteMask;

	public static StencilShadowState Default => new() {
		Enabled = false,
		FailOp = StencilOperation.Keep,
		ZFailOp = StencilOperation.Keep,
		PassOp = StencilOperation.Keep,
		CompareFunc = StencilComparisonFunction.Always,
		ReferenceValue = 0,
		TestMask = 0xFFFFFFFF,
		WriteMask = 0xFFFFFFFF,
	};
}

public readonly struct PipelineKey : IEquatable<PipelineKey>
{
	public readonly GraphicsBoardState Board;
	public readonly StencilShadowState Stencil;
	public readonly VdPrimitiveTopology Topology;
	public readonly VertexFormat VertexFormat;
	public readonly uint ColorStride;
	public readonly VdShader? VertexShader;
	public readonly VdShader? PixelShader;
	public readonly OutputDescription Output;

	public PipelineKey(
		in GraphicsBoardState board,
		in StencilShadowState stencil,
		VdPrimitiveTopology topology,
		VertexFormat vertexFormat,
		uint colorStride,
		VdShader? vertexShader,
		VdShader? pixelShader,
		in OutputDescription output) {
		Board = board;
		Stencil = stencil;
		Topology = topology;
		VertexFormat = vertexFormat;
		ColorStride = colorStride;
		VertexShader = vertexShader;
		PixelShader = pixelShader;
		Output = output;
	}

	public bool Equals(PipelineKey other) =>
		Topology == other.Topology
		&& VertexFormat == other.VertexFormat
		&& ColorStride == other.ColorStride
		&& ReferenceEquals(VertexShader, other.VertexShader)
		&& ReferenceEquals(PixelShader, other.PixelShader)
		&& Output.Equals(other.Output)
		&& BoardEquals(in Board, in other.Board)
		&& StencilEquals(in Stencil, in other.Stencil);

	static bool BoardEquals(in GraphicsBoardState a, in GraphicsBoardState b) =>
		a.Blending == b.Blending
		&& a.SourceBlend == b.SourceBlend
		&& a.DestinationBlend == b.DestinationBlend
		&& a.BlendOperation == b.BlendOperation
		&& a.AlphaSeparateBlend == b.AlphaSeparateBlend
		&& a.AlphaSourceBlend == b.AlphaSourceBlend
		&& a.AlphaDestinationBlend == b.AlphaDestinationBlend
		&& a.AlphaBlendOperation == b.AlphaBlendOperation
		&& a.DepthTest == b.DepthTest
		&& a.ColorWrite == b.ColorWrite
		&& a.AlphaWrite == b.AlphaWrite
		&& a.DepthWrite == b.DepthWrite
		&& a.CullEnable == b.CullEnable
		&& a.AlphaToCoverage == b.AlphaToCoverage
		&& a.SRGBWriteEnable == b.SRGBWriteEnable
		&& a.DepthFunc == b.DepthFunc
		&& a.FillMode == b.FillMode
		&& a.ZBias == b.ZBias;

	static bool StencilEquals(in StencilShadowState a, in StencilShadowState b) =>
		a.Enabled == b.Enabled
		&& a.FailOp == b.FailOp
		&& a.ZFailOp == b.ZFailOp
		&& a.PassOp == b.PassOp
		&& a.CompareFunc == b.CompareFunc
		&& a.ReferenceValue == b.ReferenceValue
		&& a.TestMask == b.TestMask
		&& a.WriteMask == b.WriteMask;

	public override bool Equals(object? obj) => obj is PipelineKey other && Equals(other);

	public override int GetHashCode() {
		HashCode hash = new();
		hash.Add(Topology);
		hash.Add(VertexFormat);
		hash.Add(ColorStride);
		hash.Add(RuntimeHelpers.GetHashCode(VertexShader));
		hash.Add(RuntimeHelpers.GetHashCode(PixelShader));
		hash.Add(Output);

		hash.Add(Board.Blending);
		hash.Add(Board.SourceBlend);
		hash.Add(Board.DestinationBlend);
		hash.Add(Board.BlendOperation);
		hash.Add(Board.AlphaSeparateBlend);
		hash.Add(Board.AlphaSourceBlend);
		hash.Add(Board.AlphaDestinationBlend);
		hash.Add(Board.AlphaBlendOperation);
		hash.Add(Board.DepthTest);
		hash.Add(Board.ColorWrite);
		hash.Add(Board.AlphaWrite);
		hash.Add(Board.DepthWrite);
		hash.Add(Board.CullEnable);
		hash.Add(Board.AlphaToCoverage);
		hash.Add(Board.SRGBWriteEnable);
		hash.Add(Board.DepthFunc);
		hash.Add(Board.FillMode);
		hash.Add(Board.ZBias);

		hash.Add(Stencil.Enabled);
		hash.Add(Stencil.FailOp);
		hash.Add(Stencil.ZFailOp);
		hash.Add(Stencil.PassOp);
		hash.Add(Stencil.CompareFunc);
		hash.Add(Stencil.ReferenceValue);
		hash.Add(Stencil.TestMask);
		hash.Add(Stencil.WriteMask);

		return hash.ToHashCode();
	}
}

public sealed class VeldridPipelineCache : IDisposable
{
	readonly GraphicsDevice device;
	readonly ResourceFactory factory;
	readonly Dictionary<PipelineKey, Pipeline> pipelines = [];
	readonly Func<VertexFormat, uint, VertexLayoutDescription[]> vertexLayoutFor;

	public int Count => pipelines.Count;

	public VeldridPipelineCache(
		GraphicsDevice device,
		Func<VertexFormat, uint, VertexLayoutDescription[]> vertexLayoutFor) {
		this.device = device;
		factory = device.ResourceFactory;
		this.vertexLayoutFor = vertexLayoutFor;
	}

	public Pipeline Get(in PipelineKey key, ResourceLayout[] resourceLayouts) {
		if (pipelines.TryGetValue(key, out Pipeline? existing))
			return existing;

		Pipeline created = Build(in key, resourceLayouts);
		pipelines.Add(key, created);
		return created;
	}

	static VdBlendFactor ToAlphaFactor(VdBlendFactor factor) => factor switch {
		VdBlendFactor.SourceColor => VdBlendFactor.SourceAlpha,
		VdBlendFactor.InverseSourceColor => VdBlendFactor.InverseSourceAlpha,
		VdBlendFactor.DestinationColor => VdBlendFactor.DestinationAlpha,
		VdBlendFactor.InverseDestinationColor => VdBlendFactor.InverseDestinationAlpha,
		_ => factor,
	};

	static BlendAttachmentDescription BuildBlendAttachment(in GraphicsBoardState board) {
		ColorWriteMask mask = ColorWriteMask.None;
		if (board.ColorWrite)
			mask |= ColorWriteMask.Red | ColorWriteMask.Green | ColorWriteMask.Blue;
		if (board.AlphaWrite)
			mask |= ColorWriteMask.Alpha;

		VdBlendFactor alphaSrc = board.AlphaSeparateBlend ? board.AlphaSourceBlend.ToVeldrid() : board.SourceBlend.ToVeldrid();
		VdBlendFactor alphaDst = board.AlphaSeparateBlend ? board.AlphaDestinationBlend.ToVeldrid() : board.DestinationBlend.ToVeldrid();
		VdBlendFunction alphaOp = board.AlphaSeparateBlend ? board.AlphaBlendOperation.ToVeldrid() : board.BlendOperation.ToVeldrid();

		return new BlendAttachmentDescription {
			BlendEnabled = board.Blending,
			ColorWriteMask = mask,
			SourceColorFactor = board.SourceBlend.ToVeldrid(),
			DestinationColorFactor = board.DestinationBlend.ToVeldrid(),
			ColorFunction = board.BlendOperation.ToVeldrid(),
			SourceAlphaFactor = ToAlphaFactor(alphaSrc),
			DestinationAlphaFactor = ToAlphaFactor(alphaDst),
			AlphaFunction = alphaOp,
		};
	}

	static DepthStencilStateDescription BuildDepthStencil(in GraphicsBoardState board, in StencilShadowState stencil) {
		StencilBehaviorDescription behavior = new(
			stencil.FailOp.ToVeldrid(),
			stencil.PassOp.ToVeldrid(),
			stencil.ZFailOp.ToVeldrid(),
			stencil.CompareFunc.ToVeldrid());

		return new DepthStencilStateDescription {
			DepthTestEnabled = board.DepthTest,
			DepthWriteEnabled = board.DepthWrite,
			DepthComparison = board.DepthFunc.ToVeldrid(),
			StencilTestEnabled = stencil.Enabled,
			StencilFront = behavior,
			StencilBack = behavior,
			StencilReadMask = (byte)(stencil.TestMask & 0xFF),
			StencilWriteMask = (byte)(stencil.WriteMask & 0xFF),
			StencilReference = (uint)stencil.ReferenceValue,
		};
	}

	static RasterizerStateDescription BuildRasterizer(in GraphicsBoardState board) => new() {
		CullMode = board.CullEnable ? VdFaceCullMode.Back : VdFaceCullMode.None,
		FillMode = board.FillMode.ToVeldrid(),
		FrontFace = FrontFace.Clockwise,
		DepthClipEnabled = true,
		ScissorTestEnabled = true,
	};

	Pipeline Build(in PipelineKey key, ResourceLayout[] resourceLayouts) {
		GraphicsPipelineDescription description = new() {
			BlendState = new BlendStateDescription {
				BlendFactor = RgbaFloat.White,
				AttachmentStates = [BuildBlendAttachment(in key.Board)],
				AlphaToCoverageEnabled = key.Board.AlphaToCoverage,
			},
			DepthStencilState = BuildDepthStencil(in key.Board, in key.Stencil),
			RasterizerState = BuildRasterizer(in key.Board),
			PrimitiveTopology = key.Topology,
			ResourceLayouts = resourceLayouts,
			ShaderSet = new ShaderSetDescription(
				vertexLayoutFor(key.VertexFormat, key.ColorStride),
				key.VertexShader != null && key.PixelShader != null
					? [key.VertexShader, key.PixelShader]
					: []),
			Outputs = key.Output,
			ResourceBindingModel = ResourceBindingModel.Improved,
		};

		return factory.CreateGraphicsPipeline(ref description);
	}

	public void Clear() {
		foreach (Pipeline pipeline in pipelines.Values)
			pipeline.Dispose();
		pipelines.Clear();
	}

	public void Dispose() => Clear();
}
