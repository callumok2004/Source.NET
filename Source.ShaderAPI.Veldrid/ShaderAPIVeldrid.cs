using Microsoft.Extensions.DependencyInjection;

using NeoVeldrid;

using Source.Common;
using Source.Bitmap;
using Source.Common.Bitmap;
using Source.Common.Commands;
using Source.Common.Formats.Keyvalues;
using Source.Common.Launcher;
using Source.Common.MaterialSystem;
using Source.Common.Mathematics;
using Source.Common.ShaderAPI;
using Source.Common.ShaderLib;

using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

namespace Source.ShaderAPI.Veldrid;

public enum UniformBufferBindingLocation
{
	SharedMatrices = 0,
	SharedBaseShader = 1,
	SharedVertexShader = 2,
	SharedPixelShader = 3,
	SharedBoneMatrices = 4,
	VertexShaderConstants = 5,
	PixelShaderConstants = 6
}

public enum TransformDirtyBits
{
	StateChangedVertexShader = 0x1,
	StateChangedFixedFunction = 0x2,
	StateChanged = 0x3
}

public class ShaderAPIVeldrid : IShaderAPI, IShaderDevice, IDebugTextureInfo
{
	public MeshMgr MeshMgr = null!;
	public IShaderSystem ShaderManager = null!;

	public static void DLLInit(IServiceCollection services) {
		services.AddSingleton(x => (IDebugTextureInfo)(ShaderAPIVeldrid)x.GetRequiredService<IShaderAPI>());
		services.AddSingleton(x => x.GetRequiredService<IShaderAPI>().GetShaderDevice());
		services.AddSingleton<IMeshMgr, MeshMgr>();
		services.AddSingleton<IMaterialSystemHardwareConfig, HardwareConfig>();
		services.AddSingleton<IShaderSystem, ShaderSystem>();
		services.AddSingleton<MeshMgr>();
	}

	internal IServiceProvider services = null!;
	internal IGraphicsContext? Device;
	internal GraphicsDriver Driver = GraphicsDriver.OpenGL46;

	VeldridDeviceContext? context;
	VeldridPipelineCache? pipelines;
	VeldridShaderProgramCache? programs;
	CommandList? commands;
	DeviceBuffer? zeroVertexBuffer;

	public GraphicsDevice? GraphicsDevice => context?.Device;

	public GraphicsDriver GetDriver() => Driver;

	DeviceState DeviceState = DeviceState.OK;
	ShaderDeviceInfo PresentParameters;

	public bool OnDeviceInit() {
		if (context == null)
			return false;

		((HardwareConfig)HardwareConfig).AttachDevice(context.Device);

		programs = new VeldridShaderProgramCache(context.Device);
		((ShaderSystem)ShaderManager).AttachProgramCache(programs);

		pipelines = new VeldridPipelineCache(
			context.Device,
			VeldridVertexLayout.For);

		zeroVertexBuffer = context.Device.ResourceFactory.CreateBuffer(new BufferDescription(64, BufferUsage.VertexBuffer));
		context.Device.UpdateBuffer(zeroVertexBuffer, 0, new Vector4[4]);

		commands = context.Device.ResourceFactory.CreateCommandList();
		samplerCache = new VeldridSamplerCache(context.Device);

		for (int i = 0; i < samplerSettings.Length; i++)
			samplerSettings[i] = SamplerShadowSettings.Default;

		CreateFallbackTexture();

		MeshMgr.MaterialSystem = Singleton<IMaterialSystem>();
		MeshMgr.ShaderAPI = this;
		MeshMgr.Init();
		Device!.SetSwapInterval(0);

		InitRenderState();

		ClearColor4ub(0, 0, 0, 1);
		ClearBuffers(true, true, true, -1, -1);

		return true;
	}

	RgbaFloat clearColor = RgbaFloat.Black;

	public void ClearColor3ub(byte r, byte g, byte b) => clearColor = new RgbaFloat(r / 255f, g / 255f, b / 255f, 1);
	public void ClearColor4ub(byte r, byte g, byte b, byte a) => clearColor = new RgbaFloat(r / 255f, g / 255f, b / 255f, a / 255f);

	public void ClearBuffers(bool clearColor, bool clearDepth, bool clearStencil, int renderTargetWidth = -1, int renderTargetHeight = -1) {
		if (IsDeactivated() || commands == null || context == null)
			return;

		FlushBufferedPrimitives();

		if (!frameOpen)
			BeginFrame();

		if (clearColor)
			commands.ClearColorTarget(0, this.clearColor);
		if (clearDepth || clearStencil)
			commands.ClearDepthStencil(1.0f, 0);

		clearsThisFrame++;
	}

	int clearsThisFrame;

	VertexShaderHandle activeVertexShader = VertexShaderHandle.INVALID;
	PixelShaderHandle activePixelShader = PixelShaderHandle.INVALID;
	bool pipelineChanged = false;
	ShadowStateVeldrid? currentShadow;

	internal void SetCurrentShadow(ShadowStateVeldrid shadow) {
		currentShadow = shadow;
	}

	internal void ForgetShadow(ShadowStateVeldrid shadow) {
		foreach ((ShadowStateVeldrid owner, ProgramLayout layout) key in uniformSets.Keys.Where(k => k.Item1 == shadow).ToArray()) {
			if (uniformSets.Remove(key, out ResourceSet? set))
				Retire(set);
		}

		if (currentShadow == shadow)
			currentShadow = null;
	}

	public void SetVertexShaderIndex(int index) {
		if (currentShadow != null)
			BindVertexShader(currentShadow.GetVertexShaderVariant(index));
	}

	public void SetPixelShaderIndex(int index) {
		if (currentShadow != null)
			BindPixelShader(currentShadow.GetPixelShaderVariant(index));
	}

	public int GetDynamicComboScale(ShaderType type, ReadOnlySpan<char> name) => currentShadow?.GetDynamicComboScale(type, name) ?? 0;

	public void BindVertexShader(in VertexShaderHandle vertexShader) {
		activeVertexShader = vertexShader;
		pipelineChanged = true;
	}

	public void BindPixelShader(in PixelShaderHandle pixelShader) {
		activePixelShader = pixelShader;
		pipelineChanged = true;
	}

	const int NUM_VERTEX_SHADER_CONSTANTS = 256;
	readonly float[] desiredVertexShaderConstants = new float[NUM_VERTEX_SHADER_CONSTANTS * 4];
	readonly float[] dynamicVertexShaderConstants = new float[NUM_VERTEX_SHADER_CONSTANTS * 4];

	const int NUM_PIXEL_SHADER_CONSTANTS = 256;
	readonly float[] desiredPixelShaderConstants = new float[NUM_PIXEL_SHADER_CONSTANTS * 4];
	readonly float[] dynamicPixelShaderConstants = new float[NUM_PIXEL_SHADER_CONSTANTS * 4];

	DeviceBuffer? uboMatrices;
	DeviceBuffer? uboSrgb;
	DeviceBuffer? uboBones;
	DeviceBuffer? uboVertexConstants;
	DeviceBuffer? uboPixelConstants;

	SourceVertexSharedShadowState vertexShared;

	internal SourceVertexSharedShadowState GetVertexSharedState() => vertexShared;

	internal void WriteUniforms<T>(DeviceBuffer buffer, uint offset, ref T value) where T : unmanaged
		=> VeldridUploads.Write(context!.Device, buffer, offset, ref value);

	void WriteUniforms(DeviceBuffer buffer, uint offset, nint source, uint size)
		=> VeldridUploads.Write(context!.Device, buffer, offset, source, size);

	unsafe void CreateMatrixStacks() {
		if (context == null)
			return;

		ResourceFactory factory = context.Device.ResourceFactory;

		BufferUsage uniformUsage = context.Backend == GraphicsBackend.OpenGL
			? BufferUsage.UniformBuffer | BufferUsage.Dynamic
			: BufferUsage.UniformBuffer;

		uboMatrices = factory.CreateBuffer(new BufferDescription((uint)(sizeof(Matrix4x4) * 3), uniformUsage));
		uboMatrices.Name = "ShaderAPI Shared Matrix UBO";

		Matrix4x4[] identityMatrices3 = [Matrix4x4.Identity, Matrix4x4.Identity, Matrix4x4.Identity];
		for (int i = 0; i < matrices.Length; i++)
			matrices[i] = Matrix4x4.Identity;
		context.Device.UpdateBuffer(uboMatrices, 0, identityMatrices3);

		uboBones = factory.CreateBuffer(new BufferDescription((uint)(sizeof(Matrix4x4) * Studio.MAXSTUDIOBONES), uniformUsage));
		uboBones.Name = "ShaderAPI Shared Bone UBO";

		Matrix4x4[] identityMatrices = new Matrix4x4[Studio.MAXSTUDIOBONES];
		for (int i = 0; i < Studio.MAXSTUDIOBONES; i++)
			identityMatrices[i] = Matrix4x4.Identity;
		context.Device.UpdateBuffer(uboBones, 0, identityMatrices);

		uboVertexConstants = factory.CreateBuffer(new BufferDescription(sizeof(float) * NUM_VERTEX_SHADER_CONSTANTS * 4, uniformUsage));
		uboVertexConstants.Name = "ShaderAPI Vertex Shader Constants UBO";

		uboPixelConstants = factory.CreateBuffer(new BufferDescription(sizeof(float) * NUM_PIXEL_SHADER_CONSTANTS * 4, uniformUsage));
		uboPixelConstants.Name = "ShaderAPI Pixel Shader Constants UBO";

		uboSrgb = factory.CreateBuffer(new BufferDescription(16, uniformUsage));
		uboSrgb.Name = "ShaderAPI sRGB Write State UBO";

		SrgbState srgbInit = default;
		context.Device.UpdateBuffer(uboSrgb, 0, ref srgbInit);
		lastSrgbWrite = false;
	}

	public void InitRenderState() {
		if (!IsDeactivated())
			ResetRenderState();

		SetToneMappingScaleLinear(new Vector3(1.0f, 1.0f, 1.0f));
	}

	public void SetPresentParameters(in ShaderDeviceInfo info) {
		PresentParameters = info;
	}

	void ResetRenderState(bool fullReset = true) {
		int width = 0, height = 0;
		Singleton<ILauncherManager>().DisplayedSize(out width, out height);
		ShaderViewport viewport = new ShaderViewport(0, 0, width, height);
		SetViewports(new(ref viewport));
	}

