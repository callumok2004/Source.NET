namespace Source.Common.Launcher;

public interface IOpenGLPlatform
{
	nint ContextHandle { get; }

	nint GetProcAddress(string name);
	void MakeCurrent(nint context);
	nint GetCurrentContext();
	void ClearCurrentContext();
	void DeleteContext(nint context);
	void SwapBuffers();
	void SetSyncToVerticalBlank(bool sync);
}
