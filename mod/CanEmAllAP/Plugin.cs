using System;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace CanEmAllAP
{
    [BepInPlugin(Guid, "Can 'Em All Archipelago", Version)]
    public class Plugin : BasePlugin
    {
        public const string Guid = "tyler.canemall.archipelago";
        public const string Version = "0.2.0";
        public const string GameName = "Can 'Em All";

        internal static ManualLogSource L;
        internal static string DataDir;
        internal static ConfigEntry<string> Server, Slot, Password;
        internal static ConfigEntry<bool> AutoConnect;
        internal static ConfigEntry<bool> VerboseLog;

        public override void Load()
        {
            L = Log;
            DataDir = Path.Combine(Paths.BepInExRootPath, "CanEmAllAP");
            Directory.CreateDirectory(DataDir);

            Server = Config.Bind("Connection", "Server", "localhost:38281", "Archipelago server address (host:port)");
            Slot = Config.Bind("Connection", "Slot", "", "Your slot (player) name");
            Password = Config.Bind("Connection", "Password", "", "Room password, if any");
            AutoConnect = Config.Bind("Connection", "AutoConnect", true, "Connect automatically when the game starts if a slot name is set");
            VerboseLog = Config.Bind("Debug", "VerboseLog", false, "Log every skill/milestone event");

            var harmony = new Harmony(Guid);
            harmony.PatchAll(typeof(Plugin).Assembly);

            ClassInjector.RegisterTypeInIl2Cpp<ApBehaviour>();
            var go = new GameObject("CanEmAllAP");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.AddComponent<ApBehaviour>();

            L.LogInfo($"Can 'Em All Archipelago {Version} loaded. Press F8 in game for the Archipelago panel.");
        }
    }

    /// <summary>
    /// Skill effects only come from Archipelago items. Hooking ApplySkill itself crashes this game
    /// (the methods are too small to detour safely), so the two places that call it for tree nodes
    /// are replaced instead: buying a node (SkillNode.OnClick) and restoring bought nodes on load
    /// (SkillNode.ApplyInitialState). The Master Key always works normally.
    /// </summary>
    internal static class SkillGate
    {
        /// <summary>Gate effects when connected, or when this game is set up to auto-connect to a slot
        /// (so a save loaded before the connection finishes isn't given its bought effects).</summary>
        internal static bool Enabled => ApState.Active || (Plugin.AutoConnect.Value && !string.IsNullOrEmpty(Plugin.Slot.Value));

        private static readonly MethodInfo shake = AccessTools.Method(typeof(SkillNode), "Shake");

        internal static void Apply(Skill skill)
        {
            if (skill == null) return;
            try { skill.ApplySkill(); }
            catch (Exception e) { Plugin.L.LogError($"ApplySkill({skill.skillId}) failed: {e}"); }
        }

        internal static void SetPurchasedLook(SkillNode node)
        {
            try { if (node.background != null) node.background.color = node.purchasedColor; } catch { }
            try { if (node.icon != null) node.icon.color = node.purchasedIconColor; } catch { }
        }

        internal static void Shake(SkillNode node)
        {
            try { shake?.Invoke(node, null); } catch { }
        }
    }

    [HarmonyPatch(typeof(SkillNode), nameof(SkillNode.OnClick))]
    static class Hook_BuyNode
    {
        // Same as the game's OnClick, minus skill.ApplySkill(). Anything other than a successful purchase
        // (already bought, locked, can't afford) goes to the original so its sounds/shakes still happen.
        static bool Prefix(SkillNode __instance)
        {
            if (ApBehaviour.BlocksGameInput) return false;   // a click on the F8 panel must not buy the node behind it
            try
            {
                if (!SkillGate.Enabled) return true;
                var s = __instance.skill;
                if (s == null || s.skillId == SkillId.MasterKey || s.isPurchased || __instance.IsLocked) return true;
                var mm = MoneyManager.Instance;
                if (mm == null || mm.Money < s.price) return true;

                UISound.Instance?.PlayPurchaseClip();
                mm.SetMoney(mm.Money - s.price);
                SaveManager.Instance?.CurrentSaveData?.unlockedSkills?.Add(s.skillId);
                SkillGate.SetPurchasedLook(__instance);
                s.isPurchased = true;
                __instance.RefreshLockState();
                SkillPopUp.Instance?.Show(__instance);
                SkillGate.Shake(__instance);
                __instance.skillTreeView?.NodePurchased(__instance);
                return false;
            }
            catch (Exception e)
            {
                Plugin.L.LogError($"Buying node failed, letting the game handle it: {e}");
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(SkillNode), nameof(SkillNode.ApplyInitialState))]
    static class Hook_RestoreNode
    {
        // Called on load for every bought node: colours + lock state + ApplySkill. Skip the ApplySkill.
        static bool Prefix(SkillNode __instance)
        {
            try
            {
                if (!SkillGate.Enabled) return true;
                var s = __instance.skill;
                if (s == null || s.skillId == SkillId.MasterKey) return true;
                SkillGate.SetPurchasedLook(__instance);
                __instance.RefreshLockState();
                return false;
            }
            catch (Exception e)
            {
                Plugin.L.LogError($"Restoring node failed, letting the game handle it: {e}");
                return true;
            }
        }
    }

    // ---- game hooks ----

    [HarmonyPatch(typeof(SkillTreeView), nameof(SkillTreeView.ApplySave))]
    static class Hook_TreeApplySave
    {
        static void Postfix(SkillTreeView __instance) { Game.Tree = __instance; Game.OnRunLoaded("tree loaded"); }
    }

    [HarmonyPatch(typeof(SkillTreeView), nameof(SkillTreeView.ResetSkillTree))]
    static class Hook_TreeReset
    {
        static void Postfix(SkillTreeView __instance) { Game.Tree = __instance; Game.OnRunLoaded("factory sold / tree reset"); }
    }

    [HarmonyPatch(typeof(PermanentUpgradeManager), nameof(PermanentUpgradeManager.ApplySave))]
    static class Hook_PrestigeApplySave
    {
        static void Postfix(PermanentUpgradeManager __instance) { Game.Prestige = __instance; Game.OnPrestigeLoaded(); }
    }

    [HarmonyPatch(typeof(SteamManager), nameof(SteamManager.Unlock))]
    static class Hook_Achievement
    {
        static void Prefix(SteamAchievement achievement) => Game.OnAchievement(achievement);
    }

    // While the F8 panel is open the game ignores Escape (pause), and the skill tree doesn't drag or zoom.
    [HarmonyPatch(typeof(GameManager), nameof(GameManager.UpdatePauseInput))]
    static class Hook_PauseInput
    {
        static bool Prefix() => !ApBehaviour.BlocksGameInput;
    }

    // Escape on the skill tree screen closes it and starts the next day.
    [HarmonyPatch(typeof(GameManager), nameof(GameManager.UpdatePanelInput))]
    static class Hook_PanelInput
    {
        static bool Prefix() => !ApBehaviour.BlocksGameInput;
    }

    [HarmonyPatch(typeof(SkillTreeView), nameof(SkillTreeView.UpdateDragInput))]
    static class Hook_TreeDrag
    {
        static bool Prefix() => !ApBehaviour.BlocksGameInput;
    }

    [HarmonyPatch(typeof(SkillTreeView), nameof(SkillTreeView.UpdateZoomInput))]
    static class Hook_TreeZoom
    {
        static bool Prefix() => !ApBehaviour.BlocksGameInput;
    }

    [HarmonyPatch(typeof(EndingCinematic), "OnEnable")]
    static class Hook_Ending
    {
        static void Postfix() => Game.OnEnding();
    }
}
