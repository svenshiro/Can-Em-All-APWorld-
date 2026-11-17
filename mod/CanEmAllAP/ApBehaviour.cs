using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Il2CppInterop.Runtime.InteropTypes;

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
            try
            {
                escListener = new System.Action<InputEventPtr, InputDevice>(OnInputEvent);
                InputSystem.onEvent += escListener;
            }
            catch (Exception e) { Plugin.L.LogWarning($"Could not hook keyboard events, Escape may reach the game: {e.Message}"); }
        }

        // ---- Escape swallowing ----
        // Several game scripts read Escape (pause, leaving the skill tree starts the next day). Rather than patch
        // each one, keyboard events that press Escape are marked handled while the panel is open, so the Input
        // System drops them and the game never sees the key go down. The panel is closed from here instead.
        private static Il2CppSystem.Action<InputEventPtr, InputDevice> escListener;
        private static bool escRequested;   // set by the listener, acted on in Update
        private static bool swallowingEsc;  // Escape is held down since a swallowed press

        private static void OnInputEvent(InputEventPtr ev, InputDevice device)
        {
            try
            {
                if (!panelOpen && !swallowingEsc) return;
                var kb = Keyboard.current;
                if (kb == null || ev.deviceId != kb.deviceId) return;
                var v = kb.escapeKey.ReadValueFromEventAsObject(ev);   // null when this event doesn't carry Escape
                if (v == null) return;
                if (v.Unbox<float>() > 0.5f)
                {
                    if (panelOpen && !swallowingEsc) escRequested = true;
                    swallowingEsc = true;
                    ev.handled = true;
                }
                else swallowingEsc = false;
            }
            catch (Exception e) { Plugin.L.LogWarning($"Key event failed: {e.Message}"); }
        }

        private void Update()
        {
            var kb = Keyboard.current;
            try
            {
                if (kb != null && kb.f8Key.wasPressedThisFrame) TogglePanel();
                else if (showPanel && (escRequested || (kb != null && kb.escapeKey.wasPressedThisFrame))) TogglePanel();
                escRequested = false;
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

        private static bool panelOpen;
        private static int closedFrame = -10;

        /// <summary>True while the panel is open, and for a frame after it closes, so the Escape press
        /// that closed it doesn't also pause the game (the game may read input after this script).</summary>
        internal static bool BlocksGameInput => panelOpen || Time.frameCount <= closedFrame + 1;

        private void TogglePanel()
        {
            showPanel = !showPanel;
            panelOpen = showPanel;
            if (!showPanel) { closedFrame = Time.frameCount; dragging = false; }
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

        // ---- Drawing ----
        // Everything is laid out on a virtual screen 720 units tall and scaled up to the real screen,
        // so the overlay is the same relative size at 1080p, 1440p and 4K.
        private const float VirtualHeight = 720f;
        private static readonly Color PanelBg = new(0.08f, 0.08f, 0.10f, 0.96f);
        private static readonly Color BoxBg = new(0f, 0f, 0f, 0.78f);
        private static readonly Color Border = new(0.85f, 0.65f, 0.20f, 1f);   // gold
        private static readonly Color Green = new(0.35f, 0.90f, 0.40f, 1f);
        private static readonly Color Yellow = new(1.00f, 0.85f, 0.30f, 1f);
        private static readonly Color Red = new(1.00f, 0.40f, 0.40f, 1f);

        private Texture2D white;
        private GUIStyle label, title, small, button, field, msg, badge, statusText;

        private void EnsureStyles()
        {
            if (white == null)
            {
                white = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
                white.SetPixel(0, 0, Color.white);
                white.Apply();
            }
            if (label != null) return;
            label = Copy(GUI.skin.label); label.fontSize = 15; label.wordWrap = true;
            label.normal.textColor = Color.white;
            title = Copy(label); title.fontSize = 19; title.fontStyle = FontStyle.Bold; title.wordWrap = false;
            title.normal.textColor = Border;
            small = Copy(label); small.fontSize = 13;
            small.normal.textColor = new Color(0.75f, 0.75f, 0.78f, 1f);
            button = Copy(GUI.skin.button); button.fontSize = 15;
            field = Copy(GUI.skin.button); field.fontSize = 15; field.alignment = TextAnchor.MiddleLeft; field.clipping = TextClipping.Clip;
            field.padding = new RectOffset(8, 8, field.padding.top, field.padding.bottom);
            msg = Copy(label); msg.wordWrap = false; msg.alignment = TextAnchor.MiddleLeft;
            badge = Copy(msg);
            msg.fontSize = 12; msg.richText = true;
            statusText = Copy(label); statusText.fontSize = 16; statusText.fontStyle = FontStyle.Bold; statusText.alignment = TextAnchor.MiddleLeft;
        }

        /// <summary>The IL2CPP build has no GUIStyle copy constructor, so copy the fields that matter by hand.</summary>
        private static GUIStyle Copy(GUIStyle src)
        {
            var d = new GUIStyle
            {
                font = src.font, fontSize = src.fontSize, fontStyle = src.fontStyle, alignment = src.alignment,
                wordWrap = src.wordWrap, clipping = src.clipping, richText = src.richText,
                border = new RectOffset(src.border.left, src.border.right, src.border.top, src.border.bottom),
                padding = new RectOffset(src.padding.left, src.padding.right, src.padding.top, src.padding.bottom),
                margin = new RectOffset(src.margin.left, src.margin.right, src.margin.top, src.margin.bottom),
            };
            CopyState(src.normal, d.normal); CopyState(src.hover, d.hover);
            CopyState(src.active, d.active); CopyState(src.focused, d.focused);
            return d;
        }

        private static void CopyState(GUIStyleState s, GUIStyleState d) { d.background = s.background; d.textColor = s.textColor; }

        private void OnGUI()
        {
            EnsureStyles();
            float s = Screen.height / VirtualHeight;
            var oldMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(s, s, 1f));
            float W = Screen.width / s, H = VirtualHeight;

            // Incoming items and messages, bottom left, newest at the bottom.
            float y = H - 12;
            for (int i = shown.Count - 1; i >= 0; i--)
            {
                float w = Mathf.Min(msg.CalcSize(new GUIContent(shown[i].text)).x + 20, W - 24);
                y -= 22;
                Fill(new Rect(12, y, w, 22), BoxBg);
                Fill(new Rect(12, y, 3, 22), Border);
                GUI.Label(new Rect(20, y, w - 10, 22), shown[i].text, msg);
                y -= 3;
            }

            if (!Game.SaveApproved)
            {
                var r = new Rect(W / 2 - 300, 60, 600, 150);
                Panel(r);
                GUI.Label(new Rect(r.x + 16, r.y + 12, r.width - 32, 80),
                    $"This save already has {Game.PurchasedAtConnect} skills bought. Using it would send all of them as checks at once.\n" +
                    "Start a new game for this seed, or press the button to use this save anyway.", label);
                if (GUI.Button(new Rect(r.x + r.width / 2 - 130, r.y + 102, 260, 34), "Use this save anyway", button)) Game.SaveApproved = true;
            }

            if (!showPanel)
            {
                // Small status badge, top left.
                string t = $"AP: {ApState.Status}";
                float w = Mathf.Min(badge.CalcSize(new GUIContent(t)).x, W - 140);
                var r = new Rect(10, 10, w + 100, 30);
                Fill(r, BoxBg);
                Fill(new Rect(r.x, r.y, 6, r.height), StatusColor());
                badge.normal.textColor = StatusColor();
                GUI.Label(new Rect(r.x + 14, r.y, w + 4, r.height), t, badge);
                GUI.Label(new Rect(r.x + w + 24, r.y, 70, r.height), "(F8)", small);
            }
            else DrawPanel(W, H);

            GUI.matrix = oldMatrix;
        }

        private void DrawPanel(float W, float H)
        {
            const float pw = 580, ph = 384;
            panelPos ??= new Vector2(Mathf.Round(W / 2 - pw / 2), Mathf.Round(H / 2 - ph / 2));
            HandleDrag(new Rect(panelPos.Value.x, panelPos.Value.y, pw, 44));
            // Keep at least part of the title bar on screen (also after a resolution change).
            var p = panelPos.Value;
            p.x = Mathf.Clamp(p.x, 80 - pw, W - 80);
            p.y = Mathf.Clamp(p.y, 0, H - 44);
            panelPos = p;
            float x = p.x, y = p.y;
            Panel(new Rect(x, y, pw, ph));

            GUI.Label(new Rect(x + 18, y + 10, 300, 30), "Archipelago", title);
            GUI.Label(new Rect(x + pw - 150, y + 14, 142, 24), "F8 / Esc to close", small);
            Fill(new Rect(x + 12, y + 44, pw - 24, 1), Border);

            Field(0, "Server", x, y + 58, false);
            Field(1, "Slot", x, y + 98, false);
            Field(2, "Password", x, y + 138, true);
            GUI.Label(new Rect(x + 18, y + 176, pw - 36, 40),
                "Click a field and type. Ctrl+V paste, Ctrl+C copy, Ctrl+Backspace clear, Tab next field, Enter connect.", small);

            float bw = (pw - 48) / 2;
            if (GUI.Button(new Rect(x + 18, y + 220, bw, 36), ApState.Active ? "Reconnect" : "Connect", button)) DoConnect();
            if (GUI.Button(new Rect(x + 30 + bw, y + 220, bw, 36), "Disconnect", button)) { ApState.Disconnect(); ApState.Status = "Disconnected"; }

            // Status box: colored text and a colored edge so it reads at a glance.
            var sr = new Rect(x + 18, y + 268, pw - 36, 48);
            var c = StatusColor();
            Fill(sr, new Color(c.r * 0.18f, c.g * 0.18f, c.b * 0.18f, 1f));
            Outline(sr, c, 2);
            statusText.normal.textColor = c;
            GUI.Label(new Rect(sr.x + 12, sr.y, sr.width - 24, sr.height), ApState.Status, statusText);

            var next = Game.NextMilestoneText();
            if (next != null) GUI.Label(new Rect(x + 18, y + 324, pw - 36, 26), next, label);
            int waiting = Game.WaitingForMachine;
            if (waiting > 0)
                GUI.Label(new Rect(x + 18, y + 350, pw - 36, 26),
                    $"{waiting} conveyor{(waiting == 1 ? "" : "s")} waiting for the next machine", small);
        }

        private void Field(int i, string name, float x, float y, bool secret)
        {
            GUI.Label(new Rect(x + 18, y + 4, 100, 26), name, label);
            var text = fields[i] ?? "";
            if (secret) text = new string('*', text.Length);
            if (focus == i && (int)(Time.unscaledTime * 2) % 2 == 0) text += "|";
            var r = new Rect(x + 120, y, 352, 32);
            if (GUI.Button(r, text, field)) focus = i;
            if (focus == i) Outline(r, Border, 2);
            if (GUI.Button(new Rect(x + 480, y, 82, 32), "Paste", button)) { focus = i; Paste(); }
        }

        private Vector2? panelPos;   // top-left of the panel in virtual units; null = centered
        private bool dragging;
        private Vector2 dragOffset;

        /// <summary>Drag the panel by its title bar. Event positions are already in virtual units because of GUI.matrix.</summary>
        private void HandleDrag(Rect titleBar)
        {
            var e = Event.current;
            if (e == null) return;
            switch (e.type)
            {
                case EventType.MouseDown when e.button == 0 && titleBar.Contains(e.mousePosition):
                    dragging = true;
                    dragOffset = e.mousePosition - panelPos.Value;
                    e.Use();
                    break;
                case EventType.MouseDrag when dragging:
                    panelPos = e.mousePosition - dragOffset;
                    e.Use();
                    break;
                case EventType.MouseUp when dragging:
                    dragging = false;
                    e.Use();
                    break;
            }
        }

        private static Color StatusColor() =>
            ApState.Active ? Green : ApState.Status.StartsWith("Connecting") ? Yellow : Red;

        private void Panel(Rect r)
        {
            Fill(r, PanelBg);
            Outline(r, Border, 2);
        }

        private void Fill(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, white);
            GUI.color = old;
        }

        private void Outline(Rect r, Color c, float t)
        {
            Fill(new Rect(r.x, r.y, r.width, t), c);
            Fill(new Rect(r.x, r.yMax - t, r.width, t), c);
            Fill(new Rect(r.x, r.y, t, r.height), c);
            Fill(new Rect(r.xMax - t, r.y, t, r.height), c);
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
