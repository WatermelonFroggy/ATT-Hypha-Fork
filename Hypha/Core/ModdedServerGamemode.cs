using Alta.Api.Client.HighLevel;
using Alta.Api.DataTransferModels.Models.Responses;
using Alta.Api.DataTransferModels.Utility;
using Alta.Networking.Servers;
using Alta.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using static AltaMenuItemBase.Assets.Create;

namespace Hypha.Core
{
    public class ModdedServerGamemode : GameMode
    {
        private IServerAccess server;
        private bool isHeadless;
        private int port;

        private bool isExternalLaunch;
        private bool isRunningLocally;
        public static bool IsRelaunchingTheGame { get; set; }

        public override bool IsClient => false;
        public override bool IsServer => true;

        public ModdedServerGamemode(IServerAccess server, bool isHeadless, bool isExternalLaunch = false, int port = 1757, bool isRunningLocally = true)
        {
            this.server = server;
            this.isHeadless = isHeadless;
            this.port = port;
            this.isExternalLaunch = isExternalLaunch;
            this.isRunningLocally = isRunningLocally;
        }

        public override async Task StopModeAsync(string reason)
        {
            await base.StopModeAsync(reason);
            await Migration.ModServerHandler.Current.StopAsync(new ShutdownReason(reason, isExpected: true));
        }

        public override async Task<bool> ActivateAsync()
        {
            bool result;
            if (!isExternalLaunch)
            {
                await LoadAndSwapToGameSceneAsync(server.ServerInfo.SceneIndex);
                result = ((!isHeadless) ? (await StartServer()) : (await StartHeadlessServer()));
            }
            else
            {
                logger.Info("Launching External Server");
                StartExternalServer();
                result = false;
            }
            if (!result && !ApplicationManager.IsHeadless)
            {
                await AltaSceneManager.ReturnToMainMenuAsync();
            }
            return result;
        }

        public override void OnStartSucceeded()
        {
            base.OnStartSucceeded();
            if (PlatformDriver.Instance.Mode != 0)
            {
                SingletonBehaviour<PlatformDriver>.Instance.StopActivePlatform();
            }
        }

        private async Task<bool> StartServer()
        {
            return await ModdedServerPipeline.AttemptToStartServer(server, port, isRunningLocally);
        }

        private async Task<bool> StartHeadlessServer()
        {
            SingletonBehaviour<MainMenuLogger>.Instance.Info("Starting as headless server");
            if (ApplicationManager.IsHeadless)
            {
                return await StartServer();
            }
            RestartAsHeadless();
            return true;
        }

        private void RestartAsHeadless()
        {
            if (!CommandLineArguments.Contains("/login") && !CommandLineArguments.Contains("/login_hash"))
            {
                Debug.LogError("Trying to start a headless server but your login details are not set to remember");
                return;
            }
            StartExternalServer();
            Application.Quit();
        }

        private void StartExternalServer()
        {
            ServerSaveUtility serverSaveUtility = new ServerSaveUtility(server);
            string text = Path.Combine(path2: $"{DateTime.Now:yyyy-MM-dd-HH-mm-ss}" + "_headlessServer.txt", path1: serverSaveUtility.LogsPath);
            string text2 = CommandLineArguments.RawCommandLine + " $ServerMode " + " /start_server " + server.ServerInfo.Identifier.ToString() + (isHeadless ? " true " : " false") + port + " /console /access_token " + ApiAccess.ApiClient.UserCredentials.AccessToken.Write() + " /refresh_token " + ApiAccess.ApiClient.UserCredentials.RefreshToken.Write() + " /identity_token " + ApiAccess.ApiClient.UserCredentials.IdentityToken.Write() + " -logFile \"" + text + "\"";
            logger.Debug(text2);
            Process.Start(Environment.GetCommandLineArgs()[0], text2);
        }
    }
}
