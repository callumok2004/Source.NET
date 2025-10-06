﻿using System.Diagnostics;
using System.Numerics;

namespace Source.Common;

[DebuggerDisplay("Source BSP Collision Model @ {Origin} [{Mins} -> {Maxs}] (head-node {HeadNode})")]
public struct CollisionModel
{
	public Vector3 Mins, Maxs, Origin;
	public int HeadNode;

	// vcollide_t research todo
}

[DebuggerDisplay("Source BSP Collision Surface '{Name}' (props: {SurfaceProps}, flags: {Flags})")]
public struct CollisionSurface
{
	public string Name;
	public ushort SurfaceProps;
	public ushort Flags;
}

public struct Ray {
	public Vector3 Start;
	public Vector3 Delta;
	public Vector3 StartOffset;
	public Vector3 Extents;
	public bool IsRay;
	public bool IsSwept;
}