	readonly ShaderAPITextureHandle_t[] StdTextureHandles = new ShaderAPITextureHandle_t[(int)StandardTextureId.Max];

	public void SetStandardTextureHandle(StandardTextureId id, ShaderAPITextureHandle_t handle) {
		StdTextureHandles[(int)id] = handle;
	}

	ShaderAPITextureHandle_t LinearToGammaTableTexture;
	ShaderAPITextureHandle_t LinearToGammaTableIdentityTexture;

	public void SetLinearToGammaConversionTextures(ShaderAPITextureHandle_t srgbWriteEnabledTexture, ShaderAPITextureHandle_t identityTexture) {
		LinearToGammaTableTexture = srgbWriteEnabledTexture;
		LinearToGammaTableIdentityTexture = identityTexture;
	}

	public float LinearToGamma_HardwareSpecific(float linear) => MathLib.SrgbLinearToGamma(linear);

	public void SetDefaultState() {
		currentShadow?.SetDefaultState();
	}

	public IShaderShadow NewShaderShadow(ReadOnlySpan<char> materialName) => new ShadowStateVeldrid(this, (IShaderSystemInternal)ShaderManager, materialName);
	public bool IsTranslucent(IShaderShadow renderState) => ((ShadowStateVeldrid)renderState).State.Blending;
	public bool IsAlphaTested(IShaderShadow renderState) => ((ShadowStateVeldrid)renderState).Pixel.IsAlphaTesting != 0;

	GraphicsBoardState boardState;

	public bool SetBoardState(in GraphicsBoardState state) {
		boardState = state;
		pipelineChanged = true;
		return true;
	}

	StencilShadowState stencil = StencilShadowState.Default;

	public void SetStencilEnable(bool onoff) {
		if (stencil.Enabled == onoff)
			return;
		FlushBufferedPrimitives();
		stencil.Enabled = onoff;
		pipelineChanged = true;
	}

	public void SetStencilFailOperation(StencilOperation op) {
		if (stencil.FailOp == op)
			return;
		FlushBufferedPrimitives();
		stencil.FailOp = op;
		pipelineChanged = true;
	}

	public void SetStencilZFailOperation(StencilOperation op) {
		if (stencil.ZFailOp == op)
			return;
		FlushBufferedPrimitives();
		stencil.ZFailOp = op;
		pipelineChanged = true;
	}

	public void SetStencilPassOperation(StencilOperation op) {
		if (stencil.PassOp == op)
			return;
		FlushBufferedPrimitives();
		stencil.PassOp = op;
		pipelineChanged = true;
	}

	public void SetStencilCompareFunction(StencilComparisonFunction cmpfn) {
		if (stencil.CompareFunc == cmpfn)
			return;
		FlushBufferedPrimitives();
		stencil.CompareFunc = cmpfn;
		pipelineChanged = true;
	}

	public void SetStencilReferenceValue(int reference) {
		if (stencil.ReferenceValue == reference)
			return;
		FlushBufferedPrimitives();
		stencil.ReferenceValue = reference;
		pipelineChanged = true;
	}

	public void SetStencilTestMask(uint msk) {
		if (stencil.TestMask == msk)
			return;
		FlushBufferedPrimitives();
		stencil.TestMask = msk;
		pipelineChanged = true;
	}

	public void SetStencilWriteMask(uint msk) {
		if (stencil.WriteMask == msk)
			return;
		FlushBufferedPrimitives();
		stencil.WriteMask = msk;
		pipelineChanged = true;
	}

	bool scissorEnabled;
	Rectangle scissorRect;

	public void SetScissorRect(int left, int top, int right, int bottom, bool enableScissor) {
		FlushBufferedPrimitives();
		scissorEnabled = enableScissor;
		scissorRect = new Rectangle(left, top, right - left, bottom - top);
	}

	InlineArray6<Vector4> AmbientLightCube;
	int CachedAmbientLightCube = (int)TransformDirtyBits.StateChanged;

	public void SetAmbientLightCube(ReadOnlySpan<Vector4> cube) {
		for (int i = 0; i < 6; i++) {
			if (AmbientLightCube[i] != cube[i]) {
				AmbientLightCube[i] = cube[i];
				CachedAmbientLightCube = (int)TransformDirtyBits.StateChanged;
			}
		}
	}

	Vector3 LightingOrigin;

	public void SetLightingOrigin(Vector3 lightingOrigin) {
		LightingOrigin = lightingOrigin;
	}

	public const int MAX_NUM_LIGHTS = 4;

	public int GetMaxLights() => MAX_NUM_LIGHTS;

	readonly LightDesc[] lights = new LightDesc[MAX_NUM_LIGHTS];
	readonly TransformDirtyBits[] LightChanged = new TransformDirtyBits[MAX_NUM_LIGHTS];
	readonly TransformDirtyBits[] LightEnableChanged = new TransformDirtyBits[MAX_NUM_LIGHTS];
	int numLights;

	public void SetAmbientLight(float r, float g, float b) {
		ambientLight = new Vector4(r, g, b, 1.0f);
	}

	Vector4 ambientLight;

	readonly Light[] Lights = new Light[MAX_NUM_LIGHTS];
	readonly bool[] LightEnable = new bool[MAX_NUM_LIGHTS];
	readonly VertexShaderLightTypes[] LightTypes = new VertexShaderLightTypes[MAX_NUM_LIGHTS];

	enum VertexShaderLightTypes
	{
		None = -1,
		Spot = 0,
		Point = 1,
		Directional = 2,
	}

	struct Light
	{
		public LightType Type;
		public Vector4 Diffuse;
		public Vector4 Specular;
		public Vector4 Ambient;
		public Vector3 Position;
		public Vector3 Direction;
		public float Range;
		public float Falloff;
		public float Attenuation0;
		public float Attenuation1;
		public float Attenuation2;
		public float Theta;
		public float Phi;
	}

	public void SetLight(int lightNum, in LightDesc desc) {
		if (lightNum < 0 || lightNum >= MAX_NUM_LIGHTS)
			return;

		lights[lightNum] = desc;

		FlushBufferedPrimitives();

		if (desc.Type == LightType.Disable) {
			if (LightEnable[lightNum]) {
				LightEnableChanged[lightNum] = TransformDirtyBits.StateChanged;
				LightEnable[lightNum] = false;
			}
			return;
		}

		if (!LightEnable[lightNum]) {
			LightEnableChanged[lightNum] = TransformDirtyBits.StateChanged;
			LightEnable[lightNum] = true;
		}

		Light light = default;
		switch (desc.Type) {
			case LightType.Point:
				light.Type = LightType.Point;
				light.Range = desc.Range;
				break;

			case LightType.Directional:
				light.Type = LightType.Directional;
				light.Range = 1e12f;
				break;

			case LightType.Spot:
				light.Type = LightType.Spot;
				light.Range = desc.Range;
				break;

			default:
				LightEnable[lightNum] = false;
				return;
		}

		light.Diffuse = new Vector4(desc.Color, 1.0f);
		light.Specular = new Vector4(desc.Color, 1.0f);
		light.Ambient = new Vector4(0, 0, 0, 0);
		light.Position = desc.Position;
		light.Direction = desc.Direction;
		light.Falloff = desc.Falloff;
		light.Attenuation0 = desc.Attenuation0;
		light.Attenuation1 = desc.Attenuation1;
		light.Attenuation2 = desc.Attenuation2;

		light.Theta = desc.Theta;
		light.Phi = desc.Phi;
		if (light.Phi > MathF.PI)
			light.Phi = MathF.PI;

		if (light.Theta - light.Phi > -1e-3f)
			light.Theta = light.Phi - 1e-3f;

		LightChanged[lightNum] = TransformDirtyBits.StateChanged;
		Lights[lightNum] = light;
	}

	public void DisableAllLocalLights() {
		bool flushed = false;
		for (int lightNum = 0; lightNum < MAX_NUM_LIGHTS; lightNum++) {
			if (!LightEnable[lightNum])
				continue;

			if (!flushed) {
				FlushBufferedPrimitives();
				flushed = true;
			}

			lights[lightNum].Type = LightType.Disable;
			LightEnableChanged[lightNum] = TransformDirtyBits.StateChanged;
			LightEnable[lightNum] = false;
		}
	}

	VertexShaderLightTypes ComputeLightType(int i) {
		if (!LightEnable[i])
			return VertexShaderLightTypes.None;

		return Lights[i].Type switch {
			LightType.Point => VertexShaderLightTypes.Point,
			LightType.Directional => VertexShaderLightTypes.Directional,
			LightType.Spot => VertexShaderLightTypes.Spot,
			_ => VertexShaderLightTypes.None,
		};
	}

	void SortLights(Span<int> index) {
		numLights = 0;

		for (int i = 0; i < MAX_NUM_LIGHTS; ++i) {
			VertexShaderLightTypes type = ComputeLightType(i);
			int j = numLights;
			if (type == VertexShaderLightTypes.None)
				continue;

			while (--j >= 0) {
				if (LightTypes[j] <= type)
					break;

				LightTypes[j + 1] = LightTypes[j];
				index[j + 1] = index[j];
			}
			++j;

			LightTypes[j] = type;
			index[j] = i;

			++numLights;
		}
	}

	FlashlightState flashlightState;
	Matrix4x4 flashlightWorldToTexture = Matrix4x4.Identity;
	ITexture? flashlightDepthTexture;

	public void SetFlashlightStateEx(in FlashlightState state, in Matrix4x4 worldToTexture, ITexture? depthTexture) {
		FlushBufferedPrimitives();

		flashlightState = state;
		flashlightWorldToTexture = worldToTexture;
		flashlightDepthTexture = depthTexture;
	}

	public const int PSREG_LIGHT_INFO_ARRAY = 20;

