﻿using Source.Common.GUI;
using System.Numerics;
using System;
using GameLumpId_t = int;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Source.Common.Mathematics;
using CommunityToolkit.HighPerformance;
using SharpCompress.Common;

namespace Source.Common.Formats.BSP;

public static class BSPFileCommon
{
	public const int IDBSPHEADER = ((byte)'P' << 24) + ((byte)'S' << 16) + ((byte)'B' << 8) + (byte)'V';

	public const int MINBSPVERSION = 19;
	public const int BSPVERSION = 20;

	public const int MAX_BRUSH_LIGHTMAP_DIM_WITHOUT_BORDER = 32;
	public const int MAX_BRUSH_LIGHTMAP_DIM_INCLUDING_BORDER = 35;

	public const int MAX_DISP_LIGHTMAP_DIM_WITHOUT_BORDER = 125;
	public const int MAX_DISP_LIGHTMAP_DIM_INCLUDING_BORDER = 128;

	public const int MAX_LIGHTMAP_DIM_WITHOUT_BORDER = MAX_DISP_LIGHTMAP_DIM_WITHOUT_BORDER;
	public const int MAX_LIGHTMAP_DIM_INCLUDING_BORDER = MAX_DISP_LIGHTMAP_DIM_INCLUDING_BORDER;

	public const int MAX_LIGHTSTYLES = 128;

	public const int MIN_MAP_DISP_POWER = 2;
	public const int MAX_MAP_DISP_POWER = 4;

	public const int MAX_DISP_CORNER_NEIGHBORS = 4;

	public const int MAX_MAP_MODELS = 4096;
	public const int MAX_MAP_BRUSHES = 16384;
	public const int MAX_MAP_ENTITIES = 8192;
	public const int MAX_MAP_TEXINFO = 16384;
	public const int MAX_MAP_TEXDATA = 8192;
	public const int MAX_MAP_DISPINFO = 2048;
	public const int MAX_MAP_DISP_VERTS = (MAX_MAP_DISPINFO * ((1 << MAX_MAP_DISP_POWER) + 1) * ((1 << MAX_MAP_DISP_POWER) + 1));
	public const int MAX_MAP_DISP_TRIS = ((1 << MAX_MAP_DISP_POWER) * (1 << MAX_MAP_DISP_POWER) * 2);
	public const int MAX_DISPVERTS = ((1 << MAX_MAP_DISP_POWER) + 1) * ((1 << MAX_MAP_DISP_POWER) + 1);
	public const int MAX_DISPTRIS = (1 << MAX_MAP_DISP_POWER) * (1 << MAX_MAP_DISP_POWER) * 2;
	public const int MAX_MAP_AREAS = 1024;
	public const int MAX_MAP_AREA_BYTES = (MAX_MAP_AREAS / 8);
	public const int MAX_MAP_AREAPORTALS = 1024;
	public const int MAX_MAP_PLANES = 65536;
	public const int MAX_MAP_NODES = 65536;
	public const int MAX_MAP_BRUSHSIDES = 163840;
	public const int MAX_MAP_LEAFS = 65536;
	public const int MAX_MAP_VERTS = 65536;
	public const int MAX_MAP_VERTNORMALS = 256000;
	public const int MAX_MAP_VERTNORMALINDICES = 256000;
	public const int MAX_MAP_FACES = 65536;
	public const int MAX_MAP_LEAFFACES = 65536;
	public const int MAX_MAP_LEAFBRUSHES = 65536;
	public const int MAX_MAP_PORTALS = 65536;
	public const int MAX_MAP_CLUSTERS = 65536;
	public const int MAX_MAP_LEAFWATERDATA = 32768;
	public const int MAX_MAP_PORTALVERTS = 128000;
	public const int MAX_MAP_EDGES = 256000;
	public const int MAX_MAP_SURFEDGES = 512000;
	public const int MAX_MAP_LIGHTING = 0x1000000;
	public const int MAX_MAP_VISIBILITY = 0x1000000;
	public const int MAX_MAP_TEXTURES = 1024;
	public const int MAX_MAP_WORLDLIGHTS = 8192;
	public const int MAX_MAP_CUBEMAPSAMPLES = 1024;
	public const int MAX_MAP_OVERLAYS = 8192;
	public const int MAX_MAP_WATEROVERLAYS = 16384;
	public const int MAX_MAP_TEXDATA_STRING_DATA = 256000;
	public const int MAX_MAP_TEXDATA_STRING_TABLE = 65536;
	public const int MAX_MAP_PRIMITIVES = 32768;
	public const int MAX_MAP_PRIMVERTS = 65536;
	public const int MAX_MAP_PRIMINDICES = 65536;

	public const uint MAX_KEY = 32u;
	public const uint MAX_VALUE = 1024u;

	public const int CHILDNODE_UPPER_RIGHT = 0;
	public const int CHILDNODE_UPPER_LEFT = 1;
	public const int CHILDNODE_LOWER_LEFT = 2;
	public const int CHILDNODE_LOWER_RIGHT = 3;

	public const int CORNER_LOWER_LEFT = 0;
	public const int CORNER_UPPER_LEFT = 1;
	public const int CORNER_UPPER_RIGHT = 2;
	public const int CORNER_LOWER_RIGHT = 3;


