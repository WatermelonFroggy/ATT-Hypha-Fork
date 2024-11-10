using Alta.Api.DataTransferModels.Models.Responses;
using Alta.Api.DataTransferModels.Models.Shared;
using Alta.Api.DataTransferModels.Utility;
using Alta.Networking.Servers;
using Alta.Networking;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Alta.Serialization;
using Alta.Api.DataTransferModels.Converters;
using Alta.Utilities;
using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Alta.Networking.Scripts.Player;

namespace Hypha.Migration
{
    public class ModPlayerConnectionsHandler
    {
        protected struct PlayerJoinResult
        {
            public UserInfoAndRole UserInfo { get; set; }
            public string Error { get; private set; }
            public bool IsSuccessful { get; private set; }

            private PlayerJoinResult(UserInfoAndRole userInfo)
            {
                this = default(PlayerJoinResult);
                UserInfo = userInfo;
            }

            private PlayerJoinResult(string error)
            {
                this = default(PlayerJoinResult);
                Error = error;
            }

            public static PlayerJoinResult CreateSuccessResult(UserInfoAndRole userInfo)
            {
                PlayerJoinResult result = new PlayerJoinResult(userInfo);
                result.IsSuccessful = true;
                return result;
            }

            public static PlayerJoinResult CreateDeniedResult(string error)
            {
                PlayerJoinResult result = new PlayerJoinResult(error);
                result.IsSuccessful = false;
                return result;
            }
        }

        public struct ApiServerJoinResult
        {
            public string ErrorMessage { get; set; }

            public bool IsValid { get; set; }
        }

        private static Logger logger = LogManager.GetCurrentClassLogger();

        public Func<Connection, Task<bool>> UserPrequisites;

        private ModServerHandler server;

        public event Action<Connection, PlayerMode, PlatformTarget> UserJoined;

        public event Action<Connection> UserLeft;

        public ModPlayerConnectionsHandler(ModServerHandler server)
        {
            this.server = server;
        }

        public void InitializeConnection(Connection connection)
        {
            connection.SetHandler(MessageType.RequestJoin, CheckApproved);
        }

        private async void CheckApproved(Connection connection, Stream stream)
        {
            try
            {
                Hypha.StaticJoinMessage.Serialize(connection, stream);
            }
            catch (Exception exception)
            {
                logger.Error(exception, "Failed serializing join message");
                await PlayerDenied(connection, "Failed reading join message, please update your game");
                return;
            }
            int playerId = Hypha.StaticJoinMessage.PlayerId;
            logger.Info("Received Join request from player: {0}", playerId);
            PlayerJoinResult playerJoinResult = default(PlayerJoinResult);
            if (server.ServerLocks.Count > 0)
            {
                ServerJoinLock serverJoinLock = default(ServerJoinLock);
                foreach (ServerJoinLock serverLock in server.ServerLocks)
                {
                    if (serverLock.level > serverJoinLock.level)
                    {
                        serverJoinLock = serverLock;
                    }
                }
                playerJoinResult = PlayerJoinResult.CreateDeniedResult(serverJoinLock.Message + ", check Discord");
                logger.Error("PlayerId: {0} tried to join during a wipe", playerId);
                connection.Send(null, MessageType.RequestJoinResponse, new ConfirmJoinMessage(isAllowed: false, isDoingPrerequisites: false, null, playerJoinResult.Error).Serialize);
                connection.FlushPacketManagers();
                await Task.Delay(2000);
                connection.Socket.DestroyConnection(connection, isFlushing: true);
                return;
            }
            PlayerMode playerMode = Hypha.StaticJoinMessage.PlayerMode;
            PlatformTarget platformTarget = Hypha.StaticJoinMessage.PlatformTarget;
            if (server.ServerInfo.Identifier != -1)
            {
                playerJoinResult = await CheckIfPlayerIsAllowed(connection, playerId, Hypha.StaticJoinMessage.UserCredentials, Hypha.StaticJoinMessage.Version, playerMode, platformTarget);
            }
            else
            {
                playerJoinResult.UserInfo = new UserInfoAndRole(new UserInfo(playerId, ApiAccess.ApiClient.UserClient.LoggedInUserInfo.Username), UserRolesUtility.GetRolesFromIdentityToken(ApiAccess.ApiClient.UserCredentials.IdentityToken));
            }
            if (playerJoinResult.Error == null)
            {
                connection.UserInfo = playerJoinResult.UserInfo;
                if (connection.Socket.Connections.Any((Connection item) => item.UserInfo != null && item.UserInfo.Identifier == playerId && item != connection))
                {
                    logger.Error("User: {0} is trying to connect multiple times", playerId);
                    await PlayerDenied(connection, "Already connected to this server.");
                    return;
                }
                logger.Info("Request accepted, doing prerequisites. UserId: {0}", playerId);
                connection.Send(null, MessageType.RequestJoinResponse, new ConfirmJoinMessage(isAllowed: true, isDoingPrerequisites: true, new JoinedServerInfo(server.ServerInfo)).Serialize);
                if (!(await UserPrequisites(connection)))
                {
                    await PlayerDenied(connection, "Failed join prerequisites.");
                    return;
                }
                logger.Info("Sending request response, UserId: {0}", playerId);
                connection.Send(null, MessageType.RequestJoinResponse, new ConfirmJoinMessage(isAllowed: true, isDoingPrerequisites: false).Serialize);
                PlayerAccepted(connection, playerMode, platformTarget);
            }
            else
            {
                logger.Error("Error: {0} , PlayerId: {1}, Credentials: {2}", playerJoinResult.Error, playerId, Hypha.StaticJoinMessage.UserCredentials);
                await PlayerDenied(connection, playerJoinResult.Error);
            }
        }

