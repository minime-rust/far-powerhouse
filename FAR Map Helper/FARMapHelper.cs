using System;
using System.Collections.Generic;
using Oxide.Core.Plugins;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("FAR: Map Helper", "miniMe", "1.0.1")]
    [Description("Resolves map squares and monument names for players/plugins.")]

    // using System.Collections;
    // using Facepunch;
    // using Oxide.Core;
    // using Oxide.Core.Libraries.Covalence;

    public class FARMapHelper : RustPlugin
    {
        #region Init Map Helper

        // The rest goes within a plugin class.
        [PluginReference]
        Plugin MonumentFinder;

        private float worldSize;
        private float halfSize;
        private float cellSize;
        private int cellCount;

        private void OnServerInitialized()
        {
            // Initialize geometry information and prevent cell count below 1
            worldSize = TerrainMeta.Size.x;
            cellCount = Mathf.Max(1, Mathf.FloorToInt(worldSize * 7f / 1024f));
            cellSize = worldSize / cellCount;
            halfSize = worldSize / 2f;
        }

        #endregion

        #region Localization

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["WorldInfo"] = "worldSize {0} | grid {1}x{1} | squares {2}-{3}",
                ["WhereAmI"] = "You are in map square ({0}){1}",
                ["WhereAmIFull"] = "You are in map square ({0}){1} at coords (x{2}, z{3})",
                ["NearMonument"] = " near {0}",
                ["OffGrid"] = "off-grid",
            }, this, "en");
        }

        #endregion

        #region Monument Finder

        private sealed class MonumentAdapter
        {
            public string Alias => (string)_monumentInfo["Alias"];
            public string ShortName => (string)_monumentInfo["ShortName"];
            public string PrefabName => (string)_monumentInfo["PrefabName"];
            // public Vector3 Position => (Vector3)_monumentInfo["Position"];
            // public Quaternion Rotation => (Quaternion)_monumentInfo["Rotation"];
            // public MonoBehaviour Object => (MonoBehaviour)_monumentInfo["Object"];
            // public OBB[] BoundingBoxes => (OBB[])_monumentInfo["BoundingBoxes"]; // requires "using Facepunch;"

            private readonly Dictionary<string, object> _monumentInfo;

            public MonumentAdapter(Dictionary<string, object> monumentInfo) =>
                _monumentInfo = monumentInfo;

            // Provided by MonumentFinder for advanced spatial math; UNUSED by this plugin.
            // Keep commented to avoid “dead API” confusion. Uncomment if you need them later.

            // public Vector3 TransformPoint(Vector3 localPosition) =>
            // ((Func<Vector3, Vector3>)_monumentInfo["TransformPoint"]).Invoke(localPosition);

            // public Vector3 InverseTransformPoint(Vector3 worldPosition) =>
            // ((Func<Vector3, Vector3>)_monumentInfo["InverseTransformPoint"]).Invoke(worldPosition);

            // public Vector3 ClosestPointOnBounds(Vector3 position) =>
            // ((Func<Vector3, Vector3>)_monumentInfo["ClosestPointOnBounds"]).Invoke(position);

            public bool IsInBounds(Vector3 position) =>
                ((Func<Vector3, bool>)_monumentInfo["IsInBounds"]).Invoke(position);
        }

        // Call this method within your plugin to get the closest monument, train tunnel, or underwater lab.
        MonumentAdapter GetClosestMonument(Vector3 position)
        {
            var dictResult = MonumentFinder?.Call("API_GetClosest", position) as Dictionary<string, object>;
            return dictResult != null ? new MonumentAdapter(dictResult) : null;
        }

        #endregion

        #region Hooks & API

        [HookMethod("API_MapInfo")]
        public object API_MapInfo(Vector3 position)
        {
            if (!IsFinite(position) || position == Vector3.zero)
            {
                Puts("Invalid position polled through API, bailing!");
                return (string.Empty, string.Empty);
            }

            var mapSquare = GetGridSquare(position);
            // returns a MonumentAdapter (wrapper around the dictionary from MonumentFinder)
            var monFinder = GetClosestMonument(position);

            // If we don't get a result, assume we're nowhere near a monument
            if (monFinder == null)
                return (mapSquare, string.Empty);

            // Determine if position is inside the monument
            bool inBounds = false;
            try { inBounds = monFinder.IsInBounds(position); }
            catch { inBounds = false; }

            // Outside the monument's bounds
            if (!inBounds)
                return (mapSquare, string.Empty);

            // Inside bounds: pick a monument name (Alias -> ShortName -> PrefabName)
            var monumentName =
                !string.IsNullOrWhiteSpace(monFinder.Alias) ? monFinder.Alias :
                !string.IsNullOrWhiteSpace(monFinder.ShortName) ? monFinder.ShortName :
                monFinder.PrefabName ?? string.Empty;

            var mappedName = MonumentMapper.MapName(monumentName) ?? string.Empty;

            return (mapSquare, mappedName);
        }

        #endregion

        #region Chat Commands

        [Command("maphelper")]
        private void CmdMapHelper(BasePlayer player, string command, string[] args)
        {
            if (player == null || !player.IsConnected) return;

            string topLeft = GetGridSquare(new Vector3(-halfSize + 1, 0, halfSize - 1));
            string bottomRight = GetGridSquare(new Vector3(halfSize - 1, 0, -halfSize + 1));

            player.ChatMessage(
                Lang("WorldInfo", player.UserIDString, worldSize, cellCount, topLeft, bottomRight)
            );
        }

        [Command("whereami")]
        private void CmdWhereAmI(BasePlayer player, string command, string[] args)
        {
            if (player == null || !player.IsConnected) return;

            // Get mapSquare and monumentName at position
            Vector3 pos = player.transform.position;
            var (mapSquare, monumentName) = ((string, string))API_MapInfo(pos);

            // Show monument name if within one
            var monumentSuffix = !string.IsNullOrWhiteSpace(monumentName)
                ? Lang("NearMonument", player.userID.ToString(), monumentName)
                : string.Empty;

            // Check if "/whereami" was called with argument "full"
            bool showCoords = args.Length > 0 && args[0].Equals("full", StringComparison.OrdinalIgnoreCase);

            string message = !showCoords
                ? Lang("WhereAmI", player.UserIDString, mapSquare, monumentSuffix)
                : Lang("WhereAmIFull", player.UserIDString, mapSquare, monumentSuffix, pos.x, pos.z);

            player.ChatMessage(message);
        }

        #endregion

        #region Map Grid Logic

        private string GetGridSquare(Vector3 pos)
        {
            int x = Mathf.FloorToInt((pos.x + halfSize) / cellSize);
            int z = Mathf.FloorToInt((pos.z + halfSize) / cellSize);

            bool offGrid = x < 0 || x >= cellCount || z < 0 || z >= cellCount;
            if (offGrid)
                return Lang("OffGrid", null) ?? "off-grid";

            var columnLabel = string.Empty;
            int row = cellCount - 1 - z;
            int n = x;
            do
            {
                int rem = n % 26;
                columnLabel = ((char)('A' + rem)) + columnLabel;
                n = (n / 26) - 1;
            } while (n >= 0);

            return $"{columnLabel}{row}";
        }

        #endregion

        #region Helpers

        // Helper which checks for a valid Vector3 position
        private static bool IsFinite(in Vector3 v) =>
            !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) ||
              float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));

        // Helper for localization with a fallback to English
        private string Lang(string key, string playerId, params object[] args)
        {
            var message = lang.GetMessage(key, this, playerId); // null -> default language
            return (args == null || args.Length == 0) ? message : string.Format(message, args);
        }

        public static class MonumentMapper
        {
            private static readonly (string key, string replacement)[] Map =
            {
                ("airfield", "Airfield"),
                ("arctic_research_base", "Arctic Research Base"),
                ("desert_military_base", "Abandoned Military Base"),
                ("excavator", "Giant Excavator Pit"),
                ("ferry_terminal", "Ferry Terminal"),
                ("gas_station", "Oxum's Gas Station"),
                ("harbor_1", "Small Harbor"),
                ("harbor_2", "Large Harbor"),
                ("jungle_ziggurat", "Jungle Ziggurat"),
                ("junkyard", "Junkyard"),
                ("launch_site", "Launch Site"),
                ("lighthouse", "Lighthouse"),
                ("military_tunnel", "Military Tunnel"),
                ("mining_quarry_a", "Sulfur Quarry"),
                ("mining_quarry_b", "Stone Quarry"),
                ("mining_quarry_c", "HQM Quarry"),
                ("nuclear_missile_silo", "Missile Silo"),
                ("oilrig_1", "Large Oil Rig"),
                ("oilrig_2", "Oil Rig"),
                ("powerplant", "Power Plant"),
                ("radtown_1", "Radtown"),
                ("satellite_dish", "Satellite Dish"),
                ("radtown_small", "Sewer Branch"),
                ("sphere_tank", "The Dome"),
                ("supermarket", "Abandoned Supermarket"),
                ("trainyard", "Train Yard"),
                ("warehouse", "Mining Outpost"),
                ("water_treatment_plant", "Water Treatment Plant"),
            };

            public static string MapName(string technical)
            {
                if (string.IsNullOrEmpty(technical)) return null;
                var input = technical.ToLowerInvariant();
                // Choose longest key wins to reduce accidental matches
                var best = default((string key, string replacement));
                var bestLen = -1;

                foreach (var (key, replacement) in Map)
                {
                    var k = key.ToLowerInvariant();
                    if (input.Contains(k) && k.Length > bestLen)
                    {
                        best = (key, replacement);
                        bestLen = k.Length;
                    }
                }

                return bestLen >= 0 ? best.replacement : null;
            }
        }

        #endregion
    }
}