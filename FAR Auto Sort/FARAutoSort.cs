using System;
using System.Collections.Generic;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("FAR: Auto Sort", "miniMe", "1.0.1")]
    [Description("Player-initiated sorting of StorageContainers.")]
    public class FARAutoSort : CovalencePlugin
    {

        #region Commands

        [ChatCommand("sort")]
        private void CmdSort(BasePlayer player, string cmd, string[] args)
        {
            if (player == null) return;

            // Raycast from player's eyes to get the targeted entity
            var entity = GetTargetedContainer(player);
            if (entity == null) return; // not a storage container

            var sc = entity as StorageContainer;
            if (sc == null) return; // not a sortable storage container
            if (sc.inventory == null) return; // no inventory

            bool sortByCategory = true;
            bool sortByName = false;

            if (args != null && args.Length > 0)
            {
                switch (args[0].ToLower())
                {
                    case "both":
                        sortByName = true;
                        break;
                    case "name":
                        sortByCategory = false;
                        sortByName = true;
                        break;
                    default:
                        break;
                }
            }

            if (_sortingContainers.ContainsKey(sc.net.ID.Value))
            {
                player.ChatMessage("Sorting failed, try again.");
                return;
            }

            try
            {
                _sortingContainers[sc.net.ID.Value] = true;
                PerformSort(sc, sortByCategory, sortByName);
                sc.SendNetworkUpdate();
            }
            catch (Exception ex)
            {
                Puts($"Error sorting container (NetId {sc.net.ID}): {ex}");
            }
            finally
            {
                _sortingContainers.Remove(sc.net.ID.Value);
            }

            var sortType = sortByCategory
                         ? sortByName ? "by both, category and name"
                                      : "by category" : "by name";

            player.ChatMessage($"Container sorted {sortType}!");
        }

        #endregion

        #region Sorting Logic

        private readonly Dictionary<ulong, bool> _sortingContainers = new Dictionary<ulong, bool>();

        private void PerformSort(StorageContainer container, bool sortByCategory, bool sortByName)
        {
            var items = container.inventory.itemList;
            if (items == null || items.Count <= 1) return;

            // Make a copy to sort (snapshot Original-Items)
            var sortedItems = new List<Item>(items);
            sortedItems.Sort((a, b) =>
            {
                if (a == null || b == null) return 0;

                string nameA = (a.info?.displayName?.english ?? a.info?.shortname ?? "").Trim();
                string nameB = (b.info?.displayName?.english ?? b.info?.shortname ?? "").Trim();

                string catA = a.info?.category.ToString() ?? "";
                string catB = b.info?.category.ToString() ?? "";

                if (sortByCategory && catA != catB)
                    return string.Compare(catA, catB, StringComparison.Ordinal);

                if (sortByName)
                {
                    int cmp = string.Compare(nameA, nameB, StringComparison.Ordinal);
                    if (cmp != 0) return cmp;
                }

                int skinCmp = a.skin.CompareTo(b.skin);
                if (skinCmp != 0) return skinCmp;

                int condCmp = Math.Round(a.condition, 3).CompareTo(Math.Round(b.condition, 3));
                if (condCmp != 0) return condCmp;

                return a.uid.Value.CompareTo(b.uid.Value);
            });

            // 1. REMOVE Original-Items from the Container, don't clear()!
            var itemsToRemove = new List<Item>(container.inventory.itemList);
            foreach (var item in itemsToRemove)
                item.RemoveFromContainer();

            // 2. Move back Original-Items in sorted sequence into the container keeping add-ons!
            foreach (var item in sortedItems)
            {
                if (item == null) continue;
                item.MoveToContainer(container.inventory);
            }
        }

        #endregion

        #region Helpers

        private BaseEntity GetTargetedContainer(BasePlayer player)
        {
            var eyePos = player.eyes.position;
            var forward = player.eyes.HeadForward();
            var ray = new Ray(eyePos, forward);
            if (Physics.Raycast(ray, out var hit, 5f))
            {
                var entity = hit.GetEntity();
                if (entity is StorageContainer) return entity;
            }
            return null;
        }

        #endregion
    }
}