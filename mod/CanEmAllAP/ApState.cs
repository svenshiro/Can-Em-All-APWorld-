using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.BounceFeatures.DeathLink;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.MessageLog.Messages;
using Archipelago.MultiClient.Net.Models;
using Newtonsoft.Json.Linq;

namespace CanEmAllAP
{
    /// <summary>Archipelago connection and the per-seed data the game side needs.</summary>
    internal static class ApState
    {
        internal static ArchipelagoSession Session;
        internal static bool Connecting;
        internal static string Status = "Not connected";
        internal static bool Active => Session != null && Session.Socket.Connected && SlotReady;
        internal static bool SlotReady;

        // slot data
        internal static long LocationBase = 6_330_000;
        internal static Dictionary<string, List<string>> ItemSkills = new();
        internal static Dictionary<string, string> SkillRequires = new();   // conveyor skill -> its machine skill
        internal static HashSet<string> PrestigeItems = new();               // empty on 0.2.0 seeds
        internal static bool PrestigeOn = true;
        internal static List<long> ChildrenFedThresholds = new();
        internal static List<long> SacrificeThresholds = new();
        internal static string Goal = "ending";
        internal static long GoalChildrenFed;
        internal static int CashBundleSeconds = 60;
        internal static int UpgradePointSize = 1;
        internal static string SeedKey = "";

        // main-thread message queue for the overlay
        internal static readonly ConcurrentQueue<(string text, bool mine)> Messages = new();   // mine = involves this slot
        private static readonly HashSet<long> pendingSend = new();
        private static readonly object sendLock = new();

        internal static void Connect()
        {
            if (Connecting) return;
            var server = Plugin.Server.Value.Trim();
            var slot = Plugin.Slot.Value.Trim();
            if (string.IsNullOrEmpty(slot)) { Status = "Enter a slot name first"; return; }
            Connecting = true;
            Status = $"Connecting to {server}...";
            Task.Run(() =>
            {
                try
                {
                    Disconnect();
                    var session = ArchipelagoSessionFactory.CreateSession(server);
                    var result = session.TryConnectAndLogin(Plugin.GameName, slot, ItemsHandlingFlags.AllItems,
                        new Version(0, 6, 0), password: string.IsNullOrEmpty(Plugin.Password.Value) ? null : Plugin.Password.Value);
                    if (result is LoginSuccessful ok)
                    {
                        ReadSlotData(ok.SlotData);
                        var key = $"{session.RoomState.Seed}_{slot}";
                        if (key != SeedKey) Announced.Clear();
                        SeedKey = key;
                        Session = session;
                        session.Socket.SocketClosed += reason => { SlotReady = false; Status = $"Disconnected: {reason}"; };
                        session.MessageLog.OnMessageReceived += OnServerMessage;
                        SetUpDeathLink(session, slot);
                        SlotReady = true;
                        Status = $"Connected as {slot}";
                        Messages.Enqueue(($"Connected to Archipelago as {slot}", true));
                        Plugin.L.LogInfo($"Connected. Seed {session.RoomState.Seed}");
                    }
                    else
                    {
                        var f = (LoginFailure)result;
                        Status = "Connection failed: " + string.Join("; ", f.Errors);
                        Plugin.L.LogWarning(Status);
                    }
                }
                catch (Exception e)
                {
                    Status = "Connection failed: " + e.Message;
                    Plugin.L.LogError(e);
                }
                finally { Connecting = false; }
            });
        }

        /// <summary>Item movements: what you received and from whom, what you sent and to whom, and (option
        /// ShowAllItems) what other players send each other in any game. Chat and hints are left out.</summary>
        private static void OnServerMessage(LogMessage m)
        {
            try
            {
                if (m is not ItemSendLogMessage s || m is HintItemSendLogMessage) return;
                bool toMe = s.IsReceiverTheActivePlayer, fromMe = s.IsSenderTheActivePlayer;
                if (toMe) Announced[(s.Item.LocationId, s.Sender.Slot)] = 0;
                if (!toMe && !fromMe && !Plugin.ShowAllItems.Value) return;
                var item = ItemText(s.Item.ItemName);
                string text =
                    toMe && fromMe ? $"Found your own {item}" :
                    toMe ? $"Received {item} from {PlayerText(s.Sender.Name)}" :
                    fromMe ? $"Sent {item} to {PlayerText(s.Receiver.Name)}" :
                    s.Sender.Slot == s.Receiver.Slot ? $"{PlayerText(s.Sender.Name)} found their {item}" :
                    $"{PlayerText(s.Sender.Name)} sent {item} to {PlayerText(s.Receiver.Name)}";
                Messages.Enqueue((text, toMe || fromMe));
            }
            catch (Exception e) { Plugin.L.LogWarning($"Message failed: {e.Message}"); }
        }

