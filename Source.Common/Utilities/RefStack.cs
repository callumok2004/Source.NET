﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace Source.Common.Utilities;

/// <summary>
/// A by-reference Stack<typeparamref name="T"/> implementation. Never shrinks, but can grow to infinite size
/// <br/>
/// It only never shrinks to avoid unnecessary deallocations. (and to allow references)
/// </summary>
/// <typeparam name="T"></typeparam>
public class RefStack<T> : IEnumerable<T> where T : struct
{
	List<T[]> backing = [];
	int count = 0;

	public const int FRAGMENT_SIZE = 32;

	ref T getBacking(int index) {
		int backingPtr = index / FRAGMENT_SIZE;
		int insideBackingPtr = index % FRAGMENT_SIZE;
		if (backingPtr >= backing.Count)
			backing.Add(new T[FRAGMENT_SIZE]);
		T[] backingMemory = backing[backingPtr];
		return ref backingMemory[insideBackingPtr];
	}

	public ref T Push(in T t) {
		ref T store = ref getBacking(count++);
		store = t;
		return ref store;
	}

	public ref T Push() {
		ref T store = ref getBacking(count++);
		store = new();
		return ref store;
	}

	public nint AddToTail() {
		ref T store = ref getBacking(count++);
		store = new();
		return count - 1;
	}

	public ref T Pop() {
		if (count <= 0) {
			AssertMsg(false, "stack underflow");
			return ref Unsafe.NullRef<T>();
		}

		ref T store = ref getBacking(count--);
		return ref store;
	}
	public ref T Peek() {
		if (count <= 0) {
			AssertMsg(false, "stack underflow");
			return ref Unsafe.NullRef<T>();
		}

		ref T store = ref getBacking(count);
		return ref store;
	}

	public IEnumerator<T> GetEnumerator() {
		for (int i = 0; i < count; i++) {
			T b = getBacking(i);
			yield return b;
		}
	}

	IEnumerator IEnumerable.GetEnumerator() {
		return GetEnumerator();
	}

	public void EnsureCapacity(int v) {
		while (backing.Count < ((v / FRAGMENT_SIZE) + 1))
			backing.Add(new T[FRAGMENT_SIZE]);

	}

	public ref T Top() {
		Assert(Count > 0);
		return ref getBacking(count - 1);
	}

	public int Count => count;

	public ref T this[int index] {
		get => ref getBacking(index);
	}
}