	public const int NEIGHBOREDGE_LEFT = 0;
	public const int NEIGHBOREDGE_TOP = 1;
	public const int NEIGHBOREDGE_RIGHT = 2;
	public const int NEIGHBOREDGE_BOTTOM = 3;

	public const int HEADER_LUMPS = 64;
	public const ushort GAMELUMPFLAG_COMPRESSED = 0x0001;
	public const ushort TEXTURE_NAME_LENGTH = 128;
	public const int OCCLUDER_FLAGS_INACTIVE = 0x1;
	public const int MAXLIGHTMAPS = 4;

	public const int LEAF_FLAGS_SKY = 0x01;    // This leaf has 3D sky in its PVS;
	public const int LEAF_FLAGS_RADIAL = 0x02; // This leaf culled away some portals due to radial vis;
	public const int LEAF_FLAGS_SKY2D = 0x04;  // This leaf has 2D sky in its PVS;

	public const int ANGLE_UP = -1;
	public const int ANGLE_DOWN = -2;
	public const int DVIS_PVS = 0;
	public const int DVIS_PAS = 1;

	public const int DWL_FLAGS_INAMBIENTCUBE = 0x0001;      // This says that the light was put into the per-leaf ambient cubes.;
	public const int DWL_FLAGS_CASTENTITYSHADOWS = 0x0002;  // This says that the light will cast shadows from entities

	public const int OVERLAY_BSP_FACE_COUNT = 64;
	public const uint OVERLAY_RENDER_ORDER_NUM_BITS = 2;
	public const uint OVERLAY_NUM_RENDER_ORDERS = 1u << (int)OVERLAY_RENDER_ORDER_NUM_BITS;
	public const ushort OVERLAY_RENDER_ORDER_MASK = 0xC000;

	public const int WATEROVERLAY_BSP_FACE_COUNT = 256;
	public const uint WATEROVERLAY_RENDER_ORDER_NUM_BITS = 2;
	public const uint WATEROVERLAY_NUM_RENDER_ORDERS = 1u << (int)WATEROVERLAY_RENDER_ORDER_NUM_BITS;
	public const ushort WATEROVERLAY_RENDER_ORDER_MASK = 0xC000;

	public const int MAX_LIGHTMAPPAGE_WIDTH = 256;
	public const int MAX_LIGHTMAPPAGE_HEIGHT = 128;
}

public enum NeighborSpan : byte
{
	CORNER_TO_CORNER = 0,
	CORNER_TO_MIDPOINT = 1,
	MIDPOINT_TO_CORNER = 2
}

public enum NeighborOrientation
{
	ORIENTATION_CCW_0 = 0,
	ORIENTATION_CCW_90 = 1,
	ORIENTATION_CCW_180 = 2,
	ORIENTATION_CCW_270 = 3
}


public enum LumpIndex
{
	Entities = 0,
	Planes = 1,
	TexData = 2,
	Vertexes = 3,
	Visibility = 4,
	Nodes = 5,
	TexInfo = 6,
	Faces = 7,
	Lighting = 8,
	Occlusion = 9,
	Leafs = 10,
	FaceIDs = 11,
	Edges = 12,
	SurfEdges = 13,
	Models = 14,
	WorldLights = 15,
	LeafFaces = 16,
	LeafBrushes = 17,
	Brushes = 18,
	BrushSides = 19,
	Areas = 20,
	AreaPortals = 21,
	Unused0 = 22,
	Unused1 = 23,
	Unused2 = 24,
	Unused3 = 25,
	DispInfo = 26,
	OriginalFaces = 27,
	PhysDisp = 28,
	PhysCollide = 29,
	VertNormals = 30,
	VertNormalIndices = 31,
	DispLightmapAlphas = 32,
	DispVerts = 33,
	DispLightmapSamplePositions = 34,
	GameLump = 35,
	LeafWaterData = 36,
	Primitives = 37,
	PrimVerts = 38,
	PrimIndices = 39,
	PakFile = 40,
	ClipPortalVerts = 41,
	Cubemaps = 42,
	TexDataStringData = 43,
	TexDataStringTable = 44,
	Overlays = 45,
	LeafMinDistToWater = 46,
	FaceMacroTextureInfo = 47,
	DispTris = 48,
	PhysCollideSurface = 49,
	WaterOverlays = 50,
	LeafAmbientIndexHDR = 51,
	LeafAmbientIndex = 52,
	LightingHDR = 53,
	WorldlightsHDR = 54,
	LeafAmbientLightingHDR = 55,
	LeafAmbientLighting = 56,
	XZipPakFile = 57,
	FacesHDR = 58,
	MapFlags = 59,
	OverlayFades = 60,
}

public enum LumpVersions
{
	LUMP_LIGHTING_VERSION = 1,
	LUMP_FACES_VERSION = 1,
	LUMP_OCCLUSION_VERSION = 2,
	LUMP_LEAFS_VERSION = 1,
	LUMP_LEAF_AMBIENT_LIGHTING_VERSION = 1,
}

