﻿using Game.Shared;

using Source;
using Source.Common;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace Game.Client;

public static class ClientEntityExts
{
	public static C_BaseEntity? GetBaseEntity(this IClientUnknown? unk) => unk == null ? null : unk is C_BaseEntity cbe ? cbe : null;
}

public class ClientEntityList : BaseEntityList, IClientEntityList
{
	public IClientEntity? GetClientEntity(int entNum) {
		throw new NotImplementedException();
	}

	public IClientEntity? GetClientEntityFromHandle(BaseHandle ent) {
		throw new NotImplementedException();
	}

	public IClientNetworkable? GetClientNetworkable(int entnNum) {
		return EntityCacheInfo[entnNum].Networkable;
	}

	public IClientNetworkable? GetClientNetworkableFromHandle(BaseHandle ent) {
		throw new NotImplementedException();
	}

	public IClientUnknown? GetClientUnknownFromHandle(BaseHandle ent) {
		throw new NotImplementedException();
	}

	public int GetHighestEntityIndex() {
		return MaxUsedServerIndex;
	}

	public int GetMaxEntities() {
		throw new NotImplementedException();
	}

	public int NumberOfEntities(bool includeNonNetworkable) {
		throw new NotImplementedException();
	}

	public void SetMaxEntities(int maxEnts) {
		throw new NotImplementedException();
	}

	readonly Dictionary<IClientUnknown, LinkedListNode<PVSNotifyInfo>> PVSNotifierMap = [];
	readonly LinkedList<PVSNotifyInfo> PVSNotifyInfos = [];

	private void AddPVSNotifier(IClientUnknown? unknown) {
		if (unknown == null)
			return;

		IClientRenderable? renderable = unknown.GetClientRenderable();
		if (renderable != null) {
			IPVSNotify? notify = renderable.GetPVSNotifyInterface();
			if (notify != null) {
				LinkedListNode<PVSNotifyInfo> node = PVSNotifyInfos.AddLast(new PVSNotifyInfo());
				PVSNotifyInfo info = node.Value;
				info.Notify = notify;
				info.Renderable = renderable;
				info.InPVSStatus = 0;
				info.Node = node;

				PVSNotifierMap[unknown] = node;
			}
		}
	}

	private void RemovePVSNotifier(IClientUnknown? unknown) {
		if (unknown == null)
			return;

		IClientRenderable? renderable = unknown.GetClientRenderable();
		if (renderable != null) {
			IPVSNotify? notify = renderable.GetPVSNotifyInterface();
			if (notify != null) {
				if (!PVSNotifierMap.TryGetValue(unknown, out LinkedListNode<PVSNotifyInfo>? notifyInfo)) {
					Warning("PVS notifier not in m_PVSNotifierMap\n");
					Assert(false);
					return;
				}

				Assert(notifyInfo.Value.Notify == notify);
				Assert(notifyInfo.Value.Renderable == renderable);

				PVSNotifyInfos.Remove(notifyInfo);
				PVSNotifierMap.Remove(unknown);
				return;
			}
		}
	}

	protected override void OnAddEntity(IHandleEntity? ent, BaseHandle handle) {
		int entnum = handle.GetEntryIndex();
		ref EntityCacheInfo_t cache = ref EntityCacheInfo[entnum];

		if (entnum >= 0 && entnum < Constants.MAX_EDICTS) {
			NumServerEnts++;
			if (entnum > MaxUsedServerIndex)
				MaxUsedServerIndex = entnum;

			Assert(ent is IClientUnknown unk);
			Assert(unk.GetClientNetworkable());
			cache.Networkable = ((IClientUnknown)ent).GetClientNetworkable();
		}

		IClientUnknown? unknown = (IClientUnknown?)ent;

		AddPVSNotifier(unknown);

		C_BaseEntity? baseEntity = unknown.GetBaseEntity();
		if (baseEntity != null) {
			cache.LinkedListNode = BaseEntities.AddLast(baseEntity);

			if ((baseEntity.ObjectCaps() & EntityCapabilities.SaveNonNetworkable) != 0)
				NumClientNonNetworkable++;

			for (int i = EntityListeners.Count() - 1; i >= 0; i--)
				EntityListeners[i].OnEntityCreated(baseEntity);
		}
		else
			cache.LinkedListNode = null;
	}

	int NumServerEnts;
	int MaxServerEnts;
	int NumClientNonNetworkable;
	int MaxUsedServerIndex;

	public List<IClientEntityListener> EntityListeners = [];
	public void AddListenerEntity(IClientEntityListener listener) {

	}
	public void RemoveListenerEntity(IClientEntityListener listener) {

	}


	class PVSNotifyInfo
	{
		public IPVSNotify? Notify;
		public IClientRenderable? Renderable;
		public InPVS InPVSStatus;
		public LinkedListNode<PVSNotifyInfo>? Node;
	}

	struct EntityCacheInfo_t
	{
		public IClientNetworkable Networkable;
		public LinkedListNode<C_BaseEntity>? LinkedListNode;
	}

	InlineArrayNumEntEntries<EntityCacheInfo_t> EntityCacheInfo;
	readonly LinkedList<C_BaseEntity> BaseEntities = [];
}

public interface IClientEntityListener
{
	void OnEntityCreated(C_BaseEntity ent);
	void OnEntityDeleted(C_BaseEntity ent);
}