﻿using K4os.Hash.xxHash;

using Microsoft.Extensions.DependencyInjection;

using OpenGL;

using Source.Bitmap;
using Source.Common;
using Source.Common.Bitmap;
using Source.Common.Engine;
using Source.Common.Launcher;
using Source.Common.MaterialSystem;
using Source.Common.ShaderAPI;
using Source.MaterialSystem.Meshes;

using System.Numerics;
using System.Reflection.Metadata;
using System.Text;

namespace Source.MaterialSystem;

public enum UniformBufferBindingLocation
{
	SharedMatrices = 0,
	SharedBaseShader = 1,
	SharedVertexShader = 2,
	SharedPixelShader = 3
}

public struct GfxViewport
{
	public int X;
	public int Y;
	public int Width;
	public int Height;
	public float MinZ;
	public float MaxZ;
}

public enum CommitFuncType
{
	PerDraw,
	PerPass
}

public class ShaderAPIGl46 : IShaderAPI, IShaderDevice
{
	public MeshMgr MeshMgr;
	public IShaderSystem ShaderManager;

	public GraphicsDriver GetDriver() => Driver;
	private bool ready;

	uint renderFBO;

	public bool OnDeviceInit() {
		AcquireInternalRenderTargets();

		CreateMatrixStacks();

		ShaderManager.Init();
		MeshMgr.Init();
		Device!.SetSwapInterval(0);

		InitRenderState();

		ClearColor4ub(0, 0, 0, 1);
		ClearBuffers(true, true, true, -1, -1);

		return true;
	}

	DeviceState DeviceState = DeviceState.OK;

	public void ClearBuffers(bool clearColor, bool clearDepth, bool clearStencil, int renderTargetWidth = -1, int renderTargetHeight = -1) {
		if (IsDeactivated())
			return;

		FlushBufferedPrimitives();
		uint flags = 0;

		if (clearColor)
			flags |= Gl46.GL_COLOR_BUFFER_BIT;
		if (clearDepth)
			flags |= Gl46.GL_DEPTH_BUFFER_BIT;
		if (clearStencil)
			flags |= Gl46.GL_STENCIL_BUFFER_BIT;

		Gl46.glClear(flags);
	}

	public void ClearColor3ub(byte r, byte g, byte b) => Gl46.glClearColor(r / 255f, g / 255f, b / 255f, 1);
	public void ClearColor4ub(byte r, byte g, byte b, byte a) => Gl46.glClearColor(r / 255f, g / 255f, b / 255f, a / 255f);

	VertexShaderHandle activeVertexShader = VertexShaderHandle.INVALID;
	PixelShaderHandle activePixelShader = PixelShaderHandle.INVALID;
	bool pipelineChanged = false;
	uint lastShader;
	public void BindVertexShader(in VertexShaderHandle vertexShader) {
		activeVertexShader = vertexShader;
		pipelineChanged = true;
	}

	public void BindPixelShader(in PixelShaderHandle pixelShader) {
		activePixelShader = pixelShader;
		pipelineChanged = true;
	}

	public void CallCommitFuncs(CommitFuncType func, bool usingFixedFunction, bool force = false) {

	}

	uint uboMatrices;

	private unsafe void CreateMatrixStacks() {
		uboMatrices = glCreateBuffer();
		glObjectLabel(GL_BUFFER, uboMatrices, "ShaderAPI Shared Matrix UBO");
		glNamedBufferData(uboMatrices, sizeof(Matrix4x4) * 3, null, GL_DYNAMIC_DRAW);
		glBindBufferBase(GL_UNIFORM_BUFFER, (int)UniformBufferBindingLocation.SharedMatrices, uboMatrices);
	}

	private void AcquireInternalRenderTargets() {
		renderFBO = glCreateFramebuffer();
	}

	public void InitRenderState() {
		glDisable(GL_DEPTH_TEST);

		if (!IsDeactivated())
			ResetRenderState();
	}

	public void SetPresentParameters(in ShaderDeviceInfo info) {
		PresentParameters = info;
	}

	private void ResetRenderState(bool fullReset = true) {
		if (fullReset) {
			InitVertexAndPixelShaders();
		}

		SetDefaultState();
	}

	private void InitVertexAndPixelShaders() {
		// TODO; everything before this call
		ShaderManager.ResetShaderState();
	}

