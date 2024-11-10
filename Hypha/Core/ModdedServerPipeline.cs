using Alta.Api.Client.HighLevel;
using Alta.Api.DataTransferModels.Models.Responses;
using Alta.Networking;
using Hypha.Migration;
using NLog;
using System;
using System.Threading.Tasks;

namespace Hypha.Core
{
    public static class ModdedServerPipeline
    {
        public const string ServerIdLoggingContext = "server_id";

        private static Logger logger;

        private static GameServerInfo currentServer;

        static ModdedServerPipeline()
        {
            logger = LogManager.GetCurrentClassLogger();
        }

        public static async Task<bool> AttemptToStartServer(IServerAccess server, int port, bool isRunningLocally)
        {
            if (currentServer != null)
            {
                logger.Warn("Already trying to start server.");
                return false;
            }
            currentServer = server.ServerInfo;
            await StartServerHandler(server, port, isRunningLocally);
            return true;
        }

        private static async Task StartServerHandler(IServerAccess serverAccess, int port, bool isRunningLocally)
        {
            logger.Debug("Attempting to start {0}", serverAccess.ServerInfo.Name);
            ISocket serverSocket = NetworkManager.CreateSocket(isServer: true, TransportSystem.Kcp, port);
            serverSocket.SocketDestroyed += ServerEnded;
            currentServer = serverAccess.ServerInfo;
            GlobalDiagnosticsContext.Set("server_id", currentServer.Name);
            NetworkSceneManager.IsServer = true;
            ModServerHandler serverHandler = new(serverSocket, serverAccess, isRunningLocally);
            INetworkSceneInternal scene = (INetworkSceneInternal)NetworkSceneManager.Current;
            serverHandler.AddBootupStep((Action<string> setStatus) => scene.Initialize(serverSocket, setStatus));
            await serverHandler.Bootup();
        }

        // Never used so commenting it out
        /*        private static void StartRejected(string error)
                {
                    logger.Warn("Failed to start server: {error}", error);
                    currentServer = null;
                }*/

        private static void ServerEnded(ISocket socket)
        {
            socket.SocketDestroyed -= ServerEnded;
            logger.Debug("Server ended. ServerStartupPipeline reopened.");
            currentServer = null;
            GlobalDiagnosticsContext.Remove("server_id");
            NetworkSceneManager.IsServer = false;
        }
    }
}
