using Alta.Inventory;
using Alta.Networking;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace Hypha.Migration
{
    public class ModPostboxManager : ModPerPlayerManager<ModPostboxManager, PlayerPostboxStorage, PlayerPostboxStorageFileFormat>
    {
        public override void InitializeAsServer(SpawnData spawnData)
        {
            base.InitializeAsServer(spawnData);
            wipeData = new Dictionary<int, TradeVendorData>();
            ServerWipeManager.WipeOperationsFinished += ConcludeWipe;
        }

        public async void ConcludeWipe()
        {
            logger.Debug("Finishing Wipe of postboxes");
            foreach (KeyValuePair<int, TradeVendorData> data in wipeData)
            {
                PlayerPostboxStorage playerPostboxStorage = await GetContentAsync(data.Key, null, true);
                logger.Debug("Pushing {0} coins and {1} items for user {2}", data.Value.CoinCount, data.Value.ItemCount, data.Key);
                playerPostboxStorage.PauseSaving();
                foreach (SerializedSavedDynamicObject serializedSavedDynamicObject in data.Value.Entities)
                {
                    playerPostboxStorage.Push(serializedSavedDynamicObject);
                }
                playerPostboxStorage.ResumeSaving(false);
            }
            wipeData.Clear();
        }

        public void TradeWipe(int playerIdentifier, int coinCount, NetworkEntity dockedItem)
        {
            TradeVendorData tradeVendorData;
            if (!wipeData.TryGetValue(playerIdentifier, out tradeVendorData))
            {
                tradeVendorData = new TradeVendorData();
                wipeData[playerIdentifier] = tradeVendorData;
            }
            tradeVendorData.AddTradeVendor(coinCount, dockedItem);
        }

        public override async Task<PlayerPostboxStorage> GetContentAsync(Player player)
        {
            PlayerPostboxStorage playerPostboxStorage = await base.GetContentAsync(player);
            playerPostboxStorage.InitializeIfNew(initialContent);
            return playerPostboxStorage;
        }

        public static async void PostToPlayer(int playerIdentifier, NetworkPrefab prefab)
        {
            SerializedSavedDynamicObject saved = SerializedSavedDynamicObject.GetSaveOf(prefab);
            prefab.Entity.SceneDestroy();
            PlayerPostboxStorage playerPostboxStorage = await Instance.GetContentAsync(playerIdentifier, null, true);
            playerPostboxStorage.Push(saved);
            playerPostboxStorage.SaveIfNoPlayer();
        }

        private void OnDestroy()
        {
            ServerWipeManager.WipeOperationsFinished -= ConcludeWipe;
        }

        private static new NLog.Logger logger = LogManager.GetCurrentClassLogger();

        private Dictionary<int, TradeVendorData> wipeData;

        [SerializeField]
        private PlayerPostboxStorage.InitialContent[] initialContent;

        public class TradeVendorData
        {
            public int ItemCount => items.Count;

            public IEnumerable<SerializedSavedDynamicObject> Entities
            {
                get
                {
                    if (CoinCount > 1)
                    {
                        NetworkEntity pouch = NetworkSceneManager.Current.Spawner.Spawn(CommonPrefabs.PouchPrefab);
                        PickupDock componentInChildren = pouch.GetComponentInChildren<PickupDock>();
                        NetworkSceneManager.Current.Spawner.Spawn(CommonPrefabs.CoinPrefab).CommonPickup.DockInto(null, componentInChildren, CoinCount, true);
                        yield return SerializedSavedDynamicObject.GetSaveOf(pouch.Prefab);
                        pouch.SceneDestroy();
                        pouch = null;
                    }
                    else if (CoinCount == 1)
                    {
                        NetworkEntity pouch = NetworkSceneManager.Current.Spawner.Spawn(CommonPrefabs.CoinPrefab);
                        yield return SerializedSavedDynamicObject.GetSaveOf(pouch.Prefab);
                        pouch.SceneDestroy();
                        pouch = null;
                    }
                    foreach (SerializedSavedDynamicObject serializedSavedDynamicObject in items)
                    {
                        yield return serializedSavedDynamicObject;
                    }
                    yield break;
                }
            }

            public int CoinCount { get; private set; }


            public void AddTradeVendor(int coinCount, NetworkEntity dockedItem)
            {
                CoinCount += coinCount;
                if (dockedItem != null)
                {
                    foreach (ICleanOnSerialize cleanOnSerialize in dockedItem.Prefab.GetComponentsInPrefab<ICleanOnSerialize>())
                    {
                        cleanOnSerialize.OnSerialize();
                    }
                    items.Add(SerializedSavedDynamicObject.GetSaveOf(dockedItem.Prefab));
                    dockedItem.SceneDestroy();
                }
            }

            private List<SerializedSavedDynamicObject> items = new List<SerializedSavedDynamicObject>();
        }
    }
}
