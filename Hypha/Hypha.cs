using Alta.Api.Client.HighLevel;
using Alta.Api.DataTransferModels.Models.Responses;
using Alta.Api.DataTransferModels.Models.Shared;
using Alta.Api.DataTransferModels.Utility;
using Alta.Networking;
using Alta.Utilities;
using CrossGameplayApi;
using Hypha.Core;
using MelonLoader;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using TriangleNet;
using UnityEngine;
using UnityEngine.SceneManagement;

[assembly: MelonInfo(typeof(Hypha.Hypha), "Hypha", "0.0.1", "Hypha Team", null)]

namespace Hypha
{
    public class Hypha : MelonPlugin
    {
        public bool IsServerInstance { get; private set; }
        internal static MelonLogger.Instance Logger { get; private set; }
        public static GameServerInfo ModdedServerInfo { get; internal set; }
        public static RequestJoinMessage StaticJoinMessage { get; internal set; }


        public override void OnApplicationStarted()
        {
            Logger ??= LoggerInstance;

            foreach (string parameter in Environment.GetCommandLineArgs())
            {
                if (parameter == "$ServerMode")
                {
                    IsServerInstance = true;
                    break;
                }
            }

            StaticJoinMessage = new();

            ModdedServerInfo = new GameServerInfo()
            {
                CreatedAt = DateTime.Now,
                CurrentPlayerCount = 0,
                Description = "This is your modded server! Refer to the Hypha API to learn how you can adjust your GameServerInfo to your liking.",
                FinalStatus = Alta.Api.DataTransferModels.Enums.GameServerStatus.Online,
                Identifier = -1,
                JoinType = ServerJoinType.OpenGroup,
                LastOnline = DateTime.Now,
                LastOnlinePing = DateTime.Now,
                LaunchRegion = "europe-agones",
                LastStartedVersion = "main-1.7.2.1.42203",
                Name = "A Modded Tale",
                OnlinePlayers = Array.Empty<UserInfo>(),
                OwnerType = ServerOwnerType.World,
                Playability = 0f,
                PlayerLimit = 20,
                SceneIndex = 4,
                ServerStatus = Alta.Api.DataTransferModels.Enums.GameServerStatus.Online,
                ServerType = Alta.Api.DataTransferModels.Enums.ServerType.Normal,
                Target = 1,
                TransportSystem = 1,
                Uptime = TimeSpan.MaxValue,
            };
        }

        public override void OnLateInitializeMelon()
        {
            if (IsServerInstance)
            {
                SceneManager.sceneLoaded += (scene, loadMode) =>
                {
                    if (scene.name == "Main Menu")
                    {
                        StartModdedServer(new ModdedServerAccess(), false, false, 1757, true);
                    }
                };
            }
        }

        public override void OnGUI()
        {
            if (GUILayout.Button("Start initial server"))
            {
                IServerAccess access = new ModdedServerAccess();
                LaunchNewServerInstance(access, false, 1757);
            }

            if (GUILayout.Button("Test2"))
            {
                VrMainMenu.Instance.JoinServer(ModdedServerInfo);
            }
        }

        public static async Task<bool> StartModdedServer(IServerAccess access, bool headless = true, bool externalLaunch = true, int port = 1757, bool runningLocally = true)
        {
            return await GameModeManager.StartGameModeAsync(new ModdedServerGamemode(access, headless, externalLaunch, port, runningLocally));
        }

        public static GameVersion LatestVersion()
        {
            GenericVersionParts genericVersionParts = VersionHelper.Parse(BuildVersion.CurrentVersion.ToString());
            return new GameVersion(genericVersionParts.Stream, genericVersionParts.Season, genericVersionParts.Major, genericVersionParts.Minor, genericVersionParts.ChangeSet);
        }

        internal void LaunchNewServerInstance(IServerAccess access, bool headless = false, int port = 1757)
        {
                ServerSaveUtility serverSaveUtility = new(access);
                string logPath = Path.Combine(path2: $"{DateTime.Now:yyyy-MM-dd-HH-mm-ss}" + "_headlessServer.txt", path1: serverSaveUtility.LogsPath);
                string cla = CommandLineArguments.RawCommandLine + " $ServerMode " + " /start_server " + access.ServerInfo.Identifier.ToString() + (headless ? " true " : " false") + port + " /console /access_token " + ApiAccess.ApiClient.UserCredentials.AccessToken.Write() + " /refresh_token " + ApiAccess.ApiClient.UserCredentials.RefreshToken.Write() + " /identity_token " + ApiAccess.ApiClient.UserCredentials.IdentityToken.Write() + " -logFile \"" + logPath + "\"";

                Process.Start(Environment.GetCommandLineArgs()[0], cla);
        }
    }
}
