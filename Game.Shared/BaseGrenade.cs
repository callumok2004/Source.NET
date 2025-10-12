#if CLIENT_DLL || GAME_DLL

#if CLIENT_DLL
global using C_BaseGrenade = Game.Shared.BaseGrenade;
#endif

using Source.Common;

namespace Game.Shared;

using FIELD = Source.FIELD<BaseGrenade>;
public partial class BaseGrenade : BaseProjectile
{
	public static readonly
#if CLIENT_DLL
		RecvTable
#else
		SendTable
#endif
		DT_BaseGrenade = new(DT_BaseAnimating, [
#if CLIENT_DLL
			RecvPropFloat(FIELD.OF(nameof(Damage))),
			RecvPropFloat(FIELD.OF(nameof(DmgRadius))),
			RecvPropBool(FIELD.OF(nameof(IsLive))),
			RecvPropEHandle(FIELD.OF(nameof(Thrower))),
			RecvPropVector(FIELD.OF(nameof(Velocity))),
			RecvPropInt(FIELD.OF(nameof(Flags)))
#else
			SendPropFloat(FIELD.OF(nameof(Damage)), 10, PropFlags.RoundDown, 0, 256),
			SendPropFloat(FIELD.OF(nameof(DmgRadius)), 10, PropFlags.RoundDown, 0, 1024),
			SendPropBool(FIELD.OF(nameof(IsLive))),
			SendPropEHandle(FIELD.OF(nameof(Thrower))),
			SendPropVector(FIELD.OF(nameof(Velocity)), 0, PropFlags.NoScale),
			SendPropInt(FIELD.OF(nameof(Flags)), 16, PropFlags.Unsigned)
#endif
		]);
#if CLIENT_DLL
	public static readonly new ClientClass ClientClass = new ClientClass("BaseGrenade", null, null, DT_BaseGrenade).WithManualClassID(StaticClassIndices.CBaseGrenade);
#else
	public static readonly new ServerClass ServerClass = new ServerClass("BaseGrenade", DT_BaseGrenade).WithManualClassID(StaticClassIndices.CBaseGrenade);
#endif
	public float Damage;
	public float DmgRadius;
	public bool IsLive;
	public readonly EHANDLE Thrower = new();
	public int Flags;
}
#endif
