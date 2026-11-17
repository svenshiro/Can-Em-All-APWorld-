using System;
using System.Collections.Generic;
using System.Linq;

namespace CanEmAllAP
{
    /// <summary>Everything that touches the game: applying items, detecting checks, milestones, goal.</summary>
    internal static class Game
    {
        internal static SkillTreeView Tree;
        internal static PermanentUpgradeManager Prestige;

        private static readonly HashSet<string> appliedTree = new();
        private static readonly HashSet<string> appliedPrestige = new();
        private static int notifiedIndex = -1;
        private static int oneTimeApplied = -1;
        private static string oneTimeSeed = null;
        private static float nextPoll;
        private static bool goalSent;
        /// <summary>False while connected to a new seed with a save that already has skills bought.</summary>
        internal static bool SaveApproved = true;
        internal static int PurchasedAtConnect;

        private const string CashBundle = "Cash Bundle";
        private const string UpgradePoint = "Upgrade Point";
        private const int MilestoneOffset = 1000, SacrificeOffset = 1100, AchievementOffset = 1200;

        internal static void OnRunLoaded(string why)
        {
            appliedTree.Clear();
            Verbose($"Run loaded ({why}); received skills will be applied again");
        }

        internal static void OnPrestigeLoaded()
        {
            appliedPrestige.Clear();
        }

        // ---------- per frame ----------
        internal static void Update(float time)
        {
            if (!ApState.Active) return;
            if (oneTimeSeed != ApState.SeedKey) { oneTimeSeed = ApState.SeedKey; oneTimeApplied = ApState.LoadOneTimeApplied(); notifiedIndex = -1; goalSent = false; SaveApproved = true; PurchasedAtConnect = -1; }
            if (PurchasedAtConnect < 0 && Tree != null)
            {
                PurchasedAtConnect = CountPurchased();
                // A save from before this seed would send every bought node at once. Hold checks until the player confirms.
                if (PurchasedAtConnect > 1 && !ApState.SeedSeenBefore()) SaveApproved = false;
                ApState.MarkSeedSeen();
            }

            var received = ApState.Session.Items.AllItemsReceived;
            NotifyNew(received);
            if (Tree == null) return;
            ApplySkillItems(received);
            ApplyOneTimeItems(received);

            if (time >= nextPoll)
            {
                nextPoll = time + 1f;
                PollChecks();
            }
        }

        private static void NotifyNew(IReadOnlyCollection<Archipelago.MultiClient.Net.Models.ItemInfo> received)
        {
            var list = received as IList<Archipelago.MultiClient.Net.Models.ItemInfo> ?? received.ToList();
            if (notifiedIndex < 0) { notifiedIndex = list.Count; return; }   // don't spam the backlog on connect
            for (int i = notifiedIndex; i < list.Count; i++)
            {
                var it = list[i];
                var from = it.Player?.Name ?? "?";
                // On-screen messages come from the server's item messages (ApState.OnServerMessage); this only logs.
                Plugin.L.LogInfo($"Received {it.ItemName} from {from} ({it.LocationName})");
            }
            notifiedIndex = list.Count;
        }

        private static void ApplySkillItems(IReadOnlyCollection<Archipelago.MultiClient.Net.Models.ItemInfo> received)
        {
            var counts = new Dictionary<string, int>();
            foreach (var it in received)
            {
                var name = it.ItemName;
                if (name == null || name == CashBundle || name == UpgradePoint) continue;
                counts[name] = counts.TryGetValue(name, out var c) ? c + 1 : 1;
            }
            var lookup = BuildSkillLookup();
            // Two passes: a machine received in the same batch as its conveyors may be applied after them in the
            // first pass, and the second pass then lets those conveyors through.
            int waiting = 0;
            for (int pass = 0; pass < 2; pass++)
            {
                waiting = 0;
                foreach (var kv in counts)
                {
                    if (!ApState.ItemSkills.TryGetValue(kv.Key, out var skills)) continue;
                    int have = Math.Min(kv.Value, skills.Count);
                    for (int i = 0; i < have; i++)
                    {
                        var id = skills[i];
                        // A conveyor whose machine isn't here yet waits, and so does everything after it in the order.
                        if (ApState.SkillRequires.TryGetValue(id, out var req) && !appliedTree.Contains(req))
                        {
                            waiting += have - i;
                            break;
                        }
                        if (!lookup.TryGetValue(id, out var entry)) continue;
                        var set = entry.prestige ? appliedPrestige : appliedTree;
                        if (set.Contains(id)) continue;
                        SkillGate.Apply(entry.skill);
                        set.Add(id);
                        Verbose($"Applied {id} ({kv.Key} #{i + 1})");
                    }
                }
            }
            WaitingForMachine = waiting;
        }

        /// <summary>Received conveyors held back until the machine for their line arrives.</summary>
        internal static int WaitingForMachine;

