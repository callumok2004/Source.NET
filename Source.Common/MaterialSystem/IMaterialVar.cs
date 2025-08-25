﻿using Source.Common.ShaderAPI;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Source.Common.MaterialSystem;

public enum MaterialVarType : ushort
{
	Float = 0,
	String,
	Vector,
	Texture,
	Int,
	FourCC,
	Undefined,
	Matrix,
	Material
}

public struct MaterialVarGPU {
	public nint Program;
	public ShaderType Shader;
	public int Location;
}

public abstract class IMaterialVar
{
	protected string StringVal = "";
	protected int IntVal;
	protected Vector4 VecVal;
	protected MaterialVarType Type;
	protected ITexture? TextureValue;
	protected byte NumVectorComps;
	protected bool FakeMaterialVar;
	protected byte TempIndex;
	protected string Name = "";
	public MaterialVarGPU GPU;

	public abstract ITexture? GetTextureValue();
	public abstract ReadOnlySpan<char> GetName();
	public abstract void SetFloatValue(float val);
	public abstract void SetIntValue(int val);
	public abstract void SetStringValue(ReadOnlySpan<char> val);
	public abstract string GetStringValue();
	public abstract void SetFourCCValue(ulong type, object? data);
	public abstract void GetFourCCValue(ulong type, out object? data);

	public abstract void SetVecValue(ReadOnlySpan<float> val);
	public abstract void SetVecValue(float x, float y);
	public abstract void SetVecValue(float x, float y, float z);
	public abstract void SetVecValue(float x, float y, float z, float w);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public void SetVecValue(in Vector2 xy) => SetVecValue(xy.X, xy.Y);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public void SetVecValue(in Vector3 xyz) => SetVecValue(xyz.X, xyz.Y, xyz.Z);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public void SetVecValue(in Vector4 xyzw) => SetVecValue(xyzw.X, xyzw.Y, xyzw.Z, xyzw.W);

	public abstract void GetVecValue(Span<float> color);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void GetVecValue(out Vector2 vec) {
		Span<float> retv = stackalloc float[2];
		GetVecValue(retv);
		vec = new(retv[0], retv[1]);
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void GetVecValue(out Vector3 vec) {
		Span<float> retv = stackalloc float[2];
		GetVecValue(retv);
		vec = new(retv[0], retv[1], retv[2]);
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void GetVecValue(out Vector4 vec) {
		Span<float> retv = stackalloc float[2];
		GetVecValue(retv);
		vec = new(retv[0], retv[1], retv[2], retv[3]);
	}


	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public Vector2 GetVec2Value() {
		Span<float> retv = stackalloc float[2];
		GetVecValue(retv);
		return new(retv[0], retv[1]);
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public Vector3 GetVec3Value() {
		Span<float> retv = stackalloc float[2];
		GetVecValue(retv);
		return new(retv[0], retv[1], retv[2]);
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public Vector4 GetVec4Value() {
		Span<float> retv = stackalloc float[2];
		GetVecValue(retv);
		return new(retv[0], retv[1], retv[2], retv[3]);
	}

	public abstract void SetTextureValue(ITexture? texture);
	public abstract IMaterial? GetMaterialValue();
	public abstract void SetMaterialValue(IMaterial? material);
	public abstract bool IsDefined();
	public abstract void SetUndefined();
	public abstract void SetMatrixValue(in Matrix4x4 matrix);
	public abstract Matrix4x4 GetMatrixValue();
	public abstract bool MatrixIsIdentity();
	public abstract void CopyFrom(IMaterialVar materialVar);
	public abstract void SetValueAutodetectType(ReadOnlySpan<char> val);
	public abstract IMaterial GetOwningMaterial();
	public abstract void SetVecComponentValue(float val, int component);

	public MaterialVarType GetVarType() => Type;
	public bool IsTexture() => Type == MaterialVarType.Texture;

	protected abstract int GetIntValueInternal();
	protected abstract float GetFloatValueInternal();
	protected abstract Span<float> GetVecValueInternal();
	protected abstract void GetVecValueInternal(Span<float> val);
	protected abstract int VectorSizeInternal();

	public int GetIntValue() {
		return IntVal;
	}
	public float GetFloatValue() {
		return VecVal.X;
	}
}
