using Source.Common;
using Source.Common.Bitmap;
using Source.Common.Commands;
using Source.Common.MaterialSystem;
using Source.Common.Mathematics;
using Source.Common.ShaderAPI;
using Source.Common.ShaderLib;

using System.Diagnostics;
using System.Numerics;

namespace Source.StdShader.Gl46;

public class Water : BaseVSShader
{

	public static string HelpString = "Help for Water";
	public static int Flags = 0;
	public static List<ShaderParam> ShaderParams = [];
	public static ShaderParam[] ShaderParamOverrides = new ShaderParam[(int)ShaderMaterialVars.Count];

	public class ShaderParam
	{
		public readonly ShaderParamInfo Info;
		public readonly int Index;
		public ShaderParam(ShaderMaterialVars var, ShaderParamType type, ReadOnlySpan<char> defaultParam, ReadOnlySpan<char> help, int flags) {
			Info.Name = "override";
			Info.Type = type;
			Info.DefaultValue = new(defaultParam);
			Info.Help = new(help);
			Info.Flags = (ShaderParamFlags)flags;
			AssertMsg(ShaderParamOverrides[(int)var] == null, "Shader parameter override duplicately defined!");
			ShaderParamOverrides[(int)var] = this;
			Index = (int)var;
		}
		public ShaderParam(string name, ShaderParamType type, ReadOnlySpan<char> defaultParam, ReadOnlySpan<char> help, int flags = 0) {
			Info.Name = name;
			Info.Type = type;
			Info.DefaultValue = new(defaultParam);
			Info.Help = new(help);
			Info.Flags = (ShaderParamFlags)flags;
			Index = (int)ShaderMaterialVars.Count + ShaderParams.Count;
			ShaderParams.Add(this);
		}
		public static implicit operator int(ShaderParam param) => param.Index;
		public ReadOnlySpan<char> GetName() => Info.Name;
		public ShaderParamType GetType() => Info.Type;
		public ReadOnlySpan<char> GetDefaultValue() => Info.DefaultValue;
		public int GetFlags() => (int)Info.Flags;
		public ReadOnlySpan<char> GetHelp() => Info.Help;
	}