public static class LZMA
{
	static SevenZip.Compression.LZMA.Decoder decoder = new();
	public static void Decompress(Stream input, Stream output, long inBytes, long outBytes) {
		byte[] properties = new byte[5];
		if (input.Read(properties, 0, 5) != 5)
			throw new Exception("input .lzma is too short");

		decoder.SetDecoderProperties(properties);
		decoder.Code(input, output, inBytes, outBytes, null);
	}
}

/// <summary>
/// Analog of lump_t
/// </summary>
public struct BSPLump
{
	public int FileOffset;
	public int FileLength;
	public int Version;
	public int UncompressedSize;

	/// <summary>
	/// Read a lump from a stream. The stream must be the same stream that the BSPLump was parsed from.
	/// </summary>
	/// <param name="stream"></param>
	/// <param name="index"></param>
	/// <returns></returns>
	public byte[] ReadBytes(Stream stream) => ReadBytes<byte>(stream)!; // << Never will be null when byte
	public unsafe T[]? ReadBytes<T>(Stream stream) where T : unmanaged {
		stream.Seek(FileOffset, SeekOrigin.Begin);

		int sizeofone = Unsafe.SizeOf<T>();

		// If uncompressed size != 0, perform LZMA decompression on the lump contents
		if (UncompressedSize != 0) {
			if ((UncompressedSize % sizeofone) != 0)
				return null; // Funny size

			using BinaryReader br = new(stream);
			LZMAHeader header = default;
			header.ID = br.ReadUInt32();
			header.ActualSize = br.ReadUInt32();
			header.LZMASize = br.ReadUInt32();

			if (header.ID == LZMAHeader.LZMA_ID) {
				T[] uncompressed = new T[UncompressedSize / Unsafe.SizeOf<T>()];
				fixed (T* ptr = uncompressed) {
					using UnmanagedMemoryStream msOut = new((byte*)ptr, UncompressedSize);
					LZMA.Decompress(stream, msOut, header.LZMASize, UncompressedSize);
					return uncompressed;
				}
			}
			else {
				Warning("Invalid LZMA chunk.\n");
				return [];
			}
		}
		// Otherwise, read an uncompressed lump
		else {
			if ((FileLength % sizeofone) != 0)
				return null; // Funny size

			T[] data = new T[FileLength / Unsafe.SizeOf<T>()];
			stream.ReadExactly(MemoryMarshal.Cast<T, byte>(data));
			return data;
		}
	}
}
public struct LZMAHeader
{
	public const int LZMA_ID = ('A' << 24) | ('M' << 16) | ('Z' << 8) | 'L';

	public uint ID;
	public uint ActualSize;
	public uint LZMASize;
	public InlineArray5<byte> Properties;
}
/// <summary>
/// Analog of dheader_t
/// </summary>
public struct BSPHeader
{

	public int Identifier;
	public int Version;
	public InlineArray64<BSPLump> Lumps;
	public int MapRevision;

	public BSPLump GetLump(LumpIndex index) => Lumps[(int)index];

	/// <summary>
	/// Read a lump from a stream. The stream must be the same stream that the BSPHeader was parsed from.
	/// </summary>
	/// <param name="stream"></param>
	/// <param name="index"></param>
	/// <returns></returns>
	public byte[] ReadLumpBytes(Stream stream, LumpIndex index) {
		// Get the input stream at the start of the lump, regardless of compression
		BSPLump lump = GetLump(index);
		return lump.ReadBytes(stream);
	}
}

// level feature flags
// constexpr unsigned LVLFLAGS_BAKED_STATIC_PROP_LIGHTING_NONHDR{0x00000001u}; // was processed by vrad with -staticproplighting, no hdr data;
// constexpr unsigned LVLFLAGS_BAKED_STATIC_PROP_LIGHTING_HDR{0x00000002u};   // was processed by vrad with -staticproplighting, in hdr;
// ^^ review later.

/// <summary>
/// Analog of dflagslump_t
/// </summary>
public struct BSPFlagsLump
{
	public uint LevelFlags;
}

/// <summary>
/// Analog of lumpfileheader_t
/// </summary>
public struct BSPLumpFileHeader
{
	int LumpOffset;
	int LumpID;
	int LumpVersion;
	int LumpLength;
	int MapRevision;
}

/// <summary>
/// Analog of dgamelumpheader_t
/// </summary>
public struct BSPGameLumpHeader
{
	public int LumpCount;
}

/// <summary>
/// Analog of dgamelump_t
/// </summary>
public struct BSPGameLump
{
	public GameLumpId_t id;
	public ushort Flags;
	public ushort Version;
	public int fileofs;
	public int filelen;
}

/// <summary>
/// Analog of dmodel_t
/// </summary>
public struct BSPModel
{
	public Vector3 Mins, Maxs;
	public Vector3 Origin;
	public int HeadNode;
	public int FirstFace, NumFaces;
}

/// <summary>
/// Analog of dphysmodel_t
/// </summary>
public struct BSPPhysModel
{
	public int ModelIndex;
	public int DataSize;
	public int KeydataSize;
	public int SolidCount;
}

/// <summary>
/// Analog of dphysdisp_t
/// </summary>
struct BSPPhysDisp
{
	public ushort NumDisplacements;
}
/// <summary>
/// Analog of dvertex_t
/// </summary>
public struct BSPVertex
{
	public Vector3 Position;
}

