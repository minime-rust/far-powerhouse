using System;
using System.Collections.Generic;
using System.Linq;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;

namespace Oxide.Plugins
{
    [Info("FAR: Team Ban", "miniMe", "1.0.2")]
    [Description("Allows admins to execute kick, ban, or full data wipe actions on entire teams via console or RCON.")]
    public class FARTeamBan : CovalencePlugin
    {

        private const int ConfirmationTimeoutMinutes = 5;
        private const string DefaultReason = "No Reason";
        private const bool debug = false; // set true to bypass functions when testing
        private Dictionary<string, PendingAction> pendingActions = new Dictionary<string, PendingAction>();

        private string outputBuffer;

        private class PendingAction
        {
            public List<ulong> TargetSteamIDs { get; set; }
            public DateTime Timestamp { get; set; }
            public string BaseCommand { get; set; } // e.g. "teamban"
            public string CommandKey { get; set; }  // the complete command string
            public string Reason { get; set; }
            public float Hours { get; set; }
        }

        // --- RCON Commands ---

        [ConsoleCommand("teamban")]
        private void ConsoleCmdTeamBan(ConsoleSystem.Arg arg) =>
            HandleTeamCommand(arg, "teamban");

        [ConsoleCommand("teamkick")]
        private void ConsoleCmdTeamKick(ConsoleSystem.Arg arg) =>
            HandleTeamCommand(arg, "teamkick");

        [ConsoleCommand("teamwipe")]
        private void ConsoleCmdTeamWipe(ConsoleSystem.Arg arg) =>
            HandleTeamCommand(arg, "teamwipe");

        // --- Main logic (parse and confirm) ---

        private const string PermUse = "farteamban.use";

        private void Init() =>
            permission.RegisterPermission(PermUse, this);

        private void HandleTeamCommand(ConsoleSystem.Arg arg, string baseCommand)
        {
            outputBuffer = string.Empty;    // initialize output buffer

            // 0. Authorization gate
            BasePlayer issuer = arg.Connection?.player as BasePlayer;

            if (issuer != null && !permission.UserHasPermission(issuer.UserIDString, PermUse))
            {
                arg.ReplyWith("Error: You do not have permission to use this command.");
                return;
            }

            // 1. Get arguments
            if (arg.Args == null || arg.Args.Length < 1)
            {
                string usage;

                switch (baseCommand)
                {
                    case "teamban":
                        usage = $"{baseCommand} <SteamID> [\"Reason\"] [Hours]";
                        break;
                    case "teamkick":
                        usage = $"{baseCommand} <SteamID> [\"Reason\"]";
                        break;
                    case "teamwipe":
                        usage = $"{baseCommand} <SteamID>";
                        break;
                    default:
                        usage = $"{baseCommand} <SteamID> [...]";
                        break;
                }

                outputBuffer = $"Error: Missing required argument <SteamID>. Usage: {usage}";
                if (arg.Connection == null) Puts(outputBuffer);
                else arg.ReplyWith(outputBuffer);
                return;
            }

            string targetInput = arg.GetString(0);

            // Rust's Arg system handles quoted strings correctly for arg.GetString(1)
            string reason = arg.GetString(1, DefaultReason);
            float hours = 0f;

            // Get hours only if a reason was given.
            if (reason != DefaultReason && baseCommand == "teamban" && arg.Args.Length > 2)
                hours = arg.GetFloat(2, 0f);

            // 2. Get target team
            ulong targetSteamId;
            List<ulong> teamIDs = GetTeamMemberSteamIDs(targetInput, out targetSteamId);

            if (teamIDs == null || teamIDs.Count == 0)
            {
                outputBuffer = $"Error: Could not resolve target player '{targetInput}' or the associated team.";
                if (arg.Connection == null) Puts(outputBuffer);
                else arg.ReplyWith(outputBuffer);
                return;
            }

            // 3. Check confirmation
            CheckAndConfirmAction(arg, baseCommand, reason, hours, teamIDs);

            // 4. We made it this far ... output the buffer to the user
            SendOutput(arg);
        }

