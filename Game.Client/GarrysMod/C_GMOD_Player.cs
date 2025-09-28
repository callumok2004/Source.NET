﻿using Game.Client.HL2MP;
using Game.Shared;

using Source;
using Source.Common;

using System.Numerics;

namespace Game.Client.GarrysMod;
using FIELD = FIELD<C_GMOD_Player>;

[LinkEntityToClass(LocalName = "player")]
public class C_GMOD_Player : C_HL2MP_Player
{
	public static readonly RecvTable DT_GMOD_Player = new(DT_HL2MP_Player, [
		RecvPropInt(FIELD.OF(nameof(GModPlayerFlags))),
		RecvPropEHandle(FIELD.OF(nameof(HoveredWidget))),
		RecvPropEHandle(FIELD.OF(nameof(PressedWidget))),
		RecvPropEHandle(FIELD.OF(nameof(Driving))),
		RecvPropInt(FIELD.OF(nameof(DrivingMode))),
		RecvPropInt(FIELD.OF(nameof(PlayerClass))),
		RecvPropBool(FIELD.OF(nameof(CanZoom))),
		RecvPropBool(FIELD.OF(nameof(CanWalk))),
		RecvPropBool(FIELD.OF(nameof(IsTyping))),
		RecvPropFloat(FIELD.OF(nameof(StepSize))),
		RecvPropFloat(FIELD.OF(nameof(JumpPower))),
		RecvPropVector(FIELD.OF(nameof(ViewOffset))),
		RecvPropVector(FIELD.OF(nameof(ViewOffsetDucked))),
		RecvPropFloat(FIELD.OF(nameof(GestureEndTime))),
		RecvPropVector(FIELD.OF(nameof(PlayerColor))),
		RecvPropVector(FIELD.OF(nameof(WeaponColor))),
		RecvPropEHandle(FIELD.OF(nameof(Hands))),
		RecvPropInt(FIELD.OF(nameof(WaterLevel))),
		RecvPropInt(FIELD.OF(nameof(MaxArmor))),
		RecvPropFloat(FIELD.OF(nameof(Gravity))),
		RecvPropBool(FIELD.OF(nameof(SprintEnabled))),
	]);
	public static readonly new ClientClass ClientClass = ClientClass.New("GMOD_Player", null, null, DT_GMOD_Player)
															.WithManualClassID(StaticClassIndices.CGMOD_Player).WithAutoEntityCreateFn<C_GMOD_Player>();

	public int GModPlayerFlags;
	public readonly EHANDLE HoveredWidget = new();
	public readonly EHANDLE PressedWidget = new();
	public readonly EHANDLE Driving = new();
	public int DrivingMode;
	public int PlayerClass;
	public bool CanZoom;
	public bool CanWalk;
	public bool IsTyping;
	public float StepSize;
	public float JumpPower;
	public Vector3 ViewOffsetDucked;
	public float GestureEndTime;
	public Vector3 PlayerColor;
	public Vector3 WeaponColor;
	public readonly EHANDLE Hands = new();
	public int MaxArmor;
	public float Gravity;
	public bool SprintEnabled;
}