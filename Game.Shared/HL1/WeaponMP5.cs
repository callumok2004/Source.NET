#if CLIENT_DLL || GAME_DLL
using Source.Common;
namespace Game.Shared.HL1;
using FIELD = Source.FIELD<WeaponMP5>;
public class WeaponMP5 : BaseHL1MPCombatWeapon
{
	public static readonly
#if CLIENT_DLL
		RecvTable
#else
		SendTable
#endif
		DT_WeaponMP5 = new(DT_BaseHL1MPCombatWeapon, []);
#if CLIENT_DLL
	public static readonly new ClientClass ClientClass = new ClientClass("WeaponMP5", null, null, DT_WeaponMP5).WithManualClassID(StaticClassIndices.CWeaponMP5);
#else
	public static readonly new ServerClass ServerClass = new ServerClass("WeaponMP5", DT_WeaponMP5).WithManualClassID(StaticClassIndices.CWeaponMP5);
#endif
	public float InZoom;
}
#endif