	public void CommitPixelShaderLighting(int pshReg) {
		Span<int> lightIndex = stackalloc int[MAX_NUM_LIGHTS];
		SortLights(lightIndex);

		const float farAway = 10000.0f;

		Span<Vector4> lightState = stackalloc Vector4[6];
		lightState.Clear();

		Vector4 PositionOf(scoped in Light light) {
			if (light.Type != LightType.Directional)
				return new Vector4(light.Position, 0.0f);

			Vector3 pos = LightingOrigin - light.Direction * farAway;
			return new Vector4(pos, 0.0f);
		}

		int count = numLights;
		if (count > 0) {
			ref Light light = ref Lights[lightIndex[0]];
			lightState[0] = new Vector4(light.Diffuse.X, light.Diffuse.Y, light.Diffuse.Z, 0.0f);
			lightState[1] = PositionOf(in light);

			if (count > 1) {
				light = ref Lights[lightIndex[1]];
				lightState[2] = new Vector4(light.Diffuse.X, light.Diffuse.Y, light.Diffuse.Z, 0.0f);
				lightState[3] = PositionOf(in light);

				if (count > 2) {
					light = ref Lights[lightIndex[2]];
					lightState[4] = new Vector4(light.Diffuse.X, light.Diffuse.Y, light.Diffuse.Z, 0.0f);
					lightState[5] = PositionOf(in light);

					if (count > 3) {
						light = ref Lights[lightIndex[3]];
						lightState[0].W = light.Diffuse.X;
						lightState[1].W = light.Diffuse.Y;
						lightState[2].W = light.Diffuse.Z;

						Vector4 position = PositionOf(in light);
						lightState[3].W = position.X;
						lightState[4].W = position.Y;
						lightState[5].W = position.Z;
					}
				}
			}
		}

		SetPixelShaderConstant(pshReg, MemoryMarshal.Cast<Vector4, float>(lightState));
	}

	bool VertexShaderLightingChanged(int i) => (LightChanged[i] & TransformDirtyBits.StateChangedVertexShader) != 0;
	bool VertexShaderLightingEnableChanged(int i) => (LightEnableChanged[i] & TransformDirtyBits.StateChangedVertexShader) != 0;

	public void CommitVertexShaderLighting() {
		int i;
		for (i = 0; i < MAX_NUM_LIGHTS; ++i) {
			if (VertexShaderLightingChanged(i) || VertexShaderLightingEnableChanged(i))
				break;
		}

		if (i == MAX_NUM_LIGHTS)
			return;

		Span<int> lightIndex = stackalloc int[MAX_NUM_LIGHTS];
		lightIndex.Clear();
		SortLights(lightIndex);

		for (i = 0; i < MAX_NUM_LIGHTS; ++i) {
			LightEnableChanged[i] &= ~TransformDirtyBits.StateChangedVertexShader;
			LightChanged[i] &= ~TransformDirtyBits.StateChangedVertexShader;
		}

		Span<Vector4> lightState = stackalloc Vector4[5];
		for (i = 0; i < numLights; ++i) {
			ref Light light = ref Lights[lightIndex[i]];

			float w = light.Type == LightType.Directional ? 1.0f : 0.0f;
			lightState[0] = new Vector4(light.Diffuse.X, light.Diffuse.Y, light.Diffuse.Z, w);

			w = light.Type == LightType.Spot ? 1.0f : 0.0f;
			lightState[1] = new Vector4(light.Direction.X, light.Direction.Y, light.Direction.Z, w);

			lightState[2] = new Vector4(light.Position.X, light.Position.Y, light.Position.Z, 1.0f);

			if (light.Type == LightType.Spot) {
				float stopDot = MathF.Cos(light.Theta * 0.5f);
				float stopDot2 = MathF.Cos(light.Phi * 0.5f);
				float ooDot = stopDot > stopDot2 ? 1.0f / (stopDot - stopDot2) : 0.0f;
				lightState[3] = new Vector4(light.Falloff, stopDot, stopDot2, ooDot);
			}
			else {
				lightState[3] = new Vector4(0, 1, 1, 1);
			}

			lightState[4] = new Vector4(light.Attenuation0, light.Attenuation1, light.Attenuation2, 0.0f);

			SetVertexShaderConstant(VertexShaderConst.Lights + i * 5, MemoryMarshal.Cast<Vector4, float>(lightState));
		}

		vertexShared.LightCount = numLights;

		Span<float> lightEnable = stackalloc float[MAX_NUM_LIGHTS];
		lightEnable.Clear();
		for (i = 0; i < numLights; ++i)
			lightEnable[i] = 1.0f;

		vertexShared.LightEnabled = new(lightEnable[0], lightEnable[1], lightEnable[2], lightEnable[3]);
	}

	public void GetLightState(out LightState state) {
		state = default;

		Span<Vector4> cube = AmbientLightCube;
		bool ambientIsZero = true;
		for (int i = 0; i < 6; ++i) {
			if (cube[i].X != 0.0f || cube[i].Y != 0.0f || cube[i].Z != 0.0f) {
				ambientIsZero = false;
				break;
			}
		}
		state.AmbientLight = !ambientIsZero;

		state.NumLights = numLights;
		state.StaticLightVertex = (RenderMesh as MeshVeldrid)?.HasColorMesh() ?? false;
		state.StaticLightTexel = false;
	}

	public void SetVertexShaderStateAmbientLightCube() {
		if ((CachedAmbientLightCube & (int)TransformDirtyBits.StateChangedVertexShader) != 0) {
			Span<Vector4> cube = AmbientLightCube;
			SetVertexShaderConstant(VertexShaderConst.AmbientLight, MemoryMarshal.Cast<Vector4, float>(cube));
			CachedAmbientLightCube &= ~(int)TransformDirtyBits.StateChangedVertexShader;
		}
	}

	public void SetPixelShaderStateAmbientLightCube(int reg, bool forceToBlack) {
		if (forceToBlack) {
			Span<Vector4> tempCube = stackalloc Vector4[6];
			SetPixelShaderConstant(reg, MemoryMarshal.Cast<Vector4, float>(tempCube));
			return;
		}

		SetPixelShaderConstant(reg, MemoryMarshal.Cast<Vector4, float>(MemoryMarshal.CreateSpan(ref AmbientLightCube[0], 6)));
	}

	MaterialFogMode SceneFogMode = MaterialFogMode.None;

	public MaterialFogMode GetSceneFogMode() {
		return SceneFogMode;
	}

	internal IShaderUtil ShaderUtil = null!;

	public bool InFlashlightMode() {
		return ShaderUtil.InFlashlightMode();
	}

	IMesh? RenderMesh;
	IMaterialInternal? Material;

	public void RenderPass() {
		if (IsDeactivated())
			return;

		if (RenderMesh != null)
			((MeshVeldrid)RenderMesh).RenderPass();
		else
			MeshMgr.RenderPassWithVertexAndIndexBuffers();
	}

	bool IsDeactivated() => DeviceState != DeviceState.OK || context == null;

	public void InvalidateDelayedShaderConstants() {

	}

	public enum TransformType
	{
		IsIdentity = 0,
		IsCameraToWorld,
		IsGeneral
	}

	public void PushMatrix() {
		if (MatrixIsChanging()) {

		}
	}

	bool MatrixIsChanging(TransformType type = TransformType.IsGeneral) {
		if (IsDeactivated())
			return false;

		if (type != TransformType.IsGeneral)
			return false;

		FlushBufferedPrimitivesInternal();

		return true;
	}

	public void FlushBufferedPrimitives() => FlushBufferedPrimitivesInternal();

	void FlushBufferedPrimitivesInternal() {
		Assert(RenderMesh == null);
		MeshMgr.Flush();
	}

	public void PopMatrix() {
		if (MatrixIsChanging()) {
			UpdateMatrixTransform();
		}
	}

	void UpdateMatrixTransform() {

	}

	public void DrawMesh(IMesh imesh) {
		MeshVeldrid mesh = (MeshVeldrid)imesh!;
		RenderMesh = mesh;
		CommitStateChanges();
		Material!.DrawMesh(VertexCompressionType.None);
		RenderMesh = null;
	}

	void CommitStateChanges() {
		CommitVertexShaderLighting();
	}

	public IMesh GetDynamicMesh(IMaterial material, int hwSkinBoneCount, bool buffered, IMesh? vertexOverride, IMesh? indexOverride) {
		Assert(material == null || material.IsRealTimeVersion());
		return MeshMgr.GetDynamicMesh(material, 0, hwSkinBoneCount, buffered, vertexOverride, indexOverride);
	}

	public void Bind(IMaterial? material) {
		IMaterialInternal? matInt = (IMaterialInternal?)material;

		bool materialChanged;
		if (Material != null && matInt != null && Material.InMaterialPage() && matInt.InMaterialPage()) {
			materialChanged = (Material.GetMaterialPage() != matInt.GetMaterialPage());
		}
		else {
			materialChanged = (Material != matInt) || (Material != null && Material.InMaterialPage()) || (matInt != null && matInt.InMaterialPage());
		}

		if (materialChanged) {
			FlushBufferedPrimitives();
			Material = matInt;
		}
	}

	public void SetSkinningMatrices() {
		Assert(Material != null);

		if (currentNumBones == 0)
			return;

		SetVertexShaderStateSkinningMatrices();
	}

	unsafe void SetVertexShaderStateSkinningMatrices() {
		if (context == null || uboBones == null)
			return;

		GetMatrix(MaterialMatrixMode.Model, out Matrix4x4 modelMatrix);

		Matrix4x4 transposed = Matrix4x4.Transpose(modelMatrix);
		WriteUniforms(uboBones, 0, ref transposed);
	}

	public void ShadeMode(ShadeMode shadeMode) {

	}

	public bool InEditorMode() => false;

	public float GetLightMapScaleFactor() => HardwareConfig.GetHDRType() switch {
		HDRType.Float => 1.0f,
		HDRType.Integer => 16.0f,
		_ => MathLib.GammaToLinearFullRange(2.0f),
	};

	Vector4 ToneMappingScale;

	public void SetToneMappingScaleLinear(in Vector3 scale) {
		if (!HardwareConfig.SupportsPixelShaders_2_0())
			return;

		FlushBufferedPrimitives();

		Vector3 scaleToUse = scale;
		ToneMappingScale = new(scaleToUse, ToneMappingScale.W);

		switch (HardwareConfig.GetHDRType()) {
			case HDRType.None:
				ToneMappingScale.X = 1.0f;
				ToneMappingScale.Z = 1.0f;
				break;

			case HDRType.Float:
				ToneMappingScale.X = scaleToUse.X;
				ToneMappingScale.Z = 1.0f;
				break;

			case HDRType.Integer:
				ToneMappingScale.X = scaleToUse.X;
				ToneMappingScale.Z = 16.0f;
				break;
		}

		ToneMappingScale.Y = GetLightMapScaleFactor();
		ToneMappingScale.W = MathLib.LinearToGammaFullRange(ToneMappingScale.X);

		SetPixelShaderConstant((int)PixelShaderConst.LightScale, MemoryMarshal.CreateSpan(ref ToneMappingScale.X, 4));
	}

	public ref readonly Vector3 GetToneMappingScaleLinear() => ref Unsafe.As<Vector4, Vector3>(ref ToneMappingScale);