	public static readonly ShaderParam REFRACTTEXTURE = new($"${nameof(REFRACTTEXTURE)}", ShaderParamType.Texture, "_rt_WaterRefraction", "");
	public static readonly ShaderParam REFLECTTEXTURE = new($"${nameof(REFLECTTEXTURE)}", ShaderParamType.Texture, "_rt_WaterReflection", "");
	public static readonly ShaderParam REFRACTAMOUNT = new($"${nameof(REFRACTAMOUNT)}", ShaderParamType.Float, "0", "");
	public static readonly ShaderParam REFRACTTINT = new($"${nameof(REFRACTTINT)}", ShaderParamType.Color, "[1 1 1]", "refraction tint");
	public static readonly ShaderParam REFLECTAMOUNT = new($"${nameof(REFLECTAMOUNT)}", ShaderParamType.Float, "0.8", "");
	public static readonly ShaderParam REFLECTTINT = new($"${nameof(REFLECTTINT)}", ShaderParamType.Color, "[1 1 1]", "reflection tint");
	public static readonly ShaderParam NORMALMAP = new($"${nameof(NORMALMAP)}", ShaderParamType.Texture, "dev/water_normal", "normal map");
	public static readonly ShaderParam BUMPFRAME = new($"${nameof(BUMPFRAME)}", ShaderParamType.Integer, "0", "frame number for $bumpmap");
	public static readonly ShaderParam BUMPTRANSFORM = new($"${nameof(BUMPTRANSFORM)}", ShaderParamType.Matrix, "center .5 .5 scale 1 1 rotate 0 translate 0 0", "$bumpmap texcoord transform");
	public static readonly ShaderParam SCALE = new($"${nameof(SCALE)}", ShaderParamType.Vec2, "[1 1]", "");
	public static readonly ShaderParam TIME = new($"${nameof(TIME)}", ShaderParamType.Float, "", "");
	public static readonly ShaderParam WATERDEPTH = new($"${nameof(WATERDEPTH)}", ShaderParamType.Float, "", "");
	public static readonly ShaderParam CHEAPWATERSTARTDISTANCE = new($"${nameof(CHEAPWATERSTARTDISTANCE)}", ShaderParamType.Float, "", "This is the distance from the eye in inches that the shader should start transitioning to a cheaper water shader.");
	public static readonly ShaderParam CHEAPWATERENDDISTANCE = new($"${nameof(CHEAPWATERENDDISTANCE)}", ShaderParamType.Float, "", "This is the distance from the eye in inches that the shader should finish transitioning to a cheaper water shader.");
	public static readonly ShaderParam ENVMAP = new($"${nameof(ENVMAP)}", ShaderParamType.Texture, "env_cubemap", "envmap");
	public static readonly ShaderParam ENVMAPFRAME = new($"${nameof(ENVMAPFRAME)}", ShaderParamType.Integer, "0", "");
	public static readonly ShaderParam FOGCOLOR = new($"${nameof(FOGCOLOR)}", ShaderParamType.Color, "", "");
	public static readonly ShaderParam FORCECHEAP = new($"${nameof(FORCECHEAP)}", ShaderParamType.Bool, "", "");
	public static readonly ShaderParam FORCEEXPENSIVE = new($"${nameof(FORCEEXPENSIVE)}", ShaderParamType.Bool, "", "");
	public static readonly ShaderParam REFLECTENTITIES = new($"${nameof(REFLECTENTITIES)}", ShaderParamType.Bool, "", "");
	public static readonly ShaderParam FOGSTART = new($"${nameof(FOGSTART)}", ShaderParamType.Float, "", "");
	public static readonly ShaderParam FOGEND = new($"${nameof(FOGEND)}", ShaderParamType.Float, "", "");
	public static readonly ShaderParam ABOVEWATER = new($"${nameof(ABOVEWATER)}", ShaderParamType.Bool, "", "");
	public static readonly ShaderParam REFLECTBLENDFACTOR = new($"${nameof(REFLECTBLENDFACTOR)}", ShaderParamType.Float, "1.0", "");
	public static readonly ShaderParam NOFRESNEL = new($"${nameof(NOFRESNEL)}", ShaderParamType.Bool, "0", "");
	public static readonly ShaderParam NOLOWENDLIGHTMAP = new($"${nameof(NOLOWENDLIGHTMAP)}", ShaderParamType.Bool, "0", "");
	public static readonly ShaderParam SCROLL1 = new($"${nameof(SCROLL1)}", ShaderParamType.Color, "", "");
	public static readonly ShaderParam SCROLL2 = new($"${nameof(SCROLL2)}", ShaderParamType.Color, "", "");
	public static readonly ShaderParam BLURREFRACT = new($"${nameof(BLURREFRACT)}", ShaderParamType.Bool, "0", "Cause the refraction to be blurry on ps2b hardware");

	public override string? GetFallbackShader(IMaterialVar[] vars) => null;
	public override int GetFlags() => Flags;
	public override int GetNumParams() => base.GetNumParams() + ShaderParams.Count;
	public override ReadOnlySpan<char> GetParamName(int paramIndex) {
		int baseClassParamCount = base.GetNumParams();
		if (paramIndex < baseClassParamCount)
			return base.GetParamName(paramIndex);
		else
			return ShaderParams[paramIndex - baseClassParamCount].GetName();
	}
	public override ReadOnlySpan<char> GetParamHelp(int paramIndex) {
		int baseClassParamCount = base.GetNumParams();
		if (paramIndex < baseClassParamCount)
			return base.GetParamHelp(paramIndex);
		else
			return ShaderParams[paramIndex - baseClassParamCount].GetHelp();
	}
	public override ShaderParamType GetParamType(int paramIndex) {
		int baseClassParamCount = base.GetNumParams();
		if (paramIndex < baseClassParamCount)
			return base.GetParamType(paramIndex);
		else
			return ShaderParams[paramIndex - baseClassParamCount].GetType();
	}
	public override ReadOnlySpan<char> GetParamDefault(int paramIndex) {
		int baseClassParamCount = base.GetNumParams();
		if (paramIndex < baseClassParamCount)
			return base.GetParamDefault(paramIndex);
		else
			return ShaderParams[paramIndex - baseClassParamCount].GetDefaultValue();
	}

	static LightmappedGeneric_Vars Info;

