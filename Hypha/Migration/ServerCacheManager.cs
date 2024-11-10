using Alta.Networking;
using Alta.Networking.Servers;
using Alta.Serialization;
using Alta.Static;
using Alta.Utilities;
using NLog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hypha.Migration
{
    public class ServerCacheManager : CacheManager
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();
        private Dictionary<string, Task> generating = new Dictionary<string, Task>();

        private Dictionary<Connection, Queue<ValueTuple<string, uint>>> requestedFiles = new Dictionary<Connection, Queue<ValueTuple<string, uint>>>();
        private Dictionary<Connection, Dictionary<string, TaskCompletionSource<bool>>> serverChecks = new Dictionary<Connection, Dictionary<string, TaskCompletionSource<bool>>>();

        public override async Task CleanCache(string name)
        {
            Task task;
            if (generating.TryGetValue(name, out task))
            {
                await task;
            }
            IAltaFile altaFile;
            if (ModServerHandler.Current.SaveUtility.CacheFolder.FileExists(name, out altaFile))
            {
                await altaFile.DeleteAsync(false);
            }
        }

        public override async Task<T> GetCache<T>(string name, Func<Task<T>> generate, Func<T> createEmpty)
        {
            Task task;
            if (generating.TryGetValue(name, out task)) await task;
            IAltaFile file;
            T t2;

            if (ModServerHandler.Current.SaveUtility.CacheFolder.FileExists(name, out file))
            {
                Stopwatch timer = Stopwatch.StartNew();
                T t = await(await file.ReadAsync<CacheFileFormat>()).ReadAsAsync<T>(createEmpty());
                file.QueueUnload();
                logger.Info(string.Format("Loading cache {0} took {1}ms", name, timer.Elapsed.TotalMilliseconds));
                t2 = t;
            }
            else
            {
                logger.Info("Cache " + name + " doesn't exist. Generating...");
                Stopwatch timer = Stopwatch.StartNew();
                Task<T> task2 = generate();
                generating.Add(name, task2);
                T t3 = await task2;
                file = ModServerHandler.Current.SaveUtility.CacheFolder.GetFile(name);
                CacheFileFormat cacheFileFormat = new CacheFileFormat();
                cacheFileFormat.Write(t3);
                file.Content = cacheFileFormat;
                file.WriteAsync();
                file.QueueUnload();
                logger.Info(string.Format("Cache {0} took {1}ms to generate.", name, timer.Elapsed.TotalMilliseconds));
                generating.Remove(name);
                t2 = t3;
            }
            return t2;
        }

        private void Disconnected(Connection connection)
        {
            connection.Disconnected -= Disconnected;
            TaskCompletionSource<bool>[] array = serverChecks[connection].Values.ToArray();
            for (int i = 0; i < array.Length; i++)
            {
                array[i].SetResult(false);
            }
            serverChecks[connection].Clear();
            serverChecks.Remove(connection);
        }

        public override void HandleCacheRequest(Connection connection, Stream stream)
        {
            string text = null;
            stream.SerializeString(ref text, Stream.StringEncoding.ASCII);
            TaskCompletionSource<bool> taskCompletionSource = serverChecks[connection][text];
            serverChecks[connection].Remove(text);
            if (serverChecks[connection].Count == 0)
            {
                connection.Disconnected -= Disconnected;
                serverChecks.Remove(connection);
            }
            bool flag = stream.SerializeCheck(true);
            logger.Info(string.Format("{0} returned cache result for {1}. Success : {2}", connection.Identifier, text, flag));
            taskCompletionSource.SetResult(flag);
        }
    }
}
