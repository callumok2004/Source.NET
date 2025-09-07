﻿namespace Source.Common.Utilities;

public abstract class Reference<T> where T : class
{
	protected T? reference;
	public static explicit operator T?(Reference<T> reference) => reference.reference;

	public virtual bool IsValid() => reference != null;
	public T? Get() => reference!;
}
