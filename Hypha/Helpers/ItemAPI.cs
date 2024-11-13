using Alta.Api.DataTransferModels.Models.Requests;
using Alta.Networking;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace Hypha.Helpers
{
    public static class ItemAPI
    {
        public enum ItemRarity
        {
            Common = 0,
            Uncommon = 1,
            Rare = 2,
            Epic = 3,
            Legendary = 4
        }

        internal static LootValue[] foundRarities;


        internal static void Init()
        {
            foundRarities = Resources.LoadAll<LootValue>("items/lootvalue");
        }

        public static Alta.Inventory.Item CreateItem(NetworkPrefab linkedPrefab, string name, string description, ItemRarity rarity, float weight)
        {
            Alta.Inventory.Item item = ScriptableObject.CreateInstance<Alta.Inventory.Item>();

            item.name = name;
            item.lootValue = foundRarities[(int)rarity];
            item.isAssistGrabBlocked = false;
            item.description = description;
            item.destroyWhenDocked = false;
            item.Prefab = linkedPrefab;
            item.weight = weight;
            item.prefabHash = linkedPrefab.Hash;

            linkedPrefab.entity.commonPickup.item = item;

            return item;
        }
    }
}
