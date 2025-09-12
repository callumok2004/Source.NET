﻿using Microsoft.Extensions.DependencyInjection;

using Source.Common;
using Source.Common.Client;
using Source.Common.Commands;
using Source.Common.Engine;
using Source.Common.Filesystem;
using Source.Common.GameUI;
using Source.Common.MaterialSystem;
using Source.Common.Networking;
using Source.Common.Server;
using Source.Engine.Client;
using Source.Engine.Server;

using System.Reflection;

namespace Source.Engine;

/// <summary>
/// Builds a capable engine instance and provides EngineAPI to interact with it.
/// </summary>
public class EngineBuilder(ICommandLine cmdLine) : ServiceCollection
{
	public EngineBuilder MarkInterface<I, T>() where T : class, I where I : class {
		this.AddSingleton<I>(x => x.GetRequiredService<T>());
		return this;
	}

	/// <summary>
	/// Force loads an assembly.
	/// </summary>
	/// <param name="assemblyName"></param>
	/// <returns></returns>
	public EngineBuilder WithAssembly(string assemblyName)  {
		if (!assemblyName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
			assemblyName += ".dll";

		if (!Path.IsPathFullyQualified(assemblyName))
			assemblyName = Path.Combine(AppContext.BaseDirectory, assemblyName);

		Assembly.LoadFrom(assemblyName);

		return this;
	}
	public EngineBuilder WithComponent<I, T>() where T : class, I where I : class {
		PreInject<T>(this);
		this.AddSingleton<I, T>();
		return this;
	}

	public EngineBuilder WithResolvedComponent<I, T>(Func<IServiceProvider, T> resolver) where T : class, I where I : class {
		this.AddSingleton<I, T>(resolver);
		return this;
	}

	public EngineBuilder WithComponent<T>() where T : class {
		PreInject<T>(this);
		this.AddSingleton<T>();
		return this;
	}

	HashSet<Type> injectedTypelist = [];
	void PreInject<T>(IServiceCollection services) {
		if (injectedTypelist.Add(typeof(T))) {
			Type t = typeof(T);
			var preInject = t.GetMethod("DLLInit", BindingFlags.Public | BindingFlags.Static)?.CreateDelegate<PreInject>();
			if (preInject != null)
				preInject(services);
		}
	}

	public EngineBuilder WithGameUIDLL<UIDLL>() where UIDLL : class, IGameUI {
		PreInject<UIDLL>(this);
		WithComponent<IGameUI, UIDLL>();
		return this;
	}

	public EngineBuilder WithClientDLL<ClDLL>() where ClDLL : class, IBaseClientDLL {
		PreInject<ClDLL>(this);
		WithComponent<IBaseClientDLL, ClDLL>();
		return this;
	}

	public EngineBuilder WithGameDLL<SvDLL>() where SvDLL : class, IServerGameDLL {
		PreInject<SvDLL>(this);
		WithComponent<IServerGameDLL, SvDLL>();
		return this;
	}

	List<Type> Shaders = [];

	public EngineBuilder WithStdShader<StdShdrDLL>() where StdShdrDLL : class, IShaderDLL {
		PreInject<StdShdrDLL>(this);
		this.AddTransient<IShaderDLL, StdShdrDLL>();
		return this;
	}

	/// <summary>
	/// Finalizes the dependency injection setup and returns a finalized <see cref="IServiceProvider"/> (as an <see cref="EngineAPI"/>).
	/// </summary>
	/// <param name="dedicated"></param>
	/// <returns></returns>
	public EngineAPI Build(bool dedicated) {
		SetMainThread(); // Setup ThreadUtils
		// We got the ICommandLine from EngineBuilder, insert it into the app system
		this.AddSingleton(cmdLine);
		// temp
		SpewActivate(GROUP_DEVELOPER, 1);
		SpewActivate(GROUP_CONSOLE, 1);
		// Internal methods. These are class instances for better restart
		// support, and I feel like every time I try this, I end up getting
		// "static creep" where I start to revert like a primate into using
		// static singletons/god classes - if we're gonna use DI we might as
		// well go all the way with it...
		this.AddSingleton<Cbuf>();
		this.AddSingleton<CL>();
		this.AddSingleton<Cmd>();
		this.AddSingleton<Common>();
		this.AddSingleton<Con>();
		this.AddSingleton<Cvar>();
		this.AddSingleton<CvarUtilities>();
		this.AddSingleton<FileSystem>();
		this.AddSingleton<Key>();
		this.AddSingleton<Host>();
		this.AddSingleton<MatSysInterface>();
		this.AddSingleton<Net>();
		this.AddKeyedSingleton<NetworkStringTableContainer>(Realm.Client);
		this.AddKeyedSingleton<NetworkStringTableContainer>(Realm.Server);
		this.AddSingleton<Render>();
		this.AddSingleton<RenderUtils>();
		this.AddSingleton<Scr>();
		this.AddSingleton<Shader>();
		this.AddSingleton<Sound>();
		this.AddSingleton<SV>();
		this.AddSingleton<Sys>();
		this.AddSingleton<View>();
		// Engine components that we provide.
		this.AddSingleton<ICvar, Cvar>((services) => services.GetRequiredService<Cvar>());
		this.AddSingleton<ICvarQuery, DefaultCvarQuery>();
		this.AddSingleton<IHostState, HostState>();
		this.AddSingleton<CommonHostState>();
		this.AddSingleton<EngineParms>();
		this.AddSingleton<ClientDLL>();
		this.AddSingleton<IVideoMode, VideoMode_MaterialSystem>();
		this.AddSingleton<IRenderView, RenderView>();
		this.AddSingleton<IModelLoader, ModelLoader>();
		this.AddSingleton<IMod, BaseMod>();
		this.AddSingleton<IGame, Game>();
		this.AddSingleton<ModInfo>(); // This may not be valid for a while! At least until gameinfo is readable!
		// Client state and server state singletons
		this.AddSingleton<ClientState>();
		this.AddSingleton<GameServer>();
		this.AddSingleton<ClientGlobalVariables>();
		this.AddSingleton<ServerGlobalVariables>();
		// Engine VGUI and how to read it later
		this.AddSingleton<EngineVGui>();
		this.AddSingleton<IEngineVGuiInternal, EngineVGui>(x => x.GetRequiredService<EngineVGui>());
		this.AddSingleton<IEngineVGui, EngineVGui>(x => x.GetRequiredService<EngineVGui>());
		// These interfaces go to client and game dll's
		this.AddSingleton<IEngineClient, EngineClient>();
		this.AddSingleton<IEngineServer, EngineServer>();
		// We have to tell the dependency injection system how to resolve parent classes ourselves.
		this.AddSingleton<BaseClientState>(x => x.GetRequiredService<ClientState>());
		this.AddSingleton<BaseServer>(x => x.GetRequiredService<GameServer>());
		// Singleton implementations of IEngineAPI and IEngine
		this.AddSingleton<IEngineAPI, EngineAPI>();
		this.AddSingleton<IEngine, GameEngine>();

		List<Type> wantsInjection = [];
		object?[]? linkInput = [this];
		List<FieldInfo> populateLater = [];
		foreach(var assembly in ReflectionUtils.GetAssemblies()) {
			// This allows a type to define a class named SourceDllMain, with a static void Link(IServiceCollection),
			// which allows a loaded assembly to insert whatever it wants into the DI system before the provider is
			// fully built.
			Type? sourceDLL = assembly.GetTypes().FirstOrDefault(x => x.Name == "SourceDllMain");
			sourceDLL
				?.GetMethod("Link", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
				?.Invoke(null, linkInput);

			// This checks for any classes with the MarkForDependencyInjection attribute.
			// They are then injected into the service collection.
			foreach(var typeKVP in assembly.GetTypesWithAttribute<EngineComponentAttribute>()) {
				foreach(var field in typeKVP.Key.GetFields(BindingFlags.Static | BindingFlags.NonPublic)){
					if (field.GetCustomAttribute<EngineComponentReferenceAttribute>() != null)
						populateLater.Add(field);
				}

				if (typeKVP.Key.IsAbstract && typeKVP.Key.IsSealed)
					continue;

				this.AddSingleton(typeKVP.Key);
			}
		}

		// Everything else should be provided by the launcher!
		ServiceProvider provider = this.BuildServiceProvider();

		// Start using this provider for the engine
		using ServiceLocatorScope locatorScope = new(provider);

		EngineAPI api = (EngineAPI)provider.GetRequiredService<IEngineAPI>();
		foreach(var field in populateLater) {
			var attr = field.GetCustomAttribute<EngineComponentReferenceAttribute>()!;
			field.SetValue(null, attr.Key == null ? provider.GetService(field.FieldType) : provider.GetKeyedService(field.FieldType, attr.Key));
		}
		
		api.Dedicated = dedicated;
		return api;
	}
}
[AttributeUsage(AttributeTargets.Field)]
public class EngineComponentReferenceAttribute : Attribute {
	public object? Key { get; set; }
}