/// <summary>
/// Analog of dplane_t
/// </summary>
public struct BSPPlane
{
	public Vector3 Normal;
	public float Dist;
	public int Type;
}
/// <summary>
/// Analog of dnode_t
/// </summary>
public struct BSPNode
{
	public int PlaneNum;
	public InlineArray2<int> Children;
	public InlineArray3<short> Mins;
	public InlineArray3<short> Maxs;
	public ushort FirstFace;
	public ushort NumFaces;
	public short Area;
}
/// <summary>
/// Analog of texinfo_s
/// </summary>
public struct BSPTexInfo
{
	public InlineArray2<InlineArray4<float>> TextureVecsTexelsPerWorldUnits;
	public InlineArray2<InlineArray4<float>> LightmapVecsLuxelsPerWorldUnits;
	public int Flags;
	public int TexData;
}

/// <summary>
/// Analog of dtexdata_t
/// </summary>
public struct BSPTexData
{
	public Vector3 Reflectivity;
	public int NameStringTableID;
	public int Width, Height;
	public int ViewWidth, ViewHeight;
}

/// <summary>
/// Analog of doccluderdata_t
/// </summary>
public struct BSPOccluderData
{
	public int Flags;
	public int FirstPoly;
	public int PolyCount;
	public Vector3 Mins;
	public Vector3 Maxs;
	public int Area;
}

/// <summary>
/// Analog of doccluderdataV1_t
/// </summary>
public struct BSPOccluderDataV1
{
	public int Flags;
	public int FirstPoly;
	public int PolyCount;
	public Vector3 Mins;
	public Vector3 Maxs;
}

public struct BSPMSurface1
{
	public nint SurfNum;
	public InlineArray2<int> TextureMins;
	public InlineArray2<short> TextureExtents;
	public ushort NumPrims;
	public ushort FirstPrimID;
}

public struct BSPSurfaceLighting {
	public nint SurfNum;

	public unsafe ref ColorRGBExp32 AvgLightColor(int lightStyleIndex) {
		if (lightStyleIndex < 0 || lightStyleIndex >= (Samples?.Length ?? 0))
			throw new IndexOutOfRangeException();

		return ref Samples![Samples.Length - 1 - lightStyleIndex];
	}

	// Lightmap info
	public InlineArray2<short> LightmapMins;
	public InlineArray2<short> LightmapExtents;
	public InlineArray2<short> OffsetIntoLightmapPage;

	public int LastComputedFrame; 
	public int DLightBits;        
	public int DLightFrame;       

	public InlineArrayMaxLightmaps<byte> Styles;  
	public ColorRGBExp32[]? Samples;
}
public struct BSPMSurfaceNormal
{
	public nint SurfNum;
	public uint FirstVertNormal;
}

public struct BSPMSurface2
{
	public nint SurfNum; // We use this instead of pointer subtraction math in MSurf_Index
	public SurfDraw Flags;
	public Box<CollisionPlane> Plane;
	public int FirstVertIndex;
	public WorldDecalHandle_t Decals;
	public ShadowDecalHandle_t ShadowDecals;
	public OverlayFragmentHandle_t FirstOverlayFragment;
	public short MaterialSortID;
	public ushort VertBufferIndex;
	public ushort DynamicShadowsEnabled_TexInfo;
	// todo:  IDispInfo
	public int VisFrame;

	public bool DynamicShadowsEnabled {
		readonly get => (DynamicShadowsEnabled_TexInfo & 0x1) != 0;
		set {
			if (value)
				DynamicShadowsEnabled_TexInfo |= 0x1;
			else
				DynamicShadowsEnabled_TexInfo &= 0xFFFE;
		}
	}

	public ushort TexInfo {
		readonly get => (ushort)(DynamicShadowsEnabled_TexInfo >> 1);
		set {
			ushort masked = (ushort)(value & 0x7FFF);
			DynamicShadowsEnabled_TexInfo = (ushort)((DynamicShadowsEnabled_TexInfo & 0x1) | (masked << 1));
		}
	}

}

/// <summary>
/// Analog of doccluderpolydata_t
/// </summary>
public struct BSPOccluderPolyData
{
	public int FirstVertexIndex;
	public int VertexCount;
	public int PlaneNum;
}

public struct DispSubNeighbor
{
	public ushort GetNeighborIndex() => Neighbor;
	public NeighborSpan GetSpan() => (NeighborSpan)Span;
	public NeighborSpan GetNeighborSpan() => (NeighborSpan)NeighborSpan;
	public NeighborOrientation GetNeighborOrientation() => (NeighborOrientation)NeighborOrientation;

	public bool IsValid() => Neighbor != 0xFFFF;
	public void SetInvalid() => Neighbor = 0xFFFF;


	public ushort Neighbor;
	public byte NeighborOrientation;
	public byte Span;
	public byte NeighborSpan;
}

public class DispNeighbor
{
	void SetInvalid() { SubNeighbors[0].SetInvalid(); SubNeighbors[1].SetInvalid(); }
	bool IsValid() => SubNeighbors[0].IsValid() || SubNeighbors[1].IsValid();