	protected override void OnInitShaderParams(IMaterialVar[] parms, ReadOnlySpan<char> materialName) {
		if (!parms[ABOVEWATER].IsDefined()) {
			Warning($"***need to set $abovewater for material {materialName.SliceNullTerminatedString()}\n");
			parms[ABOVEWATER].SetIntValue(1);
		}

		SetFlags2(parms, MaterialVarFlags2.NeedsTangentSpaces);

		if (!parms[CHEAPWATERSTARTDISTANCE].IsDefined())
			parms[CHEAPWATERSTARTDISTANCE].SetFloatValue(500.0f);

		if (!parms[CHEAPWATERENDDISTANCE].IsDefined())
			parms[CHEAPWATERENDDISTANCE].SetFloatValue(1000.0f);

		if (!parms[SCALE].IsDefined())
			parms[SCALE].SetVecValue(1.0f, 1.0f);

		if (!parms[SCROLL1].IsDefined())
			parms[SCROLL1].SetVecValue(0.0f, 0.0f, 0.0f);

		if (!parms[SCROLL2].IsDefined())
			parms[SCROLL2].SetVecValue(0.0f, 0.0f, 0.0f);

		if (!parms[FOGCOLOR].IsDefined()) {
			parms[FOGCOLOR].SetVecValue(1.0f, 0.0f, 0.0f);
			Warning($"material {materialName.SliceNullTerminatedString()} needs to have a $fogcolor.\n");
		}

		if (!parms[REFLECTENTITIES].IsDefined())
			parms[REFLECTENTITIES].SetIntValue(0);

		if (!parms[REFLECTBLENDFACTOR].IsDefined())
			parms[REFLECTBLENDFACTOR].SetFloatValue(1.0f);

		if (!parms[FORCEEXPENSIVE].IsDefined())
			parms[FORCEEXPENSIVE].SetIntValue(1);

		if (parms[FORCEEXPENSIVE].GetIntValue() != 0 && parms[FORCECHEAP].GetIntValue() != 0)
			parms[FORCEEXPENSIVE].SetIntValue(0);

		if (parms[NOLOWENDLIGHTMAP].GetIntValue() == 0)
			SetFlags2(parms, MaterialVarFlags2.LightingLightmap);

		SetFlags2(parms, MaterialVarFlags2.LightingLightmap);

		if (Config.UseBumpmapping() && parms[NORMALMAP].IsDefined())
			SetFlags2(parms, MaterialVarFlags2.LightingBumpedLightmap);
	}
	protected override void OnInitShaderInstance(IMaterialVar[] parms, ReadOnlySpan<char> materialName) {
		Assert(parms[WATERDEPTH].IsDefined());

		if (parms[REFRACTTEXTURE].IsDefined())
			LoadTexture(REFRACTTEXTURE, (int)TextureFlags.SRGB);

		if (parms[REFLECTTEXTURE].IsDefined())
			LoadTexture(REFLECTTEXTURE, (int)TextureFlags.SRGB);

		if (parms[ENVMAP].IsDefined())
			LoadCubeMap(ENVMAP, (int)TextureFlags.SRGB);

		if (parms[NORMALMAP].IsDefined())
			LoadBumpMap(NORMALMAP);

		if (parms[(int)ShaderMaterialVars.BaseTexture].IsDefined())
			LoadTexture((int)ShaderMaterialVars.BaseTexture, (int)TextureFlags.SRGB);
	}

	readonly static ConVar r_waterforceexpensive = new("r_waterforceexpensive", "0", FCvar.Archive);

	protected override void OnDrawElements(IMaterialVar[] parms, IShaderDynamicAPI shaderAPI, VertexCompressionType vertexCompression) {
		bool forceExpensive = r_waterforceexpensive.GetBool();
		bool forceCheap = parms[FORCECHEAP].GetIntValue() != 0;// || UsingEditor(parms);
		if (forceCheap)
			forceExpensive = false;
		else
			forceExpensive = forceExpensive || (parms[FORCEEXPENSIVE].GetIntValue() != 0);
		Assert(!(forceCheap && forceExpensive));

		bool refraction = parms[REFRACTTEXTURE].IsTexture();
		bool reflection = forceExpensive && parms[REFLECTTEXTURE].IsTexture();
		bool drewSomething = false;
		if (!forceCheap && (reflection || refraction)) {
			drewSomething = true;
			DrawReflectionRefraction(parms, ShaderShadow, shaderAPI, reflection, refraction);
		}

		if (!reflection && parms[ENVMAP].IsTexture() && !IsFlagSet(parms, MaterialVarFlags.Decal)) {
			drewSomething = true;
			DrawCheapWater(parms, ShaderShadow, shaderAPI, !forceCheap, refraction);
		}

		if (!drewSomething)
			Draw();
	}

	private void DrawCheapWater(IMaterialVar[] parms, IShaderShadow? shaderShadow, IShaderDynamicAPI shaderAPI, bool blend, bool refraction) {
		// throw new NotImplementedException();
		DevWarning("cheap water\n");
	}

