using Alta.Intelligence;
using Alta.Networking;
using Alta.Networking.Scripts.Player;
using Alta.Utilities;
using Hypha.Migration;
using System.Linq;
using System.Threading.Tasks;

namespace Hypha.Utilities
{
    public class ModPerPlayerContent<TSaveFormat> : PerPlayerContent<TSaveFormat> where TSaveFormat : IAltaFileFormat, new()
    {
        public override async Task LoadAsync(int playerIdentifier, IAltaFile file, IPlayer currentPlayer = null)
        {
            PlayerIdentifier = playerIdentifier;
            File = file;
            TSaveFormat tsaveFormat = await File.ReadAsync<TSaveFormat>();
            Content = tsaveFormat;

            ModServerHandler.Current.PlayerJoined += PlayerJoined;
            if (currentPlayer == null)
            {
                foreach (Player player in Player.AllPlayers.Cast<Player>())
                {
                    if (player.UserInfo.Identifier == playerIdentifier)
                    {
                        currentPlayer = player;
                        break;
                    }
                }
            }

            TargetPlayer = currentPlayer;
        }
    }
}
