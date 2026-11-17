using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Il2CppInterop.Runtime.Attributes;
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
        private int focus = -1;
        private CursorLockMode gameLock;   // what the game last asked for (see HoldCursor)
        private bool gameVisible;
        private readonly List<(string text, bool mine, float until)> shown = new();

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
            edits[0].Set(Plugin.Server.Value); edits[1].Set(Plugin.Slot.Value); edits[2].Set(Plugin.Password.Value);
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

        private static float pendingScroll;   // mouse wheel taken from swallowed mouse events, used by the Items tab

        private static void OnInputEvent(InputEventPtr ev, InputDevice device)
        {
            try
            {
                if (!panelOpen && !swallowingEsc) return;

                // The game doesn't see the mouse at all while the panel is open: no punching or shooting when
                // clicking the panel, no turning, no weapon scrolling. Each weapon reads the buttons itself, so
                // this is simpler than patching them all. The panel gets its clicks from Unity's GUI events,
                // which don't come through here.
                var mouse = Mouse.current;
                if (panelOpen && mouse != null && ev.deviceId == mouse.deviceId)
                {
                    var s = mouse.scroll.y.ReadValueFromEventAsObject(ev);
                    if (s != null) pendingScroll += s.Unbox<float>();
                    ev.handled = true;
                    return;
                }

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

            ApState.CheckConnection();
            while (ApState.Messages.TryDequeue(out var m))
            {
                shown.Add((m.text, m.mine, Time.unscaledTime + 7f));
                if (shown.Count > 8) shown.RemoveAt(0);
            }
            shown.RemoveAll(s => s.until < Time.unscaledTime);
        }

        // ---- Cursor ----
        // The game locks and hides the mouse while playing and frees it on menus, but only when the screen changes.
        // While the panel is open the mouse is held in Confined mode, which the game never uses. If the game changes
        // the cursor while the panel is open (next day, skill tree opening, loading a save), the value differs from
        // Confined and is remembered as what the game wants, and that is what the panel restores on close.
        // A snapshot taken when the panel opened would be stale in those cases and leave the mouse free in play.
        private void LateUpdate()
        {
            if (showPanel) HoldCursor();
        }

        private void HoldCursor()
        {
            var l = Cursor.lockState;
            if (l != CursorLockMode.Confined) { gameLock = l; gameVisible = l != CursorLockMode.Locked; }
            if (!Cursor.visible) gameVisible = false;
            Cursor.lockState = CursorLockMode.Confined;
            Cursor.visible = true;
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
            if (showPanel)
            {
                gameLock = Cursor.lockState; gameVisible = Cursor.visible;
                HoldCursor();
                focus = string.IsNullOrEmpty(edits[1].Text) ? 1 : -1;
                while (typed.TryDequeue(out _)) { }
            }
            else
            {
                focus = -1;
                // Catch a change the game made this frame, then hand the cursor back the way the game wants it.
                if (Cursor.lockState != CursorLockMode.Confined) { gameLock = Cursor.lockState; gameVisible = gameLock != CursorLockMode.Locked; }
                Cursor.lockState = gameLock;
                Cursor.visible = gameVisible;
            }
        }

        // ---- Text fields ----
        // Typed characters come from Windows text input (Keyboard.onTextInput), which gives the right symbol for
        // any key and repeats while a key is held. Editing keys (Backspace, Delete, arrows) repeat on their own.
        private sealed class EditField
        {
            public string Text = "";
            public int Caret, Anchor;   // selection runs between Anchor and Caret
            public float Scroll;        // horizontal scroll in virtual units, for text wider than the box
            public bool HasSel => Caret != Anchor;
            public int SelStart => Math.Min(Caret, Anchor);
            public int SelEnd => Math.Max(Caret, Anchor);
            public string Selected => Text.Substring(SelStart, SelEnd - SelStart);
            public void Set(string t) { Text = t ?? ""; Caret = Anchor = Text.Length; Scroll = 0; }
            public void DeleteSel() { if (!HasSel) return; int s = SelStart; Text = Text.Remove(s, SelEnd - s); Caret = Anchor = s; }
            public void Insert(string s) { DeleteSel(); Text = Text.Insert(Caret, s); Caret += s.Length; Anchor = Caret; }
            public void Backspace() { if (HasSel) DeleteSel(); else if (Caret > 0) { Text = Text.Remove(Caret - 1, 1); Anchor = --Caret; } }
            public void Delete() { if (HasSel) DeleteSel(); else if (Caret < Text.Length) Text = Text.Remove(Caret, 1); }
            public void MoveTo(int pos, bool extend) { Caret = Math.Clamp(pos, 0, Text.Length); if (!extend) Anchor = Caret; }
            public void SelectAll() { Anchor = 0; Caret = Text.Length; }
        }

        private readonly EditField[] edits = { new(), new(), new() };   // server, slot, password
        private static readonly System.Collections.Concurrent.ConcurrentQueue<char> typed = new();
        private static Il2CppSystem.Action<char> textListener;
        private static bool textHooked;
        private readonly Dictionary<Key, float> nextRepeat = new();
        private float caretBlinkStart;
        private int mouseSelecting = -1;

        private static void OnTextInput(char c)
        {
            if (panelOpen && c >= ' ' && c != (char)127) typed.Enqueue(c);
        }

        private void HookTextInput()
        {
            if (textHooked) return;
            var kb = Keyboard.current;
            if (kb == null) return;
            try
            {
                textListener = new System.Action<char>(OnTextInput);
                kb.add_onTextInput(textListener);
                textHooked = true;
            }
            catch (Exception e) { Plugin.L.LogWarning($"Text input hook failed, using key fallback: {e.Message}"); textHooked = true; useKeyFallback = true; }
        }
        private bool useKeyFallback;

        /// <summary>True when the key was pressed this frame, then again every 35 ms after being held 0.45 s.</summary>
        private bool Repeat(Keyboard kb, Key key)
        {
            var c = kb[key];
            float now = Time.unscaledTime;
            if (c.wasPressedThisFrame) { nextRepeat[key] = now + 0.45f; return true; }
            if (c.isPressed && nextRepeat.TryGetValue(key, out var t) && now >= t) { nextRepeat[key] = now + 0.035f; return true; }
            return false;
        }

        private void ReadTyping(Keyboard kb)
        {
            HookTextInput();
            if (focus < 0) { while (typed.TryDequeue(out _)) { } return; }
            var f = edits[focus];
            int before = f.Caret; string beforeText = f.Text;
            bool shift = kb.shiftKey.isPressed, ctrl = kb.ctrlKey.isPressed;

            if (ctrl)
            {
                while (typed.TryDequeue(out _)) { }   // Ctrl+letter isn't text
                if (kb.aKey.wasPressedThisFrame) f.SelectAll();
                if (kb.vKey.wasPressedThisFrame) Paste();
                if (kb.cKey.wasPressedThisFrame) Clipboard.Set(f.HasSel ? f.Selected : f.Text);
                if (kb.xKey.wasPressedThisFrame) { Clipboard.Set(f.HasSel ? f.Selected : f.Text); if (f.HasSel) f.DeleteSel(); else f.Set(""); }
                if (kb.backspaceKey.wasPressedThisFrame) f.Set("");
            }
            else
            {
                if (!useKeyFallback) { var sb = new System.Text.StringBuilder(); while (typed.TryDequeue(out var c)) sb.Append(c); if (sb.Length > 0) f.Insert(sb.ToString()); }
                else { var add = FallbackKeys(kb, shift); if (add.Length > 0) f.Insert(add); }
                if (Repeat(kb, Key.Backspace)) f.Backspace();
                if (Repeat(kb, Key.Delete)) f.Delete();
            }
            if (Repeat(kb, Key.LeftArrow)) { if (f.HasSel && !shift) f.MoveTo(f.SelStart, false); else f.MoveTo(f.Caret - 1, shift); }
            if (Repeat(kb, Key.RightArrow)) { if (f.HasSel && !shift) f.MoveTo(f.SelEnd, false); else f.MoveTo(f.Caret + 1, shift); }
            if (kb.homeKey.wasPressedThisFrame) f.MoveTo(0, shift);
            if (kb.endKey.wasPressedThisFrame) f.MoveTo(f.Text.Length, shift);
            if (f.Caret != before || f.Text != beforeText) caretBlinkStart = Time.unscaledTime;

            if (kb.tabKey.wasPressedThisFrame) { focus = (focus + 1) % 3; edits[focus].SelectAll(); }
            if (kb.enterKey.wasPressedThisFrame || kb[Key.NumpadEnter].wasPressedThisFrame) DoConnect();
        }

        /// <summary>Used only if the text input hook can't be set up: a US-layout key map, no repeat.</summary>
        private static string FallbackKeys(Keyboard kb, bool shift)
        {
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
            return add;
        }

        private void Paste()
        {
            if (focus < 0) return;
            var t = Clipboard.Get();
            if (string.IsNullOrEmpty(t)) return;
            t = t.Replace("\r", "").Replace("\n", "").Trim();
            edits[focus].Insert(t);
            caretBlinkStart = Time.unscaledTime;
        }

        private void DoConnect()
        {
            Plugin.Server.Value = edits[0].Text.Trim();
            Plugin.Slot.Value = edits[1].Text.Trim();
            Plugin.Password.Value = edits[2].Text;
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
        private GUIStyle label, title, small, button, field, msg, badge, statusText, fieldText, itemName, itemCount;

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
            fieldText = Copy(msg); fieldText.richText = false; fieldText.clipping = TextClipping.Overflow;
            fieldText.padding = new RectOffset(0, 0, 0, 0); fieldText.margin = new RectOffset(0, 0, 0, 0);
            itemName = Copy(msg); itemName.fontSize = 14; itemName.clipping = TextClipping.Clip; itemName.richText = false;
            itemCount = Copy(itemName); itemCount.alignment = TextAnchor.MiddleRight; itemCount.fontStyle = FontStyle.Bold;
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
                // Your own items: dark box with a gold edge. Other players': lighter box, grey edge, dimmer text.
                bool mine = shown[i].mine;
                Fill(new Rect(12, y, w, 22), mine ? BoxBg : new Color(0f, 0f, 0f, 0.55f));
                Fill(new Rect(12, y, 3, 22), mine ? Border : new Color(0.55f, 0.55f, 0.6f, 1f));
                var old = GUI.color;
                if (!mine) GUI.color = new Color(1f, 1f, 1f, 0.8f);
                GUI.Label(new Rect(20, y, w - 10, 22), shown[i].text, msg);
                GUI.color = old;
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

        private int tab;            // 0 = connection, 1 = items
        private float itemScroll;

        private void DrawPanel(float W, float H)
        {
            const float pw = 580;
            float ph = tab == 1 ? 496 : 340;
            // Opens in the upper part of the screen so the taller Items tab fits without moving it.
            panelPos ??= new Vector2(Mathf.Round(W / 2 - pw / 2), Mathf.Round(H * 0.12f));
            // The tab buttons sit in the title bar; clicks on them must reach the buttons, not start a drag.
            HandleDrag(new Rect(panelPos.Value.x, panelPos.Value.y, pw, 44),
                       new Rect(panelPos.Value.x + 160, panelPos.Value.y + 9, 248, 28));
            // Keep at least part of the title bar on screen (also after a resolution change).
            var p = panelPos.Value;
            p.x = Mathf.Clamp(p.x, 80 - pw, W - 80);
            p.y = Mathf.Clamp(Mathf.Min(p.y, H - ph), 0, H - 44);   // the Items tab is taller: keep it on screen
            panelPos = p;
            float x = p.x, y = p.y;
            Panel(new Rect(x, y, pw, ph));

            GUI.Label(new Rect(x + 18, y + 10, 300, 30), "Archipelago", title);
            GUI.Label(new Rect(x + pw - 150, y + 14, 142, 24), "F8 / Esc to close", small);
            Tab(0, "Connection", new Rect(x + 160, y + 9, 120, 28));
            Tab(1, "Items", new Rect(x + 288, y + 9, 120, 28));
            Fill(new Rect(x + 12, y + 44, pw - 24, 1), Border);

            if (tab == 1) { DrawItems(x, y, pw, ph); return; }

            Field(0, "Server", x, y + 58, false);
            Field(1, "Slot", x, y + 98, false);
            Field(2, "Password", x, y + 138, true);
            float bw = (pw - 48) / 2;
            if (GUI.Button(new Rect(x + 18, y + 182, bw, 36), ApState.Active ? "Reconnect" : "Connect", button)) DoConnect();
            if (GUI.Button(new Rect(x + 30 + bw, y + 182, bw, 36), "Disconnect", button)) { ApState.Disconnect(); ApState.Status = "Disconnected"; }

            // Status box: colored text and a colored edge so it reads at a glance.
            var sr = new Rect(x + 18, y + 228, pw - 36, 48);
            var c = StatusColor();
            Fill(sr, new Color(c.r * 0.18f, c.g * 0.18f, c.b * 0.18f, 1f));
            Outline(sr, c, 2);
            statusText.normal.textColor = c;
            GUI.Label(new Rect(sr.x + 12, sr.y, sr.width - 24, sr.height), ApState.Status, statusText);

            var next = Game.NextMilestoneText();
            if (next != null) GUI.Label(new Rect(x + 18, y + 284, pw - 36, 26), next, label);
            int waiting = Game.WaitingForMachine;
            if (waiting > 0)
                GUI.Label(new Rect(x + 18, y + 308, pw - 36, 26),
                    $"{waiting} conveyor{(waiting == 1 ? "" : "s")} waiting for the next machine", small);
        }

        private void Tab(int i, string name, Rect r)
        {
            if (tab == i) Fill(r, new Color(Border.r, Border.g, Border.b, 0.30f));
            if (GUI.Button(r, name, button) && tab != i) { tab = i; focus = -1; itemScroll = 0; }
            if (tab == i) Outline(r, Border, 2);
        }

        // ---- Items tab ----
        // Every upgrade item in the seed with how many copies you have. The skill tree itself only shows checks
        // now, so this is where you see what you actually own.
        private void DrawItems(float x, float y, float pw, float ph)
        {
            var skills = ApState.ItemSkills;
            if (skills == null || skills.Count == 0)
            {
                GUI.Label(new Rect(x + 18, y + 62, pw - 36, 30), "Connect to a slot to see your items.", label);
                return;
            }
            var have = Game.ReceivedCounts;
            // Local functions become methods of this class, and every method of a class registered with the game
            // must have types the game understands (a List<string> parameter stopped the whole mod loading).
            // [HideFromIl2Cpp] keeps them out of that registration.
            [HideFromIl2Cpp] int Have(string n) => have.TryGetValue(n, out var c) ? c : 0;
            [HideFromIl2Cpp] string Show(string n) => n.StartsWith("Progressive ") ? n.Substring(12) : n;

            var tree = new List<string>();
            var prestige = new List<string>();
            foreach (var k in skills.Keys)
            {
                if (!ApState.PrestigeItems.Contains(k)) tree.Add(k);
                else if (ApState.PrestigeOn) prestige.Add(k);
            }
            Comparison<string> byName = (a, b) => string.Compare(Show(a), Show(b), StringComparison.OrdinalIgnoreCase);
            tree.Sort(byName); prestige.Sort(byName);

            int got = 0, total = 0;
            foreach (var k in tree) { got += Math.Min(Have(k), skills[k].Count); total += skills[k].Count; }
            foreach (var k in prestige) { got += Math.Min(Have(k), skills[k].Count); total += skills[k].Count; }
            GUI.Label(new Rect(x + 18, y + 52, pw - 36, 24),
                $"Upgrades: {got} / {total}      Cash Bundles: {Have("Cash Bundle")}      Upgrade Points: {Have("Upgrade Point")}", label);

            // Rows: a heading, then items two per row.
            const float rowH = 24, headH = 28;
            var area = new Rect(x + 18, y + 82, pw - 36, ph - 82 - 12);

            // Mouse wheel, collected from the mouse events the game isn't shown (see OnInputEvent).
            if (pendingScroll != 0) { itemScroll -= Mathf.Sign(pendingScroll) * 44f * Mathf.Max(1, Mathf.Round(Mathf.Abs(pendingScroll) / 120f)); pendingScroll = 0; }
            float cw = (area.width - 16) / 2;
            float contentH = headH + Mathf.Ceil(tree.Count / 2f) * rowH
                           + (prestige.Count > 0 ? 8 + headH + Mathf.Ceil(prestige.Count / 2f) * rowH : 0);
            float maxScroll = Mathf.Max(0, contentH - area.height);
            itemScroll = Mathf.Clamp(itemScroll, 0, maxScroll);

            GUI.BeginGroup(area);
            float yy = -itemScroll;
            [HideFromIl2Cpp] void Section(string heading, List<string> names)
            {
                GUI.Label(new Rect(0, yy, area.width, headH), heading, title);
                yy += headH;
                for (int i = 0; i < names.Count; i++)
                {
                    var r = new Rect(i % 2 * (cw + 8), yy + i / 2 * rowH, cw, rowH - 3);
                    if (r.yMax < 0 || r.y > area.height) continue;
                    var n = names[i];
                    int h = Math.Min(Have(n), skills[n].Count), t = skills[n].Count;
                    var c = h >= t ? Green : h == 0 ? new Color(0.55f, 0.55f, 0.6f, 1f) : Color.white;
                    Fill(r, new Color(1f, 1f, 1f, 0.05f));
                    Fill(new Rect(r.x, r.y, 3, r.height), c);
                    string text = Show(n);
                    if (n == "Progressive Conveyor" && Game.WaitingForMachine > 0) text += $" ({Game.WaitingForMachine} waiting)";
                    itemName.normal.textColor = c; itemCount.normal.textColor = c;
                    GUI.Label(new Rect(r.x + 9, r.y, r.width - 70, r.height), text, itemName);
                    GUI.Label(new Rect(r.xMax - 64, r.y, 58, r.height), $"{h}/{t}", itemCount);
                }
                yy += Mathf.Ceil(names.Count / 2f) * rowH;
            }
            Section(ApState.PrestigeItems.Count == 0 ? "Upgrades" : "Skill tree", tree);   // 0.2.0 seeds don't say which are prestige
            if (prestige.Count > 0) { yy += 8; Section("Prestige", prestige); }
            GUI.EndGroup();

            if (maxScroll > 0)
            {
                var track = new Rect(area.xMax - 4, area.y, 4, area.height);
                Fill(track, new Color(1f, 1f, 1f, 0.10f));
                float th = Mathf.Max(30, area.height * area.height / contentH);
                Fill(new Rect(track.x, track.y + (area.height - th) * itemScroll / maxScroll, 4, th), Border);
            }
        }

        private void Field(int i, string name, float x, float y, bool secret)
        {
            GUI.Label(new Rect(x + 18, y + 4, 100, 26), name, label);
            var f = edits[i];
            string shownText = secret ? new string('*', f.Text.Length) : f.Text;
            var r = new Rect(x + 120, y, 352, 32);
            var inner = new Rect(r.x + 8, r.y, r.width - 16, r.height);

            // Mouse: click places the caret, drag selects, double-click selects all, Shift+click extends.
            var e = Event.current;
            if (e != null)
            {
                if (e.type == EventType.MouseDown && e.button == 0 && r.Contains(e.mousePosition))
                {
                    focus = i;
                    if (e.clickCount >= 2) f.SelectAll();
                    else f.MoveTo(IndexAt(shownText, e.mousePosition.x - inner.x + f.Scroll), e.shift);
                    mouseSelecting = e.clickCount >= 2 ? -1 : i;
                    caretBlinkStart = Time.unscaledTime;
                    e.Use();
                }
                else if (e.type == EventType.MouseDrag && mouseSelecting == i)
                {
                    f.MoveTo(IndexAt(shownText, e.mousePosition.x - inner.x + f.Scroll), true);
                    e.Use();
                }
                else if (e.type == EventType.MouseUp && mouseSelecting == i) { mouseSelecting = -1; e.Use(); }
            }

            GUI.Box(r, GUIContent.none, field);
            if (focus == i) Outline(r, Border, 2);

            // Keep the caret inside the box when the text is wider than it.
            float caretX = TextX(shownText, f.Caret), full = TextX(shownText, shownText.Length);
            if (full <= inner.width - 4) f.Scroll = 0;
            else if (caretX - f.Scroll > inner.width - 4) f.Scroll = caretX - inner.width + 4;
            else if (caretX - f.Scroll < 0) f.Scroll = caretX;

            GUI.BeginGroup(inner);
            if (focus == i && f.HasSel)
            {
                float a = TextX(shownText, f.SelStart) - f.Scroll, b = TextX(shownText, f.SelEnd) - f.Scroll;
                Fill(new Rect(a, 6, b - a, inner.height - 12), SelColor);
            }
            GUI.Label(new Rect(-f.Scroll, 0, full + 20, inner.height), shownText, fieldText);
            if (focus == i && (int)((Time.unscaledTime - caretBlinkStart) * 2) % 2 == 0)
                Fill(new Rect(caretX - f.Scroll, 7, 2, inner.height - 14), Color.white);
            GUI.EndGroup();

            if (GUI.Button(new Rect(x + 480, y, 82, 32), "Paste", button)) { focus = i; Paste(); }
        }

        private static readonly Color SelColor = new(0.30f, 0.50f, 0.85f, 0.70f);

        /// <summary>Width of the first n characters, in virtual units.</summary>
        private float TextX(string s, int n) =>
            n <= 0 ? 0 : fieldText.CalcSize(new GUIContent(s.Substring(0, Math.Min(n, s.Length)).Replace(' ', ' '))).x;   // no-break space: CalcSize ignores trailing spaces

        /// <summary>Character boundary nearest to x (virtual units from the text start).</summary>
        private int IndexAt(string s, float x)
        {
            float prev = 0;
            for (int n = 1; n <= s.Length; n++)
            {
                float cur = TextX(s, n);
                if (x < (prev + cur) / 2) return n - 1;
                prev = cur;
            }
            return s.Length;
        }

        private Vector2? panelPos;   // top-left of the panel in virtual units; null = centered
        private bool dragging;
        private Vector2 dragOffset;

        /// <summary>Drag the panel by its title bar. Event positions are already in virtual units because of GUI.matrix.</summary>
        private void HandleDrag(Rect titleBar, Rect notHere)
        {
            var e = Event.current;
            if (e == null) return;
            switch (e.type)
            {
                case EventType.MouseDown when e.button == 0 && titleBar.Contains(e.mousePosition) && !notHere.Contains(e.mousePosition):
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