        private static async Task PlayerDenied(Connection connection, string errorMessage)
        {
            connection.Send(null, MessageType.RequestJoinResponse, new ConfirmJoinMessage(isAllowed: false, isDoingPrerequisites: false, null, errorMessage).Serialize);
            connection.FlushPacketManagers();
            await Task.Delay(2000);
            connection.Socket.DestroyConnection(connection, isFlushing: true);
        }

        private bool CheckConnectionLimit(Connection connection)
        {
            if (ModServerHandler.Current.PlayerLimit <= 0)
            {
                return true;
            }
            return Socket.Current.ConnectionCount <= ModServerHandler.Current.PlayerLimit;
        }

        protected virtual async Task<PlayerJoinResult> CheckIfPlayerIsAllowed(Connection connection, int playerId, string tokenString, string clientVersion, PlayerMode playerMode, PlatformTarget platformTarget)
        {
            try
            {
                Stopwatch timer = Stopwatch.StartNew();
                JwtSecurityToken jwtSecurityToken = JWTUtility.CreateFromString(tokenString, includeRawData: true);
                int num = int.Parse(jwtSecurityToken.Claims.FirstOrDefault((Claim claim) => claim.Type == "UserId").Value);
                string value = jwtSecurityToken.Claims.FirstOrDefault((Claim claim) => claim.Type == "Username").Value;
                if (num != playerId)
                {
                    return PlayerJoinResult.CreateDeniedResult("Token was for a different user: " + num);
                }
                UserInfoAndRole userInfoAndRole = new UserInfoAndRole(new UserInfo(num, value), UserRolesUtility.GetRolesFromIdentityToken(tokenString));
                if (jwtSecurityToken.Claims.Any((Claim claim) => claim.Type == "Policy" && claim.Value == "dev"))
                {
                    logger.Warn("Skipping allowed check for dev join token");
                    if (!(await ApiAccess.ApiClient.ServicesClient.IsValidShortLivedIdentityTokenAsync(jwtSecurityToken)) && !(await ApiAccess.ApiClient.SecurityClient.IsValidIdentityTokenAsync(tokenString)))
                    {
                        return PlayerJoinResult.CreateDeniedResult("Dev Join Token was invalid or expired");
                    }
                }
                else
                {
                    if (((uint)ModServerHandler.Current.ServerInfo.Target & (uint)platformTarget) == 0)
                    {
                        return PlayerJoinResult.CreateDeniedResult("Server is not allowing connections of target: " + platformTarget);
                    }
                    if (!CheckConnectionLimit(connection))
                    {
                        return PlayerJoinResult.CreateDeniedResult("Reached player limit of: " + ModServerHandler.Current.PlayerLimit);
                    }
                    if (BuildVersion.CurrentVersion.ToString() != clientVersion)
                    {
                        logger.Error("User: {0} is joining with version: {1} server-version: {2}", playerId, Hypha.StaticJoinMessage.Version, BuildVersion.CurrentVersion.ToString());
                        return PlayerJoinResult.CreateDeniedResult("Wrong game version, update to: " + BuildVersion.CurrentVersion.ToString());
                    }
                    Claim claim2 = jwtSecurityToken.Claims.FirstOrDefault((Claim claim) => claim.Type == "server_id");
                    if (claim2 == null)
                    {
                        return PlayerJoinResult.CreateDeniedResult("User provided invalid token");
                    }
                    int num2 = int.Parse(claim2.Value);
                    if (num2 != ModServerHandler.Current.ServerInfo.Identifier)
                    {
                        return PlayerJoinResult.CreateDeniedResult("Token was for a different server: " + num2);
                    }
                    if (!(await ApiAccess.ApiClient.ServicesClient.IsValidShortLivedIdentityTokenAsync(jwtSecurityToken)))
                    {
                        return PlayerJoinResult.CreateDeniedResult("Token was invalid or expired");
                    }
                    if (server.IsUsingConfig && !server.Config.IsPlayerAllowed(playerId))
                    {
                        return PlayerJoinResult.CreateDeniedResult($"Request rejected due to server {server.Config.ListType}");
                    }
                    if (Player.AllPlayers.Any((IPlayer player) => player.UserInfo.Identifier == playerId))
                    {
                        return PlayerJoinResult.CreateDeniedResult($"Player: {playerId} is already on the server");
                    }
                    logger.Debug("UserStatus: {0}", userInfoAndRole.MemberStatus);
                    if (playerMode != PlayerMode.Oculus && playerMode != PlayerMode.OpenVR && userInfoAndRole.MemberStatus < PlayerSubscriberStatus.Developer)
                    {
                        return PlayerJoinResult.CreateDeniedResult("You will need a VR headset to play");
                    }
                }
                logger.Info("Checking player join took: {0} ms", timer.Elapsed.TotalMilliseconds);
                return PlayerJoinResult.CreateSuccessResult(userInfoAndRole);
            }
            catch (Exception exception)
            {
                logger.Error(exception, "Error when checking if player: {0} is allowed on server", playerId);
                return PlayerJoinResult.CreateDeniedResult("Error checking user");
            }
        }

