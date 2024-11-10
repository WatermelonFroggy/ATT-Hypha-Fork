using Alta.Api.DataTransferModels.Models.Responses;
using Alta.Caves;
using Alta.Chunks;
using Alta.Map;
using Alta.Networking;
using Alta.Networking.Servers;
using Alta.Utilities;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace Hypha.Migration
{
    public static class ServerWipeManager
    {
        public static bool IsWiping { get; set; } = false;

        public static event Action WipeStarting;
        public static event Action WipeOperationsFinished;
        public static event Action CaveWipe;

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

        public static async Task WipeServer(bool isSettingOffline = false)
        {
            if (IsWiping)
            {
                logger.Warn("Already Wiping");
            }
            else
            {
                logger.Warn("Begining Wipe");
                ServerJoinLock serverJoinLock = new()
                {
                    message = "Server is wiping",
                    level = 10
                };
                ShutdownReason reason = new("server wipe", true);
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
                    logger.Error(ex, "Failed ending server session");
                }
                ModServerHandler.Current.ServerLocks.Add(serverJoinLock);
                IsWiping = true;
                ChunkPhysicsResolver chunkPhysicsResolver = UnityEngine.Object.FindObjectOfType<ChunkPhysicsResolver>();
                chunkPhysicsResolver?.Stop();
                hadIssues = false;
                foreach (Player player in Player.AllPlayers.Cast<Player>())
                {
                    player.Kick("WipeServer");
                }
                await WipeOperations();
                await DeleteChunksAndCache();
                await RunThroughAllPlayers(new Func<PlayerSave, Task<bool>>(SanitizePlayersSaves));
                if (hadIssues)
                {
                    logger.Fatal("FAILED TO WIPE PLEASE READ LOGS");
                    await Task.Delay(60000);
                }
                else
                {
                    logger.Warn("Wipe: wiped successfully");
                }
                IsWiping = false;
                ModServerHandler.Current.ServerLocks.Remove(serverJoinLock);
                ApplicationManager.ExternalOnApplicationQuit(reason);
            }
        }

        public static async Task FullWipe()
        {
            if (IsWiping)
            {
                logger.Warn("Already Wiping");
            }
            else
            {
                logger.Warn("Begining Wipe");
                ServerJoinLock serverJoinLock = new()
                {
                    message = "Server is wiping",
                    level = 10
                };
                ShutdownReason reason = new("server wipe", true);
                try
                {
                    await ModServerHandler.Current.ServerApiAccess.StopAsync(reason);
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "Failed ending server session");
                }
                ModServerHandler.Current.ServerLocks.Add(serverJoinLock);
                IsWiping = true;
                ChunkPhysicsResolver chunkPhysicsResolver = UnityEngine.Object.FindObjectOfType<ChunkPhysicsResolver>();
                chunkPhysicsResolver?.Stop();
                foreach (Player player in Player.AllPlayers.Cast<Player>())
                {
                    player.Kick("Server is wiping");
                }
                ModServerHandler.Current.SaveUtility.ServerFolder.Delete();
                ApplicationManager.ExternalOnApplicationQuit(reason);
            }
        }

        private static async Task FastWipeOperations()
        {
            logger.Warn("Wipe: running wipe operations");
            await WipeChunk(LocationChunkHelper.GetBestContaining(new Vector3(-882.599f, 160.6238f, 106.657f), null));
            await WipeChunk(LocationChunkHelper.GetBestContaining(new Vector3(-802.55f, 135.127f, 43.83f), null));
            ModPerPlayerManager<ModPostboxManager, PlayerPostboxStorage, PlayerPostboxStorageFileFormat>.Instance.ConcludeWipe();
        }

        public static async Task WipeCaves()
        {
            logger.Warn("Begining Cave Wipe");
            ServerJoinLock serverJoinLock = new()
            {
                message = "Wiping caves",
                level = 1
            };
            IsWiping = true;
            ModServerHandler.Current.ServerLocks.Add(serverJoinLock);
            SpawnArea spawnArea = SpawnArea.InstanceMap[SpawnAreaIdentifier.OutsideCave];
            LocationChunk bestContaining = LocationChunkHelper.GetBestContaining(spawnArea.transform.position, null);
            foreach (Player player in Player.AllPlayers.Cast<Player>())
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
            logger.Warn("Moving players out of the caves");
            await RunThroughAllPlayers(new Func<PlayerSave, Task<bool>>(MovePlayerOutOfCaves));
            logger.Warn("Clearing Caves");
            await CaveLayerManager.Instance.ClearCaveSystem();
            logger.Warn("Clearing Cave Teleporters");
            CaveWipe?.Invoke();
            IsWiping = false;
            ModServerHandler.Current.ServerLocks.Remove(serverJoinLock);
            logger.Warn("Finished Cave Wipe");
        }

        private static async Task WipeOperations()
        {
            logger.Warn("Wipe: running wipe operations");
            Chunk[] array = WipeManager.ChunksToPreWipe().ToArray<Chunk>();
            foreach (Chunk chunk in array)
            {
                if (chunk != null)
                {
                    await WipeChunk(chunk);
                }
            }
            Chunk[] array2 = null;
            WipeOperationsFinished?.Invoke();
        }

        private static async Task WipeChunk(Chunk chunk)
        {
            try
            {
                await chunk.ForceLoad();
                List<NetworkEntity> list = new(chunk.Entities.Entities);
                if (list.Count > 0)
                {
                    logger.Debug<string, int>("Wiping chunk {0} with {1} entities", chunk.ChunkIdentifier, list.Count);
                    foreach (NetworkEntity networkEntity in list)
                    {
                        IOperateOnWipe component = networkEntity.GetComponent<IOperateOnWipe>();
                        component?.WipeOperation();
                    }
                }
                if (!chunk.IsGlobal)
                {
                    chunk.ForceUnloadAndSave();
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to wipe chunk {0}", new object[] { chunk.ChunkIdentifier });
                hadIssues = true;
            }
        }

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
                logger.Error(ex, "Failed to wipe save non reserved map markers for player {0}", new object[] { save.PlayerIdentifier });
                hadIssues = true;
            }
            return true;
        }

        private static async Task<bool> MovePlayerOutOfCaves(PlayerSave save)
        {
            bool flag;
            if (!LocationChunkHelper.IsInOverworldChunk(save.Data.position))
            {
                Vector3 randomSpawnPosition = SpawnArea.InstanceMap[SpawnAreaIdentifier.OutsideCave].GetRandomSpawnPosition();
                save.Data.position = randomSpawnPosition;
                logger.Warn("Moved player {0} out of caves", save.PlayerIdentifier);
                flag = true;
            }
            else
            {
                flag = false;
            }
            return flag;
        }

        private static async Task RunThroughAllPlayers(Func<PlayerSave, Task<bool>> saveAction)
        {
            logger.Warn("Wipe: sanitizing player saves");
            foreach (IAltaFolder altaFolder in ModServerHandler.Current.SaveUtility.PlayerSaveUtility.PlayerFolder.AllSubFolders)
            {
                if (!int.TryParse(altaFolder.Name, out int playerIdentifier))
                {
                    logger.Error("Couldn't parse directory {0} as a player identifier", altaFolder.Name);
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
                        logger.Error(ex, "Failed to wipe save position for player {0}", new object[] { playerIdentifier });
                        hadIssues = true;
                    }
                }
            }
            await AltaFile.FinishAllAsync();
        }

        public static async Task DeleteChunksAndCache()
        {
            logger.Warn("Wipe: deleting saves");
            await AltaFile.FinishAllAsync();
            try
            {
                ModServerHandler.Current.SaveUtility.ChunksFolder.Delete();
                ModServerHandler.Current.SaveUtility.CacheFolder.Delete();
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to delete chunk saves!");
                hadIssues = true;
            }
        }

        public static async Task DeleteEntireSaveFolder()
        {
            logger.Warn("Wipe: deleting entire save folder for server");
            await AltaFile.FinishAllAsync();
            try
            {
                ModServerHandler.Current.SaveUtility.ServerFolder.Delete();
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to delete chunk saves!");
                hadIssues = true;
            }
        }

        private static NLog.Logger logger = LogManager.GetCurrentClassLogger();
        private static bool hadIssues = false;
    }
}