	public MaterialFogMode GetSceneFogMode() {
		return SceneFogMode;
	}

	MaterialFogMode SceneFogMode = MaterialFogMode.None;

	internal IShaderUtil ShaderUtil;

	public bool InFlashlightMode() {
		return ShaderUtil.InFlashlightMode();
	}


	Mesh? RenderMesh;
	IMaterialInternal? Material;

	uint CombobulateShadersIfChanged() {
		uint program;
		if (pipelineChanged) {
			program = ShaderCombobulator();
			lastShader = program;
			glUseProgram(program);
		}

		pipelineChanged = false;
		return lastShader;
	}

	internal void RenderPass() {
		if (IsDeactivated())
			return;

		CombobulateShadersIfChanged();

		if (RenderMesh != null)
			RenderMesh.RenderPass();
		else
			MeshMgr.RenderPassWithVertexAndIndexBuffers();
	}

	Dictionary<ulong, uint> shaderCombinations = [];
	private unsafe uint ShaderCombobulator() {
		// Determines the shader program used given the current shader handles.
		// If one does not exist, it is created.
		Span<nint> hashedData = [activeVertexShader.Handle, activePixelShader.Handle];
		ulong hash;
		fixed (nint* data = hashedData)
			hash = XXH64.DigestOf(data, hashedData.Length * sizeof(nint), 0);

		if (shaderCombinations.TryGetValue(hash, out var program))
			return program; // We have already linked a program for this shader combination

		// We need to create a program then
		program = glCreateProgram();
		glAttachShader(program, (uint)activeVertexShader.Handle);
		glAttachShader(program, (uint)activePixelShader.Handle);
		glLinkProgram(program);
		// Even invalid program states should be hashed... for now.
		// Maybe a time based thing for invalid programs, to try allowing for the shader developer to recover, etc...
		shaderCombinations[hash] = program;

		if (!ShaderSystem.IsValidProgram(program, out string? error)) {
			Warning("WARNING: Shader combobulation linker error.\n");
			Warning(error);
			Warning("\n");
			return 0;
		}

		return program;
	}

	private bool IsDeactivated() {
		return DeviceState != DeviceState.OK;
	}

