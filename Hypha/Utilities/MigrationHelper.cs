using Alta.Networking;
using Hypha.Migration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using HarmonyLib;
using Alta.Networking.Servers;
using Alta.Chunks;
using Alta.Utilities;
using System.Runtime.Remoting.Messaging;

namespace Hypha.Utilities
{
    /*    [HarmonyPatch(typeof(NetworkScene), nameof(NetworkScene.Awake))]
        public static class NetworkSceneReplacer
        {
            public static bool Prefix(NetworkScene __instance)
            {
                NetworkSceneManager.Current = __instance.gameObject.AddComponent<ModNetworkScene>();
                NetworkSceneManager.CurrentInternal = (ModNetworkScene)NetworkSceneManager.Current;
                UnityEngine.Object.Destroy(__instance);

                Hypha.Logger.Msg(ConsoleColor.Blue, "Successfully swapped out NetworkScene for ModNetworkScene :)");
                return false;
            }
        }*/

    [HarmonyPatch(typeof(NetworkScene), nameof(NetworkScene.InitializeAsServer))]
    public static class NetworkManagerFiller
    {
        [HarmonyPrefix]
        public static async void FillInit(NetworkScene __instance)
        {
            if ((ModServerHandler.Current.ServerInfo.Target & 2) != 0) __instance.IsSimpleServer = true;
            __instance.Spawner = new ServerSpawnManager(__instance, __instance.entityManager);

            if (AstarPath.active == null && __instance.aStar != null) GameObject.Instantiate(__instance.aStar);

            if (__instance.GlobalChunk == null) __instance.globalChunk = __instance.transform.Find("Global Chunk").GetComponent<Chunk>();
            if (__instance.VoidChunk == null) __instance.voidChunk = __instance.transform.Find("Void Chunk").GetComponent<Chunk>();

            __instance.GlobalChunk.InitializeContentManager();
            __instance.entityManager.RegisterEmbedded(__instance.embeddedEntities);
            await __instance.GlobalChunk.ForceLoad();

            if (__instance.VoidChunk != null)
            {
                __instance.VoidChunk.InitializeContentManager();
                await __instance.VoidChunk.ForceLoad();
            }

            __instance.entityManager.InitializeAsServer(__instance.embeddedEntities);
            __instance.entityManager.ChunkEmbedded(__instance.embeddedEntities);
            
            GarbageCollectTimer garbageTimer = __instance.GetComponent<GarbageCollectTimer>();
            if (garbageTimer != null) garbageTimer.enabled = true;
            NetworkScene.sceneLogger.Info("TEMP pre server started");
            // if (__instance.ServerStarted != null) Irrelevant as nothing uses it. You should probably look into using a different kind of ServerStarted event
            NetworkScene.sceneLogger.Info("TEMP scene started");
        }
    }

    [HarmonyPatch(typeof(StreamerManager), nameof(StreamerManager.Update))]
    public static class StreamerManagerReplacer
    {
        public static bool Prefix(StreamerManager __instance)
        {
            __instance.gameObject.AddComponent<ModStreamerManager>();
            UnityEngine.Object.Destroy(__instance);

            Hypha.Logger.Msg(ConsoleColor.Blue, "Successfully swapped out StreamerManager for ModStreamerManager :)");
            return false;
        }
    }

    [HarmonyPatch(typeof(ChunkFileHelper), nameof(ChunkFileHelper.GetFolder))]
    public static class ChunkFolderFix
    {
        public static void Postfix(ref IAltaFolder __result, int playerIdentifier = 0)
        {
            IAltaFolder altaFolder;

            if (playerIdentifier == 0) altaFolder = ModServerHandler.Current.SaveUtility.ChunksFolder;
            else altaFolder = ModServerHandler.Current.SaveUtility.PlayerSaveUtility.PlayerFolder.GetSubfolder(playerIdentifier.ToString());

            __result = altaFolder;
        }
    }

    [HarmonyPatch(typeof(ServerHandler), nameof(ServerHandler.StartDummyLocalServer))]
    public static class AntiNewServerHandler
    {
        public static bool Prefix() => false;
    }
}
