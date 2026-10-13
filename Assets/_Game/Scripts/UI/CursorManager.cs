using UnityEngine;
using UnityEngine.EventSystems;

namespace ElementalBuddies
{
    // Eigene Mauszeiger (Hardware-Cursor, selbst gezeichnet: art-src/cursor/make_cursors.py → Resources/Cursors/).
    //   Zeiger    – Menüs, Pause, Wellen-Draft/Händler, Game Over, Maus über UI, Hauptmenü
    //   Bauen     – Zeiger mit Plus-Plakette, solange ein Buddy zum Platzieren gewählt ist (Ghost aktiv)
    //   Fadenkreuz – im Spiel beim Zielen (Maus über der Welt)
    // Startet sich selbst (RuntimeInitializeOnLoadMethod) und überlebt Szenenwechsel; kein Szenen-Objekt nötig.
    // PlayerSettings.defaultCursor ist ebenfalls der Zeiger (gilt vor dem ersten Frame und in Builds).
    public class CursorManager : MonoBehaviour
    {
        public enum Mode { None, Pointer, Build, Crosshair }

        public static CursorManager Instance { get; private set; }

        // Hotspots in Pixeln (Texturen 32×32): Pfeilspitze oben links, Fadenkreuz mittig
        private static readonly Vector2 PointerHotspot = new Vector2(1f, 1f);
        private static readonly Vector2 CrosshairHotspot = new Vector2(16f, 16f);

        private Texture2D _pointer, _build, _crosshair;
        private Mode _current = Mode.None;

        public Mode Current => _current;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null) return;
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

            return Mode.Crosshair;
        }

        public void Apply(Mode mode)
        {
            if (mode == _current) return;
            _current = mode;
            switch (mode)
            {
                case Mode.Crosshair: Set(_crosshair, CrosshairHotspot); break;
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
