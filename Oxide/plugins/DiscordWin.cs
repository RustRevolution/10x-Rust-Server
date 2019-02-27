using Oxide.Core.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using Oxide.Core;

namespace Oxide.Plugins
{
    [Info("DiscordWin", "Visagalis", "1.1.0")]
    [Description("Starts event to win some stuff via Discord.")]
    class DiscordWin : RustPlugin
    {
        [PluginReference]
        Plugin DiscordMessages;
        [PluginReference] 
        Plugin ServerRewards;
        [PluginReference] 
        Plugin Economics;

        string secretCode = null;
        int reward = 0;

        List<ulong> claimedReward = new List<ulong>();

        public static class DWConfig
        {
            public static bool UseEconomics = true;
            public static bool UseServerRewards = true;
            public static string WebHookUrl = "";
            public static string DiscordChannel = "#events";
            public static int RewardBase = 100;
            public static int RewardMinPlusVariance = 50;
            public static int RewardMaxPlusVariance = 100;
            public static float RewardMultiplyBaseMinVariance = 1.0f;
            public static float RewardMultiplyBaseMaxVariance = 2.5f;
            public static int EventLengthMinVariance = 300;
            public static int EventLengthMaxVariance = 900;
            public static int EventOccurenceMinVariance = 3600;
            public static int EventOccurenceMaxVariance = 7200;
            public static int EventAnnounceMinVariance = 60;
            public static int EventAnnounceMaxVariance = 120;
            public static int MaximumWinners = 0;
        }

        void LoadDefaultConfig()
        {
            Puts("Generating new config file...");
            LoadConfig();
        }

        void InitConfig()
        {
            DWConfig.UseEconomics = int.Parse(Config["Settings", "Reward with Economics"].ToString()) == 1;
            DWConfig.UseServerRewards = int.Parse(Config["Settings", "Reward with ServerRewards"].ToString()) == 1;
            DWConfig.WebHookUrl =  Config["Settings", "Discord WebhookURL"].ToString();
            DWConfig.DiscordChannel =  Config["Settings", "Discord Channel"].ToString();
            DWConfig.RewardBase =  int.Parse(Config["Settings", "Reward Base"].ToString());
            DWConfig.RewardMinPlusVariance =  int.Parse(Config["Settings", "Reward Plus Min Variance"].ToString());
            DWConfig.RewardMaxPlusVariance =  int.Parse(Config["Settings", "Reward Plus Max Variance"].ToString());
            DWConfig.RewardMultiplyBaseMinVariance =  float.Parse(Config["Settings", "Reward Multiply Base Min Variance"].ToString());
            DWConfig.RewardMultiplyBaseMaxVariance =  float.Parse(Config["Settings", "Reward Multiply Base Max Variance"].ToString());
            DWConfig.EventLengthMinVariance =  int.Parse(Config["Settings", "Event Length Min Variance (seconds)"].ToString());
            DWConfig.EventLengthMaxVariance =  int.Parse(Config["Settings", "Event Length Max Variance (seconds)"].ToString());
            DWConfig.EventOccurenceMinVariance =  int.Parse(Config["Settings", "Event Occurence Min Variance (seconds)"].ToString());
            DWConfig.EventOccurenceMaxVariance =  int.Parse(Config["Settings", "Event Occurence Max Variance (seconds)"].ToString());
            DWConfig.EventAnnounceMinVariance =  int.Parse(Config["Settings", "Event Announce Min Variance (seconds)"].ToString());
            DWConfig.EventAnnounceMaxVariance =  int.Parse(Config["Settings", "Event Announce Max Variance (seconds)"].ToString());
            DWConfig.MaximumWinners =  int.Parse(Config["Settings", "Event Max Winners Count (0 for infinity)"].ToString());
        }

        void LoadConfig()
        {
            SetConfig("Settings", "Reward with Economics", "1"); // UseEconomics
            SetConfig("Settings", "Reward with ServerRewards", "1"); // UseServerRewards
            SetConfig("Settings", "Discord WebhookURL", ""); //WebHookUrl
            SetConfig("Settings", "Discord Channel", "#events"); //DiscordChannel
            SetConfig("Settings", "Reward Base", "100"); // RewardBase
            SetConfig("Settings", "Reward Plus Min Variance", "50"); // RewardMinPlusVariance
            SetConfig("Settings", "Reward Plus Max Variance", "100"); // RewardMaxPlusVariance
            SetConfig("Settings", "Reward Multiply Base Min Variance", "1.0"); // RewardMultiplyBaseMinVariance
            SetConfig("Settings", "Reward Multiply Base Max Variance", "2.5"); // RewardMultiplyBaseMaxVariance
            SetConfig("Settings", "Event Length Min Variance (seconds)", "300"); // EventLengthMinVariance
            SetConfig("Settings", "Event Length Max Variance (seconds)", "900"); // EventLengthMaxVariance
            SetConfig("Settings", "Event Occurence Min Variance (seconds)", "3600"); // EventOccurenceMinVariance
            SetConfig("Settings", "Event Occurence Max Variance (seconds)", "900"); // EventOccurenceMaxVariance
            SetConfig("Settings", "Event Announce Min Variance (seconds)", "60"); // EventAnnounceMinVariance
            SetConfig("Settings", "Event Announce Max Variance (seconds)", "90"); // EventAnnounceMaxVariance
            SetConfig("Settings", "Event Max Winners Count (0 for infinity)", "0"); // MaximumWinners
        }

        void SetConfig(params object[] args)
        {
            List<string> stringArgs = (from arg in args select arg.ToString()).ToList();
            stringArgs.RemoveAt(args.Length - 1);

            if (Config.Get(stringArgs.ToArray()) == null) Config.Set(args);
        }