	InlineArray2<DispSubNeighbor> SubNeighbors;
}


public class DispCornerNeighbors
{
	public void SetInvalid() { NumNeighbors = 0; }
	public InlineArray4<short> /*MAX_DISP_CORNER_NEIGHBORS == 4*/ Neighbors;
	public byte NumNeighbors;
}


public class DispVert
{
	public Vector3 Vector;
	public float Dist;
	public float Alpha;
}

public enum DispTriTags
{
	TagSurface = 1 << 0,
	TagWalkable = 1 << 1,
	TagBuildable = 1 << 2,
	FlagSurfProp1 = 1 << 3,
	FlagSurfProp2 = 1 << 4,
	TagRemove = 1 << 5
}

public class DispTri
{
	public DispTriTags Tags;
}

/// <summary>
/// Analog of ddispinfo_t
/// </summary>
public class BSPDispInfo
{
	public int NumVerts() => ((1 << Power) + 1) * ((1 << Power) + 1);
	public int NumTris() => (1 << Power) * (1 << Power) * 2;

	public Vector3 StartPosition;
	public int DispVertStart;
	public int DispTriStart;
	public int Power;
	public int MinTess;
	public float SmoothingAngle;
	public int Contents;
	public ushort MapFace;
	public int LightmapAlphaStart;
	public int LightmapSamplePositionStart;
	public InlineArray4<DispNeighbor> EdgeNeighbors;
	public InlineArray4<DispCornerNeighbors> CornerNeighbors;
	InlineArray10<long> AllowedVerts;
}

/// <summary>
/// Analog of dedge_t
/// </summary>
public struct BSPEdge
{
	public InlineArray2<ushort> V;
}

public enum BSPPrimitiveType
{
	TriList = 0,
	TriStrip = 1,
}

/// <summary>
/// Analog of dprimitive_t
/// </summary>
public struct BSPPrimitive
{
	public byte Type;
	public ushort FirstIndex;
	public ushort IndexCount;
	public ushort FirstVert;
	public ushort VertCount;
}

/// <summary>
/// Analog of dprimvert_t
/// </summary>
public struct BSPPrimVert
{
	public Vector3 Pos;
}

/// <summary>
/// Analog of dface_t
/// </summary>
public struct BSPFace
{
	public ushort PlaneNum;
	public byte Side;
	public byte OnNode;
	public int FirstEdge;
	public short NumEdges;
	public short TexInfo;
	public short DispInfo;
	public short SurfaceFogVolumeID;
	public InlineArray4<byte> /*MAXLIGHTMAPS == 4*/ Styles;
	public int LightOffset;
	public float Area;
	public InlineArray2<int> LightmapTextureMinsInLuxels;
	public InlineArray2<int> LightmapTextureSizeInLuxels;
	public int OrigFace;
	public ushort GetNumPrims() => (ushort)(NumPrims & 0x7FFF);
	public void SetNumPrims(ushort prims) {
		Assert((prims & 0x8000) == 0);
		NumPrims &= unchecked((ushort)~0x7FFF);
		NumPrims |= unchecked((ushort)(prims & 0x7FFF));
	}
	public bool AreDynamicShadowsEnabled() => (NumPrims & 0x8000) == 0;
	public void SetDynamicShadowsEnabled(bool enabled) {
		if (enabled)
			NumPrims &= unchecked((ushort)~0x8000);
		else
			NumPrims |= 0x8000;
	}

	public ushort NumPrims;
	public ushort FirstPrimID;
	public uint SmoothingGroups;
}

/// <summary>
/// Analog of dfaceid_t
/// </summary>
public struct BSPFaceID
{
	public ushort HammerFaceID;
}

/// <summary>
/// Analog of dleaf_version_0_t
/// </summary>
public struct BSPLeafVersion0
{
	public int Contents;
	public short Cluster;

	short areaandflags;
	public short Area => throw new NotImplementedException();  // 9 bits 
	public short Flags => throw new NotImplementedException(); // 7 bits

	public InlineArray3<short> Mins;
	public InlineArray3<short> Maxs;

	public ushort FirstLeafFace;
	public ushort NumLeafFaces;
	public ushort FirstLeafBrush;
	public ushort NumLeafBrushes;
	public short LeafWaterDataID;

	CompressedLightCube m_AmbientLighting;
}

public struct CompressedLightCube
{
	public InlineArray6<ColorRGBExp32> Color;
}

/// <summary>
/// Analog of dleaf_t
/// </summary>
public struct BSPLeaf
{
	public int Contents;
	public short Cluster;

	short areaandflags;

	public short Area => throw new NotImplementedException();  // 9 bits 
	public short Flags => throw new NotImplementedException(); // 7 bits

	public InlineArray3<short> Mins;
	public InlineArray3<short> Maxs;

	public ushort FirstLeafFace;
	public ushort NumLeafFaces;

	public ushort FirstLeafBrush;
	public ushort NumLeafBrushes;
	public short LeafWaterDataID;
}

/// <summary>
/// Analog of dleafambientlighting_t
/// </summary>
public struct BSPLeafAmbientLighting
{
	public CompressedLightCube Cube;
	public byte X;
	public byte Y;
	public byte Z;
	public byte Pad;
}

