using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CanEmAllAP
{
    /// <summary>Runs the mod every frame and draws the Archipelago panel (F8) and messages.
    /// IMGUI text fields don't receive keys in this game (it uses the new Input System only),
    /// so typing is read straight from the keyboard.</summary>
    public class ApBehaviour : MonoBehaviour
    {
        public ApBehaviour(IntPtr ptr) : base(ptr) { }

        private bool showPanel;
        private bool triedAutoConnect;
        private readonly string[] fields = new string[3];   // server, slot, password
        private int focus = -1;
        private CursorLockMode prevLock;
        private bool prevVisible;
        private readonly List<(string text, float until)> shown = new();

        private static readonly Key[] Letters =
        {
            Key.A, Key.B, Key.C, Key.D, Key.E, Key.F, Key.G, Key.H, Key.I, Key.J, Key.K, Key.L, Key.M,
            Key.N, Key.O, Key.P, Key.Q, Key.R, Key.S, Key.T, Key.U, Key.V, Key.W, Key.X, Key.Y, Key.Z
        };
        private static readonly Key[] Digits = { Key.Digit0, Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5, Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9 };
        private static readonly Key[] Numpad = { Key.Numpad0, Key.Numpad1, Key.Numpad2, Key.Numpad3, Key.Numpad4, Key.Numpad5, Key.Numpad6, Key.Numpad7, Key.Numpad8, Key.Numpad9 };
        private const string ShiftDigits = ")!@#$%^&*(";

        private void Start()
        {
            fields[0] = Plugin.Server.Value; fields[1] = Plugin.Slot.Value; fields[2] = Plugin.Password.Value;
        }

        private void Update()
        {
            var kb = Keyboard.current;
            try
            {
                if (kb != null && kb.f8Key.wasPressedThisFrame) TogglePanel();
                if (showPanel && kb != null) ReadTyping(kb);
            }
            catch (Exception e) { Plugin.L.LogError($"Input failed: {e.Message}"); }

            if (!triedAutoConnect && Time.time > 3f)
            {
                triedAutoConnect = true;
                if (Plugin.AutoConnect.Value && !string.IsNullOrEmpty(Plugin.Slot.Value)) ApState.Connect();
            }

            try { Game.Update(Time.time); }
            catch (Exception e) { Plugin.L.LogError($"Update failed: {e}"); }

            while (ApState.Messages.TryDequeue(out var m))
            {
                shown.Add((m, Time.time + 6f));
                if (shown.Count > 6) shown.RemoveAt(0);
            }
            shown.RemoveAll(s => s.until < Time.time);
        }

        private void LateUpdate()
        {
            // The game locks the mouse while playing; free it while the panel is open.
            if (showPanel) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
        }

        private void TogglePanel()
        {
            showPanel = !showPanel;
            if (showPanel) { prevLock = Cursor.lockState; prevVisible = Cursor.visible; focus = string.IsNullOrEmpty(fields[1]) ? 1 : -1; }
            else { focus = -1; Cursor.lockState = prevLock; Cursor.visible = prevVisible; }
        }

        private void ReadTyping(Keyboard kb)
        {
            if (focus < 0) return;
            bool shift = kb.shiftKey.isPressed;
            if (kb.ctrlKey.isPressed)
            {
                if (kb.vKey.wasPressedThisFrame) Paste();
                if (kb.cKey.wasPressedThisFrame) Clipboard.Set(fields[focus] ?? "");
                if (kb.xKey.wasPressedThisFrame) { Clipboard.Set(fields[focus] ?? ""); fields[focus] = ""; }
                if (kb.backspaceKey.wasPressedThisFrame) fields[focus] = "";
                return;
            }
            string add = "";
            for (int i = 0; i < Letters.Length; i++)
                if (kb[Letters[i]].wasPressedThisFrame) add += shift ? (char)('A' + i) : (char)('a' + i);
            for (int i = 0; i < 10; i++)
            {
                if (kb[Digits[i]].wasPressedThisFrame) add += shift ? ShiftDigits[i] : (char)('0' + i);
                if (kb[Numpad[i]].wasPressedThisFrame) add += (char)('0' + i);
            }
            if (kb.periodKey.wasPressedThisFrame || kb[Key.NumpadPeriod].wasPressedThisFrame) add += shift ? ">" : ".";
            if (kb.semicolonKey.wasPressedThisFrame) add += shift ? ":" : ";";
            if (kb.minusKey.wasPressedThisFrame) add += shift ? "_" : "-";
            if (kb[Key.Slash].wasPressedThisFrame) add += shift ? "?" : "/";
            if (kb.spaceKey.wasPressedThisFrame) add += " ";
            if (add.Length > 0) fields[focus] = (fields[focus] ?? "") + add;

            if (kb.backspaceKey.wasPressedThisFrame && !string.IsNullOrEmpty(fields[focus]))
                fields[focus] = fields[focus].Substring(0, fields[focus].Length - 1);
            if (kb.tabKey.wasPressedThisFrame) focus = (focus + 1) % 3;
            if (kb.enterKey.wasPressedThisFrame || kb[Key.NumpadEnter].wasPressedThisFrame) DoConnect();
        }

        private void Paste()
        {
            if (focus < 0) return;
            var t = Clipboard.Get();
            if (string.IsNullOrEmpty(t)) return;
            t = t.Replace("\r", "").Replace("\n", "").Trim();
            fields[focus] = (fields[focus] ?? "") + t;
        }

        private void DoConnect()
        {
            Plugin.Server.Value = (fields[0] ?? "").Trim();
            Plugin.Slot.Value = (fields[1] ?? "").Trim();
            Plugin.Password.Value = fields[2] ?? "";
            focus = -1;
            ApState.Connect();
        }

        private void OnGUI()
        {
            float y = Screen.height - 30;
            for (int i = shown.Count - 1; i >= 0; i--) { Shadowed(new Rect(12, y, Screen.width - 24, 24), shown[i].text); y -= 24; }

            if (!Game.SaveApproved)
            {
                GUI.Box(new Rect(Screen.width / 2 - 260, 80, 520, 120), "");
                GUI.Label(new Rect(Screen.width / 2 - 250, 90, 500, 60),
                    $"This save already has {Game.PurchasedAtConnect} skills bought. Using it would send all of them as checks at once.\n" +
                    "Start a new game for this seed, or press the button to use this save anyway.");
                if (GUI.Button(new Rect(Screen.width / 2 - 120, 160, 240, 28), "Use this save anyway")) Game.SaveApproved = true;
            }

            if (!showPanel) { Shadowed(new Rect(12, 8, 600, 22), $"AP: {ApState.Status}   (F8)"); return; }

            GUI.Box(new Rect(10, 10, 380, 250), "Archipelago  (F8 to close)");
            Field(0, "Server", 38, false);
            Field(1, "Slot", 66, false);
            Field(2, "Password", 94, true);
            GUI.Label(new Rect(20, 118, 360, 26), "Click a field and type. Ctrl+V paste, Ctrl+C copy, Ctrl+Backspace clear, Tab next, Enter connect.");
            if (GUI.Button(new Rect(20, 144, 170, 26), ApState.Active ? "Reconnect" : "Connect")) DoConnect();
            if (GUI.Button(new Rect(210, 144, 170, 26), "Disconnect")) { ApState.Disconnect(); ApState.Status = "Disconnected"; }
            GUI.Label(new Rect(20, 178, 360, 44), ApState.Status);
            var next = Game.NextMilestoneText();
            if (next != null) GUI.Label(new Rect(20, 226, 360, 22), next);
        }

        private void Field(int i, string label, float y, bool secret)
        {
            GUI.Label(new Rect(20, y, 80, 22), label);
            var text = fields[i] ?? "";
            if (secret) text = new string('*', text.Length);
            if (focus == i && (int)(Time.unscaledTime * 2) % 2 == 0) text += "|";
            if (GUI.Button(new Rect(100, y, 220, 22), (focus == i ? "> " : "") + text)) focus = i;
            if (GUI.Button(new Rect(324, y, 56, 22), "Paste")) { focus = i; Paste(); }
        }

        private static void Shadowed(Rect r, string text)
        {
            var old = GUI.color;
            GUI.color = Color.black;
            GUI.Label(new Rect(r.x + 1, r.y + 1, r.width, r.height), text);
            GUI.color = old;
            GUI.Label(r, text);
        }
    }

    /// <summary>Clipboard through Unity, with the Windows clipboard as a fallback.</summary>
    internal static class Clipboard
    {
        [DllImport("user32.dll")] private static extern bool OpenClipboard(IntPtr h);
        [DllImport("user32.dll")] private static extern bool CloseClipboard();
        [DllImport("user32.dll")] private static extern bool EmptyClipboard();
        [DllImport("user32.dll")] private static extern IntPtr GetClipboardData(uint fmt);
        [DllImport("user32.dll")] private static extern IntPtr SetClipboardData(uint fmt, IntPtr mem);
        [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr h);
        [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr h);
        [DllImport("kernel32.dll")] private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);
        private const uint CF_UNICODETEXT = 13;

        public static string Get()
        {
            try { var t = GUIUtility.systemCopyBuffer; if (!string.IsNullOrEmpty(t)) return t; } catch { }
            try
            {
                if (!OpenClipboard(IntPtr.Zero)) return "";
                try
                {
                    var h = GetClipboardData(CF_UNICODETEXT);
                    if (h == IntPtr.Zero) return "";
                    var p = GlobalLock(h);
                    try { return Marshal.PtrToStringUni(p) ?? ""; } finally { GlobalUnlock(h); }
                }
                finally { CloseClipboard(); }
            }
            catch (Exception e) { Plugin.L.LogWarning($"Clipboard read failed: {e.Message}"); return ""; }
        }

        public static void Set(string text)
        {
            try { GUIUtility.systemCopyBuffer = text; return; } catch { }
            try
            {
                if (!OpenClipboard(IntPtr.Zero)) return;
                try
                {
                    EmptyClipboard();
                    var bytes = (text.Length + 1) * 2;
                    var mem = GlobalAlloc(0x0002, (UIntPtr)bytes);
                    var p = GlobalLock(mem);
                    Marshal.Copy(text.ToCharArray(), 0, p, text.Length);
                    Marshal.WriteInt16(p, text.Length * 2, 0);
                    GlobalUnlock(mem);
                    SetClipboardData(CF_UNICODETEXT, mem);
                }
                finally { CloseClipboard(); }
            }
            catch (Exception e) { Plugin.L.LogWarning($"Clipboard write failed: {e.Message}"); }
        }
    }
}