        private static Dictionary<string, (Skill skill, bool prestige)> BuildSkillLookup()
        {
            var d = new Dictionary<string, (Skill, bool)>();
            try
            {
                var nodes = Tree.nodes;
                for (int i = 0; i < nodes.Length; i++)
                {
                    var s = nodes[i]?.skill;
                    if (s != null) d[s.skillId.ToString()] = (s, false);
                }
                if (Prestige != null)
                {
                    var p = Prestige.nodes;
                    for (int i = 0; i < p.Length; i++)
                    {
                        var s = p[i]?.skill;
                        if (s != null) d[s.skillId.ToString()] = (s, true);
                    }
                }
            }
            catch (Exception e) { Plugin.L.LogError($"Skill lookup failed: {e.Message}"); }
            return d;
        }

        private static void ApplyOneTimeItems(IReadOnlyCollection<Archipelago.MultiClient.Net.Models.ItemInfo> received)
        {
            var list = received as IList<Archipelago.MultiClient.Net.Models.ItemInfo> ?? received.ToList();
            if (oneTimeApplied >= list.Count) return;
            var money = MoneyManager.Instance;
            var save = SaveManager.Instance?.CurrentSaveData;
            if (money == null || save == null) return;
            for (int i = oneTimeApplied; i < list.Count; i++)
            {
                var name = list[i].ItemName;
                if (name == CashBundle)
                {
                    long amount = CashBundleAmount(save);
                    money.ChangeMoney(amount);
                    Plugin.L.LogInfo($"Cash Bundle: +${amount:N0}");
                }
                else if (name == UpgradePoint)
                {
                    save.upgradePoints += 1;
                    try { Prestige?.UpdatePointsText(); } catch { }
                }
            }
            oneTimeApplied = list.Count;
            ApState.SaveOneTimeApplied(oneTimeApplied);
        }

        private static long CashBundleAmount(SaveData save)
        {
            int dayLen = 120;
            try { var gm = GameManager.Instance; if (gm != null && gm.dayDuration > 0) dayLen = gm.dayDuration; } catch { }
            double seconds = Math.Max(1, save.day) * (double)dayLen;
            double perSecond = save.totalMoney / seconds;
            return Math.Max(100L, (long)(perSecond * ApState.CashBundleSeconds));
        }

        // ---------- checks ----------
        private static int CountPurchased()
        {
            int n = 0;
            try { var nodes = Tree.nodes; for (int i = 0; i < nodes.Length; i++) if (nodes[i]?.skill?.isPurchased == true) n++; } catch { }
            return n;
        }

        private static void PollChecks()
        {
            if (!SaveApproved) return;
            var ids = new List<long>();
            try
            {
                var nodes = Tree.nodes;
                for (int i = 0; i < nodes.Length; i++)
                {
                    var s = nodes[i]?.skill;
                    if (s != null && s.isPurchased) ids.Add(ApState.LocationBase + (int)s.skillId);
                }
                if (Prestige != null)
                {
                    var p = Prestige.nodes;
                    for (int i = 0; i < p.Length; i++)
                    {
                        var s = p[i]?.skill;
                        if (s != null && s.isPurchased) ids.Add(ApState.LocationBase + (int)s.skillId);
                    }
                }
                var life = SaveManager.Instance?.CurrentSaveData?.lifetimeStats;
                if (life != null)
                {
                    for (int i = 0; i < ApState.ChildrenFedThresholds.Count; i++)
                        if (life.totalCans >= ApState.ChildrenFedThresholds[i]) ids.Add(ApState.LocationBase + MilestoneOffset + i);
                    for (int i = 0; i < ApState.SacrificeThresholds.Count; i++)
                        if (life.totalKilled >= ApState.SacrificeThresholds[i]) ids.Add(ApState.LocationBase + SacrificeOffset + i);
                    if (!goalSent && ApState.Goal == "children_fed" && life.totalCans >= ApState.GoalChildrenFed)
                    {
                        goalSent = true;
                        ApState.SendGoal();
                    }
                }
            }
            catch (Exception e) { Plugin.L.LogError($"Check poll failed: {e.Message}"); }
            if (ids.Count > 0) ApState.Check(ids);
        }

        internal static void OnAchievement(SteamAchievement a)
        {
            if (!ApState.Active) return;
            ApState.Check(ApState.LocationBase + AchievementOffset + (int)a);
            Verbose($"Achievement {a}");
        }

        internal static void OnEnding()
        {
            Plugin.L.LogInfo("Ending cinematic started");
            if (ApState.Active && ApState.Goal == "ending" && !goalSent)
            {
                goalSent = true;
                ApState.SendGoal();
            }
        }

        /// <summary>Next Children Fed milestone, for the overlay.</summary>
        internal static string NextMilestoneText()
        {
            var life = SaveManager.Instance?.CurrentSaveData?.lifetimeStats;
            if (life == null || ApState.ChildrenFedThresholds.Count == 0) return null;
            foreach (var t in ApState.ChildrenFedThresholds)
                if (life.totalCans < t) return $"Children Fed: {life.totalCans:N0} / next check at {t:N0}";
            return $"Children Fed: {life.totalCans:N0} (all milestones done)";
        }

        private static void Verbose(string s) { if (Plugin.VerboseLog.Value) Plugin.L.LogInfo(s); }
    }
}
