using Alta.Api.DataTransferModels.Models.Requests;
using Alta.Api.DataTransferModels.Models.Responses;
using Alta.Networking;
using HarmonyLib;
using Hypha.Core;
using MelonLoader;
using NLog;
using NLog.Internal;
using Steamworks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Hypha.Utilities
{
    // Fill in "claAppends" to add any extra parameters to the game
    [HarmonyPatch(typeof(Environment), nameof(Environment.GetCommandLineArgs))]
    public static class CLAExtender
    {
        internal static string[] claAppends = new string[0];

        public static void Postfix(ref string[] __result)
        {
            string[] newCLA = new string[__result.Length + claAppends.Length];

            for (int i = 0; i < __result.Length; i++) newCLA[i] = __result[i];
            for (int i = __result.Length; i < newCLA.Length; i++) newCLA[i] = claAppends[i - __result.Length];

            __result = newCLA;
        }
    }


    // Makes the game's usage of NLog visible in the MelonLoader console
    [HarmonyPatch(typeof(Logger))]
    public static class NLogPatches
    {
        [HarmonyPatch(nameof(Logger.WriteToTargets), new Type[] { typeof(LogEventInfo), typeof(TargetWithFilterChain) })]
        public static void Prefix(LogEventInfo logEvent, TargetWithFilterChain targetsForLevel)
        {
            LogAppropriately(logEvent);
        }

        [HarmonyPatch(nameof(Logger.WriteToTargets), new Type[] { typeof(Type), typeof(LogEventInfo), typeof(TargetWithFilterChain) })]
        public static void Prefix(Type wrapperType, LogEventInfo logEvent, TargetWithFilterChain targetsForLevel)
        {
            LogAppropriately(logEvent);
        }

        public static void LogAppropriately(LogEventInfo logEvent)
        {
            string msg = "NLOG INFO: ";
            msg += logEvent.CallerMemberName + " ";
            msg += logEvent.FormattedMessage != null ? logEvent.FormattedMessage : logEvent.Message;

            if (logEvent.Level == LogLevel.Info)
            {
                Hypha.Logger.Msg("NLOG INFO: " + logEvent.FormattedMessage);
            }

            else if (logEvent.Level == LogLevel.Error)
            {
                Hypha.Logger.Error("NLOG ERROR: " + logEvent.FormattedMessage);
            }

            else if (logEvent.Level == LogLevel.Warn)
            {
                Hypha.Logger.Warning("NLOG WARN: " + logEvent.FormattedMessage);
            }

            else if (logEvent.Level == LogLevel.Debug)
            {
                Hypha.Logger.Msg("NLOG DEBUG: " + logEvent.FormattedMessage);
            }

            else if (logEvent.Level == LogLevel.Fatal)
            {
                Hypha.Logger.Error("NLOG FATAL: " + logEvent.FormattedMessage);
            }

            else if (logEvent.Level == LogLevel.Trace)
            {
                Hypha.Logger.Warning("NLOG TRACE: " + logEvent.FormattedMessage);
            }
        }
    }


    [HarmonyPatch(typeof(GameServerInfoExtensions), nameof(GameServerInfoExtensions.JoinServerAsync))]
    public static class JoinServerFix
    {
        public static bool Prefix(GameServerInfo gameServer, ref Task<ServerJoinResult> __result)
        {
            if (gameServer is ModdedServerInfo)
            {
                GameServerInfoExtensions.logger.Debug("Joining modded server, skipping API check");

                __result = JoinResultForModdedServer(gameServer as ModdedServerInfo);

                return false;
            }

            return true;
        }

        internal static async Task<ServerJoinResult> JoinResultForModdedServer(ModdedServerInfo gameServer)
        {
            ServerJoinResult newResult = new()
            {
                IsAllowed = true,
                ConnectionInfo = new()
                {
                    Address = IPAddress.Parse(gameServer.IP),
                    GamePort = gameServer.Port
                }
            };

            return newResult;
        }
    }

    [HarmonyPatch(typeof(PrefabManager), nameof(PrefabManager.PrepareSpawnSetups))]
    public static class PrefabWarmupEvent
    {
        public static void Postfix() => Hypha.InvokePrefabWarmup();
    }
}
