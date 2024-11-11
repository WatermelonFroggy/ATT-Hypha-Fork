using Alta.Api.Client.HighLevel;
using Alta.Api.DataTransferModels.Models.Responses;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hypha.Core
{
    internal class ModdedServerAccess : IServerAccess
    {
        public GameServerInfo ServerInfo => Hypha.ServerToHost;

        public bool IsActive { get; set; }

        public int? ServerSession { get; set; }

        public Task<GameServerInfo> GetTemplateServer()
        {
            return Task.FromResult(ServerInfo);
        }

        public async Task Ping()
        {
            // Implement
            Hypha.Logger.Msg("Ping not implemented");
        }

        public async Task SetOnlinePlayersAsync(IEnumerable<UserInfo> onlinePlayers)
        {
            // Make it sync
            ServerInfo.OnlinePlayers = onlinePlayers;
        }

        public async Task StartAsync(ServerStartArguments startupArgs)
        {
            Hypha.Logger.Msg("StartAsync should not be ran on a multiplayer server!! We don't rely on Alta's servers and therefore will not launch on them");
        }

        public async Task StopAsync(ShutdownReason reason)
        {
            Hypha.Logger.Msg("StopAsync should not be ran on a multiplayer server!! We don't rely on Alta's servers and therefore will not launch on them");
        }
    }
}
