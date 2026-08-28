using NeoVeldrid;
using NeoVeldrid.SPIRV;

using Silk.NET.Shaderc;

using System.Runtime.InteropServices;
using System.Text;

namespace Source.ShaderAPI.Veldrid;

public static unsafe class SourceGlslCompiler
{
	static readonly Shaderc shaderc = Shaderc.GetApi();
	static readonly Compiler* compiler = shaderc.CompilerInitialize();

	static ShaderKind KindFor(VdShaderStages stage) => stage switch {
		VdShaderStages.Vertex => ShaderKind.VertexShader,
		VdShaderStages.Fragment => ShaderKind.FragmentShader,
		VdShaderStages.Geometry => ShaderKind.GeometryShader,
		VdShaderStages.Compute => ShaderKind.ComputeShader,
		_ => ShaderKind.VertexShader,
	};

	public static byte[] Compile(string sourceText, string fileName, VdShaderStages stage) {
		CompileOptions* options = null;
		Silk.NET.Shaderc.CompilationResult* result = null;

		try {
			options = shaderc.CompileOptionsInitialize();
			if (options == null)
				throw new SpirvCompilationException("Failed to initialize compile options.");

			shaderc.CompileOptionsSetOptimizationLevel(options, OptimizationLevel.Zero);
			shaderc.CompileOptionsSetAutoMapLocations(options, true);

			byte[] sourceBytes = Encoding.ASCII.GetBytes(sourceText);
			byte[] fileNameBytes = Encoding.ASCII.GetBytes(fileName + '\0');
			byte[] entryPointBytes = "main\0"u8.ToArray();

			fixed (byte* sourcePtr = sourceBytes)
			fixed (byte* fileNamePtr = fileNameBytes)
			fixed (byte* entryPtr = entryPointBytes) {
				result = shaderc.CompileIntoSpv(compiler,
					sourcePtr, (nuint)sourceBytes.Length,
					KindFor(stage),
					fileNamePtr,
					entryPtr,
					options);
			}

			if (result == null)
				throw new SpirvCompilationException("Shaderc returned null result.");

			if (shaderc.ResultGetCompilationStatus(result) != CompilationStatus.Success) {
				byte* errorPtr = shaderc.ResultGetErrorMessage(result);
				string error = errorPtr != null ? Marshal.PtrToStringUTF8((nint)errorPtr) ?? "Unknown error" : "Unknown error";
				throw new SpirvCompilationException("GLSL compilation failed: " + error);
			}

			byte* bytes = shaderc.ResultGetBytes(result);
			nuint length = shaderc.ResultGetLength(result);
			byte[] spirv = new byte[(int)length];
			new Span<byte>(bytes, (int)length).CopyTo(spirv);
			return spirv;
		}
		finally {
			if (result != null) shaderc.ResultRelease(result);
			if (options != null) shaderc.CompileOptionsRelease(options);
		}
	}
}

public sealed class CompiledSpirv
{
	public required byte[] Bytes;
	public required string Name;
	public required VdShaderStages Stage;
	public required string EntryPoint;
	public Dictionary<int, string> SamplerNames = [];
}


public sealed class VeldridProgram
{
	public required VdShader[] Shaders;
	public required int[] UniformBindings;
	public required int[] TextureBindings;
	public required Dictionary<int, string> SamplerNames;
	public object? Layout;
}

public sealed class VeldridShaderProgramCache : IDisposable
{
	readonly GraphicsDevice device;
	readonly Dictionary<nint, CompiledSpirv> blobs = [];
	readonly Dictionary<(nint, nint), VeldridProgram> programs = [];
	nint nextHandle = 1;

	public VeldridShaderProgramCache(GraphicsDevice device) {
		this.device = device;
	}

	public nint Register(CompiledSpirv blob) {
		nint handle = nextHandle++;
		blobs[handle] = blob;
		return handle;
	}

	public CompiledSpirv? Lookup(nint handle) => blobs.TryGetValue(handle, out CompiledSpirv? blob) ? blob : null;

	public VeldridProgram? GetProgram(nint vertexHandle, nint pixelHandle) {
		if (vertexHandle == 0 || pixelHandle == 0)
			return null;

		if (programs.TryGetValue((vertexHandle, pixelHandle), out VeldridProgram? existing))
			return existing;

		if (!blobs.TryGetValue(vertexHandle, out CompiledSpirv? vertex) || !blobs.TryGetValue(pixelHandle, out CompiledSpirv? pixel))
			return null;

		VdShader[] created;
		try {
			created = device.ResourceFactory.CreateFromSpirv(
				new ShaderDescription(VdShaderStages.Vertex, vertex.Bytes, vertex.EntryPoint),
				new ShaderDescription(VdShaderStages.Fragment, pixel.Bytes, pixel.EntryPoint));
		}
		catch (Exception ex) {
			Warning($"Veldrid: failed to link {vertex.Name} + {pixel.Name}: {ex.Message}\n");
			return null;
		}

		HashSet<SpirvBindings.Resource> used = [];
		SpirvBindings.Read(vertex.Bytes, used);
		SpirvBindings.Read(pixel.Bytes, used);

		VeldridProgram program = new() {
			Shaders = created,
			UniformBindings = [.. used.Where(r => r.Set == 0).Select(r => (int)r.Binding).Order()],
			TextureBindings = [.. used.Where(r => r.Set == 1).Select(r => (int)r.Binding).Order()],
			SamplerNames = pixel.SamplerNames,
		};

		programs[(vertexHandle, pixelHandle)] = program;
		return program;
	}

	public void Dispose() {
		foreach (VeldridProgram program in programs.Values) {
			foreach (VdShader shader in program.Shaders)
				shader.Dispose();
		}
		programs.Clear();
		blobs.Clear();
	}
}