	internal void InvalidateDelayedShaderConstraints() {
		// TODO FIXME
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

	private bool MatrixIsChanging(TransformType type = TransformType.IsGeneral) {
		if (IsDeactivated())
			return false;

		if (type != TransformType.IsGeneral)
			return false;

		FlushBufferedPrimitivesInternal();

		return true;
	}
	public void FlushBufferedPrimitives() => FlushBufferedPrimitivesInternal();
	private void FlushBufferedPrimitivesInternal() {
		Assert(RenderMesh == null);
		MeshMgr.Flush();
	}

	public void PopMatrix() {
		if (MatrixIsChanging()) {
			UpdateMatrixTransform();
		}
	}

	private void UpdateMatrixTransform() {

	}

	public void DrawMesh(IMesh imesh) {
		Mesh mesh = (Mesh)imesh!;
		RenderMesh = mesh;
		VertexFormat vertexFormat = RenderMesh.GetVertexFormat();
		SetVertexDecl(vertexFormat, RenderMesh.HasColorMesh(), RenderMesh.HasFlexMesh(), Material!.IsUsingVertexID());
		CommitStateChanges();
		Material!.DrawMesh(VertexCompressionType.None);
		RenderMesh = null;
	}

	private void CommitStateChanges() {
		// todo
	}

	private void SetVertexDecl(VertexFormat vertexFormat, bool hasColorMesh, bool hasFleshMesh, bool usingMorph) {
		// Gl46.glVertexAttribPointer() i think we need here
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

	internal void SetSkinningMatrices() {
		throw new NotImplementedException();
	}

	public void SetDefaultState() {
	}

	internal void ShadeMode(ShadeMode shadeMode) {
		throw new NotImplementedException();
	}

	public bool InEditorMode() {
		return false; // todo...?
	}

	public void SetVertexShaderConstant(int var, Span<float> vec) {
		SetVertexShaderConstantInternal(var, vec);
	}

	private void SetVertexShaderConstantInternal(int var, Span<float> vec) {
		// I'm so tired of looking at this stuff
	}

	bool UsingTextureRenderTarget;

	public void SetViewports(ReadOnlySpan<ShaderViewport> viewports) {
		Assert(viewports.Length == 1);
		if (viewports.Length != 1)
			return;

		GfxViewport viewport = new();
		viewport.X = viewports[0].TopLeftX;
		viewport.Y = viewports[0].TopLeftY;
		viewport.Width = viewports[0].Width;
		viewport.Height = viewports[0].Height;
		viewport.MinZ = viewports[0].MinZ;
		viewport.MaxZ = viewports[0].MaxZ;

		if (UsingTextureRenderTarget) {
			int maxWidth = 0, maxHeight = 0;
			GetBackBufferDimensions(out maxWidth, out maxHeight);
		}
	}

	public void GetViewports(Span<ShaderViewport> viewports) {
		throw new NotImplementedException();
	}

	public ImageFormat GetBackBufferFormat() {
		// MaterialSystem is a prison of an architecture and I don't know how to reliably pass this information thru at the moment.
		// If other formats are used this will need to be changed. For now this will work fine.
		// It seems like IMaterialSystem::GetBackBufferFormat -> ShaderDevice::GetBackBufferFormat -> retrieve
		// PresentParameters.BackBufferFormat but what actually sets that I'm not sure yet
		return ImageFormat.RGBA8888;
	}
	public void GetBackBufferDimensions(out int width, out int height) {
		width = PresentParameters.DisplayMode.Width;
		height = PresentParameters.DisplayMode.Height;
	}

	ShaderDeviceInfo PresentParameters;
	bool ResetRenderStateNeeded = false;
	ulong CurrentFrame;
	nint TextureMemoryUsedLastFrame;

	public void BeginFrame() {
		if (ResetRenderStateNeeded) {
			ResetRenderState(false);
			ResetRenderStateNeeded = false;
		}

		++CurrentFrame;
		TextureMemoryUsedLastFrame = 0;
	}

	public void EndFrame() {
		ExportTextureList();
	}

	private void ExportTextureList() {

	}

	internal bool SetMode(IWindow window, in ShaderDeviceInfo info) {
		ShaderDeviceInfo actualInfo = info;
		if (!InitDevice(window, in actualInfo)) {
			return false;
		}

		if (!OnDeviceInit())
			return false;

		return true;
	}

	internal IServiceProvider services;
	internal IGraphicsContext? Device;
	internal GraphicsDriver Driver;

	public bool InitDevice(IWindow window, in ShaderDeviceInfo deviceInfo) {
		IGraphicsProvider graphics = services.GetRequiredService<IGraphicsProvider>();
		SetPresentParameters(in deviceInfo);
		Device = graphics.CreateContext(in deviceInfo, window);
		if (Device == null)
			return false;

		Driver = deviceInfo.Driver;
		unsafe {
			if (0 != (deviceInfo.Driver & GraphicsDriver.OpenGL))
				GL_LoadExtensions(graphics.GL_LoadExtensionsPtr());
		}

		return true;
	}

	internal unsafe void GL_LoadExtensions(delegate* unmanaged[Cdecl]<byte*, void*> loadExts) {
		Gl46.Import((name) => {
			byte[] data = Encoding.UTF8.GetBytes(name);
			fixed (byte* ptr = data)
				return (nint)loadExts(ptr);
		});
	}

	public bool IsActive() => Device != null;
	public bool IsUsingGraphics() => IsActive();


	public void Present() {
		FlushBufferedPrimitives();
		bool validPresent = true;
		bool inMainThread = ThreadInMainThread();

		if (!inMainThread) {
			throw new Exception();
		}

		Device!.SwapBuffers();
	}

	bool IShaderDevice.IsDeactivated() => IsDeactivated();


	MaterialMatrixMode currentMode;
	public void MatrixMode(MaterialMatrixMode mode) {
		currentMode = mode;
	}

	public unsafe void LoadMatrix(in Matrix4x4 m4x4) {
		int szm4x4 = sizeof(Matrix4x4);
		int loc = (int)currentMode * szm4x4;
		Matrix4x4 transposed = m4x4; // Matrix4x4.Transpose(m4x4);
		glNamedBufferSubData(uboMatrices, loc, szm4x4, &transposed);
	}

	public void LoadIdentity() {
		LoadMatrix(Matrix4x4.Identity);
	}

	public int GetCurrentNumBones() {
		return 0;
	}

	Dictionary<uint, Dictionary<ulong, int>> locs = [];

	public unsafe int LocateShaderUniform(ReadOnlySpan<char> name) {
		if (!activeVertexShader.IsValid()) {
			Warning("WARNING: Attempted to locate uniform on an invalid vertex shader!\n");
			return -1;
		}

		if (!activePixelShader.IsValid()) {
			Warning("WARNING: Attempted to locate uniform on an invalid pixel shader!\n");
			return -1;
		}

		// If the name starts with $, go up one
		if (name.Length > 0 && name[0] == '$')
			name = name[1..];

		// Combobulate
		uint shader = CombobulateShadersIfChanged();

		// Then get shader ID -> shader uniform lookup table
		if (!locs.TryGetValue(shader, out var lookup))
			lookup = locs[shader] = [];

		// Then compute uniform name -> hash symbol
		// and look up if we've queried for this parameter yet
		ulong hash = name.Hash();
		if (lookup.TryGetValue(hash, out int loc))
			return loc;

		Span<byte> bytes = stackalloc byte[name.Length * 2];
		int byteLen = Encoding.ASCII.GetBytes(name, bytes);
		fixed (byte* uniformName = bytes)
			lookup[hash] = loc = glGetUniformLocation(shader, uniformName);

		return loc;
	}

	public nint GetCurrentProgram() => (nint)CombobulateShadersIfChanged();
	uint GetCurrentProgramInternal() => CombobulateShadersIfChanged();

	public void SetShaderUniform(int uniform, int integer) {
#if GL_DEBUG
		int i = glGetError();
#endif
		glProgramUniform1i(GetCurrentProgramInternal(), uniform, integer);
#if GL_DEBUG
		if ((i = glGetError()) != 0)
			AssertMsg(false, $"GL error {i}");
#endif
	}

	public void SetShaderUniform(int uniform, uint integer) {
		glProgramUniform1ui(GetCurrentProgramInternal(), uniform, integer);
	}

	public void SetShaderUniform(int uniform, float fl) {
		glProgramUniform1f(GetCurrentProgramInternal(), uniform, fl);
	}

	public void SetShaderUniform(int uniform, ReadOnlySpan<float> flConsts) {
		glProgramUniform1fv(GetCurrentProgramInternal(), uniform, flConsts);
	}

	internal void BindTexture(Sampler sampler, int frame, ShaderAPITextureHandle_t textureHandle) {
		CombobulateShadersIfChanged();
		if (textureHandle == INVALID_SHADERAPI_TEXTURE_HANDLE)
			return; // TODO: can we UNSET the sampler???

		glActiveTexture(GL_TEXTURE0 + (int)sampler);
		glBindTexture(GL_TEXTURE_2D, (uint)textureHandle);
	}

	public bool CanDownloadTextures() {
		if (IsDeactivated())
			return false;

		return IsActive();
	}

	ShaderAPITextureHandle_t ModifyTextureHandle;

	public void ModifyTexture(ShaderAPITextureHandle_t textureHandle) {
		ModifyTextureHandle = textureHandle;
	}

	public struct TextureLoadInfo
	{
		public ShaderAPITextureHandle_t Handle;
		public int Width;
		public int Height;
		public int ZOffset;
		public int Level;
		public ImageFormat SrcFormat;
	}

	public void TexImageFromVTF(IVTFTexture? vtf, int vtfFrame) {
		Assert(vtf != null);
		Assert(ModifyTextureHandle != INVALID_SHADERAPI_TEXTURE_HANDLE);

		ref TextureLoadInfo info = ref (stackalloc TextureLoadInfo[1])[0];
		info.Handle = ModifyTextureHandle;
		info.Width = 0;
		info.Height = 0;
		info.ZOffset = 0;
		info.Level = 0;
		info.SrcFormat = vtf.Format();

		if (vtf.Depth() > 1) {
			throw new NotImplementedException("Multidepth textures not supported yet");
		}
		else if (vtf.IsCubeMap()) {
			throw new NotImplementedException("Cubemap textures not supported yet");
		}
		else {
			LoadTextureFromVTF(in info, vtf, vtfFrame);
		}
	}

	private unsafe void LoadTextureFromVTF(in TextureLoadInfo info, IVTFTexture vtf, int vtfFrame) {
		vtf.ImageFileInfo(vtfFrame, 0, info.Level, out int start, out int size);

		if (info.SrcFormat.IsCompressed()) {
			Span<byte> data = vtf.ImageData(vtfFrame, 0, info.Level);
			vtf.ComputeMipLevelDimensions(info.Level, out int w, out int h, out _);
			glGetError();
			fixed (byte* bytes = data)
				glCompressedTextureSubImage2D((uint)info.Handle, info.Level, 0, 0, w, h, ImageLoader.GetGLImageInternalFormat(info.SrcFormat), data.Length, bytes);
			// Msg("err: " + glGetErrorName() + "\n");
		}
		else {
			Span<byte> data = vtf.ImageData(vtfFrame, 0, info.Level);
			TexSubImage2D(info.Level, 0, 0, 0, 0, vtf.Width(), vtf.Height(), info.SrcFormat, 0, data);
		}
	}

	public unsafe void CreateTextures(
		ShaderAPITextureHandle_t[] textureHandles,
		int count,
		int width,
		int height,
		int depth,
		ImageFormat imageFormat,
		ushort mipCount,
		int copies,
		CreateTextureFlags creationFlags,
		ReadOnlySpan<char> debugName,
		ReadOnlySpan<char> textureGroup) {
		if (depth == 0)
			depth = 1;

		bool isCubeMap = (creationFlags & CreateTextureFlags.Cubemap) != 0;
		bool isRenderTarget = (creationFlags & CreateTextureFlags.RenderTarget) != 0;
		bool managed = (creationFlags & CreateTextureFlags.Managed) != 0;
		bool isDepthBuffer = (creationFlags & CreateTextureFlags.DepthBuffer) != 0;
		bool isDynamic = (creationFlags & CreateTextureFlags.Dynamic) != 0;
		bool isSRGB = (creationFlags & CreateTextureFlags.SRGB) != 0;

		fixed (ShaderAPITextureHandle_t* handles = textureHandles)
			glCreateTextures(GL_TEXTURE_2D, textureHandles.Length, (uint*)handles);

		for (int i = 0; i < count; i++) {
			ShaderAPITextureHandle_t handle = textureHandles[i];
			glObjectLabel(GL_TEXTURE, (uint)handle, $"ShaderAPI Texture '{debugName}' [frame {i}]");
			glTextureStorage2D((uint)handle, mipCount, ImageLoader.GetGLImageInternalFormat(imageFormat), width, height);
		}
	}

	public ShaderAPITextureHandle_t CreateTextureHandle() {
		ShaderAPITextureHandle_t handle = 0;
		CreateTextureHandles(new Span<ShaderAPITextureHandle_t>(ref handle));
		return handle;
	}

	public unsafe void CreateTextureHandles(Span<int> textureHandles) {
		int idxCreating = 0;
		fixed (ShaderAPITextureHandle_t* handles = textureHandles)
			glCreateTextures(GL_TEXTURE_2D, textureHandles.Length, (uint*)handles);
	}

	public ShaderAPITextureHandle_t CreateDepthTexture(ImageFormat imageFormat, ushort width, ushort height, Span<char> debugName, bool texture) {
		ShaderAPITextureHandle_t handle = CreateTextureHandle();
		glObjectLabel(GL_TEXTURE, (uint)handle, $"ShaderAPI Depth Texture '{debugName}'");
		glTextureStorage2D((uint)handle, 1, GL_DEPTH24_STENCIL8, width, height);
		return handle;
	}

	internal bool IsTexture(ShaderAPITextureHandle_t handle) {
		return true; // TODO
	}

	internal void DeleteTexture(ShaderAPITextureHandle_t handle) {
		// TODO
	}

	public ImageFormat GetNearestSupportedFormat(ImageFormat fmt, bool filteringRequired = true) {
		return FindNearestSupportedFormat(fmt, false, false, filteringRequired);
	}

	public ImageFormat FindNearestSupportedFormat(ImageFormat format, bool isVertexTexture, bool isRenderTarget, bool filterableRequired) {
		return format;
	}

	public int GetCurrentDynamicVBSize() {
		return (1024 + 512) * 1024; // See if it's still needed to use smaller sizes at certain points... how would this even work, I wonder
	}

	public unsafe void TexSubImage2D(int mip, int face, int x, int y, int z, int width, int height, ImageFormat srcFormat, int srcStride, Span<byte> imageData) {
		glGetError();
		glPixelStorei(GL_UNPACK_ROW_LENGTH, srcStride / srcFormat.SizeInBytes());
		fixed (byte* data = imageData)
			glTextureSubImage2D((uint)ModifyTextureHandle, mip, x, y, width, height, ImageLoader.GetGLImageUploadFormat(srcFormat), GL_UNSIGNED_BYTE, data);
		glPixelStorei(GL_UNPACK_ROW_LENGTH, 0);
		var err = glGetError();
		Assert(err == 0);
	}

	public void ReacquireResources() {
		ReacquireResourcesInternal();
	}

	int releaseResourcesCount = 0;

	private void ReacquireResourcesInternal(bool resetState = false, bool forceReacquire = false, ReadOnlySpan<char> forceReason = default) {
		if (--releaseResourcesCount != 0) {
			Warning($"ReacquireResources has no effect, now at level {releaseResourcesCount}.\n");
			DevWarning("ReacquireResources being discarded is a bug: use IsDeactivated to check for a valid device.\n");
			Assert(false);
			if (releaseResourcesCount < 0)
				releaseResourcesCount = 0;
			return;
		}

		if (resetState) {
			ResetRenderState();
		}

		RestoreShaderObjects();
		MeshMgr.RestoreBuffers();
		ShaderUtil.RestoreShaderObjects(services);
	}

	private void RestoreShaderObjects() {

	}

	public void ReleaseResources() {
		releaseResourcesCount++;
	}

	public void SetShaderUniform(IMaterialVar textureVar) {
		int uniform = LocateShaderUniform(textureVar.GetName());
		if (uniform == -1)
			return;
		switch (textureVar.GetVarType()) {
			case MaterialVarType.Float: SetShaderUniform(uniform, textureVar.GetFloatValue()); break;
			case MaterialVarType.Int: SetShaderUniform(uniform, textureVar.GetIntValue()); break;
		}
	}

	ulong lastBoardUploadHash;
	internal void SetBoardState(in GraphicsBoardState state) {
		ulong currHash = state.Hash();
		if (currHash != lastBoardUploadHash) {
			glToggle(GL_BLEND, state.Blending);

			glBlendFunc(state.SourceBlend.GLEnum(), state.DestinationBlend.GLEnum());
			glBlendEquation(state.BlendOperation.GLEnum());

			glToggle(GL_DEPTH_TEST, state.DepthTest);
			glDepthMask(state.DepthWrite);

			lastBoardUploadHash = currHash;
		}
	}

	public bool DoRenderTargetsNeedSeparateDepthBuffer() {
		return true;
	}

	public void EnableLinearColorSpaceFrameBuffer(bool v) {
		// I'm dealing with this later
	}

	public void SetRenderTargetEx(int renderTargetID, ShaderAPITextureHandle_t colorTextureHandle = -1, ShaderAPITextureHandle_t depthTextureHandle = -1) {
		FlushBufferedPrimitives();

		if (colorTextureHandle == -1 && depthTextureHandle == -1) {
			glBindFramebuffer(GL_FRAMEBUFFER, 0);
			return;
		}

		glBindFramebuffer(GL_FRAMEBUFFER, renderFBO);

		if (colorTextureHandle == -2)
			glFramebufferTexture2D(GL_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_TEXTURE_2D, 0, 0);
		else if (colorTextureHandle >= 0)
			glFramebufferTexture2D(GL_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_TEXTURE_2D, (uint)colorTextureHandle, 0);

		if (depthTextureHandle == -2)
			glFramebufferTexture2D(GL_FRAMEBUFFER, GL_DEPTH_STENCIL_ATTACHMENT, GL_TEXTURE_2D, 0, 0);
		else if (depthTextureHandle >= 0)
			glFramebufferTexture2D(GL_FRAMEBUFFER, GL_DEPTH_STENCIL_ATTACHMENT, GL_TEXTURE_2D, (uint)depthTextureHandle, 0);

		var status = glCheckFramebufferStatus(GL_FRAMEBUFFER);
		Assert(status == GL_FRAMEBUFFER_COMPLETE, "Framebuffer incomplete");
		glBindFramebuffer(GL_FRAMEBUFFER, 0);
	}
}