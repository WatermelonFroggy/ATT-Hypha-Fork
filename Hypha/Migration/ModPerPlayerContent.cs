using Alta.Networking;
using Alta.Networking.Scripts.Player;
using Alta.Networking.Servers;
using Alta.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hypha.Migration
{
    public class ModPerPlayerContent<TSaveFormat> : IPerPlayerContent<TSaveFormat> where TSaveFormat : IAltaFileFormat, new()
    {
        public IPlayer TargetPlayer { get; private set; }
        public Player Player { get; private set; }
        public int PlayerIdentifier { get; private set; }

        public IAltaFile File { get; private set; }
        public TSaveFormat Content { get; private set; }
        public bool IsSavingPaused { get; private set; }


        public virtual async Task LoadAsync(int playerIdentifier, IAltaFile file, IPlayer currentPlayer = null)
        {
            PlayerIdentifier = playerIdentifier;
            File = file;
            TSaveFormat tsaveFormat = await File.ReadAsync<TSaveFormat>();
            Content = tsaveFormat;

            ModServerHandler.Current.PlayerJoined += PlayerJoined;
            if (currentPlayer == null)
            {
                foreach (Player player in Player.AllPlayers)
                {
                    if (player.UserInfo.Identifier == playerIdentifier)
                    {
                        currentPlayer = player;
                        break;
                    }
                }
            }

            Player = currentPlayer.PlayerController.NetworkPlayer;
        }

        public void Save(bool isUnloading)
        {
            if (!NetworkSceneManager.Current.HasSaveFiles) return;
            File.WriteAsync();
            if (isUnloading) File.QueueUnload();
        }

        public void PlayerJoined(Player newPlayer)
        {
            if (newPlayer.UserInfo.Identifier == PlayerIdentifier)
            {
                Player = newPlayer;
                Player.DestroyedByScene += this.LosePlayer;
            }
        }

        public void LosePlayer(NetworkEntity entity, bool isUnload)
        {
            Player = null;
        }

        public void SaveIfNoPlayer()
        {
            if (!IsSavingPaused && Player != null)
            {
                Save(false);
            }
        }

        public void PauseSaving()
        {
            IsSavingPaused = true;
        }

        public void ResumeSaving()
        {
            IsSavingPaused = false;
        }
    }
}
