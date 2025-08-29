﻿using Game.Client;
using Game.Server;

using Source.FileSystem;

using Source.Common;
using Source.Common.Commands;
using Source.Common.Engine;
using Source.Common.Filesystem;
using Source.Engine;

using Steamworks;
using Source.Common.Launcher;
using Source.SDLManager;
using Source.Common.Input;
using Nucleus.SDL3Window;
using System.Runtime.Loader;
using System.Reflection;
using System.Runtime.InteropServices;
using Source.Common.MaterialSystem;
using Source;
using Game.UI;
using Source.StdShader.Gl46;
using Microsoft.Extensions.DependencyInjection;

namespace Source.Launcher;

public class Bootloader : IDisposable
{
	ICommandLine commandLine;
	IEngineAPI? engineAPI;

	string baseDir;
	bool isEditMode;
	bool isTextMode;

	public Bootloader() {
		commandLine = new CommandLine();
		commandLine.CreateCmdLine(Environment.CommandLine);
		GetBaseDirectory(commandLine, out baseDir);
		SteamAPI.Init();
		isTextMode = commandLine.CheckParm("-textmode");
	}
	public void Boot() {
		bool needsRestart;
		do {
			engineAPI = new EngineBuilder(commandLine)
				// These assemblies have no reference to them, so they must be manually loaded.
				.WithAssembly("Source.GUI")
				.WithAssembly("Source.VTF")
				// Base file system implementation
				.WithComponent<IFileSystem, BaseFileSystem>()
				// SDL3 specific components. This provides ILauncherManager and IGraphicsProvider (the current implementation merges both classes)
				.WithComponent<SDL3_LauncherManager>()
				.WithResolvedComponent<ILauncherManager, SDL3_LauncherManager>(x => x.GetRequiredService<SDL3_LauncherManager>())
				.WithResolvedComponent<IGraphicsProvider, SDL3_LauncherManager>(x => x.GetRequiredService<SDL3_LauncherManager>())
				.WithComponent<ISystem, SDL3_System>()
				// SDL3 input system
				.WithComponent<IInputSystem, SDL3_InputSystem>()
				// Rendering abstraction
				.WithComponent<IMaterialSystem, MaterialSystem.MaterialSystem>()
				// Our game DLL's. Server/game impl, client impl, UI impl.
				.WithGameDLL<ServerGameDLL>()
				.WithClientDLL<HLClient>()
				.WithGameUIDLL<GameUI>()
				// Shaders we want to load
				.WithStdShader<StdShaderGl46>()
				// Let the engine builder take over and inject engine-specific dependencies
				.Build(dedicated: false);
			// Generate our startup information
			PreInit();
			// Run the game
			var res = engineAPI.Run();
			// If the engine requested a restart, re-loop
			needsRestart = res == IEngineAPI.Result.InitRestart || res == IEngineAPI.Result.RunRestart;
		} while (needsRestart);
	}

	static void GetBaseDirectory(ICommandLine cmdLine, out string baseDirectory) {
		baseDirectory = cmdLine.CheckParm("-basedir", out var values) ? values.FirstOrDefault() ?? AppContext.BaseDirectory : AppContext.BaseDirectory;
	}

	private void PreInit() {
		StartupInfo info = new();
		info.BaseDirectory = baseDir;
		info.InitialMod = DetermineInitialMod();
		info.InitialGame = DetermineInitialGame();
		info.TextMode = isTextMode;

		engineAPI!.SetStartupInfo(in info);
	}

	const string defaultHalfLife2GameDirectory = "hl2";

	private string DetermineInitialMod() {
		return !isEditMode ? commandLine.ParmValue("-game", defaultHalfLife2GameDirectory) : throw new NotImplementedException("No editmode support");
	}

	private string DetermineInitialGame() {
		return !isEditMode ? commandLine.ParmValue("-game", defaultHalfLife2GameDirectory) : throw new NotImplementedException("No editmode support");
	}
	public void Dispose() {
		SteamAPI.Shutdown();
	}
}

internal class Program
{
	static void Main(string[] _) {
		using (Bootloader bootloader = new())
			bootloader.Boot();
	}
}