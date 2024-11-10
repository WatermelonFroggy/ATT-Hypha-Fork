using Alta.Api.Client.HighLevel;
using Alta.Api.DataTransferModels.Models.Responses;
using Alta.Console;
using Alta.Networking;
using Alta.Networking.Servers;
using Alta.Serialization;
using Alta.Static;
using Alta.Utilities;
using Alta.WebServer;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Hypha.Migration
{
    public class ModServerHandler : ServerHandler
    {
        public new static ModServerHandler Current { get; private set; }

        public ServerLockCollection ServerLocks { get; } = new ServerLockCollection();
        public ServerSaveUtility SaveUtility { get; private set; }
        public new SettingsFileAccess<ServerSettings> ServerConfig { get; private set; }

        public GameServerInfo ServerInfo => Hypha.ModdedServerInfo;

        public ServerConfiguration Config { get; private set; }
        public bool IsUsingConfig => Config != null;

        public int Connections => socket.ConnectionCount;
        public IServerAccess ServerApiAccess { get; private set; }

        public bool IsRunning { get; private set; }
        public bool IsBottingUp { get; private set; }
        public new bool IsDummyLocalServer { get; }

        public event Action<Player> PlayerJoined;
        public event Action<Player> PlayerLeft;

        public event Action BootupFinished;
        private event Action BootupProgressed;

        public int PlayerLimit => Hypha.ModdedServerInfo.PlayerLimit.Value;


        private static Logger logger = LogManager.GetCurrentClassLogger();

        private ModPlayerConnectionsHandler playerJoinHandler;
        private ISocket socket;

        private ServerHeartbeat heartbeat;
        private WebServerThread webServer;
        private Dictionary<Connection, PlayerMode> playerModes = new();
        private Dictionary<Connection, PlatformTarget> platformTargets = new();
        private bool isRunningLocally;
        private string status;

        private Queue<BootupStep> bootupSteps;
        private Queue<ConnectionStep> connectionSteps;
        public new delegate Task BootupStep(Action<string> progressNotification);
        public new delegate Task ConnectionStep(Connection connection, Action<string> progressNotification);


        public ModServerHandler(ISocket socket, IServerAccess serverAccess, bool isRunningLocally)
        {
            if (Current != null)
            {
                Debug.LogError("Can not have multiple servers running at once.", null);
                socket.Dispose();
                return;
            }

            Current = this;
            ServerHandler.Current = this;
            this.isRunningLocally = isRunningLocally;
            this.socket = socket;
            ServerApiAccess = serverAccess;
            serverAccess.IsActive = true;
            SaveUtility = new ServerSaveUtility(serverAccess);
            if (CommandLineArguments.Contains("/db_settings"))
            {
                ServerConfig = new SettingsFileAccess<ServerSettings>(ServerInfo.Identifier.ToString());
            }
            else
            {
                ServerConfig = new SettingsFileAccess<ServerSettings>(SaveUtility.ServerFolder, "ServerConfiguration");
            }

            ServerConfig = new SettingsFileAccess<ServerSettings>();

            CacheManager.Instance = new ServerCacheManager();
            socket.ConnectionCreated += ConnectionCreated;
            playerJoinHandler = new ModPlayerConnectionsHandler(this)
            {
                UserPrequisites = new Func<Connection, Task<bool>>(this.UserPrerequisites)
            };
            playerJoinHandler.UserJoined += UserJoined;
            playerJoinHandler.UserLeft += UserLeft;
            if (NetworkSceneManager.Current.HasSaveFiles)
            {
                ModStreamerManager.Instance.StartAutoSave();
            }
            bootupSteps = new Queue<BootupStep>();
            connectionSteps = new Queue<ConnectionStep>();
            AddBootupStep(new BootupStep(Initialize));
            Debug.UpdateLogFolderPath(SaveUtility.LogsPath);
        }

        private ModServerHandler()
        {
            Current = this;
            ServerHandler.Current = this;
            IsDummyLocalServer = false;
            ServerConfig = new SettingsFileAccess<ServerSettings>();
        }


        public void AddBootupStep(BootupStep step)
        {
            if (bootupSteps != null)
            {
                bootupSteps.Enqueue(step);
                return;
            }
            logger.Error("Too late to add bootup step. Try within an embedded entities 'InitializeAsServer'.");
        }

        public void AddConnectionStep(ConnectionStep step)
        {
            connectionSteps.Enqueue(step);
            if (Player.AllPlayers.Count > 0)
            {
                logger.Error("Adding connection step after players have joined. Some players missed this step.");
            }
        }

        public async Task Bootup()
        {
            logger.Info("Starting bootup");
            IsBottingUp = true;
            while (bootupSteps.Count > 0)
            {
                await bootupSteps.Dequeue()(SetBootupMessage);
            }
            logger.Info("Finished bootup");
            BootupFinished?.Invoke();
            IsBottingUp = false;
            bootupSteps = null;
            SetBootupMessage("Ready");
        }

        public async void CheckForNoResponseFromConnectionAsync(Connection connection)
        {
            CancellationTokenSource source = new();
            connection.Disconnected += CancelWait;
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(2.0), source.Token);
            }
            catch (TaskCanceledException)
            {
                connection.Disconnected -= CancelWait;
                logger.Info("No respond from connection: {0} check has been cancelled", connection.Identifier);
                return;
            }
            if (connection.Player == null && !source.IsCancellationRequested)
            {
                logger.Warn("Connection: {0} has not responded with a ready to spawn within 2 minutes, terminating connection", connection.Identifier);
                connection.Disconnect("Client did not respond with 'ready to spawn'");
            }
            connection.Disconnected -= CancelWait;
            void CancelWait(Connection _)
            {
                source.Cancel();
            }
        }

        public void ConnectionCreated(Connection connection)
        {
            logger.Info("Connection Created: " + connection.Identifier);
            playerJoinHandler.InitializeConnection(connection);
            connection.SetHandler(MessageType.ServerDiagnostics, new SerializeConnectionMethod(RequestServerDiagnosticsMessage.Serialize));
            connection.SetHandler(MessageType.Ping, new SerializeConnectionMethod(PingMessage.Serialize));
            connection.SetHandler(MessageType.EntityInternal, new SerializeConnectionMethod(InternalMessage.Serialize));
            connection.SetHandler(MessageType.SceneSerialize, new SerializeConnectionMethod(SceneSerializationMessage.Serialize));
            connection.SetHandler(MessageType.FileCacheRequest, delegate (Connection a, Stream b)
            {
                CacheManager.Instance.HandleCacheRequest(a, b);
            });
            connection.SetHandler(MessageType.ConnectionPauseRequest, new SerializeConnectionMethod(connection.HandleConnectionPause));
        }

        private async Task HandleConnectionsChange(Connection connection)
        {
            int count = connection.Socket.Connections.Count();
            try
            {
                await ServerApiAccess.SetOnlinePlayersAsync(from item in connection.Socket.Connections
                                                            where item.UserInfo != null
                                                            select item.UserInfo.UserInfo);
            }
            catch (Exception exception)
            {
                logger.Error(exception, "Failed setting online players list for server");
            }
            // await AgonesController.HandlePlayersCountChange(count);
        }


        public async Task Initialize(Action<string> progress)
        {
            progress("Initializing Server");
            ServerStartArguments startupArgs = new()
            {
                IsRunningLocally = false,
                Version = Hypha.LatestVersion()
            };
            if (isRunningLocally)
            {
                startupArgs.GamePort = new int?(1757);
                startupArgs.LocalAddress = ServerUtilities.GetLocalIPv4();
                if (ServerRemoteConsole.IsActive)
                {
                    startupArgs.LoggingPort = new int?(1759);
                    startupArgs.ConsolePort = new int?(1758);
                }
                if (WebSocketRemoteConsole.IsActive)
                {
                    startupArgs.WebSocketPort = new int?(1760);
                }
                startupArgs.WebServerPort = new int?(1761);
            }

            webServer = new WebServerThread(1761);
            await ServerApiAccess.StartAsync(startupArgs);
            GeneralGameEvents.OnServerStarted();

            if (CommandLineArguments.Contains("/heartbeat"))
            {
                heartbeat = new ServerHeartbeat();
                heartbeat.StartSending(ServerApiAccess.ServerInfo.Name);
            }

            if (CommandLineArguments.Contains("/wipe"))
            {
                NetworkSceneManager.Current.ServerStarted += WipeServerAsync;
            }

            IsRunning = true;
            PingServer();
            DynamicPhysicsTimeSet.Start();
        }

        public async Task OnServerStopped(ShutdownReason reason)
        {
            webServer.Dispose();
            webServer = null;
            heartbeat?.StopSending();
            GeneralGameEvents.OnServerEnded();
            try
            {
                await ServerApiAccess.StopAsync(reason);
                logger.Info("Successfully deregistered server");
            }
            catch (Exception ex)
            {
                logger.Error("Failed to deregister server: {0}", ex.Message);
            }
        }

        public async void PingServer()
        {
            await Task.Delay(15000);
            int failedCount = 0;
            while (IsRunning)
            {
                try
                {
                    await ServerApiAccess.Ping();
                    failedCount = 0;
                }
                catch (Exception exception)
                {
                    logger.Error(exception, "Encountered issues sending server ping, fail count: {0}", failedCount);
                    failedCount++;
                    int num = 10;
                    string environmentVariable = Environment.GetEnvironmentVariable("MAX_PING_FAIL_COUNT");
                    if (!string.IsNullOrEmpty(environmentVariable) && int.TryParse(environmentVariable, out var result))
                    {
                        logger.Debug("using max fail count of: {0}", result);
                        num = result;
                    }
                    if (failedCount >= num)
                    {
                        logger.Fatal("Encountered issues with sending server ping for {0} times, shutting server down", failedCount);
                        ApplicationManager.ExternalOnApplicationQuit(new ShutdownReason("Server ping issues", isExpected: false));
                    }
                }
                await Task.Delay(TimeSpan.FromSeconds(15.0));
            }
        }

        public void RemovePlayer(Player player)
        {
            logger.Debug("Player has left the server: {0}", player.UserInfo.Username);
            ConsoleEvents.PlayerLeft.Invoke(() => new PlayerJoinLeaveData(player));
            try
            {
                player.PlayerController?.OnPrePlayerLeaving();
                if (!ApplicationManager.IsQuitting && NetworkSceneManager.Current.HasSaveFiles)
                {
                    player.SaveControllerAndState();
                    player.WriteSave(isUnloading: true);
                }
                player.PlayerController?.OnPlayerLeaving();
            }
            catch (Exception exception)
            {
                logger.Error(exception, "Failed to Save player {0} on disconnect", player.UserInfo);
            }
            player.ChunkManager.HandleLeave();
            player.DestroyWithAssociatedObjects();
            PlayerLeft?.Invoke(player);
            playerModes.Remove(player.ConnectionToRemotePlayer);
            platformTargets.Remove(player.ConnectionToRemotePlayer);
        }

        public void SendPlayerOwnership(Player player)
        {
            player.SetLocal();
        }

        public void SetBootupMessage(string message)
        {
            logger.Info("Bootup status: " + message);
            status = message;
            BootupProgressed?.Invoke();
        }

        public async void SpawnPlayerToClient(Connection connection, Stream stream)
        {
            try
            {
                if (!NetworkSceneManager.SceneLoadWait.Task.IsCompleted)
                {
                    logger.Info("Waiting for scene load before spawning player: {0} to client", connection.UserInfo);
                    await NetworkSceneManager.SceneLoadWait.Task;
                }
                if (!connection.IsApproved)
                {
                    logger.Warn("Player: {0} connection disconnected while waiting for scene load", connection.UserInfo);
                    return;
                }
                logger.Info("Player: {0} signalled ready to spawn, starting player spawn process", connection.UserInfo.Username);
                INetworkSceneInternal scene = (INetworkSceneInternal)NetworkSceneManager.Current;
                scene.AddConnection(connection);
                IAltaFile saveFile = null;
                PlayerMode playerMode = playerModes[connection];
                PlatformTarget playerTarget = platformTargets[connection];
                if (PlayerModeUtilities.IsNormalPlayerMode(playerMode) && NetworkSceneManager.Current.HasSaveFiles)
                {
                    saveFile = await SaveUtility.PlayerSaveUtility.Load(connection.UserInfo.Identifier);
                }
                if (!connection.IsApproved)
                {
                    logger.Warn("Connection: {0} was disconnected before server was able to load save for player: {1}", connection.Identifier, connection.UserInfo.Username);
                    return;
                }
                logger.Info("Spawning Player Object: {0}.", connection.UserInfo.Username);
                Player player = (Player)scene.Spawner.SpawnInternal(CommonPrefabs.PlayerTemplate.Prefab.Hash);
                await player.InitializePlayerOnServer(connection, playerMode, playerTarget, saveFile);
                player.Chunk = player.Scene.GlobalChunk;
                scene.SceneSerializer.InitialSyncEntities(player);
                scene.GlobalChunk.Players.Add(player);
                SendPlayerOwnership(player);
                player.StartUp();
                ConsoleEvents.PlayerJoined.Invoke(() => new PlayerJoinLeaveData(player));
                PlayerJoined?.Invoke(player);
            }
            catch (Exception exception)
            {
                logger.Error(exception, "Error in SpawnPlayerToClient " + exception.ToString());
            }
        }

        public async Task StopAsync(ShutdownReason reason)
        {
            IsRunning = false;
            if (NetworkSceneManager.Current != null)
            {
                NetworkSceneManager.Current.ServerStarted -= WipeServerAsync;
            }
            logger.Info("Stopping Server, reason: {Reason}", reason.Reason);
            if (!ServerWipeManager.IsWiping)
            {
                ModStreamerManager.Instance?.SaveWorld(isInstant: true).Wait();
            }
            NetworkManager.CloseSocket(socket);
            await OnServerStopped(reason);
            playerJoinHandler.UserPrequisites = null;
            playerJoinHandler.UserJoined -= UserJoined;
            playerJoinHandler.UserLeft -= UserLeft;
            if (!ServerWipeManager.IsWiping)
            {
                SaveUtility.Unload();
                Current = null;
            }
        }

        public void SyncStatus(Connection connection, Stream stream) => stream.SerializeString(ref status);

        public async void UserJoined(Connection connection, PlayerMode playerMode, PlatformTarget platformTarget)
        {
            logger.Info("User joined on connection {0}: {1}", connection.Identifier, connection.UserInfo);
            logger.Info("Waiting for player to signal theyre ready to spawn");
            connection.SetHandler(MessageType.ClientIsReadyToSpawn, SpawnPlayerToClient);
            playerModes.Add(connection, playerMode);
            platformTargets.Add(connection, platformTarget);
            CheckForNoResponseFromConnectionAsync(connection);
            await HandleConnectionsChange(connection);
        }

        public async void UserLeft(Connection connection)
        {
            logger.Info("User left: " + connection.UserInfo.Username);
            Player player = connection.Player.PlayerController.NetworkPlayer;
            if (player == null)
            {
                logger.Warn("Connection without player got disconnected");
                return;
            }
            AltaCoroutine.DelayCall(async delegate
            {
                RemovePlayer(player);
                await HandleConnectionsChange(connection);
            });
        }

        public async Task<bool> UserPrerequisites(Connection connection)
        {
            TaskCompletionSource<bool> prerequisitesCheck = new();
            logger.Info("Checking user prerequisites on connection {0}: {1}", connection.Identifier, connection.UserInfo);
            if (bootupSteps != null)
            {
                logger.Info("Waiting for bootup steps for " + connection.Identifier);
                BootupProgressed += CheckProgress;
            }
            string connectionStatus;
            CheckProgress();
            connectionStatus = "";
            return await prerequisitesCheck.Task;
            async void CheckProgress()
            {
                if (bootupSteps != null)
                {
                    connection.Send(null, MessageType.ServerStatus, SyncStatus);
                }
                else
                {
                    try
                    {
                        foreach (ConnectionStep connectionStep in connectionSteps)
                        {
                            if (connection.IsDisposed)
                            {
                                break;
                            }
                            await connectionStep(connection, delegate (string value)
                            {
                                logger.Info("Connection Step for {0} - {1}", connection.Identifier, value);
                                connectionStatus = value;
                                connection.Send(null, MessageType.ServerStatus, SyncConnectionStatus);
                            });
                        }
                    }
                    catch (Exception exception)
                    {
                        logger.Error(exception, "Error during connection steps for {0}", connection.Identifier);
                        prerequisitesCheck.SetResult(result: false);
                    }
                    BootupProgressed -= CheckProgress;
                    prerequisitesCheck.TrySetResult(!connection.IsDisposed);
                }
            }
            void SyncConnectionStatus(Connection _, Stream stream)
            {
                stream.SerializeString(ref connectionStatus);
            }
        }

        public void WipeCache() => SaveUtility.CacheFolder.Delete();

        public async Task WipeServer(bool isSettingOffline)
        {
            IsRunning = false;
            await ServerWipeManager.WipeServer(isSettingOffline);
        }

        public async void WipeServerAsync()
        {
            NetworkSceneManager.Current.ServerStarted -= WipeServerAsync;
            await WipeServer(isSettingOffline: false);
        }
    }
}
