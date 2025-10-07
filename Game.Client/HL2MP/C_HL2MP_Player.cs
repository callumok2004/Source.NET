using Game.Client.HL2;
using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Commands;
using Source.Common.Mathematics;

using System.Numerics;

namespace Game.Client.HL2MP;
using FIELD = FIELD<C_HL2MP_Player>;

public partial class C_HL2MP_Player : C_BaseHLPlayer
{
	static ConVar cl_playermodel = new("none", FCvar.UserInfo | FCvar.Archive | FCvar.ServerCanExecute, "Default Player Model");
	static ConVar cl_defaultweapon = new("weapon_physcannon", FCvar.UserInfo | FCvar.Archive, "Default Spawn Weapon");


	public static readonly RecvTable DT_HL2MPLocalPlayerExclusive = new([
		RecvPropVector(FIELD.OF_NAMED(nameof(NetworkOrigin), nameof(Origin))),

		RecvPropFloat(FIELD.OF_VECTORELEM(nameof(AngEyeAngles), 0)),
		RecvPropFloat(FIELD.OF_VECTORELEM(nameof(AngEyeAngles), 1)),
	]); public static readonly ClientClass CC_HL2MPLocalPlayerExclusive = new ClientClass("HL2MPLocalPlayerExclusive", null, null, DT_HL2MPLocalPlayerExclusive);

	public static readonly RecvTable DT_HL2MPNonLocalPlayerExclusive = new([
		RecvPropVector(FIELD.OF_NAMED(nameof(NetworkOrigin), nameof(Origin))),

		RecvPropFloat(FIELD.OF_VECTORELEM(nameof(AngEyeAngles), 0)),
		RecvPropFloat(FIELD.OF_VECTORELEM(nameof(AngEyeAngles), 1)),
	]); public static readonly ClientClass CC_HL2MPNonLocalPlayerExclusive = new ClientClass("HL2MPNonLocalPlayerExclusive", null, null, DT_HL2MPNonLocalPlayerExclusive);


	public static readonly RecvTable DT_HL2MP_Player = new(DT_BasePlayer, [
		RecvPropDataTable("hl2mplocaldata", DT_HL2MPLocalPlayerExclusive),
		RecvPropDataTable("hl2mpnonlocaldata", DT_HL2MPNonLocalPlayerExclusive),
		RecvPropEHandle(FIELD.OF(nameof(Ragdoll))),
		RecvPropInt(FIELD.OF(nameof(SpawnInterpCounter))),
		RecvPropInt(FIELD.OF(nameof(PlayerSoundType))),
		RecvPropBool(FIELD.OF(nameof(IsWalking)))
	]);
	public static readonly new ClientClass ClientClass = new ClientClass("HL2MP_Player", null, null, DT_HL2MP_Player)
															.WithManualClassID(StaticClassIndices.CHL2MP_Player);

	public QAngle AngEyeAngles;
	readonly InterpolatedVar<QAngle> IV_AngEyeAngles = new(nameof(AngEyeAngles));
	public EHANDLE Ragdoll = new();
	public int SpawnInterpCounter;
	public int PlayerSoundType;
	public bool IsWalking;

	public C_HL2MP_Player() {
		AddVar(FIELD.OF(nameof(AngEyeAngles)), IV_AngEyeAngles, LatchFlags.LatchSimulationVar);
	}

	public override void PostDataUpdate(DataUpdateType updateType) {
		base.PostDataUpdate(updateType);
	}

	public override ref readonly QAngle EyeAngles() {
		return ref AngEyeAngles;
	}

	public override void CalcView(ref Vector3 eyeOrigin, ref QAngle eyeAngles, ref float zNear, ref float zFar, ref float fov) {
		if ((LifeState)LifeState != Source.LifeState.Alive) {
			
		}
		base.CalcView(ref eyeOrigin, ref eyeAngles, ref zNear, ref zFar, ref fov);
	}
}
