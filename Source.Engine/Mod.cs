﻿using Microsoft.Extensions.DependencyInjection;

using Source.Common.Engine;
using Source.Common.MaterialSystem;
using Source.Engine.Client;

namespace Source.Engine;

public class BaseMod(IServiceProvider services, EngineParms host_parms, SV SV, IMaterialSystem materials) : IMod
{
	private bool IsServerOnly(IEngineAPI api) => ((EngineAPI)api).Dedicated;

	public bool Init(string initialMod, string initialGame) {
		host_parms.Mod = initialMod;
		host_parms.Game = initialGame;

		ClientState? cl = services.GetService<ClientState>();

		if(cl != null) {
			cl.RestrictServerCommands = false;
			cl.RestrictClientCommands = false;
		}

		int width = 1600;
		int height = 900;
		bool windowed = true;

		IGame? game = services.GetService<IGame>();
		bool windowOK = game?.CreateGameWindow(width, height, windowed) ?? false;
		if (!windowOK)
			return false;

		services.GetRequiredService<IMaterialSystem>().ModInit();

		MaterialSystemConfig config = new MaterialSystemConfig();
		config.Width = width;
		config.Height = height;

		return materials.SetMode(in config);
	}

	public IMod.Result Run() {
		IMod.Result res = IMod.Result.RunOK;
		IEngine eng = services.GetRequiredService<IEngine>();
		IEngineAPI engineAPI = services.GetRequiredService<IEngineAPI>();

		if (IsServerOnly(engineAPI)) {
			if (eng.Load(true, host_parms.BaseDir)) {
				// Dedicated stuff one day?
			}
		}
		else {
			eng.SetQuitting(IEngine.Quit.NotQuitting);

			if(eng.Load(false, host_parms.BaseDir)) {
#if !SWDS
				if (engineAPI.MainLoop())
					res = IMod.Result.RunRestart;

				eng.Unload();
#endif

				SV.ShutdownGameDLL();
			}
		}

		return res;
	}

	public void Shutdown() {

	}
}
