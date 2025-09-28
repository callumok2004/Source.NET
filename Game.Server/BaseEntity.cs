﻿using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Engine;
using Source.Common.Mathematics;

using System.Numerics;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;

namespace Game.Server;
using FIELD = Source.FIELD<BaseEntity>;

public partial class BaseEntity : IServerEntity
{
	public const int TEAMNUM_NUM_BITS = 15; // < gmod increased 6 -> 15

	private static void SendProxy_AnimTime(SendProp prop, object instance, IFieldAccessor field, ref DVariant outData, int element, int objectID)
		=> throw new NotImplementedException();
	private static void SendProxy_SimulationTime(SendProp prop, object instance, IFieldAccessor field, ref DVariant outData, int element, int objectID)
		=> throw new NotImplementedException();

	public static SendTable DT_AnimTimeMustBeFirst = new(nameof(DT_AnimTimeMustBeFirst), [
		SendPropInt (FIELD.OF(nameof(AnimTime)), 8, PropFlags.Unsigned|PropFlags.ChangesOften|PropFlags.EncodedAgainstTickCount, proxyFn: SendProxy_AnimTime),
	]);
	public static object? SendProxy_ClientSideAnimation(SendProp prop, object instance, IFieldAccessor data, SendProxyRecipients recipients, int objectID) {
		throw new NotImplementedException();
	}
	public static SendTable DT_PredictableId = new(nameof(DT_PredictableId), [
		SendPropPredictableId(FIELD.OF(nameof(PredictableID))),
		SendPropInt(FIELD.OF(nameof(IsPlayerSimulated)), 1, PropFlags.Unsigned)
	]);

