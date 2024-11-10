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
        public GameServerInfo ServerInfo => Hypha.ModdedServerInfo;

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
            ServerInfo.OnlinePlayers = onlinePlayers;
        }

        public async Task StartAsync(ServerStartArguments startupArgs)
        {
            // Implement
            Hypha.Logger.Msg("StartAsync not implemented");
        }

        public async Task StopAsync(ShutdownReason reason)
        {
            // Implement
            Hypha.Logger.Msg("StopAsync not implemented");
        }
    }
}