        void OnServerInitialized()
        {
            LoadConfig();
            InitConfig();
            int minReward = (int)(DWConfig.RewardBase * DWConfig.RewardMultiplyBaseMinVariance + DWConfig.RewardMinPlusVariance);
            int maxReward = (int)(DWConfig.RewardBase * DWConfig.RewardMultiplyBaseMaxVariance + DWConfig.RewardMaxPlusVariance);

            Puts($"Event will happen every approx. {(DWConfig.EventOccurenceMinVariance + DWConfig.EventOccurenceMaxVariance)/2} seconds.");
            Puts($"Event will last for approx. {(DWConfig.EventLengthMinVariance + DWConfig.EventLengthMaxVariance)/2} seconds.");
            if(DWConfig.MaximumWinners > 0)
                Puts($"Or until {DWConfig.MaximumWinners} player claims reward.");
            Puts($"Event will announce every approx. {(DWConfig.EventAnnounceMinVariance + DWConfig.EventAnnounceMaxVariance)/2} seconds.");
            Puts($"Event will reward approx. {(minReward + maxReward)/2} points for Economics: {DWConfig.UseEconomics} and ServerRewards: {DWConfig.UseServerRewards}.");
            timer.Once(UnityEngine.Random.Range(DWConfig.EventOccurenceMinVariance, DWConfig.EventOccurenceMaxVariance), StartEvent);
        }

        void Unloaded()
        {
            EndEvent("\nPlugin unloaded!");
        }

        [ChatCommand("get")]
        void cmdTest(BasePlayer player, string command, string[] args)
        {
            if (string.IsNullOrEmpty(secretCode))
            {
                Tell(player, "Event haven't started yet!");
                return;
            }
            if(args.Length != 1 || args[0] != secretCode)
            {
                Tell(player, "You've entered incorrect secret code!");
                return;
            }
            
            if(args[0] == secretCode)
            {
                if (claimedReward.Contains(player.userID))
                    Tell(player, "You have already claimed this reward!");
                else
                {
                    claimedReward.Add(player.userID);
                    Tell(player, $"You have claimed <color=orange>{reward}</color> points reward!");
                    SendMessageToDiscord($"`{player.displayName}` have claimed `{reward}` points reward!", ":information_source:");
                    if(DWConfig.UseServerRewards)
                        ServerRewards?.Call("AddPoints", player.userID, reward);
                    if(DWConfig.UseEconomics)
                        Economics?.Call("Deposit", player.userID, (double)reward);
                }

                if (DWConfig.MaximumWinners > 0 && DWConfig.MaximumWinners <= claimedReward.Count)
                    EndEvent($"\nMaximum amount of players ({DWConfig.MaximumWinners}) has claimed prize!");
            }
        }

        void Tell(BasePlayer player, string message)
        {
            Player.Message(player, message);
        }

        void Broadcast(string message)
        {
            Server.Broadcast(message);
        }

        void StartEvent()
        {
            double finalMultiplyVariance = 1;
            if (DWConfig.RewardMultiplyBaseMinVariance > 1)
                finalMultiplyVariance = UnityEngine.Random.Range(DWConfig.RewardMultiplyBaseMinVariance, DWConfig.RewardMultiplyBaseMaxVariance);

            int finalPlusVariance = 0;
            if (DWConfig.RewardMaxPlusVariance > 1)
                finalPlusVariance = UnityEngine.Random.Range(DWConfig.RewardMinPlusVariance, DWConfig.RewardMaxPlusVariance);

            reward = (int)(DWConfig.RewardBase * finalMultiplyVariance + finalPlusVariance);
            secretCode = Guid.NewGuid().ToString().Replace("-", "").ToUpper();
            SendMessageToDiscord($"Event has begun! Use `/get {secretCode}` in game to claim `{reward}` points!", ":star:");
            ContinueslyBroadcast();
            timer.Once(UnityEngine.Random.Range(DWConfig.EventLengthMinVariance, DWConfig.EventLengthMaxVariance), () => EndEvent());
        }

        void ContinueslyBroadcast()
        {
            if (string.IsNullOrEmpty(secretCode) || reward == 0)
                return;

            if (claimedReward.Count == 0)
            {
                Broadcast($"Event has started to get <color=orange>{reward}</color> points!" +
                $"\nJoin our discord server, check <color=orange>{DWConfig.DiscordChannel}</color> channel for details how to claim event prize!");
            }
            else
            {
                Broadcast($"Event to get <color=orange>{reward}</color> points is still in progress!" +
                    $"\nJoin our discord server, check <color=orange>{DWConfig.DiscordChannel}</color> channel for details how to claim event prize!");
            }

            timer.Once(UnityEngine.Random.Range(DWConfig.EventAnnounceMinVariance, DWConfig.EventAnnounceMaxVariance), ContinueslyBroadcast);
        }

        void EndEvent(string reason = "")
        {
            if (secretCode == null || reward == 0)
                return;

            secretCode = null;
            SendMessageToDiscord($"Event to get `{reward}` points has ended!{reason}", ":checkered_flag:");
            Broadcast($"Event to get <color=orange>{reward}</color> points has ended!{reason}");

            reward = 0;
            claimedReward.Clear();
            timer.Once(UnityEngine.Random.Range(DWConfig.EventOccurenceMinVariance, DWConfig.EventOccurenceMaxVariance), StartEvent);
        }

        void SendMessageToDiscord(string message, string icon = ":bulb:")
        {
            if (string.IsNullOrEmpty(DWConfig.WebHookUrl))
                Interface.Oxide.LogWarning(
                    $"WARNING! Discord webhook URL is not defined! Message which wasn't sent: {message}");
            else
                DiscordMessages?.Call("API_SendTextMessage", DWConfig.WebHookUrl, $"{icon} {message}");
        }
    }
}