	public static SendTable DT_BaseEntity = new([
		SendPropDataTable("AnimTimeMustBeFirst", DT_AnimTimeMustBeFirst, SendProxy_ClientSideAnimation),

		SendPropInt(FIELD.OF(nameof(SimulationTime)), SIMULATION_TIME_WINDOW_BITS, PropFlags.Unsigned | PropFlags.ChangesOften | PropFlags.EncodedAgainstTickCount, proxyFn: SendProxy_SimulationTime /* todo */),
		SendPropVector(FIELD.OF(nameof(Origin)), -1, PropFlags.Coord | PropFlags.ChangesOften, 0, Constants.HIGH_DEFAULT, proxyFn: null /* todo */),
		SendPropInt(FIELD.OF(nameof(InterpolationFrame)), NOINTERP_PARITY_MAX_BITS, PropFlags.Unsigned),
		SendPropModelIndex(FIELD.OF(nameof(ModelIndex))),
		SendPropDataTable(nameof(Collision), FIELD.OF(nameof(Collision)), CollisionProperty.DT_CollisionProperty),
		SendPropInt(FIELD.OF(nameof(RenderFX)), 8, PropFlags.Unsigned),
		SendPropInt(FIELD.OF(nameof(RenderMode)), 8, PropFlags.Unsigned),
		SendPropInt(FIELD.OF(nameof(Effects)), (int)EntityEffects.MaxBits, PropFlags.Unsigned),
		SendPropInt(FIELD.OF(nameof(ColorRender)), 32, PropFlags.Unsigned),
		SendPropInt(FIELD.OF(nameof(TeamNum)), TEAMNUM_NUM_BITS, 0),
		SendPropInt(FIELD.OF(nameof(CollisionGroup)), 5, PropFlags.Unsigned),
		SendPropFloat(FIELD.OF(nameof(Elasticity)), 0, PropFlags.Coord | PropFlags.NoScale),
		SendPropFloat(FIELD.OF(nameof(ShadowCastDistance)), 12, PropFlags.Unsigned),
		SendPropEHandle(FIELD.OF(nameof(OwnerEntity))),
		SendPropEHandle(FIELD.OF(nameof(EffectEntity))),
		SendPropEHandle(FIELD.OF(nameof(MoveParent))),
		SendPropInt(FIELD.OF(nameof(ParentAttachment)), NUM_PARENTATTACHMENT_BITS, PropFlags.Unsigned),
		SendPropInt(FIELD.OF(nameof(MoveType)), (int)Source.MoveType.MaxBits, PropFlags.Unsigned ),
		SendPropInt(FIELD.OF(nameof(MoveCollide)), (int)Source.MoveCollide.MaxBits, PropFlags.Unsigned ),
		SendPropQAngles (FIELD.OF(nameof(AngRotation)), 24, PropFlags.ChangesOften | PropFlags.RoundDown, SendProxy_Angles ),
		SendPropInt( FIELD.OF(nameof( TextureFrameIndex) ),     8, PropFlags.Unsigned ),
		SendPropDataTable( "predictable_id", DT_PredictableId, SendProxy_SendPredictableId ),
		SendPropInt(FIELD.OF(nameof(SimulatedEveryTick)),       1, PropFlags.Unsigned ),
		SendPropInt(FIELD.OF(nameof(AnimatedEveryTick)),        1, PropFlags.Unsigned ),
		SendPropBool( FIELD.OF(nameof( AlternateSorting ))),

		// The rest of this is Garry's Mod specific in order
		SendPropInt(FIELD.OF(nameof(TakeDamage)), 8),
		SendPropInt(FIELD.OF(nameof(RealClassName)), 16, PropFlags.Unsigned),

		SendPropInt(FIELD.OF(nameof(OverrideMaterial)), 16, PropFlags.Unsigned, SendProxy_OverrideMaterial),

		SendPropInt(FIELD.OF_ARRAYINDEX(nameof(OverrideSubMaterials), 0), 16, PropFlags.Unsigned),
		SendPropArray2(null, 32, "OverrideSubMaterials"),

		SendPropInt(FIELD.OF(nameof(Health)), 32, PropFlags.Normal | PropFlags.ChangesOften | PropFlags.VarInt),
		SendPropInt(FIELD.OF(nameof(MaxHealth)), 32),
		SendPropInt(FIELD.OF(nameof(SpawnFlags)), 32),
		SendPropInt(FIELD.OF(nameof(GModFlags)), 7),
		SendPropBool(FIELD.OF(nameof(OnFire))),
		SendPropFloat(FIELD.OF(nameof(CreationTime)), 0, PropFlags.NoScale),

		SendPropFloat(FIELD.OF_ARRAYINDEX(nameof(Velocity), 0), 0, PropFlags.NoScale | PropFlags.ChangesOften),
		SendPropFloat(FIELD.OF_ARRAYINDEX(nameof(Velocity), 1), 0, PropFlags.NoScale | PropFlags.ChangesOften),
		SendPropFloat(FIELD.OF_ARRAYINDEX(nameof(Velocity), 2), 0, PropFlags.NoScale | PropFlags.ChangesOften),

		SendPropGModTable(FIELD.OF(nameof(GMOD_DataTable))),

		// Addon exposed data tables
		SendPropArray3(FIELD.OF_ARRAY(nameof(GMOD_bool)), SendPropBool(FIELD.OF_ARRAYINDEX(nameof(GMOD_bool), 0))),
		SendPropArray3(FIELD.OF_ARRAY(nameof(GMOD_float)), SendPropFloat(FIELD.OF_ARRAYINDEX(nameof(GMOD_float), 0))),
		SendPropArray3(FIELD.OF_ARRAY(nameof(GMOD_int)), SendPropInt(FIELD.OF_ARRAYINDEX(nameof(GMOD_int), 0))),
		SendPropArray3(FIELD.OF_ARRAY(nameof(GMOD_Vector)),SendPropVector(FIELD.OF_ARRAYINDEX(nameof(GMOD_Vector), 0))),
		SendPropArray3(FIELD.OF_ARRAY(nameof(GMOD_QAngle)), SendPropQAngles(FIELD.OF_ARRAYINDEX(nameof(GMOD_QAngle), 0))),
		SendPropArray3(FIELD.OF_ARRAY(nameof(GMOD_EHANDLE)), SendPropEHandle(FIELD.OF_ARRAYINDEX(nameof(GMOD_EHANDLE), 0))),
		SendPropString(FIELD.OF(nameof(GMOD_String0))),
		SendPropString(FIELD.OF(nameof(GMOD_String1))),
		SendPropString(FIELD.OF(nameof(GMOD_String2))),
		SendPropString(FIELD.OF(nameof(GMOD_String3))),

		// Creation IDs
		SendPropInt(FIELD.OF(nameof(CreationID)), 24),
		SendPropInt(FIELD.OF(nameof(MapCreatedID)), 16),
	]);