        private void CheckAndConfirmAction(ConsoleSystem.Arg arg, string baseCommand, string reason, float hours, List<ulong> teamIDs)
        {
            string commandKey = $"{baseCommand} {arg.FullString}".Trim();

            // 1. Lazy cleanup
            CleanupPendingActions();

            if (pendingActions.ContainsKey(commandKey))
            {
                // CONFIRMATION STEP
                PendingAction action = pendingActions[commandKey];

                if ((DateTime.UtcNow - action.Timestamp).TotalMinutes <= ConfirmationTimeoutMinutes)
                {
                    // Valid confirmation. Go for it!
                    ExecuteAction(action);
                    pendingActions.Clear();
                    outputBuffer += $"Success: The command '{action.CommandKey}' has been executed.\n";
                }
                else
                {
                    // Timeout! Needs another confirmation.
                    pendingActions.Remove(commandKey);
                    outputBuffer += $"Warning: The command '{action.CommandKey}' has timed out! Please repeat!\n";
                    StartNewAction(commandKey, teamIDs, baseCommand, reason, hours);
                }
            }
            else
            {
                // PRE-CONFIRMATION STEP
                // Starts new action, ignores older or similar commands (if commandKey changed)
                StartNewAction(commandKey, teamIDs, baseCommand, reason, hours);
            }
        }

        private void StartNewAction(string commandKey, List<ulong> teamIDs, string baseCommand, string reason, float hours)
        {
            pendingActions[commandKey] = new PendingAction
            {
                TargetSteamIDs = teamIDs,
                CommandKey = commandKey,
                BaseCommand = baseCommand,
                Reason = reason,
                Hours = hours,
                Timestamp = DateTime.UtcNow
            };

            // List all team members (Name and ID)
            List<string> memberDetails = new List<string>();
            foreach (ulong steamId in teamIDs)
            {
                BasePlayer player = BasePlayer.FindAwakeOrSleeping(steamId.ToString());
                string playerName = GetPlayerName(steamId, player);
                memberDetails.Add($"Player **{playerName}** [{steamId}]");
            }

            // Ensure consistant and meaningful output
            outputBuffer += $"WARNING: Pre-confirmation started for command '{commandKey}' " + $"targeting {teamIDs.Count} player(s).\n";
            outputBuffer += $"Affected Members: {string.Join(", ", memberDetails)}\n";
            outputBuffer += $"TO EXECUTE: Repeat the exact command '{commandKey}' within " + $"{ConfirmationTimeoutMinutes} minutes.\n";
        }

        private List<ulong> GetTeamMemberSteamIDs(string input, out ulong targetSteamId)
        {
            targetSteamId = 0;

            if (!ulong.TryParse(input, out targetSteamId))
                return null;

            // This works for online AND offline players
            RelationshipManager.PlayerTeam team =
                RelationshipManager.ServerInstance.FindPlayersTeam(targetSteamId);

            // Player has a team
            if (team != null && team.members != null && team.members.Count > 0)
                return team.members.ToList();

            // Solo player fallback
            return new List<ulong> { targetSteamId };
        }

        private string GetPlayerName(ulong steamId, BasePlayer player)
        {
            IPlayer iPlayer = covalence.Players.FindPlayerById(steamId.ToString());
            return player?.displayName ?? iPlayer?.Name ?? steamId.ToString();
        }

