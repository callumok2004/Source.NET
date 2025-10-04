﻿using Source;
using Source.Common;

using static System.Runtime.InteropServices.JavaScript.JSType;
using System;

using FIELD = Source.FIELD<Game.Client.C_BasePlayer>;
using System.Numerics;
using Game.Shared;
using Source.Common.Mathematics;

namespace Game.Client;

public partial class C_BasePlayer : C_BaseCombatCharacter, IGameEventListener2
{
	public static readonly RecvTable DT_PlayerState = new([
		RecvPropInt(FIELD.OF(nameof(DeadFlag)))
	]); public static readonly ClientClass CC_PlayerState = new("PlayerState", null, null, DT_PlayerState);

	public static readonly RecvTable DT_LocalPlayerExclusive = new([
		RecvPropDataTable(nameof(Local), FIELD.OF(nameof(Local)), PlayerLocalData.DT_Local, 0, DataTableRecvProxy_PointerDataTable),
		RecvPropFloat(FIELD.OF(nameof(Friction))),
		RecvPropArray3(FIELD.OF_ARRAY(nameof(Ammo)), RecvPropInt( FIELD.OF_ARRAYINDEX(nameof(Ammo)))),
		RecvPropInt(FIELD.OF(nameof(OnTarget))),
		RecvPropInt(FIELD.OF(nameof(TickBase))),
		RecvPropInt(FIELD.OF(nameof(NextThinkTick))),
		RecvPropEHandle(FIELD.OF(nameof(LastWeapon))),
		RecvPropEHandle(FIELD.OF(nameof(GroundEntity))),
		RecvPropVector(FIELD.OF(nameof(BaseVelocity))),
		RecvPropEHandle(FIELD.OF(nameof(ConstraintEntity))),
		RecvPropVector(FIELD.OF(nameof(ConstraintCenter))),
		RecvPropFloat(FIELD.OF(nameof(ConstraintRadius))),
		RecvPropFloat(FIELD.OF(nameof(ConstraintWidth))),
		RecvPropFloat(FIELD.OF(nameof(ConstraintSpeedFactor))),
		RecvPropFloat(FIELD.OF(nameof(DeathTime))),
		RecvPropFloat(FIELD.OF(nameof(LaggedMovementValue))),
		RecvPropEHandle(FIELD.OF(nameof(TonemapController))),
		RecvPropEHandle(FIELD.OF(nameof(ViewEntity))),
		RecvPropBool(FIELD.OF(nameof(DisableWorldClicking))),
	]);

	public virtual void PreThink() {}
	public virtual void PostThink() {}

	public void SetViewAngles(in QAngle angles) {
		SetLocalAngles(angles);
		SetNetworkAngles(angles);
	}

	private static void RecvProxy_LocalVelocityX(ref readonly RecvProxyData data, object instance, IFieldAccessor field) {
		throw new NotImplementedException();
	}

	private static void RecvProxy_LocalVelocityY(ref readonly RecvProxyData data, object instance, IFieldAccessor field) {
		throw new NotImplementedException();
	}

	private static void RecvProxy_LocalVelocityZ(ref readonly RecvProxyData data, object instance, IFieldAccessor field) {
		throw new NotImplementedException();
	}

	public static readonly ClientClass CC_LocalPlayerExclusive = new ClientClass("LocalPlayerExclusive", null, null, DT_LocalPlayerExclusive);

	public static readonly RecvTable DT_BasePlayer = new(DT_BaseCombatCharacter, [
		RecvPropDataTable(nameof(pl), FIELD.OF(nameof(pl)), DT_PlayerState),
		RecvPropEHandle(FIELD.OF(nameof(Vehicle))),
		RecvPropEHandle(FIELD.OF(nameof(UseEntity))),
		RecvPropInt(FIELD.OF(nameof(LifeState))),
		RecvPropEHandle(FIELD.OF(nameof(ColorCorrectionCtrl))), // << gmod specific
		RecvPropFloat(FIELD.OF(nameof(MaxSpeed))),
		RecvPropInt(FIELD.OF(nameof(Flags))),
		RecvPropInt(FIELD.OF(nameof(ObserverMode))),
		RecvPropEHandle(FIELD.OF(nameof(ObserverTarget))),
		RecvPropInt(FIELD.OF(nameof(FOV))),
		RecvPropInt(FIELD.OF(nameof(FOVStart))),
		RecvPropFloat(FIELD.OF(nameof(FOVTime))),
		RecvPropFloat(FIELD.OF(nameof(DefaultFOV))),
		RecvPropEHandle(FIELD.OF(nameof(ZoomOwner))),

		RecvPropEHandle(FIELD.OF_ARRAYINDEX(nameof(ViewModel), 0)),
		RecvPropArray(FIELD.OF_ARRAY(nameof(ViewModel))),

		RecvPropString(FIELD.OF(nameof(LastPlaceName))),
		RecvPropBool(FIELD.OF(nameof(UseWeaponsInVehicle))),
		RecvPropDataTable("localdata", DT_LocalPlayerExclusive),
	]); public static readonly new ClientClass ClientClass = new ClientClass("BasePlayer", null, null, DT_BasePlayer);


	static C_BasePlayer? localPlayer;
	public static C_BasePlayer? GetLocalPlayer() => localPlayer;

	public void FireGameEvent(IGameEvent ev) {
		throw new NotImplementedException();
	}

	public override void Dispose() {
		base.Dispose();
		if (this == localPlayer) {
			localPlayer = null;
		}
	}

	public override void PostDataUpdate(DataUpdateType updateType) {
		if(updateType == DataUpdateType.Created) {
			int localPlayerIndex = engine.GetLocalPlayer();

			if(localPlayerIndex == Index) {
				localPlayer = this;
			}
		}

		base.PostDataUpdate(updateType);
	}

	bool DeadFlag;
	readonly PlayerState pl = new();
	public readonly PlayerLocalData Local = new();
	readonly EHANDLE Vehicle = new();
	readonly EHANDLE UseEntity = new();
	readonly EHANDLE ObserverTarget = new();
	readonly EHANDLE ZoomOwner = new();
	readonly EHANDLE ConstraintEntity = new();
	readonly EHANDLE TonemapController = new();
	readonly EHANDLE ViewEntity = new();
	InlineArrayNewMaxViewmodels<EHANDLE> ViewModel = new(); 
	bool DisableWorldClicking;
	float MaxSpeed;
	int Flags;
	int ObserverMode;
	int FOV;
	int TickBase;
	int FOVStart;
	float FOVTime;
	float DefaultFOV;
	Vector3 ConstraintCenter;
	float ConstraintRadius;
	float ConstraintWidth;
	float ConstraintSpeedFactor;
	InlineArray18<char> LastPlaceName;
	readonly EHANDLE ColorCorrectionCtrl = new();
	bool UseWeaponsInVehicle;
	public bool OnTarget;
	public double DeathTime;
	public double LaggedMovementValue;
	readonly EHANDLE LastWeapon = new();
}
