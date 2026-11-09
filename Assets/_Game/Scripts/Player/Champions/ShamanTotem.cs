using System.Collections.Generic;
using UnityEngine;

namespace ElementalBuddies
{
    // Totem des Schamanen (Kriegstotem / Steinhaut-Totem) als Laufzeit-Objekt. Entsteht im Cast und damit auf ALLEN
    // Rechnern (Besitzer sofort, andere per Cast-RPC): Optik überall, die Wirkung des Pulses entscheidet der Zauber mit
    // Net.IsServer. Lebt Lifetime Sekunden und versinkt danach. Ohne Modell-Prefab baut es einen einfachen Platzhalter.
    public class ShamanTotem : MonoBehaviour
    {
        public enum TotemKind { War, Stoneskin }

        [Tooltip("Optik-Kind, das beim Aufstellen aus dem Boden wächst und am Ende versinkt (leer = ganzes Objekt).")]
        public Transform Visual;
        [Tooltip("Ankerpunkt für die Aura (leer = Fußpunkt).")]
        public Transform AuraAnchor;

        public TotemKind Kind { get; private set; }
        public ShamanKit Owner { get; private set; }
        public float Radius { get; private set; }
        public float Lifetime { get; private set; }
        public float Age => _age;
        public bool Expired => _ending;
        public int PulseCount { get; private set; }

        private float _age, _pulseInterval, _nextPulse, _endT;
        private bool _ending;
        private System.Action<ShamanTotem> _onPulse;
        private GameObject _aura;
        private Vector3 _visualBase;
        private readonly Dictionary<int, float> _fxThrottle = new Dictionary<int, float>();

        private const float RiseTime = 0.25f;
        private const float SinkTime = 0.4f;

        // Totem erzeugen: modelPrefab (Totem-Modell, optional mit ShamanTotem), auraPrefab (Bodenring, für auraAuthoredSize m
        // Radius gebaut, wird auf radius skaliert). onPulse läuft alle pulseInterval s (erster Puls sofort) auf allen Rechnern.
        public static ShamanTotem Spawn(GameObject modelPrefab, GameObject auraPrefab, float auraAuthoredSize, Vector3 position,
            Quaternion rotation, TotemKind kind, ShamanKit owner, float radius, float lifetime, float pulseInterval,
            System.Action<ShamanTotem> onPulse)
        {
            GameObject go = modelPrefab != null ? Instantiate(modelPrefab, position, rotation) : new GameObject(kind == TotemKind.War ? "WarTotem" : "StoneskinTotem");
            go.transform.SetPositionAndRotation(position, rotation);
            var totem = go.GetComponent<ShamanTotem>();
            if (totem == null) totem = go.AddComponent<ShamanTotem>();
            if (modelPrefab == null) totem.BuildPlaceholder(kind, radius, auraPrefab == null);
            totem.Setup(kind, owner, radius, lifetime, pulseInterval, onPulse, auraPrefab, auraAuthoredSize);
            return totem;
        }

        public void Setup(TotemKind kind, ShamanKit owner, float radius, float lifetime, float pulseInterval,
            System.Action<ShamanTotem> onPulse, GameObject auraPrefab, float auraAuthoredSize)
        {
            Kind = kind;
            Owner = owner;
            Radius = Mathf.Max(0.5f, radius);
            Lifetime = Mathf.Max(0.1f, lifetime);
            _pulseInterval = Mathf.Max(0.1f, pulseInterval);
            _onPulse = onPulse;
            _nextPulse = 0f;
            if (Visual == null) Visual = transform;
            _visualBase = Visual.localScale;
            if (Visual != transform) Visual.localScale = new Vector3(_visualBase.x, 0.01f, _visualBase.z);
            if (auraPrefab != null)
            {
                Transform anchor = AuraAnchor != null ? AuraAnchor : transform;
                _aura = Instantiate(auraPrefab, anchor.position + Vector3.up * 0.03f, Quaternion.identity, transform);
                float k = auraAuthoredSize > 0f ? Radius / auraAuthoredSize : 1f;
                CombatUtil.ScaleFx(_aura, auraPrefab.transform.localScale, k, false);
            }
        }