/// <summary>
/// Analog of dleafambientindex_t
/// </summary>
public struct BSPLeafAmbientIndex
{
	public ushort AmbientSampleCount;
	public ushort FirstAmbientSample;
}

/// <summary>
/// Analog of dbrushside_t
/// </summary>
public struct dbrushside_t
{
	public ushort PlaneNum;
	public short TexInfo;
	public short DispInfo;
	public short Bevel;
}

/// <summary>
/// Analog of dbrush_t
/// </summary>
public struct BSPBrush
{
	public int FirstSide;
	public int NumSides;
	public int Contents;
}

/// <summary>
/// Analog of dvis_t
/// </summary>
public struct BSPVis
{
	public int NumClusters;
	public InlineArray8<InlineArray2<int>> BitOffsets;
}

/// <summary>
/// Analog of dareaportal_t
/// </summary>
public struct BSPAreaPortal
{
	public ushort PortalKey;
	public ushort OtherArea;
	public ushort FirstClipPortalVert;
	public ushort ClipPortalVerts;
	public int PlaneNum;
}

/// <summary>
/// Analog of darea_t
/// </summary>
public struct BSPArea
{
	public int NumAreaPortals;
	public int FirstAreaPortal;
}

/// <summary>
/// Analog of dleafwaterdata_t
/// </summary>
public struct BSPLeafWaterData
{
	public float SurfaceZ;
	public float MinZ;
	public short SurfaceTexInfoID;
}

public class FaceMacroTextureInfo
{
	public ushort MacroTextureNameID;
}

// lights that were used to illuminate the world
public enum EmitType
{
	Surface,
	Point,
	SpotLight,
	SkyLight,
	QuakeLight,
	SkyAmbient
}


/// <summary>
/// Analog of dworldlight_t
/// </summary>
public struct BSPWorldLight
{
	public Vector3 Origin;
	public Vector3 Intensity;
	public Vector3 Normal;
	public Vector3 ShadowCastOffset; // Verify this.
	public int Cluster;
	public EmitType Type;
	public int Style;
	public float StopDot;
	public float StopDot2;
	public float Exponent;
	public float Radius;
	public float ConstantAttn;
	public float LinearAttn;
	public float QuadraticAttn;
	public int Flags;
	public int TexInfo;
	public int Owner;
}

/// <summary>
/// Analog of dcubemapsample_t
/// </summary>
public struct BSPCubeMapSample
{
	public InlineArray3<int> Origin;
	public byte size;
}

/// <summary>
/// Analog of doverlay_t
/// </summary>
public struct BSPOverlay
{
	public int Id;
	public short TexInfo;

	public void SetFaceCount(ushort count) {
		FaceCountAndRenderOrder &= BSPFileCommon.OVERLAY_RENDER_ORDER_MASK;
		FaceCountAndRenderOrder |= (ushort)(count & ~BSPFileCommon.OVERLAY_RENDER_ORDER_MASK);
	}
	public ushort GetFaceCount() => (ushort)(FaceCountAndRenderOrder & ~BSPFileCommon.OVERLAY_RENDER_ORDER_MASK);
	public void SetRenderOrder(ushort order) {
		FaceCountAndRenderOrder &= unchecked((ushort)~BSPFileCommon.OVERLAY_RENDER_ORDER_MASK);
		FaceCountAndRenderOrder |= unchecked((ushort)(order << (int)(16u - BSPFileCommon.OVERLAY_RENDER_ORDER_NUM_BITS)));
	}
	public ushort GetRenderOrder() => (ushort)(FaceCountAndRenderOrder >> (int)(16u - BSPFileCommon.OVERLAY_RENDER_ORDER_NUM_BITS));

	public ushort FaceCountAndRenderOrder;
	public InlineArray64<int> /*OVERLAY_BSP_FACE_COUNT == 64*/ Faces;
	public InlineArray2<float> U;
	public InlineArray2<float> V;
	public InlineArray4<Vector3> UVPoints;
	public Vector3 Origin;
	public Vector3 BasisNormal;
}

/// <summary>
/// Analog of doverlayfade_t
/// </summary>
public struct BSPOverlayFade
{
	public float FadeDistMinSq;
	public float FadeDistMaxSq;
}

/// <summary>
/// Analog of dwateroverlay_t
/// </summary>
public struct BSPWaterOverlay
{
	int nId;
	short nTexInfo;

	public void SetFaceCount(ushort count) {
		FaceCountAndRenderOrder &= unchecked((ushort)~BSPFileCommon.WATEROVERLAY_RENDER_ORDER_MASK);
		FaceCountAndRenderOrder |= (ushort)(count & unchecked((ushort)~BSPFileCommon.WATEROVERLAY_RENDER_ORDER_MASK));
	}
	public ushort GetFaceCount() {
		return (ushort)(FaceCountAndRenderOrder & unchecked((ushort)~BSPFileCommon.WATEROVERLAY_RENDER_ORDER_MASK));
	}
	public void SetRenderOrder(ushort order) {
		FaceCountAndRenderOrder &= unchecked((ushort)~BSPFileCommon.WATEROVERLAY_RENDER_ORDER_MASK);
		FaceCountAndRenderOrder |= (ushort)(order << (int)(16u - BSPFileCommon.WATEROVERLAY_RENDER_ORDER_NUM_BITS));
	}
	public ushort GetRenderOrder() {
		return (ushort)(FaceCountAndRenderOrder >> (int)(16u - BSPFileCommon.WATEROVERLAY_RENDER_ORDER_NUM_BITS));
	}

