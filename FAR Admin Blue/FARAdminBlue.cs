using System;
using CompanionServer;
using ConVar;
using Facepunch;
using Facepunch.Math;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("FAR: Admin Blue", "miniMe", "1.0.0")]
    [Description("Makes admin names show like regular players in chat")]
    class FARAdminBlue : RustPlugin
    {
        private const string PLAYER_COLOR = "#5af";

        private object OnPlayerChat(BasePlayer player, string message, Chat.ChatChannel channel)
        {
            if (player == null) return null;

            // Only intercept admin messages
            var authLevel = player.net?.connection?.authLevel ?? 0;
            if (authLevel == 0) return null; // Let normal players pass through

            // Admin detected - handle their message with blue color
            SendChatMessage(player, message, channel);
            return true; // Suppress original green message
        }

        private void SendChatMessage(BasePlayer player, string message, Chat.ChatChannel channel)
        {
            var displayName = player.displayName;
            var chatEntry = new Chat.ChatEntry
            {
                Channel = channel,
                Message = message,
                UserId = player.UserIDString,
                Username = displayName,
                Color = PLAYER_COLOR,
                Time = Epoch.Current
            };

            RCon.Broadcast(RCon.LogType.Chat, chatEntry);

            switch (channel)
            {
                case Chat.ChatChannel.Team:     // Team chat
                    HandleTeamChat(player, message, displayName);
                    break;
                case Chat.ChatChannel.Global:   // Global chat
                    if (Chat.globalchat)
                        ConsoleNetwork.BroadcastToAllClients("chat.add2", 0, player.userID, message, displayName, PLAYER_COLOR, 1f);
                    break;
                default:                        // Local/proximity chat
                    HandleLocalChat(player, message, displayName);
                    break;
            }
        }

        private void HandleTeamChat(BasePlayer player, string message, string displayName)
        {
            var playerTeam = RelationshipManager.ServerInstance.FindPlayersTeam(player.userID);
            if (playerTeam == null) return;

            var onlineMemberConnections = playerTeam.GetOnlineMemberConnections();
            if (onlineMemberConnections != null)
                ConsoleNetwork.SendClientCommand(onlineMemberConnections, "chat.add2", 1, player.userID,
                    message, displayName, PLAYER_COLOR, 1f);

            playerTeam.BroadcastTeamChat(player.userID, displayName, message, PLAYER_COLOR);
        }

        private void HandleLocalChat(BasePlayer player, string message, string displayName)
        {
            var radius = 2500f;
            foreach (var basePlayer in BasePlayer.activePlayerList)
            {
                var sqrMagnitude = (basePlayer.transform.position - player.transform.position).sqrMagnitude;
                if (sqrMagnitude <= radius)
                    ConsoleNetwork.SendClientCommand(basePlayer.net.connection, "chat.add2", 0, player.userID,
                        message, displayName, PLAYER_COLOR, Mathf.Clamp01(radius - sqrMagnitude + 0.2f));
            }
        }
    }
}