	public ref readonly Vector3 GetLocalOrigin() => ref Origin;
	private static void SendProxy_OverrideMaterial(SendProp prop, object instance, IFieldAccessor field, ref DVariant outData, int element, int objectID) {
		Warning("SendProxy_OverrideMaterial not yet implemented\n");
	}
	private static void SendProxy_Angles(SendProp prop, object instance, IFieldAccessor field, ref DVariant outData, int element, int objectID) {
		Warning("SendProxy_Angles not yet implemented\n");
	}
	private static object? SendProxy_SendPredictableId(SendProp prop, object instance, IFieldAccessor data, SendProxyRecipients recipients, int objectID) {
		Warning("SendProxy_SendPredictableId not yet implemented\n");
		return null;
	}

	public byte RenderFX;
	public byte RenderMode;
	public byte OldRenderMode;
	public int Effects;
	public Color ColorRender;
	public int TeamNum;
	public int CollisionGroup;
	public float Elasticity;
	public float ShadowCastDistance;
	public byte ParentAttachment;
	public byte MoveType;
	public byte MoveCollide;
	public QAngle AngRotation;
	public bool TextureFrameIndex;
	public bool SimulatedEveryTick;
	public bool AnimatedEveryTick;
	public bool AlternateSorting;

	public byte TakeDamage;
	public ushort RealClassName;
	public ushort OverrideMaterial;
	public InlineArray32<ushort> OverrideSubMaterials;
	public int Health;
	public int MaxHealth;
	public int SpawnFlags;
	public int GModFlags;
	public bool OnFire;
	public float CreationTime;
	public Vector3 Velocity;
	public int CreationID;
	public int MapCreatedID;

	public readonly PredictableId PredictableID = new();
	public readonly bool IsPlayerSimulated;

	public readonly GModTable GMOD_DataTable = new();

	public readonly EHANDLE OwnerEntity = new();
	public readonly EHANDLE EffectEntity = new();
	public readonly EHANDLE MoveParent = new();
	public readonly EHANDLE GroundEntity = new();

	public int LifeState;
	public Vector3 BaseVelocity;
	public int NextThinkTick;
	public byte WaterLevel;

	InlineArray32<bool> GMOD_bool;
	InlineArray32<float> GMOD_float;
	InlineArray32<int> GMOD_int;
	InlineArray32<Vector3> GMOD_Vector;
	InlineArray32<QAngle> GMOD_QAngle;
	InlineArray32<EHANDLE> GMOD_EHANDLE; // << ENSURE THESE ARE INITIALIZED!!!!
	InlineArray512<char> GMOD_String0;
	InlineArray512<char> GMOD_String1;
	InlineArray512<char> GMOD_String2;
	InlineArray512<char> GMOD_String3;


	public static readonly ServerClass ServerClass = ServerClass.New("BaseEntity", DT_BaseEntity).WithManualClassID(StaticClassIndices.CBaseEntity);

	public float AnimTime;
	public float SimulationTime;
	public Vector3 Origin;
	public Vector3 NetworkAngles;
	public byte InterpolationFrame;
	public int ModelIndex;
	public CollisionProperty Collision = new();
	public float Friction;

	public ICollideable? GetCollideable() {
		throw new NotImplementedException();
	}

	public int GetModelIndex() {
		throw new NotImplementedException();
	}

	public ReadOnlySpan<char> GetModelName() {
		throw new NotImplementedException();
	}

	public IServerNetworkable? GetNetworkable() {
		throw new NotImplementedException();
	}

	public BaseHandle? GetRefEHandle() {
		throw new NotImplementedException();
	}

	public void SetModelIndex(int index) {
		throw new NotImplementedException();
	}

	public void SetRefEHandle(BaseHandle handle) {
		throw new NotImplementedException();
	}
}
