/*using Oxide.Ext.Discord;
using Oxide.Ext.Discord.Attributes;
using Oxide.Ext.Discord.DiscordObjects;
using System.Collections.Generic;
//using Rust;
using System;   //Convert*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Oxide.Core.Plugins;
using UnityEngine;
using Newtonsoft.Json.Linq;
using Oxide.Ext.Discord;
using Oxide.Ext.Discord.Attributes;
using Oxide.Ext.Discord.DiscordObjects;

namespace Oxide.Plugins
{
    [Info("Chat With Discord", "BuzZ", "0.0.1")]
	[Description(".")]

/*======================================================================================================================= 
*
*   THANKS TO THE OXIDE/UMOD TEAM for coding quality, ideas, and time spent for the community
*   This is based on Rustcord, without agreement, as a discord extension testing environment
*
*=======================================================================================================================*/

    class ChatWithDiscord : RustPlugin
    {
		[DiscordClient] private DiscordClient _client;

        //private bool ConfigChanged;
        bool debug = false;
        //string apikiki = "api key here";
        //string channel = "369810354819170304";
//////////////////////////////////////////////////////////////////////////////////////////////
        /*void Loaded()
        {
            Discord.CreateClient(this, apikiki);
            //Discord.CreateHeartbeat(300f);

        }*/
/*
#region CONFIG

        protected override void LoadDefaultConfig()
            {
                LoadVariables();
            }

        private void LoadVariables()
        {

            apikiki = Convert.ToString(GetConfig("API KEY", "value", "api key here"));
            channel = Convert.ToString(GetConfig("CHANNEL ID", "value", "369810354819170304"));

            if (!ConfigChanged) return;
            SaveConfig();
            ConfigChanged = false;
        }

        private object GetConfig(string menu, string datavalue, object defaultValue)
        {
            var data = Config[menu] as Dictionary<string, object>;
            if (data == null)
            {
                data = new Dictionary<string, object>();
                Config[menu] = data;
                ConfigChanged = true;
            }
            object value;
            if (!data.TryGetValue(datavalue, out value))
            {
                value = defaultValue;
                data[datavalue] = value;
                ConfigChanged = true;
            }
            return value;
        }

#endregion
*/
//////////////////////////////////////////////////////////////////////////////////////////////



		private Settings _settings;
		
		private class Settings
		{
			public string Apikey { get; set; }
			private string _Botid { get; set; }
			public string Botid(string id = null)
			{
				if(id != null)
				{
					_Botid = id;
				}
				return _Botid;
			}
			public string Commandprefix { get; set; }
			public List<Channel> Channels { get; set; }
			public Dictionary<string, List<string>> Commandroles { get; set; }

			public class Channel
			{
				public string Channelid { get; set; }
				/*public List<string> perms = new List<string>()
                {
                    "cmd_allow", // allow command check
					"cmd_players",
					"cmd_kick",
					"cmd_com",
					"p_connect",
					"p_aconnect",// more details
					"p_disconnect",
					"p_death",
					"p_chat",
					"p_report",
					"msg_plugininit",
					"msg_serverinit"
                };*/
                public List<string> perms { get; set; }
				/*
					Permissions:
					cmd_allow // allow command check
					cmd_players
					cmd_kick
					cmd_com
					p_connect
					p_aconnect // more details
					p_disconnect
					p_death
					p_chat
					p_report
					msg_plugininit
					msg_serverinit
				*/
			}

		}


		private Settings GetDefaultSettings()
		{
			return new Settings
			{
				Apikey = "APIkey",
				Commandprefix = "!",
				Channels = new List<Settings.Channel> { new Settings.Channel { Channelid = "Channel ID here",
					perms = new List<string>(){"cmd_allow", "cmd_players", "cmd_kick", "cmd_com", "p_connect", "p_aconnect", "p_disconnect", "p_death", "p_chat", "p_report", "msg_plugininit", "msg_serverinit"} } },

				Commandroles = new Dictionary<string, List<string>> { { "command", new List<string>(){"rolename1", "rolename2"} } }
			};
		}


		protected override void LoadDefaultConfig()
		{
			PrintWarning("Attempting to create default config...");
			Config.Clear();
			Config.WriteObject(GetDefaultSettings(), true);
			Config.Save();
		}

		private void OnServerInitialized()
		{
			if (_client != null)
			{
				_settings?.Channels.Where(x => x.perms.Contains("msg_serverinit")).ToList().ForEach(ch => {
					Channel.GetChannel(_client, ch.Channelid, chan =>
					{
						chan.CreateMessage(_client, Translate("RUST_OnInitMsg"));
					});
				});
			}
		}


		private void Loaded()
		{
			_settings = Config.ReadObject<Settings>();

			if (string.IsNullOrEmpty(_settings.Apikey) || _settings.Apikey == null || _settings.Apikey == "APIkey")
			{
				PrintError("API key is empty or invalid!");
				return;
			}
			
			foreach(var dc in Discord.Clients)
			{
				if(dc.Settings.ApiToken == _settings.Apikey)
					Discord.CloseClient(dc);
			}
			//Discord.Clients.Clear();
			Discord.CreateClient(this, _settings.Apikey);
		}


		void Discord_Ready(Oxide.Ext.Discord.DiscordEvents.Ready rdy)
		{
			timer.Once(1f, ()=>{
				Puts("Connection established to " + _client.DiscordServer.name);
				_settings?.Channels.Where(x => x.perms.Contains("msg_plugininit")).ToList().ForEach(ch => {
					Channel.GetChannel(_client, ch.Channelid, (Channel c)=>{
						c.CreateMessage(_client, "Chat Initialized!");
					});
				});
			});
			_settings.Botid(rdy.User.id);
		}

		private void Unload()
		{
			Discord.CloseClient(_client);
		}

		protected override void LoadDefaultMessages()
		{
			lang.RegisterMessages(new Dictionary<string, string>
			{
				{ "Discord_PlayersResponse", ":mag_right: Connected Players [{count}/{maxplayers}]: {playerslist}" },
				{ "RUST_OnInitMsg", "Server online." },
				{ "RUST_OnPlayerInit", ":white_check_mark: {playername} has connected!" },
				{ "RUST_OnPlayerInitAdmin", ":clipboard: {playername} has connected! (IP: {playerip}    SteamID: {playersteamid})" },
				{ "RUST_OnPlayerDisconnect", ":x: {playername} has disconnected! ({reason})" },
				{ "RUST_OnPlayerReport", ":warning: {playername}: {message}"}
			}, this);
		}


		private void OnPlayerChat(ConsoleSystem.Arg arg)
		{
			if(arg.Player() == null) return;

			string pname = arg.Player().displayName;
			string msg = arg.FullString;
			_settings.Channels.Where(x => x.perms.Contains("p_chat")).ToList().ForEach(ch =>
			{
				Channel.GetChannel(_client, ch.Channelid, chan =>
				{
					chan.CreateMessage(_client, ":speech_left: " + pname + ": " + msg);
				});
			});
		}
//////////////////////////////////////////////////////////////////////////////

		private void DiscordToGameCmd(string command, string param, User author, string channelid)
		{
			switch (command)
			{
				case "players":
				{
					string listStr = string.Empty;
					var pList = BasePlayer.activePlayerList;
					int i = 0;
					foreach (var player in pList)
					{
						listStr += player.displayName + "[" + i++ + "]";
						if (i != pList.Count)
							listStr += ",";
					}
					Channel.GetChannel(_client, channelid, chan =>
					{
						// Connected Players [{count}/{maxplayers}]: {playerslist}
						chan.CreateMessage(_client, Translate("Discord_PlayersResponse", new Dictionary<string, string>
						{
							{ "count", Convert.ToString(BasePlayer.activePlayerList.Count) },
							{ "maxplayers", Convert.ToString(ConVar.Server.maxplayers) },
							{ "playerslist", listStr }
						}));
					});
					break;
				}
				case "kick":
				{
					if(String.IsNullOrEmpty(param))
					{
						Channel.GetChannel(_client, channelid, chan =>
						{
							chan.CreateMessage(_client, "Syntax: !kick <name> <reason>");
						});
						return;
					}
					string[] _param = param.Split(' ');
					if(_param.Count() < 2)
					{
						Channel.GetChannel(_client, channelid, chan =>
						{
							chan.CreateMessage(_client, "Syntax: !kick <name> <reason>");
						});
						return;
					}
					BasePlayer plr = BasePlayer.Find(_param[0]);
					if(plr == null)
					{
						Channel.GetChannel(_client, channelid, chan =>
						{
							chan.CreateMessage(_client, "Error: player not found");
						});
						return;
					}
					plr.Kick(param.Remove(0, _param[0].Length+1));
					break;
				}
				case "com":
				{
					if(String.IsNullOrEmpty(param))
					{
						Channel.GetChannel(_client, channelid, chan =>
						{
							chan.CreateMessage(_client, "Syntax: !com <command>");
						});
						return;
					}
					string[] _param = param.Split(' ');
					if(_param.Count() > 1)
						this.rust.RunServerCommand(_param[0], param.Remove(0, _param[0].Length+1));
					else
						this.rust.RunServerCommand(param);
					break;
				}
			}

		}
////////////////////////////////////////////////////////////////////////////////////////
		private void Discord_MessageCreate(Message message)
		{
			Settings.Channel channelidx = FindChannelById(message.channel_id);
			if (channelidx == null)
				return;
			
			if (message.author.id == _settings.Botid()) return;
			if (message.content[0] == _settings.Commandprefix[0])
			{
				if(!channelidx.perms.Contains("cmd_allow"))
					return;
				string cmd;
				string msg;
				try
				{
					cmd = message.content.Split(' ')[0].ToLower();
					if (string.IsNullOrEmpty(cmd.Trim()))
						cmd = message.content.Trim().ToLower();
				}
				catch
				{
					cmd = message.content.Trim().ToLower();
				}

				cmd = cmd.Remove(0, 1);

				msg = message.content.Remove(0, 1 + cmd.Length).Trim();
				cmd = cmd.Trim();
				cmd = cmd.ToLower();

				if(!channelidx.perms.Contains("cmd_"+cmd))
					return;
				if(!_settings.Commandroles.ContainsKey(cmd))
				{
					DiscordToGameCmd(cmd, msg, message.author, message.channel_id);
					return;
				}
				var roles = _settings.Commandroles[cmd];
				if(roles.Count == 0)
				{
					DiscordToGameCmd(cmd, msg, message.author, message.channel_id);
					return;
				}
				
				_client.DiscordServer.GetGuildMember(_client, message.author.id, x=>
				{
					
					foreach(var roleid in x.roles)
					{
						var rolename = GetRoleNameById(roleid);
						if(roles.Contains(rolename))
						{
							DiscordToGameCmd(cmd, msg, message.author, message.channel_id);
							break;
						}
					}
				});
			}
			else
			{
				if(!channelidx.perms.Contains("p_chat")) return;
				PrintToChat("[DISCORD] " + message.author.username + ": " + message.content);
				Puts("[DISCORD] " + message.author.username + ": " + message.content); // TEMP
			}
		}






		private string Translate(string msg, Dictionary<string, string> parameters = null)
		{
			if (string.IsNullOrEmpty(msg))
				return string.Empty;

			msg = lang.GetMessage(msg, this);

			if (parameters != null)
			{
				foreach (var lekey in parameters)
				{
					if (msg.Contains("{" + lekey.Key + "}"))
						msg = msg.Replace("{" + lekey.Key + "}", lekey.Value);
				}
			}

			return msg;
		}

		private Settings.Channel FindChannelById(string id)
		{
			foreach (var ch in _settings.Channels)
			{
				if (ch.Channelid == id)
					return ch;
			}
			return null;
		}

		private string GetRoleNameById(string id)
		{
			foreach(var r in _client.DiscordServer.roles)
			{
				if(r.id == id)
					return r.name;
			}
			return "";
		}







    }
}



