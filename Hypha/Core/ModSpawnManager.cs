using Alta.Networking;
using Alta.Networking.Internal;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace Hypha.Core
{
    [Obsolete("Don't use until we figure stuff out")]
    public class ModSpawnManager : SpawnManager
    {
        public bool IsServer { get; private set; }
        private Dictionary<uint, TrackedEvent<EntitySpawnedHandler>> entitySpawnedListeners = new();


        public ModSpawnManager(INetworkScene networkScene, ILinkProvider linkProvider, bool isServer) : base(networkScene, linkProvider)
        {
            NetworkScene = networkScene;
            LinkProvider = linkProvider;
            SpawnHelper.Manager = this;
            IsServer = isServer;
        }

        public override void Destroy(NetworkEntity networkEntity, bool isUnload = false)
        {
            if (!IsServer)
            {
                logger.Error("Destroy called on client. This isn't right");
                return;
            }

            networkEntity.PrepareForDestroy();
            DestroyLocal(networkEntity, isUnload);
        }

        private void CheckSpawnEvent(NetworkPrefab prefab)
        {
            if (entitySpawnedListeners.Count > 0)
            {
                CheckSpawnEvent(prefab.Entity);
                for (int i = 0; i < prefab.Embedded.Count; i++)
                {
                    CheckSpawnEvent(prefab.Embedded[i]);
                }
            }
        }

        private void CheckSpawnEvent(NetworkEntity entity)
        {
            try
            {
                if (entitySpawnedListeners.TryGetValue(entity.Identifier, out var value)) RunSpawnEvent(value, entity);
            }
            catch (Exception ex)
            {
                logger.Error(ex.InnerException ?? ex, "Error during check spawned event for entity: {0}", entity.SafeName);
            }
        }


        public override bool SpawnAsClient(uint prefabHash, uint entityIdentifier, uint[] embeddedIdentifiers, TransformSave transformSave, bool isSettingScale, out NetworkEntity entity)
        {
            NetworkEntityLink link;

            if (!IsServer)
            {
                link = LinkProvider.GetLink(entityIdentifier);
                if (!link.IsEmpty)
                {
                    if (link.IsFake)
                    {
                        logger.Error("Attempting to spawn {0} on fake entity id: {1}", prefabHash, entityIdentifier);
                        entity = null;
                        return false;
                    }
                    entity = link.NetworkEntity;
                    logger.Warn("Attempting to spawn - Using existing entity {0}. Existing hash: {1}. New Hash: {2}", entity.name, entity.Prefab.Hash, prefabHash);
                    if (entity.Prefab == null || entity.Prefab.Hash != prefabHash)
                    {
                        entity = null;
                        return false;
                    }
                    return false;
                }
                entity = SpawnLocal(prefabHash, link, embeddedIdentifiers, transformSave, isSettingScale);
                if (entity == null)
                {
                    logger.Error("Spawn Failed {0} - {1}", prefabHash, entityIdentifier);
                    return false;
                }

                CheckSpawnEvent(entity.Prefab);
                return true;
            }

            link = LinkProvider.GetLink(entityIdentifier);
            if (!link.IsEmpty)
            {
                entity = link.NetworkEntity;
                return false;
            }

            entity = null;
            return false;
        }

        public override NetworkEntity SpawnInternal(uint prefab)
        {
            if (!IsServer)
            {
                logger.Error("SpawnInternal was called on the client. This shouldn't happen");
            }

            return SpawnLocal(prefab, LinkProvider.GetNextLink(), null, null, false);
        }

        private void RunSpawnEvent(TrackedEvent<EntitySpawnedHandler> handler, NetworkEntity entity)
        {
            logger.Info("Finished waiting for " + entity.name);
            ((MonoBehaviour)NetworkScene).StartCoroutine(SpawnEvent(handler, entity));
        }

        private IEnumerator SpawnEvent(TrackedEvent<EntitySpawnedHandler> handler, NetworkEntity entity)
        {
            yield return CoroutineYields.EndOfFrame;
            handler.Invoke(entity);
            handler.Clear();
            entitySpawnedListeners.Remove(entity.Identifier);
        }

        public override bool WaitForSpawn(uint entityIdentifier, EntitySpawnedHandler handler)
        {
            if (!IsServer)
            {
                NetworkEntityLink link = base.LinkProvider.GetLink(entityIdentifier);
                if (link.IsFake)
                {
                    logger.Error("Waiting for spawn of entity link thats marked as fake: {0}", entityIdentifier);
                    return true;
                }
                if (!entitySpawnedListeners.TryGetValue(entityIdentifier, out var value))
                {
                    if (!link.IsEmpty)
                    {
                        logger.Trace("Entity {0} was already spawned for {1}", link.NetworkEntity.name, handler.Method.Name);
                        handler(link.NetworkEntity);
                        return false;
                    }
                    value = new TrackedEvent<EntitySpawnedHandler>();
                    entitySpawnedListeners.Add(entityIdentifier, value);
                }
                if (!link.IsEmpty)
                {
                    logger.Info("Finished waiting for " + link.NetworkEntity.name);
                    value += handler;
                    value.Invoke(link.NetworkEntity);
                    value.Clear();
                    entitySpawnedListeners.Remove(link.Index);
                    return false;
                }
                logger.Info("Waiting for " + entityIdentifier);
                return true;
            }

            NetworkEntity entity = LinkProvider.GetEntity(entityIdentifier);
            if (entity != null)
            {
                handler(entity);
            }

            return false;
        }

        public override void SpawnExisting(NetworkPrefab prefab)
        {
            if (!IsServer)
            {
                logger.Error("SpawnExisting was called on the client. This shouldn't happen");
            }
        }
    }
}