        public static async Task<bool> ValidateConsoleToken(JwtSecurityToken token)
        {
            Claim claim2 = token.Claims.FirstOrDefault((Claim claim) => claim.Type == "server_id");
            if (claim2 != null)
            {
                int num = int.Parse(claim2.Value);
                if (num == ModServerHandler.Current.ServerInfo.Identifier)
                {
                    return await ApiAccess.ApiClient.ServicesClient.IsValidShortLivedIdentityTokenAsync(token);
                }
                logger.Error("Token was for a different server: {0}", num);
                return false;
            }
            logger.Error("Token didnt have a server_id claim");
            return false;
        }

        private void PlayerAccepted(Connection connection, PlayerMode playerMode, PlatformTarget playerSystem)
        {
            connection.Disconnected += ConnectionRemoved;
            this.UserJoined?.Invoke(connection, playerMode, playerSystem);
        }

        private async void ConnectionRemoved(Connection connection)
        {
            await OnUserLeft(connection);
            this.UserLeft?.Invoke(connection);
        }

        protected virtual async Task OnUserLeft(Connection connection)
        {
            try
            {
                logger.Info("Disconnecting user {0} from server {1}", connection.UserInfo.Username, server.ServerInfo.Name);
            }
            catch (Exception exception)
            {
                logger.Error(exception, "Error removing player from server");
            }
        }
    }
}
