using Alta.Map;
using Alta.Networking;
using Alta.Utilities;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hypha.Migration
{
    public class ModPerPlayerManager<TThis, TContent, TSaveFormat> : NetworkEntityBehaviour, IAutoSave where TThis : ModPerPlayerManager<TThis, TContent, TSaveFormat> where TContent : class, IPerPlayerContent<TSaveFormat>, new() where TSaveFormat : IAltaFileFormat, new()
    {
        public static TThis Instance { get; set; }
        public Dictionary<int, TContent> contents = new Dictionary<int, TContent>();
        public Dictionary<int, Task> getContentTasks = new Dictionary<int, Task>();
        public static Logger logger = LogManager.GetCurrentClassLogger();


        public void Awake()
        {
            if (Instance == null)
            {
                Instance = (TThis)(object)this;
                return;
            }
            if (Instance != (TThis)(object)this) Destroy(this);
        }

        public void AutoSave()
        {
            foreach (KeyValuePair<int, TContent> keyValuePair in contents)
            {
                keyValuePair.Value.Save(false);
            }
        }

        public virtual async Task<TContent> GetContentAsync(Player player)
        {
            TContent tcontent;
            if (player == null)
            {
                logger.Error("Cannot get content manager for null player.");
                tcontent = default;
            }
            else
            {
                tcontent = await GetContentAsync(player.UserInfo.Identifier, null, true);
            }
            return tcontent;
        }

        public async Task<TContent> GetContentAsync(int playerIdentifier, Player player = null, bool isCreatingNew = true)
        {
            Task task;
            if (getContentTasks.TryGetValue(playerIdentifier, out task))
            {
                await task;
            }
            TContent content;
            if (!contents.TryGetValue(playerIdentifier, out content) && isCreatingNew)
            {
                content = new TContent();
                TaskCompletionSource<bool> readTask = new TaskCompletionSource<bool>();
                getContentTasks.Add(playerIdentifier, readTask.Task);
                try
                {
                    IAltaFile altaFile = await PlayerDataFileHelper<TSaveFormat>.Instance.GetFileAsync(playerIdentifier);
                    await content.LoadAsync(playerIdentifier, altaFile, null);
                    contents[playerIdentifier] = content;
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "Failed to load content for player {0}", new object[] { playerIdentifier });
                }
                readTask.SetResult(true);
                getContentTasks.Remove(playerIdentifier);
            }
            return content;
        }


    }
}
