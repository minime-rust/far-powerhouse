namespace Oxide.Plugins
{
    [Info("FARTCLockAuthGuard", "miniMe", "1.0.1")]
    [Description("Prevents auth list changes on Tool Cupboards while unlocked.")]
    public class FARTCLockAuthGuard : RustPlugin
    {
        const string PermBypass = "tcauthguard.bypass";
        const string _missingLock = "This TC needs to be locked before you can change auth!";
        void Init() => permission.RegisterPermission(PermBypass, this);

        object OnCupboardAuthorize(BuildingPrivlidge tc, BasePlayer player)
        {
            if (tc == null || HasBypass(player)) return null;

            // Allow initial owner authorization on a freshly placed TC (auth list empty),
            if ((tc.authorizedPlayers == null || tc.authorizedPlayers.Count == 0) &&
                tc.OwnerID != 0UL && tc.OwnerID == player.userID) return null;

            if (IsCupboardLocked(tc)) return null;  // locked: vanilla behavior allowed
            Notify(player); return true;            // no lock: block new auths
        }

        object OnCupboardClearList(BuildingPrivlidge tc, BasePlayer player)
        {
            if (tc == null || HasBypass(player) || IsCupboardLocked(tc)) return null;
            Notify(player); return true;            // no lock: block auth clearing
        }

        object OnCupboardDeauthorize(BuildingPrivlidge tc, BasePlayer player)
        {
            if (tc == null || HasBypass(player) || IsCupboardLocked(tc)) return null;
            Notify(player); return true;            // no lock: block de-auths
        }

        static bool IsCupboardLocked(BuildingPrivlidge tc)
        {
            var lockEntity = tc.GetSlot(BaseEntity.Slot.Lock) as BaseLock;
            return lockEntity != null && lockEntity.IsLocked();
        }

        bool HasBypass(BasePlayer player) =>
            player != null && permission.UserHasPermission(player.UserIDString, PermBypass);

        static void Notify(BasePlayer player)
        {
            if (player != null)
                player.ChatMessage(_missingLock);
        }
    }
}