        private void ExecuteAction(PendingAction action)
        {
            outputBuffer += $"Executing action '{action.BaseCommand}' on {action.TargetSteamIDs.Count} player(s)...\n";

            foreach (ulong steamId in action.TargetSteamIDs)
            {
                // Try to find player online, sleeper or covalence
                BasePlayer player = BasePlayer.FindAwakeOrSleeping(steamId.ToString());
                string playerName = GetPlayerName(steamId, player);

                switch (action.BaseCommand)
                {
                    case "teamban":
                        // banid <SteamID> <Name> [Reason] [Hours]
                        int hoursInt = (int)action.Hours;
                        if (debug)
                        {
                            outputBuffer += $"[DEBUG] -> BAN: Player \"{playerName}\" [{steamId}] for {action.Hours}h.\n";
                        }
                        else
                        {
                            ConsoleSystem.Run(ConsoleSystem.Option.Server, $"banid {steamId} \"{playerName}\" \"{action.Reason}\" {hoursInt}");
                            outputBuffer += $"-> BAN: Player {playerName} [{steamId}] for {action.Hours}h.\n";
                        }

                        break;

                    case "teamkick":
                        // kick <SteamID> [Reason]
                        if (debug)
                        {
                            outputBuffer += $"[DEBUG] -> KICK: Player \"{playerName}\" [{steamId}].\n";
                        }
                        else
                        {
                            ConsoleSystem.Run(ConsoleSystem.Option.Server, $"kick {steamId} \"{action.Reason}\"");
                            outputBuffer += $"-> KICK: Player \"{playerName}\" [{steamId}].\n";
                        }
                        break;

                    case "teamwipe":
                        // wipe player/team (best effort)
                        if (debug)
                        {
                            outputBuffer += $"[DEBUG] -> WIPE: Player \"{playerName}\" [{steamId}]. Data and entities wiped.\n";
                        }
                        else
                        {
                            WipePlayerData(steamId, player, playerName);
                            outputBuffer += $"-> WIPE: Player \"{playerName}\" [{steamId}]. Data and entities wiped.\n";
                        }
                        break;
                }
            }

            outputBuffer += $"Action '{action.BaseCommand}' completed successfully for {action.TargetSteamIDs.Count} player(s).\n";
        }

        private void WipePlayerData(ulong steamId, BasePlayer player, string playerName)
        {
            // a) Strip inventory (only possible if player alive)
            if (player != null && !player.IsDead())
            {
                player.inventory.Strip();
                player.SendNetworkUpdateImmediate();
                outputBuffer += $"  > Inventory cleared for \"{playerName}\".\n";
            }
            else
            {
                outputBuffer += $"  > Inventory clear skipped (no active player entity): [{steamId}].\n";
            }

            // b) Blueprint wipe (only possible if player alive)
            if (player != null && !player.IsDead())
            {
                player.blueprints.Reset();
                player.SendNetworkUpdateImmediate();
                outputBuffer += $"  > Blueprints reset for \"{playerName}\".\n";
            }
            else
            {
                outputBuffer += $"  > Blueprint wipe skipped (no active player entity): [{steamId}]\n";
            }

            // c) Delete player entities
            ConsoleSystem.Run(ConsoleSystem.Option.Server, $"deleteby {steamId}");
            outputBuffer += $"  > Entities wiped for {steamId} using 'deleteby' command.\n";

            // d) Remove player (only possible if player alive)
            if (player != null && !player.IsDead())
                player.Die();
            else
                outputBuffer += $"  > Player kill skipped (no active player entity): [{steamId}].\n";

        }

        private string GetActionVerb(string baseCommand, int count)
        {
            string target = count > 1 ? "team" : "solo player";
            switch (baseCommand)
            {
                case "teamban": return $"Ban {target}";
                case "teamkick": return $"Kick {target}";
                case "teamwipe": return $"Wipe data for {target}";
                default: return $"Execute action on {target}";
            }
        }

        private void CleanupPendingActions()
        {
            DateTime cutoff = DateTime.UtcNow.AddMinutes(-ConfirmationTimeoutMinutes);
            var keysToRemove = new List<string>();

            foreach (var pair in pendingActions)
                if (pair.Value.Timestamp < cutoff)
                    keysToRemove.Add(pair.Key);

            foreach (string key in keysToRemove)
                pendingActions.Remove(key);
        }

        private void SendOutput(ConsoleSystem.Arg arg)
        {
            outputBuffer = outputBuffer.TrimEnd('\n');
            if (arg.Connection != null || arg.IsRcon)
                arg.ReplyWith(outputBuffer); // F1 console or RCon
            Puts(outputBuffer); // also send to server log
        }
    }
}