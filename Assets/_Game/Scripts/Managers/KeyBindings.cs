using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace ElementalBuddies
{
    // Belegbare Aktionen (Reihenfolge = Reihenfolge in der Tastaturbelegungs-Seite)
    public enum BindId
    {
        MoveUp, MoveDown, MoveLeft, MoveRight, Jump,
        Primary, Secondary,
        SpellFire, SpellIce, SpellEarth, SpellLight,
        Build1, Build2, Build3, Build4,
        Ping, PingAlt
    }

    // Zentrale Tastenbelegung: ein gemeinsames InputActionAsset (Resources/Input/PlayerInputs), das PlayerController,
    // InteractionManager, PingSystem und das Hauptmenü benutzen. Eigene Belegungen liegen als Overrides im Asset und
    // werden als JSON in den PlayerPrefs gespeichert („keybinds“) – im Koop nur lokal, jeder Rechner hat seine eigene.
    // Fest (nicht belegbar): Esc, Mauszeiger/Zielen, Linksklick zum Bauen/Auswählen, Rechtsklick „Platzieren abbrechen“
    // und die Menü-Tasten des Hauptmenüs. F1–F12 sind frei belegbar: die DevTools-F-Tasten gibt es nur im Editor/
    // Development-Build bei aktivierten DevTools (dort löst eine darauf gelegte Aktion zusätzlich den Cheat aus).
    public static class KeyBindings
    {
        public const string PrefKey = "keybinds";
        public const string ResourcePath = "Input/PlayerInputs";
        public const string MapName = "Player";

        public enum Group { Bewegung, Kampf, Zauber, Buddies, Sonstiges }

        public class Entry
        {
            public BindId Id;
            public Group Group;
            public string Label;
            public string Action;
            public string Part;      // Composite-Teil („up“ …) oder null
            public int Occurrence;   // n-te einfache Bindung der Aktion (Ping: 0 = G, 1 = Mausrad-Klick)
            public bool AllowMainMouse; // Links-/Rechtsklick erlaubt (nur Kampf-Aktionen; Klicks teilen sie sich mit Bauen/Auswählen)
        }

        public static readonly Entry[] Entries =
        {
            new Entry { Id = BindId.MoveUp, Group = Group.Bewegung, Label = "Vorwärts", Action = "Move", Part = "up" },
            new Entry { Id = BindId.MoveDown, Group = Group.Bewegung, Label = "Rückwärts", Action = "Move", Part = "down" },
            new Entry { Id = BindId.MoveLeft, Group = Group.Bewegung, Label = "Links", Action = "Move", Part = "left" },
            new Entry { Id = BindId.MoveRight, Group = Group.Bewegung, Label = "Rechts", Action = "Move", Part = "right" },
            new Entry { Id = BindId.Jump, Group = Group.Bewegung, Label = "Springen", Action = "Jump" },
            new Entry { Id = BindId.Primary, Group = Group.Kampf, Label = "Grundangriff", Action = "Skill6", AllowMainMouse = true },
            new Entry { Id = BindId.Secondary, Group = Group.Kampf, Label = "Mobilität / Verteidigung", Action = "SkillE", AllowMainMouse = true },
            new Entry { Id = BindId.SpellFire, Group = Group.Zauber, Label = "Feuer-Fähigkeit", Action = "SpellFire", AllowMainMouse = true },
            new Entry { Id = BindId.SpellIce, Group = Group.Zauber, Label = "Eis-Fähigkeit", Action = "SpellIce", AllowMainMouse = true },
            new Entry { Id = BindId.SpellEarth, Group = Group.Zauber, Label = "Erd-Fähigkeit", Action = "SpellEarth", AllowMainMouse = true },
            new Entry { Id = BindId.SpellLight, Group = Group.Zauber, Label = "Licht-Fähigkeit", Action = "SpellLight", AllowMainMouse = true },
            new Entry { Id = BindId.Build1, Group = Group.Buddies, Label = "Buddy 1 – Feuer", Action = "Build1" },
            new Entry { Id = BindId.Build2, Group = Group.Buddies, Label = "Buddy 2 – Eis", Action = "Build2" },
            new Entry { Id = BindId.Build3, Group = Group.Buddies, Label = "Buddy 3 – Erde", Action = "Build3" },
            new Entry { Id = BindId.Build4, Group = Group.Buddies, Label = "Buddy 4 – Licht", Action = "Build4" },
            new Entry { Id = BindId.Ping, Group = Group.Sonstiges, Label = "Ping", Action = "Ping", Occurrence = 0 },
            new Entry { Id = BindId.PingAlt, Group = Group.Sonstiges, Label = "Ping (Alternative)", Action = "Ping", Occurrence = 1 },
        };

        // Feste Belegungen – nur zur Anzeige (ausgegraut) in der Belegungs-Seite
        public static readonly string[,] Fixed =
        {
            { "Zielen", "Mauszeiger" },
            { "Bauen / Buddy auswählen", "Linke Maustaste" },
            { "Platzieren abbrechen", "Rechte Maustaste" },
            { "Pause / Abbrechen", "Esc" },
        };

        // Belegung geändert (Neubelegen, Zurücksetzen, Laden) → Beschriftungen aktualisieren
        public static event Action OnBindingsChanged;

        private static InputActionAsset _asset;
        private static bool _loaded;

        public static InputActionAsset Asset
        {
            get
            {
                if (_asset == null) _asset = Resources.Load<InputActionAsset>(ResourcePath);
                if (_asset != null && !_loaded && Application.isPlaying) Load();
                return _asset;
            }
        }

        public static Entry Get(BindId id) => Entries[(int)id];

        public static InputAction Action(string name)
        {
            var a = Asset;
            var map = a != null ? a.FindActionMap(MapName) : null;
            return map != null ? map.FindAction(name) : null;
        }

        public static InputAction ActionOf(BindId id) => Action(Get(id).Action);

        // Index der Bindung des Eintrags in action.bindings, -1 wenn nicht gefunden
        public static int BindingIndex(Entry e, InputAction action)
        {
            if (action == null) return -1;
            var b = action.bindings;
            int seen = 0;
            for (int i = 0; i < b.Count; i++)
            {
                if (e.Part != null)
                {
                    if (b[i].isPartOfComposite && string.Equals(b[i].name, e.Part, StringComparison.OrdinalIgnoreCase)) return i;
                }
                else if (!b[i].isComposite && !b[i].isPartOfComposite)
                {
                    if (seen == e.Occurrence) return i;
                    seen++;
                }
            }
            return -1;
        }

        public static string EffectivePath(BindId id)
        {
            var e = Get(id);
            var action = Action(e.Action);
            int i = BindingIndex(e, action);
            return i >= 0 ? action.bindings[i].effectivePath : null;
        }

        public static string DefaultPath(BindId id)
        {
            var e = Get(id);
            var action = Action(e.Action);
            int i = BindingIndex(e, action);
            return i >= 0 ? action.bindings[i].path : null;
        }

        public static bool IsDefault(BindId id) => SamePath(EffectivePath(id), DefaultPath(id));

        public static bool SamePath(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return string.IsNullOrEmpty(a) && string.IsNullOrEmpty(b);
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        // ---------------- Laden / Speichern ----------------

        // Jeder Play-Start (und jeder Build-Start) beginnt mit den gespeicherten Overrides, nicht mit Resten im Speicher
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Init()
        {
            _loaded = false;
            OnBindingsChanged = null;
            _asset = null;
            var a = Asset;
            if (a == null) Debug.LogWarning("KeyBindings: Resources/" + ResourcePath + " fehlt – Tastaturbelegung inaktiv.");
        }

        private static void Load()
        {
            _loaded = true;
            _asset.RemoveAllBindingOverrides();
            string json = PlayerPrefs.GetString(PrefKey, "");
            if (string.IsNullOrEmpty(json)) return;
            try { _asset.LoadBindingOverridesFromJson(json); }
            catch (Exception ex)
            {
                Debug.LogWarning("KeyBindings: gespeicherte Belegung unlesbar, Standard wird benutzt. " + ex.Message);
                _asset.RemoveAllBindingOverrides();
            }
        }

        private static void Save()
        {
            if (_asset == null) return;
            bool any = false;
            foreach (var b in _asset.bindings)
                if (b.overridePath != null) { any = true; break; }
            if (any) PlayerPrefs.SetString(PrefKey, _asset.SaveBindingOverridesAsJson());
            else PlayerPrefs.DeleteKey(PrefKey);
            PlayerPrefs.Save();
        }

        private static void Changed()
        {
            Save();
            OnBindingsChanged?.Invoke();
        }

#if UNITY_EDITOR
        // Overrides hängen am geladenen Asset-Objekt: nach dem Play-Mode im Editor wieder entfernen
        [UnityEditor.InitializeOnLoadMethod]
        private static void EditorHook()
        {
            UnityEditor.EditorApplication.playModeStateChanged += s =>
            {
                if (s != UnityEditor.PlayModeStateChange.EnteredEditMode) return;
                var a = Resources.Load<InputActionAsset>(ResourcePath);
                if (a != null) a.RemoveAllBindingOverrides();
                _loaded = false;
            };
        }
#endif

        // ---------------- Ändern ----------------

        // Darf der Pfad auf diesen Eintrag? Links-/Rechtsklick nur für Kampf-Aktionen (Klick-Teilung mit Bauen/Auswählen)
        public static bool IsAllowed(Entry e, string path)
        {
            if (string.IsNullOrEmpty(path)) return true;
            if (IsMainMouse(path)) return e.AllowMainMouse;
            return true;
        }

        public static bool IsMainMouse(string path) =>
            SamePath(path, "<Mouse>/leftButton") || SamePath(path, "<Mouse>/rightButton");

        // Setzt die Bindung des Eintrags. Hat eine andere Aktion schon diese Taste, wird getauscht (sie bekommt die alte
        // Taste); passt die alte Taste dort nicht (Maustaste auf Nicht-Kampf-Aktion), ist die andere Aktion danach frei.
        // message: kurze Rückmeldung für die UI.
        public static void SetBinding(BindId id, string path, out string message)
        {
            message = null;
            var e = Get(id);
            var action = Action(e.Action);
            int index = BindingIndex(e, action);
            if (index < 0) return;
            string old = action.bindings[index].effectivePath;
            if (SamePath(old, path))
            {
                message = $"„{e.Label}“ bleibt auf {Name(path)}.";
                return;
            }

            BindId? other = null;
            foreach (var o in Entries)
                if (o.Id != id && SamePath(EffectivePath(o.Id), path)) { other = o.Id; break; }

            Apply(id, path);
            if (other.HasValue)
            {
                var oe = Get(other.Value);
                string def = DefaultPath(other.Value);
                if (!string.IsNullOrEmpty(old) && IsAllowed(oe, old))
                {
                    Apply(other.Value, old);
                    message = $"{Name(path)} war „{oe.Label}“ – getauscht: „{oe.Label}“ liegt jetzt auf {Name(old)}.";
                }
                else if (!string.IsNullOrEmpty(def) && !IsUsed(def))
                {
                    // Tausch nicht möglich (alte Taste leer bzw. Maustaste) → die andere Aktion bekommt ihren freien Standard
                    Apply(other.Value, def);
                    message = $"{Name(path)} war „{oe.Label}“ – „{oe.Label}“ liegt wieder auf {Name(def)}.";
                }
                else
                {
                    Apply(other.Value, "");
                    message = $"{Name(path)} war „{oe.Label}“ – „{oe.Label}“ ist jetzt nicht belegt.";
                }
            }
            else message = $"„{e.Label}“ liegt jetzt auf {Name(path)}.";
            Changed();
        }

        private static bool IsUsed(string path)
        {
            foreach (var o in Entries)
                if (SamePath(EffectivePath(o.Id), path)) return true;
            return false;
        }

        // Override setzen; entspricht der Pfad dem Standard, wird der Override entfernt
        private static void Apply(BindId id, string path)
        {
            var e = Get(id);
            var action = Action(e.Action);
            int index = BindingIndex(e, action);
            if (index < 0) return;
            if (path != null && SamePath(path, action.bindings[index].path) && !string.IsNullOrEmpty(path))
                action.RemoveBindingOverride(index);
            else
                action.ApplyBindingOverride(index, path ?? "");
        }

        public static void ResetBinding(BindId id, out string message)
        {
            string def = DefaultPath(id);
            SetBinding(id, def, out message);
        }

        public static void ResetAll()
        {
            if (Asset == null) return;
            _asset.RemoveAllBindingOverrides();
            Changed();
        }

        // ---------------- Neubelegen ----------------

        private static InputActionRebindingExtensions.RebindingOperation _op;
        private static int _endFrame = -10;
        public static bool IsRebinding => _op != null;
        public static BindId RebindingId { get; private set; }

        // Esc/Enter dieses (oder des letzten) Frames gehörten dem Neubelegen → Menüs sollen sie ignorieren
        public static bool InputConsumed => _op != null || Time.frameCount - _endFrame <= 1;

        // Wartet auf eine Taste der Tastatur oder eine Maustaste. Esc bricht ab, nach timeout Sekunden ebenso.
        // done(message, changed) wird immer aufgerufen.
        public static void StartRebind(BindId id, float timeout, Action<string, bool> done)
        {
            CancelRebind();
            var e = Get(id);
            var action = Action(e.Action);
            int index = BindingIndex(e, action);
            if (index < 0) { done?.Invoke("Aktion nicht gefunden.", false); return; }

            // Aktion muss während des Neubelegens aus sein (im Spiel ist das Asset aktiv)
            bool wasEnabled = action.enabled;
            if (wasEnabled) action.Disable();
            RebindingId = id;

            string msg = null;
            bool changed = false;
            float started = Time.realtimeSinceStartup;
            _op = action.PerformInteractiveRebinding(index)
                .WithExpectedControlType("Button")
                .WithControlsHavingToMatchPath("<Keyboard>")
                .WithControlsHavingToMatchPath("<Mouse>")
                .WithControlsExcluding("<Keyboard>/anyKey")
                .WithControlsExcluding("<Mouse>/press")
                .WithControlsExcluding("<Pointer>/press")
                .WithCancelingThrough("<Keyboard>/escape")
                .WithMatchingEventsBeingSuppressed()
                .WithTimeout(timeout)
                .OnPotentialMatch(op =>
                {
                    // Nur echte Tasten und Maustasten (kein Mausrad, keine Bewegung); Links-/Rechtsklick nur für Kampf
                    for (int i = op.candidates.Count - 1; i >= 0; i--)
                    {
                        var c = op.candidates[i];
                        string p = GeneralPath(c);
                        if (!(c is ButtonControl) || p == null || !IsAllowed(e, p)) op.RemoveCandidate(c);
                    }
                    // Mit eigenem Callback schließt die Operation nicht selbst ab
                    if (op.candidates.Count > 0) op.Complete();
                })
                .OnApplyBinding((op, path) =>
                {
                    SetBinding(id, path, out msg);
                    changed = true;
                })
                .OnComplete(op => Finish(action, wasEnabled, () => done?.Invoke(msg, changed)))
                .OnCancel(op => Finish(action, wasEnabled, () =>
                    done?.Invoke(timeout > 0f && Time.realtimeSinceStartup - started >= timeout - 0.1f
                        ? "Keine Taste erkannt – abgebrochen." : "Abgebrochen – Belegung unverändert.", false)));
            _op.Start();
        }

        // Erlaubte Steuerelemente als allgemeiner Pfad (<Keyboard>/q, <Mouse>/middleButton), sonst null
        private static string GeneralPath(InputControl c)
        {
            if (c == null || c.device == null) return null;
            if (c.device is Keyboard)
            {
                if (c is KeyControl) return "<Keyboard>/" + c.name;
                return null;
            }
            if (c.device is Mouse)
            {
                var n = c.name;
                if (n == "leftButton" || n == "rightButton" || n == "middleButton" || n == "forwardButton" || n == "backButton")
                    return "<Mouse>/" + n;
            }
            return null;
        }

        private static void Finish(InputAction action, bool wasEnabled, Action callback)
        {
            var op = _op;
            _op = null;
            _endFrame = Time.frameCount;
            if (wasEnabled && action != null && !action.enabled) action.Enable();
            callback?.Invoke();
            op?.Dispose();
        }

        public static void CancelRebind()
        {
            if (_op != null) _op.Cancel();
        }

        // ---------------- Anzeige ----------------

        public static string Short(BindId id) => ShortName(EffectivePath(id));
        public static string Name(BindId id) => Name(EffectivePath(id));

        // Für Fließtext: „Taste Q“, „Leertaste“, „Linke Maustaste“, „nicht belegt“
        public static string Long(BindId id) => LongName(EffectivePath(id));

        // Bewegungstasten zusammengefasst: „WASD“ bzw. „Pfeil hoch/…“
        public static string MoveKeys()
        {
            string u = Short(BindId.MoveUp), l = Short(BindId.MoveLeft), d = Short(BindId.MoveDown), r = Short(BindId.MoveRight);
            if (u.Length == 1 && l.Length == 1 && d.Length == 1 && r.Length == 1) return u + l + d + r;
            return u + "/" + l + "/" + d + "/" + r;
        }

        public static BindId SpellOf(int element) => (BindId)((int)BindId.SpellFire + Mathf.Clamp(element, 0, 3));
        public static BindId BuildOf(int index) => (BindId)((int)BindId.Build1 + Mathf.Clamp(index, 0, 3));

        public static BindId OfSlot(AbilitySlot slot)
        {
            switch (slot)
            {
                case AbilitySlot.Primary: return BindId.Primary;
                case AbilitySlot.Secondary: return BindId.Secondary;
                default: return SpellOf(AbilitySlots.ElementOf(slot));
            }
        }

        public static string LongName(string path)
        {
            string n = Name(path);
            if (string.IsNullOrEmpty(path)) return n;
            return n.Length <= 3 && !path.StartsWith("<Mouse>", StringComparison.OrdinalIgnoreCase) ? "Taste " + n : n;
        }

        public static string Name(string path) => Describe(path, false);
        public static string ShortName(string path) => Describe(path, true);

        private static readonly Dictionary<string, string[]> Special = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            // Pfad-Name → { lang, kurz }
            { "leftButton", new[] { "Linke Maustaste", "LMT" } },
            { "rightButton", new[] { "Rechte Maustaste", "RMT" } },
            { "middleButton", new[] { "Mausrad-Klick", "MMT" } },
            { "forwardButton", new[] { "Maustaste 5", "M5" } },
            { "backButton", new[] { "Maustaste 4", "M4" } },
            { "space", new[] { "Leertaste", "Leer" } },
            { "enter", new[] { "Eingabe", "Enter" } },
            { "numpadEnter", new[] { "Num-Eingabe", "NEnt" } },
            { "tab", new[] { "Tab", "Tab" } },
            { "backspace", new[] { "Rücktaste", "Rück" } },
            { "leftShift", new[] { "Umschalt links", "Umsch" } },
            { "rightShift", new[] { "Umschalt rechts", "Umsch" } },
            { "leftCtrl", new[] { "Strg links", "Strg" } },
            { "rightCtrl", new[] { "Strg rechts", "Strg" } },
            { "leftAlt", new[] { "Alt links", "Alt" } },
            { "rightAlt", new[] { "Alt Gr", "AltGr" } },
            { "leftMeta", new[] { "Befehl links", "Cmd" } },
            { "rightMeta", new[] { "Befehl rechts", "Cmd" } },
            { "capsLock", new[] { "Feststelltaste", "Fest" } },
            { "upArrow", new[] { "Pfeil hoch", "Hoch" } },
            { "downArrow", new[] { "Pfeil runter", "Runter" } },
            { "leftArrow", new[] { "Pfeil links", "Links" } },
            { "rightArrow", new[] { "Pfeil rechts", "Rechts" } },
            { "delete", new[] { "Entfernen", "Entf" } },
            { "insert", new[] { "Einfügen", "Einfg" } },
            { "home", new[] { "Pos1", "Pos1" } },
            { "end", new[] { "Ende", "Ende" } },
            { "pageUp", new[] { "Bild hoch", "BildH" } },
            { "pageDown", new[] { "Bild runter", "BildR" } },
            { "escape", new[] { "Esc", "Esc" } },
            { "contextMenu", new[] { "Menütaste", "Menü" } },
        };

        private static string Describe(string path, bool shortForm)
        {
            if (string.IsNullOrEmpty(path)) return shortForm ? "–" : "nicht belegt";
            int slash = path.LastIndexOf('/');
            string ctrl = slash >= 0 ? path.Substring(slash + 1) : path;
            if (Special.TryGetValue(ctrl, out var names)) return shortForm ? names[1] : names[0];

            if (path.StartsWith("<Keyboard>", StringComparison.OrdinalIgnoreCase))
            {
                if (ctrl.StartsWith("numpad", StringComparison.OrdinalIgnoreCase) && ctrl.Length > 6)
                {
                    string rest = ctrl.Substring(6);
                    return (shortForm ? "N" : "Num ") + NumpadSymbol(rest);
                }
                // Buchstaben/Ziffern/Satzzeichen: Beschriftung nach aktuellem Tastatur-Layout (QWERTZ: <Keyboard>/y = „Z“)
                var kb = Keyboard.current;
                if (kb != null)
                {
                    var c = InputControlPath.TryFindControl(kb, path);
                    if (c != null && !string.IsNullOrEmpty(c.displayName))
                    {
                        string d = c.displayName.Trim();
                        if (d.Length == 1) return d.ToUpperInvariant();
                        if (d.Length > 0 && d.Length <= 3) return d;
                    }
                }
                if (ctrl.StartsWith("digit", StringComparison.OrdinalIgnoreCase)) return ctrl.Substring(5);
                if (ctrl.Length == 1) return ctrl.ToUpperInvariant();
            }
            string human = InputControlPath.ToHumanReadableString(path, InputControlPath.HumanReadableStringOptions.OmitDevice);
            if (string.IsNullOrEmpty(human)) human = ctrl;
            if (shortForm && human.Length > 6) human = human.Substring(0, 6);
            return human.Length == 1 ? human.ToUpperInvariant() : human;
        }

        private static string NumpadSymbol(string rest)
        {
            switch (rest.ToLowerInvariant())
            {
                case "plus": return "+";
                case "minus": return "−";
                case "multiply": return "×";
                case "divide": return "/";
                case "period": return ",";
                case "equals": return "=";
                default: return rest;
            }
        }

        // ---------------- Hilfen für Eingabe-Code ----------------

        // Ist die Aktion (auch) an dieses Steuerelement gebunden? (z. B. Grundangriff auf linker Maustaste)
        public static bool Uses(InputAction action, InputControl control)
        {
            if (action == null || control == null) return false;
            var controls = action.controls;
            for (int i = 0; i < controls.Count; i++)
                if (controls[i] == control) return true;
            return false;
        }
    }
}