        /// <summary>Names from other games could contain '&lt;', which the text renderer would read as a tag.</summary>
        private static string Clean(string s) => (s ?? "?").Replace("<", "‹").Replace(">", "›");
        internal static string ItemText(string name) => $"<color=#f0c050>{Clean(name)}</color>";
        internal static string PlayerText(string name) => $"<color=#7fc8ff>{Clean(name)}</color>";

        /// <summary>Items the server announced to this slot while connected, by (location, sender slot). Anything
        /// received without an announcement was found while this game was offline.</summary>
        internal static readonly ConcurrentDictionary<(long loc, int slot), byte> Announced = new();
        internal static bool WasAnnounced(ItemInfo it) => Announced.ContainsKey((it.LocationId, it.Player?.Slot ?? -1));

        /// <summary>The socket can drop without a close event (seen when the server goes away), which left the
        /// status reading "Connected". Called every frame.</summary>
        internal static void CheckConnection()
        {
            if (Connecting || Active || Session == null) return;
            if (Status.StartsWith("Connected"))
            {
                Status = "Connection lost. Press Reconnect.";
                Messages.Enqueue(("Lost connection to the Archipelago server", true));
                Plugin.L.LogWarning("Connection lost");
            }
        }

        // ---- Death Link ----
        internal static bool DeathLinkOn;
        internal static int DeathLinkChance = 7;   // percent per day
        private static DeathLinkService deathLink;
        /// <summary>Set by the network thread, handled on the main thread by Game.UpdateDeathLink.</summary>
        internal static volatile string DeathLinkFrom;

        private static void SetUpDeathLink(ArchipelagoSession session, string slot)
        {
            deathLink = null;
            if (!DeathLinkOn) return;
            try
            {
                var dl = session.CreateDeathLinkService();
                dl.OnDeathLinkReceived += d =>
                {
                    if (d.Source == slot) return;
                    DeathLinkFrom = string.IsNullOrEmpty(d.Cause) ? d.Source : $"{d.Source} ({d.Cause})";
                    Plugin.L.LogInfo($"Death Link received from {DeathLinkFrom}");
                };
                dl.EnableDeathLink();
                deathLink = dl;
                Plugin.L.LogInfo("Death Link on");
            }
            catch (Exception e) { Plugin.L.LogError($"Death Link setup failed: {e.Message}"); }
        }

        internal static void SendDeathLink(string cause)
        {
            var dl = deathLink;
            var slot = Plugin.Slot.Value.Trim();
            if (dl == null || !Active) return;
            Task.Run(() =>
            {
                try { dl.SendDeathLink(new DeathLink(slot, cause)); Plugin.L.LogInfo($"Death Link sent: {cause}"); }
                catch (Exception e) { Plugin.L.LogError($"Sending Death Link failed: {e.Message}"); }
            });
        }

        internal static void Disconnect()
        {
            var s = Session;
            Session = null;
            SlotReady = false;
            try { s?.Socket.DisconnectAsync(); } catch { }
        }

