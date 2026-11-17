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
            LogRebirthTable();
        }

        // How many points selling the factory gives for a run's total earnings. The formula is compiled into the
        // game, so it's logged once to tune the apworld's prestige logic and Upgrade Point size.
        private static bool rebirthLogged;
        private static void LogRebirthTable()
        {
            if (rebirthLogged) return;
            rebirthLogged = true;
            try
            {
                var parts = new List<string>();
                foreach (long m in new long[] { 1_000, 10_000, 100_000, 1_000_000, 10_000_000, 100_000_000, 1_000_000_000, 10_000_000_000, 100_000_000_000 })
                    parts.Add($"${m:N0} -> {PermanentUpgradeManager.CalculateRebirthPoints(m)}");
                Plugin.L.LogInfo("Rebirth points for total earned: " + string.Join(", ", parts));
                var save = SaveManager.Instance?.CurrentSaveData;
                if (save != null)
                    Plugin.L.LogInfo($"This run: earned ${save.totalMoney:N0}, selling now gives {PermanentUpgradeManager.CalculateRebirthPoints((long)save.totalMoney)} points");
            }
            catch (Exception e) { Plugin.L.LogWarning($"Rebirth table failed: {e.Message}"); }
        }

        // ---------- per frame ----------
        internal static void Update(float time)
        {
            if (!ApState.Active) return;
            if (oneTimeSeed != ApState.SeedKey)
            {
                oneTimeSeed = ApState.SeedKey; oneTimeApplied = ApState.LoadOneTimeApplied();
                shownIndex = ApState.LoadShown(); arrival.Clear(); countedItems = -1;
                goalSent = false; SaveApproved = true; PurchasedAtConnect = -1;
            }
            if (PurchasedAtConnect < 0 && Tree != null)
            {
                PurchasedAtConnect = CountPurchased();
                // A save from before this seed would send every bought node at once. Hold checks until the player confirms.
                if (PurchasedAtConnect > 1 && !ApState.SeedSeenBefore()) SaveApproved = false;
                ApState.MarkSeedSeen();
            }

            UpdateDeathLink();
            var received = ApState.Session.Items.AllItemsReceived;
            CountReceived(received);
            NotifyNew(received);
            if (Tree == null) return;
            ApplySkillItems();
            ApplyOneTimeItems(received);

            if (time >= nextPoll)
            {
                nextPoll = time + 1f;
                PollChecks();
            }
        }

        /// <summary>How many of each item this slot has received (for the Items tab and applying skills).</summary>
        internal static Dictionary<string, int> ReceivedCounts = new();
        private static int countedItems = -1;

        private static void CountReceived(IReadOnlyCollection<Archipelago.MultiClient.Net.Models.ItemInfo> received)
        {
            if (received.Count == countedItems && ReceivedCounts.Count > 0) return;
            var counts = new Dictionary<string, int>();
            foreach (var it in received)
            {
                var name = it.ItemName;
                if (name != null) counts[name] = counts.TryGetValue(name, out var c) ? c + 1 : 1;
            }
            ReceivedCounts = counts;
            countedItems = received.Count;
        }

        // Items found while this game was offline come in the item list with no server announcement, so the
        // on-screen messages (ApState.OnServerMessage) never show them. Every received item is looked at once:
        // a couple of seconds after it arrives (the announcement can come just after the item), if it wasn't
        // announced it's shown here. That waits until a run is loaded, so items received on the main menu
        // still pop up when you get into the game. The count of items looked at is saved per seed.
        private static int shownIndex;
        private static readonly List<float> arrival = new();
        private const int OfflineListMax = 6;

        private static void NotifyNew(IReadOnlyCollection<Archipelago.MultiClient.Net.Models.ItemInfo> received)
        {
            var list = received as IList<Archipelago.MultiClient.Net.Models.ItemInfo> ?? received.ToList();
            float now = UnityEngine.Time.unscaledTime;
            while (arrival.Count < list.Count) arrival.Add(now);
            if (Tree == null || shownIndex >= list.Count) return;

            var offline = new List<Archipelago.MultiClient.Net.Models.ItemInfo>();
            int i = shownIndex;
            for (; i < list.Count; i++)
            {
                if (now - arrival[i] < 2f) break;
                var it = list[i];
                Plugin.L.LogInfo($"Received {it.ItemName} from {it.Player?.Name ?? "?"} ({it.LocationName})");
                if (!ApState.WasAnnounced(it)) offline.Add(it);
            }
            if (i == shownIndex) return;
            shownIndex = i;
            ApState.SaveShown(i);
            if (offline.Count == 0) return;

            if (offline.Count > OfflineListMax)
                ApState.Messages.Enqueue(($"Received {offline.Count} items while offline. The last {OfflineListMax - 1} are below; F8 > Items shows everything.", true));
            foreach (var it in offline.Skip(offline.Count > OfflineListMax ? offline.Count - (OfflineListMax - 1) : 0))
            {
                var from = it.Player == null || it.Player.Slot == 0 ? "the server" : it.Player.Name;
                ApState.Messages.Enqueue(($"Received {ApState.ItemText(it.ItemName)} from {ApState.PlayerText(from)} <color=#a0a0a8>(while offline)</color>", true));
            }
        }

        private static void ApplySkillItems()
        {
            var counts = ReceivedCounts;
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
                    // ChangeMoney(x) sets the balance to x rather than adding, so add to the current balance ourselves.
                    long before = money.Money;
                    money.SetMoney(before + amount);
                    Plugin.L.LogInfo($"Cash Bundle: +${amount:N0} (${before:N0} -> ${money.Money:N0})");
                }
                else if (name == UpgradePoint)
                {
                    save.upgradePoints += ApState.UpgradePointSize;
                    Plugin.L.LogInfo($"Upgrade Point: +{ApState.UpgradePointSize} (now {save.upgradePoints})");
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

        // ---- Death Link ----
        // The player can't die in this game, so a received death link ends the shift instead: the shift timer is
        // run out, and if the night shift follows, that one too, so the elevator comes down right away. Cans still
        // on the line when the shift ends are handled however the game normally handles them.
        // A shift is running while its timer icon (sun for day, moon for night) is showing. Between shifts
        // (elevator, skill tree, menus) a death link does nothing.
        private static bool dlActive;
        private static float dlStart, dlIdleSince = -1, lastTimer = -1;
        private static int timerDirection;   // -1 counts down, +1 counts up, 0 not seen yet (assume down)

        private static bool InShift(GameManager gm)
        {
            try
            {
                if (gm.timerParent != null && !gm.timerParent.activeInHierarchy) return false;
                return (gm.sunTimer != null && gm.sunTimer.activeInHierarchy) || (gm.nightTimer != null && gm.nightTimer.activeInHierarchy);
            }
            catch { return false; }
        }

        private static void UpdateDeathLink()
        {
            var gm = GameManager.Instance;
            float now = UnityEngine.Time.unscaledTime;

            // Learn which way the shift timer runs from normal play.
            if (gm != null && InShift(gm) && !gm.IsPaused && !dlActive)
            {
                float t = gm.Timer, d = t - lastTimer;
                if (lastTimer >= 0 && Math.Abs(d) > 0.0001f && Math.Abs(d) < 1f) timerDirection = d < 0 ? -1 : 1;
                lastTimer = t;
            }

            var from = ApState.DeathLinkFrom;
            if (from != null)
            {
                ApState.DeathLinkFrom = null;
                if (gm == null || !InShift(gm))
                {
                    ApState.Messages.Enqueue(($"Death Link from {ApState.PlayerText(from)}, but no shift is running. Safe!", true));
                    Plugin.L.LogInfo("Death Link ignored: no shift running");
                }
                else
                {
                    dlActive = true; dlStart = now; dlIdleSince = -1;
                    ApState.Messages.Enqueue(($"<color=#ff6060>Death Link</color> from {ApState.PlayerText(from)}: your shift is over!", true));
                    Plugin.L.LogInfo($"Death Link: ending the shift (timer {gm.Timer:F1}, runs {(timerDirection > 0 ? "up" : "down")}, " +
                                     $"sun {gm.sunTimer?.activeInHierarchy}, night {gm.nightTimer?.activeInHierarchy}, elevator {gm.elevatorState})");
                }
            }

            UpdateCursedCan(gm);

            if (!dlActive) return;
            if (gm == null || now - dlStart > 45f) { dlActive = false; return; }
            if (InShift(gm))
            {
                dlIdleSince = -1;
                try { gm.Timer = timerDirection > 0 ? Math.Max(gm.dayDuration, gm.nightShiftDuration) + 1 : 0f; }
                catch (Exception e) { Plugin.L.LogError($"Death Link timer failed: {e.Message}"); dlActive = false; }
            }
            else if (dlIdleSince < 0) dlIdleSince = now;
            else if (now - dlIdleSince > 8f) { dlActive = false; Plugin.L.LogInfo("Death Link: shift ended"); }
        }

        // Sending: the player can't die, so each day shift rolls once (death_link_chance percent). On a hit, a
        // "cursed can" ships at a random point in that day shift and sends the death link. Rolling per day rather
        // than per creature or can keeps it even across the run instead of piling up when the factory is huge.
        // The last rolled day is saved per seed, so reloading a day doesn't roll it again.
        private static int rolledDay = -1;
        private static string rolledSeed;
        private static float cursedAt = -1, shiftSeconds;
        private static readonly System.Random rng = new();

        private static void UpdateCursedCan(GameManager gm)
        {
            if (!ApState.DeathLinkOn || ApState.DeathLinkChance <= 0 || gm == null) return;
            var save = SaveManager.Instance?.CurrentSaveData;
            if (save == null) return;
            if (rolledSeed != ApState.SeedKey) { rolledSeed = ApState.SeedKey; rolledDay = ApState.LoadRolledDay(); cursedAt = -1; }

            bool dayShift = false;
            try { dayShift = InShift(gm) && gm.sunTimer != null && gm.sunTimer.activeInHierarchy; } catch { }
            if (!dayShift) { if (cursedAt >= 0 && !InShift(gm)) cursedAt = -1; return; }   // a cursed can only ships in its own day

            if (save.day != rolledDay)
            {
                rolledDay = save.day;
                ApState.SaveRolledDay(rolledDay);
                shiftSeconds = 0;
                cursedAt = -1;
                if (rng.Next(100) < ApState.DeathLinkChance)
                {
                    int len = gm.dayDuration > 0 ? gm.dayDuration : 120;
                    cursedAt = len * (0.2f + 0.6f * (float)rng.NextDouble());
                    Plugin.L.LogInfo($"Day {save.day}: a cursed can will ship after {cursedAt:F0}s of the shift");
                }
            }
            if (cursedAt < 0 || gm.IsPaused || dlActive) return;
            long cans = save.lifetimeStats?.totalCans ?? 0;
            if (shiftSeconds < cursedAt)
            {
                shiftSeconds += UnityEngine.Time.deltaTime;
                cansAtDue = cans;   // the cursed can is the first one made after this point
                return;
            }
            if (cans <= cansAtDue) return;   // waiting for the factory to make a can
            cursedAt = -1;
            ApState.SendDeathLink("shipped a cursed can");
            // Archipelago doesn't send your own death link back to you, so end this shift here too.
            dlActive = true; dlStart = UnityEngine.Time.unscaledTime; dlIdleSince = -1;
            ApState.Messages.Enqueue(("<color=#ff6060>A cursed can went down the line!</color> Death Link sent, and your shift is over.", true));
            Plugin.L.LogInfo("Cursed can shipped: Death Link sent, ending own shift");
        }

        private static long cansAtDue;

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