	public unsafe void SetVertexShaderConstant(int var, Span<float> vec) {
		int numVecs = vec.Length / 4;
		Assert(var + numVecs <= NUM_VERTEX_SHADER_CONSTANTS);

		numVecs = AdjustUpdateRange(vec, desiredVertexShaderConstants.AsSpan(var * 4), numVecs, out int skip);
		if (numVecs == 0)
			return;

		var += skip;
		Span<float> src = vec.Slice(skip * 4, numVecs * 4);

		src.CopyTo(desiredVertexShaderConstants.AsSpan(var * 4));
		src.CopyTo(dynamicVertexShaderConstants.AsSpan(var * 4));

		if (context != null && uboVertexConstants != null) {
			fixed (float* pSrc = src)
				WriteUniforms(uboVertexConstants, (uint)(var * sizeof(Vector4)), (nint)pSrc, (uint)(numVecs * sizeof(Vector4)));
		}
	}

	public unsafe void SetPixelShaderConstant(int var, Span<float> vec) {
		int numVecs = vec.Length / 4;
		Assert(var + numVecs <= NUM_PIXEL_SHADER_CONSTANTS);

		numVecs = AdjustUpdateRange(vec, desiredPixelShaderConstants.AsSpan(var * 4), numVecs, out int skip);
		if (numVecs == 0)
			return;

		var += skip;
		Span<float> src = vec.Slice(skip * 4, numVecs * 4);

		src.CopyTo(desiredPixelShaderConstants.AsSpan(var * 4));
		src.CopyTo(dynamicPixelShaderConstants.AsSpan(var * 4));

		if (context != null && uboPixelConstants != null) {
			fixed (float* pSrc = src)
				WriteUniforms(uboPixelConstants, (uint)(var * sizeof(Vector4)), (nint)pSrc, (uint)(numVecs * sizeof(Vector4)));
		}
	}

	static int AdjustUpdateRange(ReadOnlySpan<float> src, ReadOnlySpan<float> dst, int numVecs, out int skip) {
		ReadOnlySpan<uint> srcU = MemoryMarshal.Cast<float, uint>(src);
		ReadOnlySpan<uint> dstU = MemoryMarshal.Cast<float, uint>(dst);
		skip = 0;
		int i = 0;

		while (numVecs > 0 && ((srcU[i] ^ dstU[i]) | (srcU[i + 1] ^ dstU[i + 1]) | (srcU[i + 2] ^ dstU[i + 2]) | (srcU[i + 3] ^ dstU[i + 3])) == 0) {
			i += 4; numVecs--; skip++;
		}

		if (numVecs == 0) return 0;

		int tail = i + numVecs * 4 - 4;
		while (numVecs > 1 && ((srcU[tail] ^ dstU[tail]) | (srcU[tail + 1] ^ dstU[tail + 1]) | (srcU[tail + 2] ^ dstU[tail + 2]) | (srcU[tail + 3] ^ dstU[tail + 3])) == 0) {
			tail -= 4; numVecs--;
		}

		return numVecs;
	}

	readonly List<ShaderViewport> viewports = [];

	public void SetViewports(ReadOnlySpan<ShaderViewport> newViewports) {
		viewports.Clear();
		foreach (ShaderViewport viewport in newViewports)
			viewports.Add(viewport);
	}

	void ApplyViewports() {
		if (commands == null)
			return;

		for (int i = 0; i < viewports.Count; i++) {
			ShaderViewport vp = viewports[i];
			commands.SetViewport((uint)i, new Viewport(vp.TopLeftX, vp.TopLeftY, vp.Width, vp.Height, vp.MinZ, vp.MaxZ));
		}
	}

	public void GetViewports(Span<ShaderViewport> dest) {
		for (int i = 0; i < dest.Length && i < viewports.Count; i++)
			dest[i] = viewports[i];
	}

	public void PreInit(IShaderUtil shaderUtil, IServiceProvider services) {
		ShaderUtil = shaderUtil;
		this.services = services;
		MeshMgr = services.GetRequiredService<MeshMgr>();
		ShaderManager = services.GetRequiredService<IShaderSystem>();
	}

	IMaterialSystemHardwareConfig? _HardwareConfig;
	IMaterialSystemHardwareConfig HardwareConfig => _HardwareConfig ??= Singleton<IMaterialSystemHardwareConfig>();

	public ImageFormat GetBackBufferFormat() => ImageFormat.RGBA8888;

	public bool SupportsShadowDepthTextures() => ((HardwareConfig)HardwareConfig).SupportsShadowDepthTexturesCap;
	public ImageFormat GetShadowDepthTextureFormat() => ((HardwareConfig)HardwareConfig).ShadowDepthTextureFormat;
	public ImageFormat GetNullTextureFormat() => ((HardwareConfig)HardwareConfig).NullTextureFormat;

	public void GetBackBufferDimensions(out int width, out int height) {
		Singleton<ILauncherManager>().DisplayedSize(out width, out height);
	}

	bool frameOpen;
	bool lastSrgbWrite;

	struct SrgbState
	{
		public int Write;
		public int Pad0;
		public int Pad1;
		public int Pad2;
	}

	public void BeginFrame() {
		if (commands == null || context == null || frameOpen)
			return;

		commands.Begin();
		commands.SetFramebuffer(context.Swapchain.Framebuffer);

		if (viewports.Count > 0) {
			ShaderViewport vp = viewports[0];
			commands.SetViewport(0, new Viewport(vp.TopLeftX, vp.TopLeftY, vp.Width, vp.Height, vp.MinZ, vp.MaxZ));
		}

		frameOpen = true;
		VeldridUploads.BeginFrame(commands);
	}

	public void EndFrame() {
		if (commands == null || context == null || !frameOpen)
			return;

		VeldridUploads.EndFrame();
		commands.End();
		context.Device.SubmitCommands(commands);
		frameOpen = false;
		AdvanceRetirement();
	}

	VdTexture? fallbackTexture;
	TextureView? fallbackTextureView;
	readonly TextureView?[] textureViews = new TextureView?[(int)Sampler.MaxSamplers];

	void CreateFallbackTexture() {
		if (context == null)
			return;

		TextureDescription description = TextureDescription.Texture2D(1, 1, 1, 1, VdPixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled);
		fallbackTexture = context.Device.ResourceFactory.CreateTexture(ref description);
		fallbackTexture.Name = "ShaderAPI Fallback Texture";

		Span<byte> white = [255, 255, 255, 255];
		unsafe {
			fixed (byte* data = white)
				context.Device.UpdateTexture(fallbackTexture, (nint)data, 4, 0, 0, 0, 1, 1, 1, 0, 0);
		}

		fallbackTextureView = context.Device.ResourceFactory.CreateTextureView(fallbackTexture);
	}

	const int RetireFrames = 3;
	readonly List<IDisposable>[] retired = [[], [], []];
	int retireFrame;

	void Retire(IDisposable? resource) {
		if (resource != null)
			retired[retireFrame].Add(resource);
	}

	void AdvanceRetirement() {
		retireFrame = (retireFrame + 1) % RetireFrames;

		List<IDisposable> due = retired[retireFrame];
		for (int i = 0; i < due.Count; i++)
			due[i].Dispose();

		due.Clear();
	}

	readonly Dictionary<VdTexture, TextureView> textureViewCache = [];

	TextureView ViewFor(ShaderAPITextureHandle_t handle, int sampler) {
		InternalTextureInfo? tex = GetTexture(handle);
		VdTexture? target = tex?.Current;

		if (target == null)
			return fallbackTextureView!;

		if (textureViewCache.TryGetValue(target, out TextureView? existing))
			return existing;

		TextureView created = context!.Device.ResourceFactory.CreateTextureView(target);
		textureViewCache[target] = created;
		return created;
	}

	sealed class ProgramLayout
	{
		public required ResourceLayout Uniforms;
		public required ResourceLayout Textures;
		public required int[] UniformBindings;
		public required int[] TextureBindings;
		public required int[] TextureSlots;
	}

	readonly Dictionary<string, ProgramLayout> programLayouts = [];

	bool UsesAbsoluteBindings => context!.Backend == GraphicsBackend.Vulkan;

	int[] UniformBindingsFor(VeldridProgram program) {
		if (!UsesAbsoluteBindings)
			return program.UniformBindings;

		int[] all = new int[UniformBufferCount];
		for (int i = 0; i < all.Length; i++)
			all[i] = i;

		return all;
	}

	int[] TextureBindingsFor(VeldridProgram program) {
		if (!UsesAbsoluteBindings)
			return program.TextureBindings;

		int count = HardwareConfig.GetSamplerCount() * 2;
		int[] all = new int[count];
		for (int i = 0; i < count; i++)
			all[i] = i;

		return all;
	}

	const int UniformBufferCount = 8;

	static string NameForUniformBinding(int binding) => binding switch {
		0 => "source_matrices",
		1 => "source_base_sharedUBO",
		2 => "source_vertex_sharedUBO",
		3 => "source_pixel_sharedUBO",
		4 => "source_bone_matrices",
		5 => "source_vs_constants",
		6 => "source_ps_constants",
		_ => "source_srgb_state",
	};

	ProgramLayout GetProgramLayout(VeldridProgram program) {
		if (program.Layout is ProgramLayout attached)
			return attached;

		int[] uniformBindings = UniformBindingsFor(program);
		int[] textureBindings = TextureBindingsFor(program);

		string signature = $"{string.Join(',', uniformBindings)}|{string.Join(',', textureBindings)}|"
			+ string.Join(',', program.SamplerNames.OrderBy(p => p.Key).Select(p => $"{p.Key}={p.Value}"));
		if (programLayouts.TryGetValue(signature, out ProgramLayout? cached)) {
			program.Layout = cached;
			return cached;
		}

		ResourceFactory factory = context!.Device.ResourceFactory;

		const VdShaderStages bothStages = VdShaderStages.Vertex | VdShaderStages.Fragment;

		ResourceLayoutElementDescription[] uniformElements = new ResourceLayoutElementDescription[uniformBindings.Length];
		for (int i = 0; i < uniformBindings.Length; i++)
			uniformElements[i] = new ResourceLayoutElementDescription(NameForUniformBinding(uniformBindings[i]), ResourceKind.UniformBuffer, bothStages);

		Dictionary<string, int> slotByName = [];
		foreach ((int declaredSlot, string declaredName) in program.SamplerNames)
			slotByName[declaredName] = declaredSlot;

		ResourceLayoutElementDescription[] textureElements = new ResourceLayoutElementDescription[textureBindings.Length];
		int[] textureSlots = new int[textureBindings.Length];
		for (int i = 0; i < textureBindings.Length; i++) {
			int binding = textureBindings[i];
			int slot = binding / 2;
			bool isTexture = (binding & 1) == 0;
			string name = program.SamplerNames.TryGetValue(slot, out string? declared) ? declared : $"Sampler{slot}";

			textureSlots[i] = name.EndsWith("Raw", StringComparison.Ordinal)
				&& slotByName.TryGetValue(name[..^3], out int baseSlot) ? baseSlot : slot;

			textureElements[i] = new ResourceLayoutElementDescription(
				$"{name}_{(isTexture ? "tex" : "smp")}",
				isTexture ? ResourceKind.TextureReadOnly : ResourceKind.Sampler,
				VdShaderStages.Fragment);
		}

		ProgramLayout layout = new() {
			Uniforms = factory.CreateResourceLayout(new ResourceLayoutDescription(uniformElements)),
			Textures = factory.CreateResourceLayout(new ResourceLayoutDescription(textureElements)),
			UniformBindings = uniformBindings,
			TextureBindings = textureBindings,
			TextureSlots = textureSlots,
		};

		programLayouts.Add(signature, layout);
		program.Layout = layout;
		return layout;
	}

