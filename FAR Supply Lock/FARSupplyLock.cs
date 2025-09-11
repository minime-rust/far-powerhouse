using System.Collections.Generic;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("FAR: Supply Lock", "Ghosty/miniMe", "2.0.0")]
    [Description("Locks Supply Drops to the player or their team, using entity chain tracking.")]
    public class FARSupplyLock : RustPlugin
    {
        private Dictionary<ulong, ulong> dropOwners = new Dictionary<ulong, ulong>(); // drop.net.ID -> player.userID
        private Dictionary<ulong, ulong> planeOwners = new Dictionary<ulong, ulong>(); // plane.net.ID -> player.userID
        private Dictionary<ulong, ulong> signalOwners = new Dictionary<ulong, ulong>(); // signal.net.ID -> player.userID

        private void Init()
        {
            dropOwners.Clear();
            planeOwners.Clear();
            signalOwners.Clear();
        }

        // 1. Player throws Supply Signal
        void OnExplosiveThrown(BasePlayer player, BaseEntity entity, ThrownWeapon item)
        {
            if (player == null || entity == null) return;
            if (!(entity is SupplySignal sig)) return;

            // store mapping: signalId -> playerId
            signalOwners[sig.net.ID.Value] = player.userID;
        }

        // 1b. Player calls Supply Drop from Excavator
        void OnExcavatorSuppliesRequested(ExcavatorSignalComputer computer, BasePlayer player, BaseEntity cargoPlane)
        {
            if (player == null || cargoPlane == null) return;
            if (cargoPlane is CargoPlane plane)
            {
                planeOwners[plane.net.ID.Value] = player.userID;
            }
        }

        // 2. CargoPlane spawns and is linked to a SupplySignal
        void OnCargoPlaneSignaled(CargoPlane plane, SupplySignal signal)
        {
            if (plane == null || signal == null) return;

            ulong sigId = signal.net.ID.Value;
            if (signalOwners.TryGetValue(sigId, out ulong playerID))
            {
                planeOwners[plane.net.ID.Value] = playerID;
                signalOwners.Remove(sigId); // consume
            }
        }

        // 3. SupplyDrop spawns from CargoPlane
        void OnSupplyDropDropped(SupplyDrop drop, CargoPlane plane)
        {
            if (drop == null) return;

            if (plane != null && planeOwners.TryGetValue(plane.net.ID.Value, out ulong playerID))
            {
                dropOwners[drop.net.ID.Value] = playerID;
                planeOwners.Remove(plane.net.ID.Value); // plane consumed
            }

            // keep original author's MakeLootable behaviour
            drop.MakeLootable();
        }

        // 4. Restrict looting
        object CanLootEntity(BasePlayer player, StorageContainer container)
        {
            if (player == null || container == null) return null;
            if (!(container is SupplyDrop drop)) return null;

            if (dropOwners.TryGetValue(drop.net.ID.Value, out ulong ownerID))
            {
                if (ownerID == player.userID) return null; // owner allowed

                var team = RelationshipManager.ServerInstance?.FindPlayersTeam(ownerID);
                if (team != null && team.members != null && team.members.Contains(player.userID))
                    return null; // teammate allowed

                player.ChatMessage("<size=20><color=red>Supply</color>Lock</size>\n<size=15><color=white>This supply drop is locked!\nYou must be in a team with this player to loot the drop.</size>");
                return false;
            }
            return null; // not tracked => allow
        }

        // 5. Cleanup on despawn
        void OnEntityKill(BaseNetworkable entity)
        {
            if (entity == null) return;
            if (entity is SupplyDrop drop) dropOwners.Remove(drop.net.ID.Value);
            else if (entity is SupplySignal sig) signalOwners.Remove(sig.net.ID.Value);
            else if (entity is CargoPlane plane) planeOwners.Remove(plane.net.ID.Value);
        }
    }
}
