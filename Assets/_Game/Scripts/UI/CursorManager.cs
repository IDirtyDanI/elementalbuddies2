using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace ElementalBuddies
{
    // Eigene Mauszeiger (Hardware-Cursor, selbst gezeichnet: art-src/cursor/make_cursors.py → Resources/Cursors/).
    //   Zeiger    – Menüs, Pause, Wellen-Draft/Händler, Game Over, Maus über UI, Hauptmenü
    //   Bauen     – Zeiger mit Plus-Plakette, solange ein Buddy zum Platzieren gewählt ist (Ghost aktiv)
    //   Hand      – im Spiel (Maus über der Welt): Zeigehand, Finger oben links; bei gedrückter linker
    //               Maustaste die abgedunkelte Variante (cursor_hand_press). Fehlt die Hand-Textur,
    //               greift das Fadenkreuz (cursor_crosshair), danach der Zeiger.
    // Startet sich selbst (RuntimeInitializeOnLoadMethod) und überlebt Szenenwechsel; kein Szenen-Objekt nötig.
    // PlayerSettings.defaultCursor ist ebenfalls der Zeiger (gilt vor dem ersten Frame und in Builds).
    public class CursorManager : MonoBehaviour
    {
        public enum Mode { None, Pointer, Build, Hand, HandPress }

        public static CursorManager Instance { get; private set; }

        // Hotspots in Pixeln (Texturen 32×32, Ursprung oben links): Pfeilspitze, Zeigefingerspitze, Fadenkreuz-Mitte
        private static readonly Vector2 PointerHotspot = new Vector2(1f, 1f);
        private static readonly Vector2 HandHotspot = new Vector2(2f, 1f);
        private static readonly Vector2 CrosshairHotspot = new Vector2(16f, 16f);

        private Texture2D _pointer, _build, _hand, _handPress, _crosshair;
        private Mode _current = Mode.None;

        public Mode Current => _current;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            // Touch-Geräte haben keinen Mauszeiger (Paket U): kein eigener Cursor, keine Abfragen pro Bild
            if (Instance != null || MobilePlatform.TouchMode) return;
            var go = new GameObject("CursorManager");
            DontDestroyOnLoad(go);
            go.AddComponent<CursorManager>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            _pointer = Resources.Load<Texture2D>("Cursors/cursor_pointer");
            _build = Resources.Load<Texture2D>("Cursors/cursor_build");
            _hand = Resources.Load<Texture2D>("Cursors/cursor_hand");
            _handPress = Resources.Load<Texture2D>("Cursors/cursor_hand_press");
            _crosshair = Resources.Load<Texture2D>("Cursors/cursor_crosshair");
            Apply(Mode.Pointer);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // macOS/Windows setzen den Cursor beim Fokuswechsel teils zurück -> erneut anwenden
        void OnApplicationFocus(bool focus)
        {
            if (!focus) return;
            var m = _current;
            _current = Mode.None;
            Apply(m == Mode.None ? Mode.Pointer : m);
        }

        void LateUpdate()
        {
            Apply(Decide());
        }

        private static Mode Decide()
        {
            // Hauptmenü / Lobby: keine Spielszene (kein InteractionManager) -> Zeiger
            var im = InteractionManager.Instance;
            if (im == null) return Mode.Pointer;

            // Menüs/Fenster: Pause, eigenes Kartenfenster (Draft/Händler), Solo-Zeitstopp, Game Over
            if (InteractionManager.LocalInputBlocked) return Mode.Pointer;
            if (GameManager.Instance != null && GameManager.Instance.IsGameOver) return Mode.Pointer;

            // Maus über HUD/Panel
            var es = EventSystem.current;
            if (es != null && es.IsPointerOverGameObject()) return Mode.Pointer;

            // Bau-Modus: Buddy zum Platzieren gewählt
            if (im.SelectedIndex >= 0) return Mode.Build;

            // Spiel, Maus über der Welt: Zeigehand (gedrückt, solange die linke Maustaste gehalten wird)
            var mouse = Mouse.current;
            return mouse != null && mouse.leftButton.isPressed ? Mode.HandPress : Mode.Hand;
        }

        public void Apply(Mode mode)
        {
            if (mode == _current) return;
            _current = mode;
            switch (mode)
            {
                case Mode.HandPress when _handPress != null: Set(_handPress, HandHotspot); break;
                case Mode.Hand:
                case Mode.HandPress:
                    if (_hand != null) Set(_hand, HandHotspot);
                    else if (_crosshair != null) Set(_crosshair, CrosshairHotspot);
                    else Set(_pointer, PointerHotspot);
                    break;
                case Mode.Build: Set(_build != null ? _build : _pointer, PointerHotspot); break;
                default: Set(_pointer, PointerHotspot); break;
            }
        }

        private static void Set(Texture2D tex, Vector2 hotspot)
        {
            // Fehlt die Textur, bleibt der System-Cursor (null)
            Cursor.SetCursor(tex, tex != null ? hotspot : Vector2.zero, CursorMode.Auto);
        }
    }
}