	ushort FaceCountAndRenderOrder;
	public InlineArray256<int> /* WATEROVERLAY_BSP_FACE_COUNT == 256 */ Faces;
	public InlineArray2<float> U;
	public InlineArray2<float> V;
	public InlineArray4<Vector3> UVPoints;
	public Vector3 Origin;
	public Vector3 BasisNormal;
}

// epair_t is weird

public enum Contents
{
	/// <summary>
	/// No contents
	/// </summary>
	Empty = 0,
	/// <summary>
	/// an eye is never valid in a solid
	/// </summary>
	Solid = 0x1,
	/// <summary>
	/// translucent, but not watery (glass)
	/// </summary>
	Window = 0x2,
	Aux = 0x4,
	/// <summary>
	/// alpha-tested "grate" textures.  Bullets/sight pass through, but solids don't
	/// </summary>
	Grate = 0x8,
	Slime = 0x10,
	Water = 0x20,
	/// <summary>
	/// block AI line of sight
	/// </summary>
	BlockLOS = 0x40,
	/// <summary>
	/// things that cannot be seen through (may be non-solid though)
	/// </summary>
	Opaque = 0x80,
	LastVisibleContents = 0x80,
	AllVisibleContents = (LastVisibleContents | (LastVisibleContents - 1)),
	TestFogVolume = 0x100,
	/// <summary>
	/// per team contents used to differentiate collisions 
	/// </summary>
	Team1 = 0x800,
	/// <summary>
	/// between players and objects on different teams
	/// </summary>
	Team2 = 0x1000,
	/// <summary>
	/// ignore CONTENTS_OPAQUE on surfaces that have SURF_NODRAW
	/// </summary>
	IgnoreNoDrawOpaque = 0x2000,
	/// <summary>
	/// hits entities which are MOVETYPE_PUSH (doors, plats, etc.)
	/// </summary>
	Moveable = 0x4000,
	/// <summary>
	/// remaining contents are non-visible, and don't eat brushes
	/// </summary>
	AreaPortal = 0x8000,
	PlayerClip = 0x10000,
	MonsterClip = 0x20000,
	/// <summary>
	/// currents can be added to any other contents, and may be mixed
	/// </summary>
	Current0 = 0x40000,
	Current90 = 0x80000,
	Current180 = 0x100000,
	Current270 = 0x200000,
	CurrentUp = 0x400000,
	CurrentDown = 0x800000,
	/// <summary>
	/// removed before bsping an entity
	/// </summary>
	Origin = 0x1000000,
	/// <summary>
	/// should never be on a brush, only in game
	/// </summary>
	Monster = 0x2000000,
	Debris = 0x4000000,
	/// <summary>
	/// brushes to be added after vis leafs
	/// </summary>
	Detail = 0x8000000,
	/// <summary>
	/// auto set if any surface has trans
	/// </summary>
	Translucent = 0x10000000,
	Ladder = 0x20000000,
	/// <summary>
	/// use accurate hitboxes on trace
	/// </summary>
	HitBox = 0x40000000,
}

public enum Surf : ushort
{
	/// <summary>
	/// value will hold the light strength
	/// </summary>
	Light = 0x0001,
	/// <summary>
	/// don't draw, indicates we should skylight + draw 2d sky but not draw the 3D skybox
	/// </summary>
	Sky2D = 0x0002,
	/// <summary>
	/// don't draw, but add to skybox
	/// </summary>
	Sky = 0x0004,
	/// <summary>
	/// turbulent water warp
	/// </summary>
	Warp = 0x0008,
	Trans = 0x0010,
	/// <summary>
	/// the surface can not have a portal placed on it
	/// </summary>
	NoPortal = 0x0020,
	/// <summary>
	/// FIXME: This is an xbox hack to work around elimination of trigger surfaces, which breaks occluders
	/// </summary>
	Trigger = 0x0040,
	/// <summary>
	/// don't bother referencing the texture
	/// </summary>
	NoDraw = 0x0080,
	/// <summary>
	/// make a primary bsp splitter			  
	/// </summary>
	Hint = 0x0100,
	/// <summary>
	/// completely ignore, allowing non-closed brushes
	/// </summary>
	Skip = 0x0200,
	/// <summary>
	/// Don't calculate light
	/// </summary>
	NoLight = 0x0400,
	/// <summary>
	/// calculate three lightmaps for the surface for bumpmapping
	/// </summary>
	BumpLight = 0x0800,
	/// <summary>
	/// Don't receive shadows
	/// </summary>
	NoShadows = 0x1000,
	/// <summary>
	/// Don't receive decals
	/// </summary>
	NoDecals = 0x2000,
	/// <summary>
	/// Don't subdivide patches on this surface 
	/// </summary>
	NoChop = 0x4000,
	/// <summary>
	/// surface is part of a hitbox
	/// </summary>
	Hitbox = 0x8000,
}


