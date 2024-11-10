using Alta.Api.DataTransferModels.Models.Responses;
using Alta.Caves;
using Alta.Chunks;
using Alta.Map;
using Alta.Networking.Servers;
using Alta.Networking;
using Alta.Utilities;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace Hypha.Migration
{
    public static class ServerWipeManager
    {
        // Token: 0x17000B53 RID: 2899
        // (get) Token: 0x06003BD0 RID: 15312 RVA: 0x00033A35 File Offset: 0x00031C35
        // (set) Token: 0x06003BD1 RID: 15313 RVA: 0x00033A3C File Offset: 0x00031C3C
        public static bool IsWiping { get; set; } = false;

        // Token: 0x14000105 RID: 261
        // (add) Token: 0x06003BD2 RID: 15314 RVA: 0x00134F8C File Offset: 0x0013318C
        // (remove) Token: 0x06003BD3 RID: 15315 RVA: 0x00134FC0 File Offset: 0x001331C0
        public static event Action WipeStarting;

        // Token: 0x14000106 RID: 262
        // (add) Token: 0x06003BD4 RID: 15316 RVA: 0x00134FF4 File Offset: 0x001331F4
        // (remove) Token: 0x06003BD5 RID: 15317 RVA: 0x00135028 File Offset: 0x00133228
        public static event Action WipeOperationsFinished;

        // Token: 0x14000107 RID: 263
        // (add) Token: 0x06003BD6 RID: 15318 RVA: 0x0013505C File Offset: 0x0013325C
        // (remove) Token: 0x06003BD7 RID: 15319 RVA: 0x00135090 File Offset: 0x00133290
        public static event Action CaveWipe;

        // Token: 0x06003BD8 RID: 15320 RVA: 0x001350C4 File Offset: 0x001332C4
        private static void MigrateLockboxes()
        {
            foreach (IAltaFile altaFile in ModServerHandler.Current.SaveUtility.ChunksFolder.AllFiles.ToList<IAltaFile>())
            {
                if (altaFile.Name.StartsWith("Personal Chunk Storage "))
                {
                    string text = altaFile.Name.Replace("Personal Chunk Storage ", "").Replace("_backup", "").Split(new char[] { '.' })[0];
                    IAltaFolder subfolder = ModServerHandler.Current.SaveUtility.PlayerSaveUtility.PlayerFolder.GetSubfolder(text);
                    altaFile.MoveTo(subfolder);
                    ModServerHandler.Current.SaveUtility.ChunksFolder.RemoveFile(altaFile);
                }
            }
        }

        // Token: 0x06003BD9 RID: 15321 RVA: 0x001351AC File Offset: 0x001333AC
        public static async Task WipeServer(bool isSettingOffline = false)
        {
            if (ServerWipeManager.IsWiping)
            {
                ServerWipeManager.logger.Warn("Already Wiping");
            }
            else
            {
                ServerWipeManager.logger.Warn("Begining Wipe");
                ServerJoinLock serverJoinLock = new ServerJoinLock
                {
                    message = "Server is wiping",
                    level = 10
                };
                ShutdownReason reason = new ShutdownReason("server wipe", true);
                try
                {
                    await ModServerHandler.Current.ServerApiAccess.StopAsync(reason);
                    if (isSettingOffline)
                    {
                        await ApiAccess.ApiClient.ServerClient.SetServerAsOfflineAsync(ModServerHandler.Current.ServerInfo.Identifier, false);
                    }
                }
                catch (Exception ex)
                {
                    ServerWipeManager.logger.Error(ex, "Failed ending server session");
                }
                ModServerHandler.Current.ServerLocks.Add(serverJoinLock);
                ServerWipeManager.IsWiping = true;
                ChunkPhysicsResolver chunkPhysicsResolver = UnityEngine.Object.FindObjectOfType<ChunkPhysicsResolver>();
                if (chunkPhysicsResolver != null)
                {
                    chunkPhysicsResolver.Stop();
                }
                ServerWipeManager.hadIssues = false;
                foreach (Player player in Player.AllPlayers)
                {
                    player.Kick("WipeServer");
                }
                await ServerWipeManager.WipeOperations();
                await ServerWipeManager.DeleteChunksAndCache();
                await ServerWipeManager.RunThroughAllPlayers(new Func<PlayerSave, Task<bool>>(ServerWipeManager.SanitizePlayersSaves));
                if (ServerWipeManager.hadIssues)
                {
                    ServerWipeManager.logger.Fatal("FAILED TO WIPE PLEASE READ LOGS");
                    await Task.Delay(60000);
                }
                else
                {
                    ServerWipeManager.logger.Warn("Wipe: wiped successfully");
                }
                ServerWipeManager.IsWiping = false;
                ModServerHandler.Current.ServerLocks.Remove(serverJoinLock);
                ApplicationManager.ExternalOnApplicationQuit(reason);
            }
        }

        // Token: 0x06003BDA RID: 15322 RVA: 0x001351F4 File Offset: 0x001333F4
        public static async Task FullWipe()
        {
            if (ServerWipeManager.IsWiping)
            {
                ServerWipeManager.logger.Warn("Already Wiping");
            }
            else
            {
                ServerWipeManager.logger.Warn("Begining Wipe");
                ServerJoinLock serverJoinLock = new ServerJoinLock
                {
                    message = "Server is wiping",
                    level = 10
                };
                ShutdownReason reason = new ShutdownReason("server wipe", true);
                try
                {
                    await ModServerHandler.Current.ServerApiAccess.StopAsync(reason);
                }
                catch (Exception ex)
                {
                    ServerWipeManager.logger.Error(ex, "Failed ending server session");
                }
                ModServerHandler.Current.ServerLocks.Add(serverJoinLock);
                ServerWipeManager.IsWiping = true;
                ChunkPhysicsResolver chunkPhysicsResolver = UnityEngine.Object.FindObjectOfType<ChunkPhysicsResolver>();
                if (chunkPhysicsResolver != null)
                {
                    chunkPhysicsResolver.Stop();
                }
                foreach (Player player in Player.AllPlayers)
                {
                    player.Kick("Server is wiping");
                }
                ModServerHandler.Current.SaveUtility.ServerFolder.Delete();
                ApplicationManager.ExternalOnApplicationQuit(reason);
            }
        }

        // Token: 0x06003BDB RID: 15323 RVA: 0x00135234 File Offset: 0x00133434
        private static async Task FastWipeOperations()
        {
            ServerWipeManager.logger.Warn("Wipe: running wipe operations");
            await ServerWipeManager.WipeChunk(LocationChunkHelper.GetBestContaining(new Vector3(-882.599f, 160.6238f, 106.657f), null));
            await ServerWipeManager.WipeChunk(LocationChunkHelper.GetBestContaining(new Vector3(-802.55f, 135.127f, 43.83f), null));
            ModPerPlayerManager<ModPostboxManager, PlayerPostboxStorage, PlayerPostboxStorageFileFormat>.Instance.ConcludeWipe();
        }

        // Token: 0x06003BDC RID: 15324 RVA: 0x00135274 File Offset: 0x00133474
        public static async Task WipeCaves()
        {
            ServerWipeManager.logger.Warn("Begining Cave Wipe");
            ServerJoinLock serverJoinLock = new ServerJoinLock
            {
                message = "Wiping caves",
                level = 1
            };
            ServerWipeManager.IsWiping = true;
            ModServerHandler.Current.ServerLocks.Add(serverJoinLock);
            SpawnArea spawnArea = SpawnArea.InstanceMap[SpawnAreaIdentifier.OutsideCave];
            LocationChunk bestContaining = LocationChunkHelper.GetBestContaining(spawnArea.transform.position, null);
            foreach (Player player in Player.AllPlayers)
            {
                if (player.PlayerController != null)
                {
                    Vector3 vector = player.PlayerController.PlayerFeetPosition;
                    if (!LocationChunkHelper.IsInOverworldChunk(vector))
                    {
                        vector = spawnArea.GetRandomSpawnPosition();
                        player.SafeMoveToChunk(bestContaining, vector, null);
                    }
                }
            }
            ServerWipeManager.logger.Warn("Moving players out of the caves");
            await ServerWipeManager.RunThroughAllPlayers(new Func<PlayerSave, Task<bool>>(ServerWipeManager.MovePlayerOutOfCaves));
            ServerWipeManager.logger.Warn("Clearing Caves");
            await CaveLayerManager.Instance.ClearCaveSystem();
            ServerWipeManager.logger.Warn("Clearing Cave Teleporters");
            Action caveWipe = ServerWipeManager.CaveWipe;
            if (caveWipe != null)
            {
                caveWipe();
            }
            ServerWipeManager.IsWiping = false;
            ModServerHandler.Current.ServerLocks.Remove(serverJoinLock);
            ServerWipeManager.logger.Warn("Finished Cave Wipe");
        }

        // Token: 0x06003BDD RID: 15325 RVA: 0x001352B4 File Offset: 0x001334B4
        private static async Task WipeOperations()
        {
            ServerWipeManager.logger.Warn("Wipe: running wipe operations");
            Chunk[] array = WipeManager.ChunksToPreWipe().ToArray<Chunk>();
            foreach (Chunk chunk in array)
            {
                if (chunk != null)
                {
                    await ServerWipeManager.WipeChunk(chunk);
                }
            }
            Chunk[] array2 = null;
            Action wipeOperationsFinished = ServerWipeManager.WipeOperationsFinished;
            if (wipeOperationsFinished != null)
            {
                wipeOperationsFinished();
            }
        }

        // Token: 0x06003BDE RID: 15326 RVA: 0x001352F4 File Offset: 0x001334F4
        private static async Task WipeChunk(Chunk chunk)
        {
            try
            {
                await chunk.ForceLoad();
                List<NetworkEntity> list = new List<NetworkEntity>(chunk.Entities.Entities);
                if (list.Count > 0)
                {
                    ServerWipeManager.logger.Debug<string, int>("Wiping chunk {0} with {1} entities", chunk.ChunkIdentifier, list.Count);
                    foreach (NetworkEntity networkEntity in list)
                    {
                        IOperateOnWipe component = networkEntity.GetComponent<IOperateOnWipe>();
                        if (component != null)
                        {
                            component.WipeOperation();
                        }
                    }
                }
                if (!chunk.IsGlobal)
                {
                    chunk.ForceUnloadAndSave();
                }
            }
            catch (Exception ex)
            {
                ServerWipeManager.logger.Error(ex, "Failed to wipe chunk {0}", new object[] { chunk.ChunkIdentifier });
                ServerWipeManager.hadIssues = true;
            }
        }

        // Token: 0x06003BDF RID: 15327 RVA: 0x0013533C File Offset: 0x0013353C
        private static async Task<bool> SanitizePlayersSaves(PlayerSave save)
        {
            Vector3 randomSpawnPosition = NetworkSceneManager.Current.RespawnPoint.GetRandomSpawnPosition();
            save.Data.position = randomSpawnPosition;
            save.Data.home = Vector3.zero;
            try
            {
                await SingletonBehaviour<LandmarkManager>.Instance.WipePlayerSave(save.PlayerIdentifier);
            }
            catch (Exception ex)
            {
                ServerWipeManager.logger.Error(ex, "Failed to wipe save non reserved map markers for player {0}", new object[] { save.PlayerIdentifier });
                ServerWipeManager.hadIssues = true;
            }
            return true;
        }

        // Token: 0x06003BE0 RID: 15328 RVA: 0x00135384 File Offset: 0x00133584
        private static async Task<bool> MovePlayerOutOfCaves(PlayerSave save)
        {
            bool flag;
            if (!LocationChunkHelper.IsInOverworldChunk(save.Data.position))
            {
                Vector3 randomSpawnPosition = SpawnArea.InstanceMap[SpawnAreaIdentifier.OutsideCave].GetRandomSpawnPosition();
                save.Data.position = randomSpawnPosition;
                ServerWipeManager.logger.Warn("Moved player {0} out of caves", save.PlayerIdentifier);
                flag = true;
            }
            else
            {
                flag = false;
            }
            return flag;
        }

        // Token: 0x06003BE1 RID: 15329 RVA: 0x001353CC File Offset: 0x001335CC
        private static async Task RunThroughAllPlayers(Func<PlayerSave, Task<bool>> saveAction)
        {
            ServerWipeManager.logger.Warn("Wipe: sanitizing player saves");
            foreach (IAltaFolder altaFolder in ModServerHandler.Current.SaveUtility.PlayerSaveUtility.PlayerFolder.AllSubFolders)
            {
                int playerIdentifier;
                if (!int.TryParse(altaFolder.Name, out playerIdentifier))
                {
                    ServerWipeManager.logger.Error("Couldn't parse directory {0} as a player identifier", altaFolder.Name);
                }
                else
                {
                    try
                    {
                        IAltaFile altaFile = await ModServerHandler.Current.SaveUtility.PlayerSaveUtility.Load(playerIdentifier);
                        IAltaFile file = altaFile;
                        if (file.IsExisting)
                        {
                            PlayerSave playerSave = file.Content as PlayerSave;
                            bool idk = await saveAction(playerSave);

                            if (idk)
                            {
                                file.WriteAsync();
                            }
                            file = null;
                        }
                    }
                    catch (Exception ex)
                    {
                        ServerWipeManager.logger.Error(ex, "Failed to wipe save position for player {0}", new object[] { playerIdentifier });
                        ServerWipeManager.hadIssues = true;
                    }
                }
            }
            IEnumerator<IAltaFolder> enumerator = null;
            await AltaFile.FinishAllAsync();
        }

        // Token: 0x06003BE2 RID: 15330 RVA: 0x00135414 File Offset: 0x00133614
        public static async Task DeleteChunksAndCache()
        {
            ServerWipeManager.logger.Warn("Wipe: deleting saves");
            await AltaFile.FinishAllAsync();
            try
            {
                ModServerHandler.Current.SaveUtility.ChunksFolder.Delete();
                ModServerHandler.Current.SaveUtility.CacheFolder.Delete();
            }
            catch (Exception ex)
            {
                ServerWipeManager.logger.Error(ex, "Failed to delete chunk saves!");
                ServerWipeManager.hadIssues = true;
            }
        }

        // Token: 0x06003BE3 RID: 15331 RVA: 0x00135454 File Offset: 0x00133654
        public static async Task DeleteEntireSaveFolder()
        {
            ServerWipeManager.logger.Warn("Wipe: deleting entire save folder for server");
            await AltaFile.FinishAllAsync();
            try
            {
                ModServerHandler.Current.SaveUtility.ServerFolder.Delete();
            }
            catch (Exception ex)
            {
                ServerWipeManager.logger.Error(ex, "Failed to delete chunk saves!");
                ServerWipeManager.hadIssues = true;
            }
        }

        // Token: 0x04002E2E RID: 11822
        private static NLog.Logger logger = LogManager.GetCurrentClassLogger();

        // Token: 0x04002E33 RID: 11827
        private static bool hadIssues = false;
    }
}
