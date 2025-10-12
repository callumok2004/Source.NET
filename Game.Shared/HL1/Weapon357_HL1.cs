#if CLIENT_DLL || GAME_DLL
using Source.Common;
namespace Game.Shared.HL1;
using FIELD = Source.FIELD<Weapon357_HL1>;
public class Weapon357_HL1 : BaseHL1MPCombatWeapon
{
	public static readonly
#if CLIENT_DLL
		RecvTable
#else
		SendTable
#endif
		DT_Weapon357_HL1 = new(DT_BaseHL1MPCombatWeapon, [
#if CLIENT_DLL
			RecvPropFloat(FIELD.OF(nameof(InZoom)))
#else
			SendPropFloat(FIELD.OF(nameof(InZoom)), 0, PropFlags.NoScale)
#endif
		]);
#if CLIENT_DLL
	public static readonly new ClientClass ClientClass = new ClientClass("Weapon357_HL1", null, null, DT_Weapon357_HL1).WithManualClassID(StaticClassIndices.CWeapon357_HL1);
#else
	public static readonly new ServerClass ServerClass = new ServerClass("Weapon357_HL1", DT_Weapon357_HL1).WithManualClassID(StaticClassIndices.CWeapon357_HL1);
#endif
	public float InZoom;
}
#endif
