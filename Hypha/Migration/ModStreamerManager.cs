using Alta.Chunks;
using Alta.Networking.Servers;
using Alta.Utilities;
using NLog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Hypha.Migration
{
    public class ModStreamerManager : MonoBehaviour
    {
        public static ModStreamerManager Instance;

        private static NLog.Logger logger = LogManager.GetCurrentClassLogger();
        private float streamingMillisecondsAllowance = 2f;
        private bool isAutoSaving;
        private float minutesBetweenAutoSaves;

        private Task current;
        private CancellationTokenSource cancelCurrent;


        public async void AutoSave()
        {
            while (isAutoSaving)
            {
                await Task.Delay((int)(minutesBetweenAutoSaves * 60f * 1000f));
                if (!ServerWipeManager.IsWiping && current == null)
                {
                    Debug.Log("Auto Saving at " + DateTime.Now.ToString(), null);
                    try
                    {
                        await SaveWorld(false);
                    }
                    catch (Exception ex)
                    {
                        StreamerManager.logger.Error(ex, "Failed Saving world");
                    }
                }
            }
        }

        public void OnDestroy() => isAutoSaving = false;

        public void OnEnable()
        {
            if (Instance == null)
            {
                Instance = this;
                return;
            }
            if (Instance != this) Destroy(this);
        }

        public async Task SaveWorld(bool isInstant)
        {
            if (NetworkSceneManager.Current.HasSaveFiles)
            {
                if (current != null)
                {
                    if (!isInstant)
                    {
                        return;
                    }
                    cancelCurrent.Cancel();
                }
                cancelCurrent = new CancellationTokenSource();
                current = SaveWorld(isInstant, cancelCurrent.Token);
                if (isInstant)
                {
                    current.Wait();
                }
                else
                {
                    await current;
                }
                current = null;
            }
        }

        public async Task SaveWorld(bool isInstant, CancellationToken cancellationToken)
        {
            StreamerManager.logger.Info("Saving World. Instant: {0}", isInstant);
            Stopwatch timer = Stopwatch.StartNew();
            Chunk[] allChunks = Chunk.ChunksByIndex.Values.ToArray<Chunk>();
            if (isInstant)
            {
                foreach (Chunk chunk in allChunks)
                {
                    StreamerManager.logger.Trace("Instant Save: {0}", chunk.ChunkIdentifier);
                    chunk.Save().Wait();
                }
            }
            else
            {
                float allowance = Mathf.Max(1f, streamingMillisecondsAllowance - Time.deltaTime * 1000f);
                Stopwatch stopwatch = new Stopwatch();
                stopwatch.Start();
                int j;
                for (int i = 0; i < allChunks.Length; i = j + 1)
                {
                    StreamerManager.logger.Trace("Save: {0}", allChunks[i].ChunkIdentifier);
                    try
                    {
                        await allChunks[i].Save();
                    }
                    catch (Exception ex)
                    {
                        StreamerManager.logger.Error(ex, "Failed saving chunk: {0}", new object[] { allChunks[i].ChunkIdentifier });
                    }
                    if (stopwatch.Elapsed.TotalMilliseconds > (double)allowance)
                    {
                        await Task.Yield();
                        if (cancellationToken.IsCancellationRequested)
                        {
                            return;
                        }
                        allowance = Mathf.Max(1f, streamingMillisecondsAllowance - Time.deltaTime * 1000f);
                        stopwatch.Restart();
                    }
                    j = i;
                }
                await Task.Yield();
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                stopwatch = null;
            }
            foreach (IAutoSave autoSave in StreamerManager.AutoSavingBehaviours)
            {
                autoSave.AutoSave();
            }
            Player.SaveAll();
            if (isInstant)
            {
                AltaFile.FinishAllWriting();
            }
            else
            {
                await AltaFile.FinishAllAsync();
            }
            if (!isInstant)
            {
                await Task.Yield();
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
            }
            double totalMilliseconds = timer.Elapsed.TotalMilliseconds;
            RemoteFiles.BackupAllChanged(isInstant);
            StreamerManager.logger.Info("World Saved over " + totalMilliseconds.ToString("F3") + "ms.");
        }

        public void Update() => ChunkPrefabPointer.LoadAndUnload(streamingMillisecondsAllowance);

        public void StartAutoSave()
        {
            if (ModServerHandler.Current.ServerConfig.Settings.IsAutoSaving)
            {
                minutesBetweenAutoSaves = 1;
                AutoSave();
            }

        }
    }
}