	DeviceBuffer? BufferForUniformBinding(int binding) => binding switch {
		0 => uboMatrices,
		1 => currentShadow?.BaseUBO,
		2 => currentShadow?.VertexUBO,
		3 => currentShadow?.PixelUBO,
		4 => uboBones,
		5 => uboVertexConstants,
		6 => uboPixelConstants,
		_ => uboSrgb,
	};

	readonly Dictionary<(ShadowStateVeldrid, ProgramLayout), ResourceSet> uniformSets = [];

	ResourceSet? GetUniformSet(ProgramLayout layout) {
		if (context == null || currentShadow == null)
			return null;

		if (uniformSets.TryGetValue((currentShadow, layout), out ResourceSet? cached))
			return cached;

		BindableResource[] resources = new BindableResource[layout.UniformBindings.Length];
		for (int i = 0; i < resources.Length; i++) {
			DeviceBuffer? buffer = BufferForUniformBinding(layout.UniformBindings[i]);
			if (buffer == null)
				return null;

			resources[i] = buffer;
		}

		ResourceSet set = context.Device.ResourceFactory.CreateResourceSet(new ResourceSetDescription(layout.Uniforms, resources));
		uniformSets[(currentShadow, layout)] = set;
		return set;
	}

	readonly Dictionary<(ProgramLayout, long), ResourceSet> textureSets = [];

	ResourceSet? GetTextureSet(ProgramLayout layout) {
		if (context == null || samplerCache == null)
			return null;

		HashCode contents = new();
		for (int i = 0; i < layout.TextureSlots.Length; i++) {
			int unit = UnitForSlot(layout.TextureSlots[i]);
			contents.Add(boundTextures[unit]);
			contents.Add(GetTexture(boundTextures[unit])?.Sampler ?? SamplerShadowSettings.Default);
		}

		(ProgramLayout, long) key = (layout, contents.ToHashCode());
		if (textureSets.TryGetValue(key, out ResourceSet? cached))
			return cached;

		BindableResource[] resources = new BindableResource[layout.TextureBindings.Length];
		for (int i = 0; i < resources.Length; i++) {
			int binding = layout.TextureBindings[i];
			int slot = layout.TextureSlots[i];
			int unit = UnitForSlot(slot);

			if ((binding & 1) == 0) {
				resources[i] = ViewFor(boundTextures[unit], slot);
			}
			else {
				SamplerShadowSettings settings = GetTexture(boundTextures[unit])?.Sampler ?? SamplerShadowSettings.Default;
				SamplerDescription description = settings.ToDescription();
				resources[i] = samplerCache.Get(in description);
			}
		}

		ResourceSet set = context.Device.ResourceFactory.CreateResourceSet(new ResourceSetDescription(layout.Textures, resources));
		textureSets[key] = set;
		textureBindingsDirty = false;
		return set;
	}

	internal void DrawIndexed(DeviceBuffer vertexBuffer, DeviceBuffer indexBuffer, VertexFormat vertexFormat, VdPrimitiveTopology topology, uint firstIndex, uint indexCount, DeviceBuffer? colorBuffer = null, uint colorOffset = 0, uint colorStride = 0) {
		if (commands == null || context == null || pipelines == null || programs == null)
			return;

		if (!frameOpen)
			BeginFrame();

		VeldridProgram? program = programs.GetProgram(activeVertexShader.Handle, activePixelShader.Handle);
		if (program == null)
			return;

		VdShader vertex = program.Shaders[0];
		VdShader fragment = program.Shaders[1];
		ProgramLayout programLayout = GetProgramLayout(program);

		PipelineKey key = new(
			in boardState,
			in stencil,
			topology,
			vertexFormat,
			colorBuffer != null ? colorStride : 0,
			vertex,
			fragment,
			context.Swapchain.Framebuffer.OutputDescription);

		Pipeline pipeline;
		try {
			pipeline = pipelines.Get(in key, [programLayout.Uniforms, programLayout.Textures]);
		}
		catch (Exception ex) {
			Warning($"Veldrid: pipeline creation failed: {ex}\n");
			return;
		}

		ResourceSet? set = GetUniformSet(programLayout);
		if (set == null)
			return;

		if (uboSrgb != null && lastSrgbWrite != boardState.SRGBWriteEnable) {
			lastSrgbWrite = boardState.SRGBWriteEnable;
			SrgbState srgbState = new() { Write = lastSrgbWrite ? 1 : 0 };
			WriteUniforms(uboSrgb, 0, ref srgbState);
		}

		VeldridUploads.Flush();

		commands.SetPipeline(pipeline);

		VeldridVertexLayout.LayoutInfo layout = VeldridVertexLayout.InfoFor(vertexFormat, colorBuffer != null ? colorStride : 0);
		for (uint slot = 0; slot < layout.Layouts.Length; slot++) {
			if (colorBuffer != null && slot == (uint)ShaderInputAttribute.Specular)
				commands.SetVertexBuffer(slot, colorBuffer, colorOffset);
			else if (!layout.Present[slot])
				commands.SetVertexBuffer(slot, zeroVertexBuffer);
			else
				commands.SetVertexBuffer(slot, vertexBuffer, layout.Offsets[slot]);
		}

		commands.SetIndexBuffer(indexBuffer, IndexFormat.UInt16);
		commands.SetGraphicsResourceSet(0, set);

		ResourceSet? textures = GetTextureSet(programLayout);
		if (textures != null)
			commands.SetGraphicsResourceSet(1, textures);

		ApplyViewports();

		if (scissorEnabled) {
			commands.SetScissorRect(0,
				(uint)Math.Max(scissorRect.Left, 0),
				(uint)Math.Max(scissorRect.Top, 0),
				(uint)Math.Max(scissorRect.Width, 0),
				(uint)Math.Max(scissorRect.Height, 0));
		}
		else {
			commands.SetScissorRect(0, 0, 0,
				context.Swapchain.Framebuffer.Width,
				context.Swapchain.Framebuffer.Height);
		}

		commands.DrawIndexed(indexCount, 1, firstIndex, 0, 0);
	}

	public void EnableDebugTextureList(bool enable) { }
	public void EnableGetAllTextures(bool enable) { }
	public KeyValues? GetDebugTextureList() => null;
	public int GetTextureMemoryUsed(TextureMemoryType type) => 0;
	public bool IsDebugTextureListFresh(int numFramesAllowed = 1) => false;
	public bool SetDebugTextureRendering(bool enable) => false;

	public bool SetMode(IWindow window, in ShaderDeviceInfo info) {
		ShaderDeviceInfo actualInfo = info;
		actualInfo.Driver = Driver;

		if (!InitDevice(window, in actualInfo))
			return false;

		SetPresentParameters(in actualInfo);
		return OnDeviceInit();
	}

	public bool ChangeVideoMode(in ShaderDeviceInfo info) {
		SetPresentParameters(in info);
		InvokeModeChangeCallbacks();
		return true;
	}

	public bool InitDevice(IWindow window, in ShaderDeviceInfo deviceInfo) {
		IGraphicsProvider graphics = services.GetRequiredService<IGraphicsProvider>();

		if (!graphics.GetNativeWindowInfo(out NativeWindowInfo nativeInfo, window)) {
			Warning("Veldrid: could not retrieve native window handles.\n");
			return false;
		}

		IOpenGLPlatform? glPlatform = GraphicsSelection.Selected == GraphicsBackendChoice.VeldridOpenGL
			? graphics.CreateOpenGLPlatform(window)
			: null;

		try {
			context = VeldridDeviceContext.Create(in nativeInfo, in deviceInfo, deviceInfo.Driver, glPlatform);
		}
		catch (Exception ex) {
			Warning($"Veldrid: device creation failed: {ex.Message}\n");
			return false;
		}

		Device = context;
		Device.MakeCurrent();

		VeldridUploads.Configure(context.Backend);
		VeldridVertexLayout.Configure(context.Backend);

		CreateMatrixStacks();

		return true;
	}

	public bool IsActive() => Device != null;
	public bool IsUsingGraphics() => IsActive();
	bool IShaderDevice.IsDeactivated() => IsDeactivated();

	ILauncherManager LauncherManager => services.GetRequiredService<ILauncherManager>();
	public int GetCurrentAdapter() => LauncherManager.GetCurrentDisplayIndex();
	public int GetModeCount(int adapter) => LauncherManager.GetDisplayModeCount(adapter);
	public void GetModeInfo(int adapter, int mode, out ShaderDisplayMode info) => LauncherManager.GetDisplayMode(adapter, mode, out info);

	readonly List<Action> ModeChangeCallbacks = [];

	public void AddModeChangeCallBack(Action func) {
		ModeChangeCallbacks.Add(func);
	}

	public void InvokeModeChangeCallbacks() {
		foreach (Action callback in ModeChangeCallbacks)
			callback();
	}

	public void Present() {
		if (commands != null && context != null && clearsThisFrame == 0) {
			if (!frameOpen)
				BeginFrame();

			commands.ClearColorTarget(0, clearColor);
			commands.ClearDepthStencil(1.0f, 0);
		}

		EndFrame();
		Device!.SwapBuffers();

		clearsThisFrame = 0;
	}

