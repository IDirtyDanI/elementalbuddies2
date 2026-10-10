using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace ElementalBuddies
{
    // Ping-System (Plan „Fesselung“ G1): Taste G oder mittlere Maustaste setzt eine Welt-Markierung an der Maus.
    // Kontext aus der Umgebung: Gegner in der Nähe → „Gefahr!“, Schrein/Händler → „Hierher!“, sonst „Hier bauen“.
    // Im Koop geht der Ping über NetGame an alle (mit Spielernamen). Liegt auf dem Managers-Objekt (FeedbackDirector).
    public class PingSystem : MonoBehaviour
    {
        public enum PingType : byte { Build, Danger, Gather }

        [Tooltip("Mindestabstand zwischen zwei Pings eines Spielers (s).")]
        public float Cooldown = 0.8f;
        public float Lifetime = 5f;

        public static PingSystem Instance { get; private set; }
        private float _nextLocal;
        private Camera _cam;
        private TMP_FontAsset _font;
        private readonly List<Marker> _markers = new List<Marker>();

        private class Marker
        {
            public GameObject Root;
            public LineRenderer Ring, Beam;
            public TextMeshPro Label;
            public float Born;
            public Color Color;
        }

        void Awake() => Instance = this;
        void OnDestroy() { if (Instance == this) Instance = null; }

        void Start()
        {
            var hud = FindFirstObjectByType<HUDManager>(FindObjectsInactive.Include);
            if (hud != null && hud.ShardText != null) _font = hud.ShardText.font;
        }

        public static string Label(PingType t) =>
            t == PingType.Danger ? "Gefahr!" : t == PingType.Gather ? "Hierher!" : "Hier bauen";

        public static Color ColorOf(PingType t) =>
            t == PingType.Danger ? new Color(1f, 0.3f, 0.2f) : t == PingType.Gather ? new Color(1f, 0.82f, 0.3f) : new Color(0.4f, 0.8f, 1f);

        void Update()
        {
            HandleInput();
            Animate();
        }

        private void HandleInput()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null || mouse == null) return;
            bool pressed = kb.gKey.wasPressedThisFrame || mouse.middleButton.wasPressedThisFrame;
            if (!pressed || Time.unscaledTime < _nextLocal) return;
            if (PauseManager.IsPaused || InteractionManager.LocalInputBlocked) return;
            if (GameManager.Instance != null && GameManager.Instance.IsGameOver) return;
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
            if (_cam == null) _cam = Camera.main;
            if (_cam == null) return;

            var im = InteractionManager.Instance;
            int mask = im != null && im.FloorLayer.value != 0 ? im.FloorLayer.value : Physics.DefaultRaycastLayers;
            Ray ray = _cam.ScreenPointToRay(mouse.position.ReadValue());
            if (!Physics.Raycast(ray, out RaycastHit hit, 200f, mask, QueryTriggerInteraction.Ignore)) return;

            _nextLocal = Time.unscaledTime + Cooldown;
            NetGame.RequestPing(hit.point, (byte)ContextAt(hit.point));
        }

        // Kontext: Gegner in 4 m → Gefahr; Schrein/Händler in 5 m → Hierher; sonst Hier bauen
        public static PingType ContextAt(Vector3 p)
        {
            foreach (var e in CombatUtil.FindEnemies(p, 4f))
                if (e != null && !e.IsDead) return PingType.Danger;
            foreach (var c in Physics.OverlapSphere(p, 5f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide))
                if (c.GetComponentInParent<Shrine>() != null || c.GetComponentInParent<Merchant>() != null) return PingType.Gather;
            return PingType.Build;
        }

        // Alle Rechner (NetGame): Markierung zeigen
        public void ShowPing(Vector3 pos, PingType type, string who)
        {
            var color = ColorOf(type);
            var root = new GameObject("Ping");
            root.transform.position = pos + Vector3.up * 0.05f;

            var m = new Marker { Root = root, Born = Time.unscaledTime, Color = color };
            m.Ring = MakeLine(root.transform, "Ring", color, 0.12f, 33);
            m.Beam = MakeLine(root.transform, "Beam", color, 0.18f, 2);
            m.Beam.SetPosition(0, Vector3.zero);
            m.Beam.SetPosition(1, Vector3.up * 3.2f);

            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(root.transform, false);
            labelGo.transform.localPosition = Vector3.up * 3.7f;
            m.Label = labelGo.AddComponent<TextMeshPro>();
            if (_font != null) m.Label.font = _font;
            m.Label.text = string.IsNullOrEmpty(who) ? Label(type) : $"{Label(type)}\n<size=70%>{who}</size>";
            m.Label.fontSize = 6f;
            m.Label.alignment = TextAlignmentOptions.Center;
            m.Label.color = color;
            m.Label.outlineWidth = 0.25f;
            m.Label.outlineColor = new Color32(30, 18, 8, 255);
            _markers.Add(m);
            GameAudio.Play(GameAudio.Has(SfxId.Ping) ? SfxId.Ping : SfxId.UiClick, pos);
        }

        private static LineRenderer MakeLine(Transform parent, string name, Color color, float width, int points)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.positionCount = points;
            lr.loop = false;
            lr.widthMultiplier = width;
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
            lr.material = new Material(shader);
            if (lr.material.HasProperty("_BaseColor")) lr.material.SetColor("_BaseColor", color);
            lr.startColor = lr.endColor = color;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            return lr;
        }

        private void Animate()
        {
            if (_markers.Count == 0) return;
            if (_cam == null) _cam = Camera.main;
            float now = Time.unscaledTime;
            for (int i = _markers.Count - 1; i >= 0; i--)
            {
                var m = _markers[i];
                float t = now - m.Born;
                if (m.Root == null || t > Lifetime)
                {
                    if (m.Root != null)
                    {
                        foreach (var lr in m.Root.GetComponentsInChildren<LineRenderer>()) Destroy(lr.material);
                        Destroy(m.Root);
                    }
                    _markers.RemoveAt(i);
                    continue;
                }
                float fade = Mathf.Clamp01((Lifetime - t) / 0.8f);
                // Ring pulsiert nach außen (in der ersten Sekunde größer), danach ruhiges Atmen
                float r = t < 0.6f ? Mathf.Lerp(2.6f, 1.2f, t / 0.6f) : 1.2f + 0.12f * Mathf.Sin(t * 5f);
                for (int k = 0; k < m.Ring.positionCount; k++)
                {
                    float a = k / (float)(m.Ring.positionCount - 1) * Mathf.PI * 2f;
                    m.Ring.SetPosition(k, new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r));
                }
                var c = m.Color;
                c.a = fade;
                m.Ring.startColor = m.Ring.endColor = c;
                var cb = c;
                m.Beam.startColor = cb;
                cb.a = 0f;
                m.Beam.endColor = cb;
                m.Label.alpha = fade;
                if (_cam != null) m.Label.transform.rotation = _cam.transform.rotation;
            }
        }
    }
}
