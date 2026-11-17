using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Enums;
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
        internal static List<long> ChildrenFedThresholds = new();
        internal static List<long> SacrificeThresholds = new();
        internal static string Goal = "ending";
        internal static long GoalChildrenFed;
        internal static int CashBundleSeconds = 60;
        internal static string SeedKey = "";

        // main-thread message queue for the overlay
        internal static readonly ConcurrentQueue<string> Messages = new();
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
                        SeedKey = $"{session.RoomState.Seed}_{slot}";
                        Session = session;
                        session.Socket.SocketClosed += reason => { SlotReady = false; Status = $"Disconnected: {reason}"; };
                        session.MessageLog.OnMessageReceived += m =>
                        {
                            var text = m.ToString();
                            if (text.Contains(slot)) Messages.Enqueue(text);
                        };
                        SlotReady = true;
                        Status = $"Connected as {slot}";
                        Messages.Enqueue($"Connected to Archipelago as {slot}");
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
            ChildrenFedThresholds = Get("children_fed_thresholds", new List<long>());
            SacrificeThresholds = Get("sacrifice_thresholds", new List<long>());
            Goal = Get("goal", "ending");
            GoalChildrenFed = Get("goal_children_fed", 250_000L);
            CashBundleSeconds = Get("cash_bundle_seconds", 60);
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
            try { s.SetGoalAchieved(); Messages.Enqueue("Goal complete!"); Plugin.L.LogInfo("Goal sent"); }
            catch (Exception e) { Plugin.L.LogError($"Sending goal failed: {e.Message}"); }
        }

        // ---- per-seed persistent counter for one-time items (cash, upgrade points) ----
        private static string StatePath => Path.Combine(Plugin.DataDir, $"state_{San(SeedKey)}.txt");
        private static string San(string s) => string.Concat(s.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '-'));

        private static string SeenPath => Path.Combine(Plugin.DataDir, $"seen_{San(SeedKey)}.txt");
        internal static bool SeedSeenBefore() => File.Exists(SeenPath);
        internal static void MarkSeedSeen() { try { if (!File.Exists(SeenPath)) File.WriteAllText(SeenPath, DateTime.Now.ToString("s")); } catch { } }

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