	public IMesh CreateStaticMesh(VertexFormat format, ReadOnlySpan<char> textureGroup, IMaterial? material) => MeshMgr.CreateStaticMesh(format, textureGroup, material);
	public void DestroyStaticMesh(IMesh mesh) => MeshMgr.DestroyStaticMesh(mesh);
	public void ReacquireResources() => MeshMgr.RestoreBuffers();
	public void ReleaseResources() => MeshMgr.DiscardVertexBuffers();

	MaterialMatrixMode matrixMode;
	readonly Matrix4x4[] matrices = new Matrix4x4[(int)MaterialMatrixMode.Count];

	public void MatrixMode(MaterialMatrixMode mode) {
		matrixMode = mode;
	}

	Matrix4x4 CorrectClipSpace(in Matrix4x4 projection) {
		if (context == null)
			return projection;

		Matrix4x4 result = projection;

		if (context.Device.IsDepthRangeZeroToOne) {
			Matrix4x4 depth = Matrix4x4.Identity;
			depth.M33 = 0.5f;
			depth.M43 = 0.5f;
			result *= depth;
		}

		if (context.Device.IsClipSpaceYInverted) {
			Matrix4x4 flip = Matrix4x4.Identity;
			flip.M22 = -1;
			result *= flip;
		}

		return result;
	}

	public unsafe void LoadMatrix(in Matrix4x4 m4x4) {
		matrices[(int)matrixMode] = m4x4;

		if (context != null && uboMatrices != null) {
			Matrix4x4 value = matrixMode == MaterialMatrixMode.Projection ? CorrectClipSpace(in m4x4) : m4x4;
			WriteUniforms(uboMatrices, (uint)((int)matrixMode * sizeof(Matrix4x4)), ref value);
		}

		if (matrixMode == MaterialMatrixMode.View) {
			CacheWorldSpaceCameraPosition();
			UpdateVertexShaderFogParams();
		}
	}

	void UpdateVertexShaderFogParams() {
		Span<float> vertexShaderCameraPos = [
			WorldSpaceCameraPosition.X,
			WorldSpaceCameraPosition.Y,
			WorldSpaceCameraPosition.Z,
			0.0f,
		];

		SetVertexShaderConstant(VertexShaderConst.CameraPos, vertexShaderCameraPos);
	}

	public void GetMatrix(MaterialMatrixMode mode, out Matrix4x4 dst) {
		dst = matrices[(int)mode];
	}

	public void LoadIdentity() {
		LoadMatrix(Matrix4x4.Identity);
	}

	int currentNumBones;
	public int GetCurrentNumBones() => currentNumBones;
	public void SetNumBoneWeights(int numBones) => currentNumBones = numBones;

	readonly Dictionary<string, int> uniformIds = [];
	readonly List<string> uniformNames = [];
	readonly Dictionary<string, int> samplerRemap = [];

	public int LocateShaderUniform(ReadOnlySpan<char> name) {
		if (name.IsEmpty)
			return -1;

		string key = new(name);
		if (uniformIds.TryGetValue(key, out int existing))
			return existing;

		int id = uniformNames.Count;
		uniformNames.Add(key);
		uniformIds[key] = id;
		return id;
	}

	public nint GetCurrentProgram() => 0;

	public void SetShaderUniform(int uniform, int integer) {
		if (uniform < 0 || uniform >= uniformNames.Count)
			return;

		string name = uniformNames[uniform];
		if (integer < 0 || integer >= (int)Sampler.MaxSamplers)
			return;

		if (samplerRemap.TryGetValue(name, out int previous) && previous == integer)
			return;

		samplerRemap[name] = integer;
		textureBindingsDirty = true;
	}

	int UnitForSlot(int slot) {
		if (ShaderManager is not ShaderSystem system)
			return slot;

		foreach ((string name, int declared) in system.SamplerBindings) {
			if (declared != slot)
				continue;

			if (samplerRemap.TryGetValue(name, out int unit))
				return unit;

			break;
		}

		return slot;
	}
	public void SetShaderUniform(int uniform, float fl) { }
	public void SetShaderUniform(int uniform, ReadOnlySpan<float> flConsts) { }
	public void SetShaderUniform(IMaterialVar textureVar) {
		int uniform = LocateShaderUniform(textureVar.GetName());
		if (uniform == -1)
			return;

		switch (textureVar.GetVarType()) {
			case MaterialVarType.Float: SetShaderUniform(uniform, textureVar.GetFloatValue()); break;
			case MaterialVarType.Int: SetShaderUniform(uniform, textureVar.GetIntValue()); break;
		}
	}

	public void BindStandardTexture(Sampler sampler, StandardTextureId id) {
		ShaderUtil.BindStandardTexture(sampler, id);
	}

	readonly List<InternalTextureInfo?> Textures = [null];
	readonly Stack<ShaderAPITextureHandle_t> freeTextureHandles = [];
	VeldridSamplerCache? samplerCache;

	readonly ShaderAPITextureHandle_t[] boundTextures = new ShaderAPITextureHandle_t[(int)Sampler.MaxSamplers];
	readonly SamplerShadowSettings[] samplerSettings = new SamplerShadowSettings[(int)Sampler.MaxSamplers];

	InternalTextureInfo? GetTexture(ShaderAPITextureHandle_t handle) =>
		handle > 0 && handle < Textures.Count ? Textures[handle] : null;

	ShaderAPITextureHandle_t CreateTextureHandle() {
		if (freeTextureHandles.TryPop(out ShaderAPITextureHandle_t recycled)) {
			Textures[recycled] = new InternalTextureInfo();
			return recycled;
		}

		Textures.Add(new InternalTextureInfo());
		return Textures.Count - 1;
	}

	void CreateTextureHandles(Span<ShaderAPITextureHandle_t> handles) {
		for (int i = 0; i < handles.Length; i++)
			handles[i] = CreateTextureHandle();
	}

	public void BindTexture(Sampler sampler, ShaderAPITextureHandle_t textureHandle) {
		if (textureHandle == INVALID_SHADERAPI_TEXTURE_HANDLE)
			return;

		boundTextures[(int)sampler] = textureHandle;
		textureBindingsDirty = true;
	}

	bool textureBindingsDirty = true;

	public bool CanDownloadTextures() => !IsDeactivated();

	ShaderAPITextureHandle_t ModifyTextureHandle;

	public void ModifyTexture(int textureHandle) {
		ModifyTextureHandle = textureHandle;
	}

	public void TexImageFromVTF(IVTFTexture? vtf, int vtfFrame) {
		Assert(vtf != null);
		Assert(ModifyTextureHandle != INVALID_SHADERAPI_TEXTURE_HANDLE);

		InternalTextureInfo? tex = GetTexture(ModifyTextureHandle);
		if (tex == null || vtf == null)
			return;

		int faceCount = tex.IsCubeMap ? 6 : 1;
		for (int face = 0; face < faceCount; face++) {
			for (int mip = 0; mip < vtf.MipCount(); mip++) {
				Span<byte> data = vtf.ImageData(vtfFrame, face, mip);
				if (data.IsEmpty)
					continue;

				vtf.ComputeMipLevelDimensions(mip, out int mipWidth, out int mipHeight, out _);
				UploadTexture(tex, mip, face, 0, 0, mipWidth, mipHeight, vtf.Format(), data);
			}
		}
	}

	readonly HashSet<(ImageFormat, ImageFormat)> reportedConversions = [];
	readonly HashSet<ShaderAPITextureHandle_t> reportedRenderTargets = [];

	void UploadTexture(InternalTextureInfo tex, int mip, int face, int x, int y, int width, int height, ImageFormat srcFormat, Span<byte> data, int srcStride = 0) {
		if (context == null)
			return;

		VdTexture? target = tex.Current;
		if (target == null || data.IsEmpty || width <= 0 || height <= 0)
			return;

		int srcBpp = ImageLoader.SizeInBytes(srcFormat);
		int packedStride = width * srcBpp;
		if (srcStride > 0 && srcStride != packedStride) {
			byte[] packed = new byte[packedStride * height];
			for (int row = 0; row < height; row++) {
				int from = row * srcStride;
				if (from + packedStride > data.Length)
					break;

				data.Slice(from, packedStride).CopyTo(packed.AsSpan(row * packedStride));
			}

			data = packed;
		}

		Span<byte> upload = data;
		if (srcFormat != tex.Format) {
			int converted = ImageLoader.GetMemRequired(width, height, 1, tex.Format, false);
			byte[] scratch = new byte[converted];

			if (ImageLoader.ConvertImageFormat(data, srcFormat, scratch, tex.Format, width, height)) {
				upload = scratch;
			}
			else if (TrySwizzle32(data, srcFormat, scratch, tex.Format)) {
				upload = scratch;
			}
			else {
				if (reportedConversions.Add((srcFormat, tex.Format)))
					Warning($"Veldrid: cannot convert {srcFormat} to {tex.Format} (first seen on '{tex.DebugName}')\n");
				return;
			}
		}

		uint layer = (uint)(tex.IsCubeMap ? face : 0);

		int regionSize = ImageLoader.GetMemRequired(width, height, 1, tex.Format, false);
		if (upload.Length < regionSize) {
			if (reportedConversions.Add((srcFormat, tex.Format)))
				Warning($"Veldrid: dropped {width}x{height} mip {mip} of '{tex.DebugName}': have {upload.Length} bytes of {srcFormat}, need {regionSize} for {tex.Format}\n");

			return;
		}

		upload = upload[..regionSize];

		unsafe {
			fixed (byte* source = upload) {
				context.Device.UpdateTexture(
					target,
					(nint)source,
					(uint)upload.Length,
					(uint)x, (uint)y, 0,
					(uint)width, (uint)height, 1,
					(uint)mip,
					layer);
			}
		}
	}

	static bool ChannelOrderOf(ImageFormat format, out int r, out int g, out int b, out int a) {
		switch (format) {
			case ImageFormat.RGBA8888: r = 0; g = 1; b = 2; a = 3; return true;
			case ImageFormat.BGRA8888: r = 2; g = 1; b = 0; a = 3; return true;
			case ImageFormat.BGRX8888: r = 2; g = 1; b = 0; a = 3; return true;
			case ImageFormat.ARGB8888: r = 1; g = 2; b = 3; a = 0; return true;
			case ImageFormat.ABGR8888: r = 3; g = 2; b = 1; a = 0; return true;
			default: r = g = b = a = 0; return false;
		}
	}