/*

        // Called when the connection is completed
        void DiscordSocket_Initialized()
        {
            if (debug) Puts("Discord connected!");

            // When this hook is called, our Client variable
            // will be set to the main DiscordClient that
            // has been created for usssss

				Puts("Connection established to" + Client.DiscordServer.name);


			//channel.CreateMessage(Client, ":skull_crossbones: " + "message");

          //  Client.GetChannel(Client, channel);
            //DiscordObjects.GetChannelMessages(channel, null);


        }

		void Discord_Ready(Oxide.Ext.Discord.DiscordEvents.Ready rdy)
		{

            List<Channel> listchannels = new List<Channel>();
            //listchannels.Add(channel);
            listchannels = rdy.PrivateChannels;

foreach (var item in listchannels)
{
    				Puts($"{item}");

}


        }
 void Discord_GetChannel(Oxide.Ext.Discord.DiscordClient client, string channelID)
{

		void Discord_Ready(Oxide.Ext.Discord.DiscordEvents.Ready rdy)
		{

}

		void Discord_GetChannel(Oxide.Ext.Discord.DiscordObjects.Channel discordchannel)
		{




        }

        // Called when a message is created on the Discord server
        void Discord_MessageCreate(Message message)
        {
            // Add a sad reaction to the message
            //message.CreateReaction(Client, ":sad:");

				//Puts($"{message.channel_id}");


            // Post the message to chat
            rust.BroadcastChat($"Discord Message: {message.author.username} - {message.content}");
        }
    }
}
*/