        void Update()
        {
            Tick(Time.deltaTime);
        }

        // Öffentlich für synchrone Tests
        public void Tick(float dt)
        {
            if (_ending)
            {
                _endT += dt;
                float s = 1f - Mathf.Clamp01(_endT / SinkTime);
                if (Visual != null && Visual != transform) Visual.localScale = new Vector3(_visualBase.x, _visualBase.y * Mathf.Max(0.01f, s), _visualBase.z);
                if (_endT >= SinkTime) Destroy(gameObject);
                return;
            }

            _age += dt;
            if (Visual != null && Visual != transform && _age <= RiseTime + dt)
            {
                float r = Mathf.Clamp01(_age / RiseTime);
                Visual.localScale = new Vector3(_visualBase.x, _visualBase.y * Mathf.Max(0.01f, r), _visualBase.z);
            }

            if (_age >= _nextPulse && _age <= Lifetime)
            {
                _nextPulse += _pulseInterval;
                PulseCount++;
                if (_onPulse != null) _onPulse(this);
            }
            if (_age >= Lifetime) End();
        }

        // Vorzeitig beenden (z. B. neues Kriegstotem desselben Schamanen)
        public void End()
        {
            if (_ending) return;
            _ending = true;
            _endT = 0f;
            if (_aura != null)
            {
                foreach (var ps in _aura.GetComponentsInChildren<ParticleSystem>()) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
            if (Visual == null || Visual == transform) Destroy(gameObject, SinkTime);
        }

        public bool InRange(Vector3 position) => CombatUtil.HorizontalDistance(transform.position, position) <= Radius;

        // Effekt am Ziel (an dessen Transform gehängt), gedrosselt je Ziel – sonst entstünde bei jedem Puls ein neuer
        public void ShowTargetFx(Component target, GameObject prefab, float lifetime, float minInterval)
        {
            if (target == null || prefab == null) return;
            int key = target.GetInstanceID();
            if (_fxThrottle.TryGetValue(key, out float last) && _age - last < minInterval) return;
            _fxThrottle[key] = _age;
            var fx = Instantiate(prefab, target.transform.position, Quaternion.identity, target.transform);
            Destroy(fx, lifetime);
        }

        // ---------------- Platzhalter ohne Modell ----------------

        private void BuildPlaceholder(TotemKind kind, float radius, bool withDisc)
        {
            var visual = new GameObject("Visual").transform;
            visual.SetParent(transform, false);
            Visual = visual;
            Color wood = kind == TotemKind.War ? new Color(0.45f, 0.28f, 0.16f) : new Color(0.5f, 0.52f, 0.5f);
            Color top = kind == TotemKind.War ? new Color(1f, 0.35f, 0.15f) : new Color(0.3f, 0.9f, 0.6f);
            var pole = SuperBuddy.CreatePrimitive(PrimitiveType.Cylinder, "Pole", wood, visual);
            pole.transform.localPosition = new Vector3(0f, 0.65f, 0f);
            pole.transform.localScale = new Vector3(0.3f, 0.65f, 0.3f);
            var head = SuperBuddy.CreatePrimitive(PrimitiveType.Sphere, "Head", top, visual);
            head.transform.localPosition = new Vector3(0f, 1.45f, 0f);
            head.transform.localScale = Vector3.one * 0.35f;
            if (!withDisc) return;
            // Flacher, halbtransparenter Bodenring in Radiusgröße (nur ohne eigenes Aura-Prefab)
            var ring = SuperBuddy.CreatePrimitive(PrimitiveType.Cylinder, "RadiusDisc", new Color(top.r, top.g, top.b, 0.18f), transform);
            ring.transform.localPosition = new Vector3(0f, 0.02f, 0f);
            ring.transform.localScale = new Vector3(radius * 2f, 0.005f, radius * 2f);
        }
    }
}
