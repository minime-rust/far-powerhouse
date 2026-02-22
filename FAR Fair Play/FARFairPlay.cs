using System.Collections.Generic;
using Facepunch;
using Oxide.Core;
using Oxide.Core.Plugins;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("FAR: Fair Play", "miniMe", "1.0.1")]
    [Description("Cleans up non-players after a delay if conditions are met.")]

    public class FARFairPlay : RustPlugin
    {
        #region DATA

        const string PermBypass = "farfairplay.bypass";
        readonly Dictionary<ulong, ulong> sleeperClaims = new(); // sleeper → caller (lock + ownership)
        readonly Dictionary<ulong, PendingMove> pendingMoves = new(); // caller → transaction
        class PendingMove
        {
            public ulong SleeperId;     // SteamID of the sleeper
            public uint BuildingId;     // ID of the governing TC
            public float StartedAt;     // Start time
        }

        #endregion

        #region LANG

        void Init()
        {
            permission.RegisterPermission(PermBypass, this);
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["NoSleeper"] = "No sleeper found where you're looking.",
                ["WrongTeam"] = "You can only move sleepers from your own team.",
                ["NotAuthed"] = "You must be authorized on the TC to move a sleeper.",
                ["WrongTC"] = "Destination must be inside the same tool cupboard area.",
                ["SleeperNotInTC"] = "That sleeper is not inside your tool cupboard area.",
                ["SleeperLocked"] = "That sleeper is already being moved by someone else.",
                ["SleeperGone"] = "Sleeper is no longer available.",
                ["MoveSuccess"] = "Sleeper moved successfully.",
                ["MoveExpired"] = "Move request expired (60s). Please try again.",
                ["SleeperSelected"] = "Sleeper selected: {0}. Now choose a destination."
            }, this);
        }

        #endregion

        #region PLAYER CLEANUP

        void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            // skip null or dead players
            if (player == null || player.userID == 0 || player.IsDead())
                return;

            // skip passed location checks
            if (!IsEligibleForCleanup(player))
                return;

            ulong userId = player.userID;
            Vector3 userPos = player.transform.position;

            // notify interested plugins and drop a line to console log
            Interface.CallHook("OnFairPlayScheduledForRemoval", userId, userPos);
            Puts($"[Cleanup] Scheduled {userId} at {userPos.ToString()} for removal in 20 minutes");

            // arm a timer for execution after 20 minutes
            timer.Once(20f * 60f, () =>
            {
                BasePlayer p = BasePlayer.FindSleeping(userId);
                // note: Die() will only have a target if the player
                // is still a sleeper, otherwise quit gracefully
                if (p != null) p.Die();
            });
        }

        #endregion

        #region CHAT COMMAND

        [ChatCommand("move")]
        void CmdMove(BasePlayer player, string command, string[] args)
        {
            if (player == null || !player.IsAlive())
                return;

            if (!pendingMoves.TryGetValue(player.userID, out PendingMove pending))
            {
                TrySelectSleeper(player);
                return;
            }

            if (Time.realtimeSinceStartup - pending.StartedAt > 60f)
            {
                pendingMoves.Remove(player.userID);
                sleeperClaims.Remove(pending.SleeperId);
                SendReply(player, L(player, "MoveExpired"));
                return;
            }

            TrySelectDestination(player, pending);
        }

        #endregion

        #region HELPERS

        bool HasBypass(BasePlayer player) =>
            player != null && permission.UserHasPermission(player.UserIDString, PermBypass);

        string L(BasePlayer player, string key, params object[] args) =>
            string.Format(lang.GetMessage(key, this, player.UserIDString), args);

        private void GetNearbyStructureContext(BasePlayer player, out ulong ownerId, out bool isOnTugboat)
        {
            ownerId = 0;
            isOnTugboat = false;

            Vector3 position = player.transform.position;
            List<BaseEntity> entities = Pool.GetList<BaseEntity>();

            Vis.Entities(position, 3.5f, entities);

            foreach (BaseEntity entity in entities)
            {
                if (entity is Tugboat)
                {
                    isOnTugboat = true;
                    continue;
                }

                if (entity is BuildingBlock || entity is LegacyShelter)
                {
                    ulong id = entity.OwnerID;
                    if (id != 0)
                    {
                        ownerId = id;
                        break;
                    }
                }
            }

            Pool.FreeList(ref entities);
        }

        private bool IsEligibleForCleanup(BasePlayer player)
        {
            GetNearbyStructureContext(player, out ulong ownerId, out bool isOnTugboat);

            // Rule #1: Tugboat logout is always allowed → spare
            if (isOnTugboat)
                return false;

            // Rule #2: Nearby structure owned by player or team → spare
            if (IsOwnedByPlayerOrTeam(player, ownerId))
                return false;

            // Rule #3: Nothing nearby or not owned → eligible (kill)
            return true;
        }

        private bool IsOwnedByPlayerOrTeam(BasePlayer player, ulong ownerId)
        {
            if (ownerId == 0)
                return false;

            // Player owns it
            if (ownerId == player.userID)
                return true;

            // Player not in a team → no further checks
            if (player.currentTeam == 0)
                return false;

            RelationshipManager.PlayerTeam team =
                RelationshipManager.ServerInstance.FindTeam(player.currentTeam);

            if (team == null)
                return false;

            return team.members.Contains(ownerId);
        }

        void TrySelectDestination(BasePlayer player, PendingMove pending)
        {
            if (!Physics.Raycast(player.eyes.HeadRay(), out RaycastHit hit, 4f))
                return;

            BaseEntity entity = hit.GetEntity();
            if (entity == null)
                return;

            BuildingPrivlidge tc = entity.GetBuildingPrivilege();
            if (tc == null || tc.buildingID != pending.BuildingId)
            {
                SendReply(player, L(player, "WrongTC"));
                return;
            }

            BasePlayer sleeper = BasePlayer.FindSleeping(pending.SleeperId);
            if (sleeper == null)
            {
                // sleeper disappears → clear both bindings
                pendingMoves.Remove(player.userID);
                sleeperClaims.Remove(pending.SleeperId);

                SendReply(player, L(player, "SleeperGone"));
                return;
            }

            Vector3 dest = hit.point;
            dest.y += 0.1f; // safety offset
            Vector3 sleeperPos = sleeper.transform.position;

            // notify interested plugins about relocation, then execute actual relocation
            Interface.CallHook("OnFairPlayPlayerRelocated", player.userID, sleeper.userID, sleeperPos, dest);
            Puts($"[Relocate] player [{player.userID}] moved sleeper [{sleeper.userID}] from location {sleeperPos.ToString()} to location {dest.ToString()}");
            sleeper.Teleport(dest);

            pendingMoves.Remove(player.userID);
            sleeperClaims.Remove(pending.SleeperId);

            SendReply(player, L(player, "MoveSuccess"));
        }

        void TrySelectSleeper(BasePlayer player)
        {
            if (!Physics.Raycast(player.eyes.HeadRay(), out RaycastHit hit, 4f))
                return;

            BasePlayer target = hit.GetEntity() as BasePlayer;
            if (target == null || !target.IsSleeping())
            {
                SendReply(player, L(player, "NoSleeper"));
                return;
            }

            // make sure we have a TC
            BuildingPrivlidge tc = target.GetBuildingPrivilege();
            if (tc == null)
            {
                SendReply(player, L(player, "SleeperNotInTC"));
                return;
            }

            if (!HasBypass(player))
            {
                // check if player is in a team with the sleeper (no bypass)
                if (target.currentTeam == 0 || target.currentTeam != player.currentTeam)
                {
                    SendReply(player, L(player, "WrongTeam"));
                    return;
                }

                // check if player is authed on this TC (no bypass)
                if (!tc.IsAuthed(player))
                {
                    SendReply(player, L(player, "NotAuthed"));
                    return;
                }
            }

            // Is this sleeper already claimed?
            if (sleeperClaims.TryGetValue(target.userID, out ulong ownerId))
            {
                if (ownerId != player.userID)
                {
                    SendReply(player, L(player, "SleeperLocked"));
                    return;
                }
            }

            // Claim sleeper for this caller
            sleeperClaims[target.userID] = player.userID;
            pendingMoves[player.userID] = new PendingMove
            {
                SleeperId = target.userID,
                BuildingId = tc.buildingID,
                StartedAt = Time.realtimeSinceStartup
            };

            SendReply(player, L(player, "SleeperSelected", target.displayName));
        }

        #endregion
    }
}