        private static void ReadSlotData(Dictionary<string, object> data)
        {
            T Get<T>(string key, T fallback)
            {
                if (data != null && data.TryGetValue(key, out var v) && v != null)
                {
                    try { return v is JToken t ? t.ToObject<T>() : (T)Convert.ChangeType(v, typeof(T)); } catch { }
                }
                return fallback;
            }
            LocationBase = Get("location_id_base", 6_330_000L);
            ItemSkills = Get("item_skills", new Dictionary<string, List<string>>());
            SkillRequires = Get("skill_requires", new Dictionary<string, string>());   // absent on 0.1.x seeds
            PrestigeItems = new HashSet<string>(Get("prestige_items", new List<string>()));
            PrestigeOn = Get("prestige_upgrades", true);
            ChildrenFedThresholds = Get("children_fed_thresholds", new List<long>());
            SacrificeThresholds = Get("sacrifice_thresholds", new List<long>());
            Goal = Get("goal", "ending");
            GoalChildrenFed = Get("goal_children_fed", 250_000L);
            CashBundleSeconds = Get("cash_bundle_seconds", 60);
            UpgradePointSize = Math.Max(1, Get("upgrade_point_size", 1));   // seeds before 0.3.0 gave 1 point
            DeathLinkOn = Get("death_link", false);
            DeathLinkChance = Get("death_link_chance", 7);
        }

        /// <summary>Queue location ids to send; duplicates are ignored.</summary>
        internal static void Check(IEnumerable<long> ids)
        {
            var s = Session;
            if (s == null || !SlotReady) return;
            long[] toSend;
            lock (sendLock)
            {
                toSend = ids.Where(id => !s.Locations.AllLocationsChecked.Contains(id) && pendingSend.Add(id)).ToArray();
            }
            if (toSend.Length == 0) return;
            Task.Run(() =>
            {
                try { s.Locations.CompleteLocationChecks(toSend); }
                catch (Exception e) { Plugin.L.LogError($"Sending checks failed: {e.Message}"); }
                finally { lock (sendLock) foreach (var id in toSend) pendingSend.Remove(id); }
            });
        }

        internal static void Check(long id) => Check(new[] { id });

        internal static void SendGoal()
        {
            var s = Session;
            if (s == null || !SlotReady) return;
            try { s.SetGoalAchieved(); Messages.Enqueue(("Goal complete!", true)); Plugin.L.LogInfo("Goal sent"); }
            catch (Exception e) { Plugin.L.LogError($"Sending goal failed: {e.Message}"); }
        }

        // ---- per-seed persistent counter for one-time items (cash, upgrade points) ----
        private static string StatePath => Path.Combine(Plugin.DataDir, $"state_{San(SeedKey)}.txt");
        private static string San(string s) => string.Concat(s.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '-'));

        private static string SeenPath => Path.Combine(Plugin.DataDir, $"seen_{San(SeedKey)}.txt");
        internal static bool SeedSeenBefore() => File.Exists(SeenPath);
        internal static void MarkSeedSeen() { try { if (!File.Exists(SeenPath)) File.WriteAllText(SeenPath, DateTime.Now.ToString("s")); } catch { } }

        // ---- per-seed count of received items already shown on screen ----
        private static string ShownPath => Path.Combine(Plugin.DataDir, $"shown_{San(SeedKey)}.txt");
        internal static int LoadShown()
        {
            try { return File.Exists(ShownPath) ? int.Parse(File.ReadAllText(ShownPath).Trim()) : 0; } catch { return 0; }
        }
        internal static void SaveShown(int n)
        {
            try { File.WriteAllText(ShownPath, n.ToString()); } catch (Exception e) { Plugin.L.LogError(e.Message); }
        }

        private static string RolledPath => Path.Combine(Plugin.DataDir, $"deathlink_{San(SeedKey)}.txt");
        internal static int LoadRolledDay()
        {
            try { return File.Exists(RolledPath) ? int.Parse(File.ReadAllText(RolledPath).Trim()) : -1; } catch { return -1; }
        }
        internal static void SaveRolledDay(int day)
        {
            try { File.WriteAllText(RolledPath, day.ToString()); } catch (Exception e) { Plugin.L.LogError(e.Message); }
        }

        internal static int LoadOneTimeApplied()
        {
            try { return File.Exists(StatePath) ? int.Parse(File.ReadAllText(StatePath).Trim()) : 0; } catch { return 0; }
        }

        internal static void SaveOneTimeApplied(int n)
        {
            try { File.WriteAllText(StatePath, n.ToString()); } catch (Exception e) { Plugin.L.LogError(e.Message); }
        }
    }
}