	static bool TrySwizzle32(ReadOnlySpan<byte> src, ImageFormat srcFormat, Span<byte> dst, ImageFormat dstFormat) {
		if (!ChannelOrderOf(srcFormat, out int sr, out int sg, out int sb, out int sa))
			return false;
		if (!ChannelOrderOf(dstFormat, out int dr, out int dg, out int db, out int da))
			return false;

		int pixels = Math.Min(src.Length, dst.Length) / 4;
		for (int i = 0; i < pixels; i++) {
			int s = i * 4;
			int d = i * 4;
			byte red = src[s + sr];
			byte green = src[s + sg];
			byte blue = src[s + sb];
			byte alpha = src[s + sa];

			dst[d + dr] = red;
			dst[d + dg] = green;
			dst[d + db] = blue;
			dst[d + da] = alpha;
		}

		return true;
	}

	public void TexImage2D(int mip, int face, ImageFormat dstFormat, int zOffset, int width, int height, ImageFormat srcFormat, bool srcIsTiled, Span<byte> imageData) {
		InternalTextureInfo? tex = GetTexture(ModifyTextureHandle);
		if (tex == null)
			return;

		UploadTexture(tex, mip, face, 0, 0, width >> mip, height >> mip, srcFormat, imageData);
	}

	public void TexSubImage2D(int mip, int face, int x, int y, int z, int width, int height, ImageFormat srcFormat, int srcStride, Span<byte> imageData) {
		InternalTextureInfo? tex = GetTexture(ModifyTextureHandle);
		if (tex == null)
			return;

		UploadTexture(tex, mip, face, x, y, width >> mip, height >> mip, srcFormat, imageData, srcStride);
	}

	public ShaderAPITextureHandle_t CreateTexture(int width, int height, int depth, ImageFormat imageFormat, ushort mipCount, int copies, CreateTextureFlags creationFlags, ReadOnlySpan<char> debugName, ReadOnlySpan<char> textureGroup) {
		ShaderAPITextureHandle_t handle = 0;
		CreateTextures(new Span<int>(ref handle), 1, width, height, depth, imageFormat, mipCount, copies, creationFlags, debugName, textureGroup);
		return handle;
	}

	public void CreateTextures(Span<ShaderAPITextureHandle_t> textureHandles, int count, int width, int height, int depth, ImageFormat imageFormat, ushort mipCount, int copies, CreateTextureFlags creationFlags, ReadOnlySpan<char> debugName, ReadOnlySpan<char> textureGroup) {
		if (context == null)
			return;

		if (depth == 0)
			depth = 1;

		bool isCubeMap = (creationFlags & CreateTextureFlags.Cubemap) != 0;
		bool isRenderTarget = (creationFlags & CreateTextureFlags.RenderTarget) != 0;
		bool isDepthBuffer = (creationFlags & CreateTextureFlags.DepthBuffer) != 0;
		bool isSRGB = (creationFlags & CreateTextureFlags.SRGB) != 0;

		InternalTextureFlags setFlags = 0;
		if ((creationFlags & (CreateTextureFlags.Dynamic | CreateTextureFlags.Managed)) != 0)
			setFlags |= InternalTextureFlags.IsLockable;
		if ((creationFlags & CreateTextureFlags.VertexTexture) != 0)
			setFlags |= InternalTextureFlags.IsVertexTexture;

		if (mipCount == 0)
			mipCount = 1;

		CreateTextureHandles(textureHandles);

		ImageFormat dstFormat = VeldridTextureFormat.NearestSupported(imageFormat);
		VeldridTextureFormat.TryMap(dstFormat, isSRGB, out VdPixelFormat pixelFormat);

		bool depthFormat = VeldridTextureFormat.RequiresDepthStencilUsage(pixelFormat);

		TextureUsage usage;
		if (isDepthBuffer || depthFormat) {
			usage = TextureUsage.DepthStencil;
			if (!isDepthBuffer)
				usage |= TextureUsage.Sampled;
		}
		else {
			usage = TextureUsage.Sampled;
			if (isRenderTarget)
				usage |= TextureUsage.RenderTarget;
		}

		if (isCubeMap)
			usage |= TextureUsage.Cubemap;

		for (int i = 0; i < count; i++) {
			InternalTextureInfo texture = GetTexture(textureHandles[i])!;
			texture.Flags = InternalTextureFlags.IsAllocated | setFlags;
			texture.DebugName = new string(debugName.SliceNullTerminatedString());
			texture.Width = width;
			texture.Height = height;
			texture.Depth = depth;
			texture.Levels = mipCount;
			texture.Count = count;
			texture.CountIndex = i;
			texture.Format = dstFormat;
			texture.PixelFormat = pixelFormat;
			texture.CreationFlags = creationFlags;

			int copyCount = Math.Max(copies, 1);
			texture.NumCopies = (byte)copyCount;
			texture.Copies = new VdTexture[copyCount];

			for (int k = 0; k < copyCount; k++) {
				TextureDescription description = TextureDescription.Texture2D(
					(uint)width,
					(uint)height,
					(uint)mipCount,
					(uint)(isCubeMap ? 6 : 1),
					pixelFormat,
					usage);

				VdTexture created = context.Device.ResourceFactory.CreateTexture(ref description);
				created.Name = $"ShaderAPI Texture '{texture.DebugName}' [frame {i} copy {k}]";
				texture.Copies[k] = created;
			}

			texture.CurrentCopy = 0;
			ComputeStatsInfo(texture, isCubeMap, depth > 1);
		}
	}

	static void ComputeStatsInfo(InternalTextureInfo textureData, bool isCubeMap, bool isVolumeTexture) {
		textureData.SizeBytes = 0;
		textureData.SizeTexels = 0;

		if (isCubeMap || isVolumeTexture)
			return;

		int numLevels = textureData.GetLevelCount();
		for (int i = 0; i < numLevels; ++i) {
			textureData.SizeBytes += (nuint)ImageLoader.GetMemRequired(textureData.Width >> i, textureData.Height >> i, 1, textureData.GetImageFormat(), false);
			textureData.SizeTexels += (textureData.Width >> i) * (textureData.Height >> i);
		}
	}

	public ShaderAPITextureHandle_t CreateDepthTexture(ImageFormat imageFormat, ushort width, ushort height, Span<char> debugName, bool texture) {
		return CreateTexture(width, height, 1, imageFormat, 1, 1,
			CreateTextureFlags.DepthBuffer, debugName, TEXTURE_GROUP_RENDER_TARGET);
	}

	public bool IsTexture(ShaderAPITextureHandle_t handle) {
		InternalTextureInfo? tex = GetTexture(handle);
		return tex != null && (tex.Flags & InternalTextureFlags.IsAllocated) != 0;
	}

	public void DeleteTexture(ShaderAPITextureHandle_t handle) {
		InternalTextureInfo? tex = GetTexture(handle);
		if (tex == null)
			return;

		tex.Dispose();
		Textures[handle] = null;
		freeTextureHandles.Push(handle);
	}

	public ImageFormat GetNearestSupportedFormat(ImageFormat fmt, bool filteringRequired = true) => VeldridTextureFormat.NearestSupported(fmt);

	Sampler ModifySampler => Sampler.Sampler0;

	public void TexWrap(TexCoordComponent coord, TexWrapMode wrapMode) {
		ref SamplerShadowSettings settings = ref samplerSettings[(int)ModifySampler];
		VdSamplerAddressMode mode = wrapMode.ToVeldrid();

		switch (coord) {
			case TexCoordComponent.S: settings.AddressU = mode; break;
			case TexCoordComponent.T: settings.AddressV = mode; break;
			case TexCoordComponent.U: settings.AddressW = mode; break;
			default: Warning("ShaderAPIVeldrid.TexWrap: unknown coord\n"); return;
		}

		textureBindingsDirty = true;
	}

	public void TexMinFilter(TexFilterMode mode) {
		samplerSettings[(int)ModifySampler].Filter = mode.ToVeldrid();
		textureBindingsDirty = true;
	}

	public void TexMagFilter(TexFilterMode mode) {
		switch (mode) {
			case TexFilterMode.NearestMipmapNearest:
				Warning("ShaderAPIVeldrid.TexMagFilter: TexFilterMode.NearestMipmapNearest is invalid\n");
				return;
			case TexFilterMode.LinearMipmapNearest:
				Warning("ShaderAPIVeldrid.TexMagFilter: TexFilterMode.LinearMipmapNearest is invalid\n");
				return;
			case TexFilterMode.NearestMipmapLinear:
				Warning("ShaderAPIVeldrid.TexMagFilter: TexFilterMode.NearestMipmapLinear is invalid\n");
				return;
			case TexFilterMode.LinearMipmapLinear:
				Warning("ShaderAPIVeldrid.TexMagFilter: TexFilterMode.LinearMipmapLinear is invalid\n");
				return;
		}

		InternalTextureInfo? tex = GetTexture(ModifyTextureHandle);
		if (tex == null)
			return;

		tex.Sampler.Filter = mode.ToVeldrid();
		textureBindingsDirty = true;
	}

	struct TextureLock
	{
		public bool Locked;
		public int Mip;
		public int CubeID;
		public int X;
		public int Y;
		public int W;
		public int H;
		public ShaderAPITextureHandle_t Handle;
		public ImageFormat Format;
	}

	TextureLock texLock;
	byte[] lockdata = [];

	Memory<byte> GetTempLockBuffer(ImageFormat format, int width, int height) {
		int desiredLength = ImageLoader.GetMemRequired(width, height, 1, format, false);

		if (desiredLength > lockdata.Length)
			lockdata = new byte[MathLib.CeilPow2(desiredLength)];

		Array.Clear(lockdata);
		return lockdata.AsMemory()[..desiredLength];
	}

	bool BeginTexLock(int level, int cubeFaceID, int xOffset, int yOffset, int width, int height, out InternalTextureInfo? info) {
		info = GetTexture(ModifyTextureHandle);
		if (info == null || texLock.Locked)
			return false;

		texLock = new TextureLock {
			Locked = true,
			Mip = level,
			CubeID = cubeFaceID,
			X = xOffset,
			Y = yOffset,
			W = width,
			H = height,
			Handle = ModifyTextureHandle,
			Format = info.Format,
		};

		return true;
	}

	Memory<byte> GetLockRegion(InternalTextureInfo info, out int stride) {
		int bpp = ImageLoader.SizeInBytes(info.Format);
		stride = info.Width * bpp;

		int required = stride * info.Height;
		if (info.Shadow == null || info.Shadow.Length < required)
			info.Shadow = new byte[required];

		int start = (texLock.Y * stride) + (texLock.X * bpp);
		int length = Math.Max(0, Math.Min(info.Shadow.Length - start, stride * texLock.H));
		return info.Shadow.AsMemory(start, length);
	}