public enum Mask : uint
{
	All = 0xFFFFFFFF,
	/// <summary>
	/// everything that is normally solid
	/// </summary>
	Solid = Contents.Solid | Contents.Moveable | Contents.Window | Contents.Monster | Contents.Grate,
	/// <summary>
	/// everything that blocks player movement
	/// </summary>
	PlayerSolid = Contents.Solid | Contents.Moveable | Contents.PlayerClip | Contents.Window | Contents.Monster | Contents.Grate,
	/// <summary>
	/// blocks npc movement
	/// </summary>
	NPCSolid = Contents.Solid | Contents.Moveable | Contents.MonsterClip | Contents.Window | Contents.Monster | Contents.Grate,
	/// <summary>
	/// water physics in these contents
	/// </summary>
	Water = Contents.Water | Contents.Moveable | Contents.Slime,
	/// <summary>
	/// everything that blocks lighting
	/// </summary>
	Opaque = Contents.Solid | Contents.Moveable | Contents.Opaque,
	/// <summary>
	/// everything that blocks lighting, but with monsters added.
	/// </summary>
	OpaqueAndNPCs = Opaque | Contents.Monster,
	/// <summary>
	/// everything that blocks line of sight for AI
	/// </summary>
	BlockLOS = Contents.Solid | Contents.Moveable | Contents.BlockLOS,
	/// <summary>
	/// everything that blocks line of sight for AI plus NPCs
	/// </summary>
	BlockLOSAndNPCs = BlockLOS | Contents.Monster,
	/// <summary>
	/// everything that blocks line of sight for players
	/// </summary>
	Visible = Opaque | Contents.IgnoreNoDrawOpaque,
	/// <summary>
	/// everything that blocks line of sight for players, but with monsters added.
	/// </summary>
	VisibleAndNPCs = OpaqueAndNPCs | Contents.IgnoreNoDrawOpaque,
	/// <summary>
	/// bullets see these as solid
	/// </summary>
	Shot = Contents.Solid | Contents.Moveable | Contents.Monster | Contents.Window | Contents.Debris | Contents.HitBox,
	/// <summary>
	/// non-raycasted weapons see this as solid (includes grates)
	/// </summary>
	ShotHull = Contents.Solid | Contents.Moveable | Contents.Monster | Contents.Window | Contents.Debris | Contents.Grate,
	/// <summary>
	/// hits solids (not grates) and passes through everything else
	/// </summary>
	ShotPortal = Contents.Solid | Contents.Moveable | Contents.Window | Contents.Monster,
	/// <summary>
	/// everything normally solid, except monsters (world+brush only)
	/// </summary>
	SolidBrushOnly = Contents.Solid | Contents.Moveable | Contents.Window | Contents.Grate,
	/// <summary>
	/// everything normally solid for player movement, except monsters (world+brush only)
	/// </summary>
	PlayerSolidBrushOnly = Contents.Solid | Contents.Moveable | Contents.Window | Contents.PlayerClip | Contents.Grate,
	/// <summary>
	/// everything normally solid for npc movement, except monsters (world+brush only)
	/// </summary>
	NPCSolidBrushOnly = Contents.Solid | Contents.Moveable | Contents.Window | Contents.MonsterClip | Contents.Grate,
	/// <summary>
	/// just the world, used for route rebuilding
	/// </summary>
	NPCWorldStatic = Contents.Solid | Contents.Window | Contents.MonsterClip | Contents.Grate,
	/// <summary>
	/// These are things that can split areaportals
	/// </summary>
	SplitAreaPortal = Contents.Water | Contents.Slime,

	/// <summary>
	/// UNDONE: This is untested, any moving water
	/// </summary>
	Current = Contents.Current0 | Contents.Current90 | Contents.Current180 | Contents.Current270 | Contents.CurrentUp | Contents.CurrentDown,

	/// <summary>
	/// everything that blocks corpse movement
	/// UNDONE: Not used yet / may be deleted
	/// </summary>
	DeadSolid = Contents.Solid | Contents.PlayerClip | Contents.Window | Contents.Grate
}

public enum SurfDraw : uint
{
	NoLight = 0x00000001,
	Node = 0x00000002,
	Sky = 0x00000004,
	BumpLight = 0x00000008,
	NoDraw = 0x00000010,
	Trans = 0x00000020,
	PlaneBack = 0x00000040,
	Dynamic = 0x00000080,
	TangentSpace = 0x00000100,
	NoCull = 0x00000200,
	HasLightstyles = 0x00000400,
	HasDisp = 0x00000800,
	AlphaTest = 0x00001000,
	NoShadows = 0x00002000,
	NoDecals = 0x00004000,
	HasPrims = 0x00008000,
	WaterSurface = 0x00010000,
	UnderWater = 0x00020000,
	AboveWater = 0x00040000,
	HasDLight = 0x00080000,
	DLightPass = 0x00100000,
	UNUSED2 = 0x00200000,
	VertCountMask = 0xFF000000,
	SortGroupMask = 0x00C00000,
	VertCountShift = 24,
	SortGroupShift = 22,
}