	private void DrawReflectionRefraction(IMaterialVar[] parms, IShaderShadow? shaderShadow, IShaderDynamicAPI shaderAPI, bool reflection, bool refraction) {
		if (shaderShadow != null) {
			SetInitialShadowState();
			if (refraction) {
				shaderShadow.EnableTexture(Sampler.Sampler0, true);
				shaderShadow.EnableTexture(Sampler.Sampler1, true);
				shaderShadow.EnableSRGBRead(Sampler.Sampler0, true);
			}
			if (reflection) {
				shaderShadow.EnableTexture(Sampler.Sampler2, true);
				shaderShadow.EnableTexture(Sampler.Sampler3, true);
				shaderShadow.EnableSRGBRead(Sampler.Sampler2, true);
				if (parms[(int)ShaderMaterialVars.BaseTexture].IsTexture()) {
					shaderShadow.EnableTexture(Sampler.Sampler1, true);
					shaderShadow.EnableSRGBRead(Sampler.Sampler1, true);
					shaderShadow.EnableTexture(Sampler.Sampler3, true);
					shaderShadow.EnableSRGBRead(Sampler.Sampler3, true);
				}
			}
			shaderShadow.EnableTexture(Sampler.Sampler4, true);
			shaderShadow.EnableTexture(Sampler.Sampler5, true);

			VertexFormat fmt = VertexFormat.Position | VertexFormat.Normal | VertexFormat.TangentS | VertexFormat.TangentT;

			int numTexCoords = 1;
			if (parms[(int)ShaderMaterialVars.BaseTexture].IsTexture())
				numTexCoords = 3;
			shaderShadow.VertexShaderVertexFormat(fmt, numTexCoords, null, 0);

			parms[SCROLL1].GetVecValue(out Vector4 Scroll1);

			StaticShaderIndex vshIndex = new(shaderShadow, ShaderType.Vertex, "water");
			vshIndex.Set("MULTITEXTURE", MathF.Abs(Scroll1.X) > 0.0f);
			vshIndex.Set("BASETEXTURE", parms[(int)ShaderMaterialVars.BaseTexture].IsTexture());
			shaderShadow.SetVertexShader("water", vshIndex.GetIndex());

			if (HardwareConfig.SupportsPixelShaders_2_b()) {
				StaticShaderIndex pshIndex = new(shaderShadow, ShaderType.Pixel, "water");
				pshIndex.Set("REFLECT", reflection);
				pshIndex.Set("REFRACT", refraction);
				pshIndex.Set("ABOVEWATER", parms[ABOVEWATER].GetIntValue());
				pshIndex.Set("MULTITEXTURE", MathF.Abs(Scroll1.X) > 0.0f);
				pshIndex.Set("BASETEXTURE", parms[(int)ShaderMaterialVars.BaseTexture].IsTexture());
				pshIndex.Set("BLURRY_REFRACT", parms[BLURREFRACT].GetIntValue());
				pshIndex.Set("NORMAL_DECODE_MODE", (int)NormalDecodeMode.None);
				shaderShadow.SetPixelShader("water", pshIndex.GetIndex());
			}
			else {
				StaticShaderIndex pshIndex = new(shaderShadow, ShaderType.Pixel, "water");
				pshIndex.Set("REFLECT", reflection);
				pshIndex.Set("REFRACT", refraction);
				pshIndex.Set("ABOVEWATER", parms[ABOVEWATER].GetIntValue());
				pshIndex.Set("MULTITEXTURE", MathF.Abs(Scroll1.X) > 0.0f);
				pshIndex.Set("BASETEXTURE", parms[(int)ShaderMaterialVars.BaseTexture].IsTexture());
				pshIndex.Set("NORMAL_DECODE_MODE", (int)NormalDecodeMode.None);
				shaderShadow.SetPixelShader("water", pshIndex.GetIndex());
			}

			// FogToFogColor(); // TODO

			shaderShadow.EnableSRGBWrite(true);
			shaderShadow.EnableAlphaWrites(true);
		}
		if (shaderAPI != null) {
			// shaderAPI.SetDefaultState(); // TODO
			if (refraction)
				BindTexture(Sampler.Sampler0, REFRACTTEXTURE, -1);
			if (reflection)
				BindTexture(Sampler.Sampler2, REFLECTTEXTURE, -1);
			BindTexture(Sampler.Sampler4, NORMALMAP, BUMPFRAME);
			if (parms[(int)ShaderMaterialVars.BaseTexture].IsTexture()) {
				BindTexture(Sampler.Sampler1, (int)ShaderMaterialVars.BaseTexture, (int)ShaderMaterialVars.Frame);
				shaderAPI.BindStandardTexture(Sampler.Sampler3, StandardTextureId.Lightmap);
			}

			shaderAPI.BindStandardTexture(Sampler.Sampler5, StandardTextureId.NormalizationCubemapSigned);

			if (refraction)
				SetPixelShaderConstantGammaToLinear(1, REFRACTTINT);

			if (reflection) {
				if (HardwareConfig.GetHDRType() == HDRType.Integer) {
					Span<float> gammaReflectTint = stackalloc float[3];
					parms[REFLECTTINT].GetVecValue(gammaReflectTint);
					Span<float> linearReflectTint =
					[
						MathLib.GammaToLinear(gammaReflectTint[0]) * 4.0f,
						MathLib.GammaToLinear(gammaReflectTint[1]) * 4.0f,
						MathLib.GammaToLinear(gammaReflectTint[2]) * 4.0f,
						1.0f,
					];
					shaderAPI.SetPixelShaderConstant(4, linearReflectTint);
				}
				else
					SetPixelShaderConstantGammaToLinear(4, REFLECTTINT);
			}

			SetVertexShaderTextureTransform(VertexShaderConst.ShaderSpecificConst1, BUMPTRANSFORM);

			float curtime = (float)shaderAPI.CurrentTime();
			Span<float> vc0 = stackalloc float[4];
			Span<float> v0 = stackalloc float[4];
			parms[SCROLL1].GetVecValue(v0);
			vc0[0] = curtime * v0[0];
			vc0[1] = curtime * v0[1];
			parms[SCROLL2].GetVecValue(v0);
			vc0[2] = curtime * v0[0];
			vc0[3] = curtime * v0[1];
			shaderAPI.SetVertexShaderConstant(VertexShaderConst.ShaderSpecificConst3, vc0);

			Span<float> c0 = [1.0f / 3.0f, 1.0f / 3.0f, 1.0f / 3.0f, 0.0f];
			shaderAPI.SetPixelShaderConstant(0, c0);

			Span<float> c2 = [0.5f, 0.5f, 0.5f, 0.5f];
			shaderAPI.SetPixelShaderConstant(2, c2);

			Span<float> c3 = [1.0f, 0.0f, 0.0f, 0.0f];
			shaderAPI.SetPixelShaderConstant(3, c3);

			Span<float> c5 = [parms[REFLECTAMOUNT].GetFloatValue(), parms[REFLECTAMOUNT].GetFloatValue(), parms[REFRACTAMOUNT].GetFloatValue(), parms[REFRACTAMOUNT].GetFloatValue()];
			shaderAPI.SetPixelShaderConstant(5, c5);

			SetPixelShaderConstantGammaToLinear(6, FOGCOLOR);

			Span<float> c7 = [parms[FOGSTART].GetFloatValue(), parms[FOGEND].GetFloatValue() - parms[FOGSTART].GetFloatValue(), 1.0f, 0.0f];
			if (HardwareConfig.GetHDRType() == HDRType.Integer)
				c7[2] = 4.0f;

			shaderAPI.SetPixelShaderConstant(7, c7);

			// shaderAPI.SetPixelShaderFogParams(8); // TODO

			DynamicShaderIndex vshIndex = new(shaderAPI, ShaderType.Vertex);
			shaderAPI.SetVertexShaderIndex(vshIndex.GetIndex());

			if (HardwareConfig.SupportsPixelShaders_2_b()) {
				DynamicShaderIndex pshIndex = new(shaderAPI, ShaderType.Pixel);
				pshIndex.Set("PIXELFOGTYPE", shaderAPI.GetPixelFogCombo());
				pshIndex.Set("WRITE_DEPTH_TO_DESTALPHA", shaderAPI.ShouldWriteDepthToDestAlpha());
				shaderAPI.SetPixelShaderIndex(pshIndex.GetIndex());
			}
			else {
				DynamicShaderIndex pshIndex = new(shaderAPI, ShaderType.Pixel);
				pshIndex.Set("PIXELFOGTYPE", shaderAPI.GetPixelFogCombo());
				shaderAPI.SetPixelShaderIndex(pshIndex.GetIndex());
			}
		}
		Draw();
	}
}