	public bool TexLock(int level, int cubeFaceID, int xOffset, int yOffset, int width, int height, ref PixelWriter writer) {
		if (!BeginTexLock(level, cubeFaceID, xOffset, yOffset, width, height, out InternalTextureInfo? info))
			return false;

		Memory<byte> region = GetLockRegion(info!, out int stride);
		writer.SetPixelMemory(info!.Format, region.Span, stride);
		return true;
	}

	public bool TexLock(int level, int cubeFaceID, int xOffset, int yOffset, int width, int height, ref PixelWriterMem writer) {
		if (!BeginTexLock(level, cubeFaceID, xOffset, yOffset, width, height, out InternalTextureInfo? info))
			return false;

		Memory<byte> region = GetLockRegion(info!, out int stride);
		writer.SetPixelMemory(info!.Format, region, stride);
		return true;
	}

	public void TexUnlock() {
		if (!texLock.Locked) {
			texLock = default;
			return;
		}

		InternalTextureInfo? tex = GetTexture(texLock.Handle);
		if (tex?.Shadow != null) {
			int size = ImageLoader.GetMemRequired(tex.Width, tex.Height, 1, tex.Format, false);
			size = Math.Min(size, tex.Shadow.Length);
			UploadTexture(tex, texLock.Mip, texLock.CubeID, 0, 0, tex.Width, tex.Height,
				tex.Format, tex.Shadow.AsSpan(0, size));
		}

		texLock = default;
	}

	public unsafe void LoadBoneMatrix(int boneIndex, in Matrix3x4 matrix) {
		if (context == null || uboBones == null || boneIndex < 0 || boneIndex >= Studio.MAXSTUDIOBONES)
			return;

		Matrix4x4 transposed = Matrix4x4.Transpose(matrix);
		WriteUniforms(uboBones, (uint)(boneIndex * sizeof(Matrix4x4)), ref transposed);

		if (boneIndex > MaxBoneLoaded)
			MaxBoneLoaded = boneIndex;

		if (boneIndex == 0) {
			MatrixMode(MaterialMatrixMode.Model);
			LoadMatrix(matrix);
		}
	}

	int MaxBoneLoaded;

	public bool DoRenderTargetsNeedSeparateDepthBuffer() => false;

	public void EnableLinearColorSpaceFrameBuffer(bool v) { }

	public void SetRenderTargetEx(int rt, ShaderAPITextureHandle_t colorTextureHandle = (ShaderAPITextureHandle_t)ShaderRenderTarget.Backbuffer, ShaderAPITextureHandle_t depthTextureHandle = (ShaderAPITextureHandle_t)ShaderRenderTarget.Depthbuffer) {
		if (context == null || commands == null)
			return;

		FlushBufferedPrimitives();

		if (colorTextureHandle != (ShaderAPITextureHandle_t)ShaderRenderTarget.Backbuffer && reportedRenderTargets.Add(colorTextureHandle))
			Warning($"Veldrid: offscreen render target {colorTextureHandle} requested; rendering to the backbuffer instead.\n");

		if (frameOpen)
			commands.SetFramebuffer(context.Swapchain.Framebuffer);
	}

	public int GetCurrentDynamicVBSize() => MeshMgr.VERTEX_BUFFER_SIZE * 32;

	public int GetMaxVerticesToRender(IMaterial material) => MeshMgr.GetMaxVerticesToRender(material);
	public int GetMaxIndicesToRender() => MeshMgr.GetMaxIndicesToRender();

	public IShaderDevice GetShaderDevice() => this;

	public bool IsHWMorphingEnabled() => false;

	Vector4 WorldSpaceCameraPosition = new(0, 0, 0.01f, 0);

	void CacheWorldSpaceCameraPosition() {
		ref Matrix4x4 view = ref matrices[(int)MaterialMatrixMode.View];

		WorldSpaceCameraPosition.X = -(view.M41 * view.M11 + view.M42 * view.M12 + view.M43 * view.M13);
		WorldSpaceCameraPosition.Y = -(view.M41 * view.M21 + view.M42 * view.M22 + view.M43 * view.M23);
		WorldSpaceCameraPosition.Z = -(view.M41 * view.M31 + view.M42 * view.M32 + view.M43 * view.M33);
		WorldSpaceCameraPosition.W = 1.0f;

		if (MathF.Abs(WorldSpaceCameraPosition.Z) <= 0.00001f)
			WorldSpaceCameraPosition.Z = 0.01f;
	}

	public void GetWorldSpaceCameraPosition(ref Span<float> eyePos) {
		CacheWorldSpaceCameraPosition();

		if (eyePos.Length > 0) eyePos[0] = WorldSpaceCameraPosition.X;
		if (eyePos.Length > 1) eyePos[1] = WorldSpaceCameraPosition.Y;
		if (eyePos.Length > 2) eyePos[2] = WorldSpaceCameraPosition.Z;
	}

	public int GetPixelFogCombo() => 0;

	public double CurrentTime() => Platform.Time;

	public FlashlightState GetFlashlightState(out Matrix4x4 worldToTexture) {
		worldToTexture = flashlightWorldToTexture;
		return flashlightState;
	}

	public FlashlightState GetFlashlightStateEx(out Matrix4x4 worldToTexture, out ITexture? depthTexture) {
		worldToTexture = flashlightWorldToTexture;
		depthTexture = flashlightDepthTexture;
		return flashlightState;
	}

	public bool ShouldWriteDepthToDestAlpha() => false;

	public void MarkUnusedVertexFields(int v, Span<bool> unusedTexCoords) {

	}

	readonly Dictionary<RenderParamInt, int> intRenderingParameters = [];

	public int GetIntRenderingParameter(RenderParamInt parm) => intRenderingParameters.GetValueOrDefault(parm);

	public void ExecuteCommandBuffer(ICommandStorageBuffer storage) {
		ExecuteCommandBuffer(storage, storage.Base());
	}

	void ExecuteCommandBuffer(ICommandStorageBuffer storage, Span<byte> cmdBuf) {
		int offset = 0;

		while (true) {
			CommandBufferCommand cmd = (CommandBufferCommand)MemoryMarshal.Read<int>(cmdBuf[offset..]);
			switch (cmd) {
				case CommandBufferCommand.End:
					return;

				case CommandBufferCommand.Jump: {
						int reference = MemoryMarshal.Read<int>(cmdBuf[(offset + 4)..]);
						storage = storage.Reference(reference);
						cmdBuf = storage.Base();
						offset = 0;
						continue;
					}

				case CommandBufferCommand.Jsr: {
						int reference = MemoryMarshal.Read<int>(cmdBuf[(offset + 4)..]);
						ExecuteCommandBuffer(storage.Reference(reference));
						offset += 8;
						break;
					}

				case CommandBufferCommand.SetPixelShaderFloatConst: {
						int firstReg = MemoryMarshal.Read<int>(cmdBuf[(offset + 4)..]);
						int numRegs = MemoryMarshal.Read<int>(cmdBuf[(offset + 8)..]);
						SetPixelShaderConstant(firstReg, MemoryMarshal.Cast<byte, float>(cmdBuf.Slice(offset + 12, numRegs * 16)));
						offset += 12 + numRegs * 16;
						break;
					}

				case CommandBufferCommand.SetVertexShaderFloatConst: {
						int firstReg = MemoryMarshal.Read<int>(cmdBuf[(offset + 4)..]);
						int numRegs = MemoryMarshal.Read<int>(cmdBuf[(offset + 8)..]);
						SetVertexShaderConstant(firstReg, MemoryMarshal.Cast<byte, float>(cmdBuf.Slice(offset + 12, numRegs * 16)));
						offset += 12 + numRegs * 16;
						break;
					}

				case CommandBufferCommand.SetPixelShaderFogParams:
					offset += 8;
					break;

				case CommandBufferCommand.StoreEyePosInPsConst: {
						int reg = MemoryMarshal.Read<int>(cmdBuf[(offset + 4)..]);
						SetPixelShaderConstant(reg, MemoryMarshal.CreateSpan(ref WorldSpaceCameraPosition.X, 4));
						offset += 8;
						break;
					}

				case CommandBufferCommand.CommitPixelShaderLighting: {
						int reg = MemoryMarshal.Read<int>(cmdBuf[(offset + 4)..]);
						CommitPixelShaderLighting(reg);
						offset += 8;
						break;
					}

				case CommandBufferCommand.SetPixelShaderStateAmbientLightCube: {
						int reg = MemoryMarshal.Read<int>(cmdBuf[(offset + 4)..]);
						SetPixelShaderConstant(reg, MemoryMarshal.CreateSpan(ref AmbientLightCube[0].X, 24));
						offset += 8;
						break;
					}

				case CommandBufferCommand.SetAmbientCubeDynamicStateVertexShader:
					SetVertexShaderStateAmbientLightCube();
					offset += 4;
					break;

				case CommandBufferCommand.SetDepthFeatheringConst:
					offset += 12;
					break;

				case CommandBufferCommand.BindStandardTexture: {
						Sampler sampler = (Sampler)MemoryMarshal.Read<int>(cmdBuf[(offset + 4)..]);
						StandardTextureId texture = (StandardTextureId)MemoryMarshal.Read<int>(cmdBuf[(offset + 8)..]);
						BindStandardTexture(sampler, texture);
						offset += 12;
						break;
					}

				case CommandBufferCommand.BindShaderApiTextureHandle: {
						Sampler sampler = (Sampler)MemoryMarshal.Read<int>(cmdBuf[(offset + 4)..]);
						ShaderAPITextureHandle_t texture = (ShaderAPITextureHandle_t)MemoryMarshal.Read<nint>(cmdBuf[(offset + 8)..]);
						BindTexture(sampler, texture);
						offset += 8 + IntPtr.Size;
						break;
					}

				case CommandBufferCommand.SetPsHIndex:
					SetPixelShaderIndex(MemoryMarshal.Read<int>(cmdBuf[(offset + 4)..]));
					offset += 8;
					break;

				case CommandBufferCommand.SetVsHIndex:
					SetVertexShaderIndex(MemoryMarshal.Read<int>(cmdBuf[(offset + 4)..]));
					offset += 8;
					break;

				default:
					throw new InvalidOperationException($"Unknown command {(int)cmd}");
			}
		}
	}
}
