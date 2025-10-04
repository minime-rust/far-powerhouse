using System;
using System.Collections.Generic;
using CompanionServer;
using ConVar;
using Facepunch;
using Facepunch.Math;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("FAR: Admin Blue", "miniMe", "1.0.1")]
    [Description("Makes admin names show like regular players in chat")]

    class FARAdminBlue : RustPlugin
    {
        private const string PLAYER_COLOR = "#5af";

        private object OnPlayerChat(BasePlayer player, string message, Chat.ChatChannel channel)
        {
            if (player == null) return null;

            var authLevel = player.net?.connection?.authLevel ?? 0;
            if (authLevel == 0) return null; // Non-admins -> normal flow

            // Admin - handle their message with blue color
            SendChatMessage(player, message, channel);
            return true; // Suppress original green message
        }

        private void SendChatMessage(BasePlayer player, string message, Chat.ChatChannel channel)
        {
            var displayName = player.displayName;
            RCon.Broadcast(RCon.LogType.Chat, new Chat.ChatEntry
            {
                Channel = channel,
                Message = message,
                UserId = player.UserIDString,
                Username = displayName,
                Color = PLAYER_COLOR,
                Time = Epoch.Current
            });

            switch (channel)
            {
                case Chat.ChatChannel.Global:
                    if (Chat.globalchat)
                        ConsoleNetwork.BroadcastToAllClients("chat.add2", 0, player.userID,
                            message, displayName, PLAYER_COLOR, 1f);
                    break;

                case Chat.ChatChannel.Team:
                    var team = RelationshipManager.ServerInstance.FindPlayersTeam(player.userID);
                    if (team != null)
                    {
                        var conns = team.GetOnlineMemberConnections();
                        if (conns != null && conns.Count > 0)
                            ConsoleNetwork.SendClientCommand(conns, "chat.add2", 1, player.userID,
                                message, displayName, PLAYER_COLOR, 1f);

                        team.BroadcastTeamChat(player.userID, displayName, message, PLAYER_COLOR);
                    }
                    break;

                default: // Local / proximity
                    const float radius = 2500f;
                    var playerPos = player.transform.position;
                    foreach (var basePlayer in BasePlayer.activePlayerList)
                    {
                        var sqrDist = (basePlayer.transform.position - playerPos).sqrMagnitude;
                        if (sqrDist <= radius)
                        {
                            var alpha = Mathf.Clamp01(radius - sqrDist + 0.2f);
                            ConsoleNetwork.SendClientCommand(basePlayer.net.connection, "chat.add2", 0,
                                player.userID, message, displayName, PLAYER_COLOR, alpha);
                        }
                    }
                    break;
            }
        }
    }
}