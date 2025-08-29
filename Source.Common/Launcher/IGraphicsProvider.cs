﻿using Source.Common.ShaderAPI;

namespace Source.Common.Launcher;

public interface IGraphicsProvider {
	bool PrepareContext(GraphicsDriver driver);
	IGraphicsContext? CreateContext(in ShaderDeviceInfo driver, IWindow window = null);

	unsafe delegate* unmanaged[Cdecl]<byte*, void*> GL_LoadExtensionsPtr();
}
