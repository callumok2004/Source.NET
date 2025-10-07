﻿#if CLIENT_DLL || GAME_DLL
using Source.Common;
namespace Game.Shared.GarrysMod;
using FIELD = Source.FIELD<WeaponAR2>;
public class WeaponAR2 : HL2MPMachineGun
{
	public static readonly
#if CLIENT_DLL
		RecvTable
#else
		SendTable
#endif
		DT_WeaponAR2 = new(DT_HL2MPMachineGun, [
#if CLIENT_DLL

#else

#endif
		]);
#if CLIENT_DLL
	public static readonly new ClientClass ClientClass = new ClientClass("WeaponAR2", null, null, DT_WeaponAR2).WithManualClassID(StaticClassIndices.CWeaponAR2);
#else
	public static readonly new ServerClass ServerClass = new ServerClass("WeaponAR2", DT_WeaponAR2).WithManualClassID(StaticClassIndices.CWeaponAR2);
#endif
}
#endif