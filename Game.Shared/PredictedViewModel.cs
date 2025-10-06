﻿#if CLIENT_DLL
global using PredictedViewModel = Game.Client.C_PredictedViewModel;
namespace Game.Client;
#else
global using PredictedViewModel = Game.Server.PredictedViewModel;
namespace Game.Server;
#endif

using Source.Common;
using Game.Shared;

public class
#if CLIENT_DLL
	C_PredictedViewModel
#else
	PredictedViewModel
#endif

	: BaseViewModel
{
	public static readonly
#if CLIENT_DLL
		RecvTable
#else
		SendTable
#endif
		DT_PredictedViewModel = new(DT_BaseViewModel, []);
#if CLIENT_DLL
	public static readonly new ClientClass ClientClass = new ClientClass("PredictedViewModel", null, null, DT_PredictedViewModel).WithManualClassID(StaticClassIndices.CPredictedViewModel);
#else
#pragma warning disable CS0109 // Member does not hide an inherited member; new keyword is not required
	public static readonly new ServerClass ServerClass = new ServerClass("PredictedViewModel", DT_PredictedViewModel).WithManualClassID(StaticClassIndices.CPredictedViewModel);
#pragma warning restore CS0109 // Member does not hide an inherited member; new keyword is not required
#endif
}