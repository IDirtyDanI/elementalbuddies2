using System.Collections.Generic;
using UnityEngine;

namespace ElementalBuddies
{
    // Weltenbaum (Eis + Erde + Licht): Bollwerk + Heiler. Viel Leben, nimmt weniger Schaden.
    // Wurzeln alle ~4 s: bis zu MaxRootTargets Gegner in Reichweite (dem Nexus am nächsten zuerst) werden festgehalten
    // (wie die Dornenfalle: greifen weiter an) und nehmen EffectiveDamage. Heil-Aura jede Sekunde für Buddies, alle Spielfiguren im
    // Heil-Radius (+ gedrosselt Nexus). Mehrspieler: Heilung/Schaden nur auf dem Server, Clients zeigen dieselbe Optik.
    // Dornen: ein Anteil des erlittenen Schadens geht an den nächsten Gegner im Nahkampf-Abstand zurück (Näherung, siehe TakeDamage).
    public class WorldTreeBuddy : SuperBuddy
    {
        [Header("Wurzeln")]
        public int MaxRootTargets = 6;
        public float RootDuration = 2.5f;

        [Header("Heil-Aura")]
        public float HealInterval = 1f;
        [Range(0f, 1f)] public float HealPercent = 0.03f;   // vom MaxHP des Ziels
        public float HealFlat = 4f;
        [Tooltip("Faktor auf die Heilung des Baums selbst (sonst heilt er sich gegen Fernkämpfer endlos hoch).")]
        [Range(0f, 1f)] public float SelfHealFactor = 0.15f;
        [Range(0f, 0.05f)] public float NexusHealPercentPerSecond = 0.005f; // gedeckelt wie beim Licht-Buddy
        [Tooltip("Heil-Radius (0 = Reichweite).")]
        public float HealRadius = 0f;

        [Header("Dornen")]
        [Range(0f, 2f)] public float ThornsFactor = 0.2f;
        public float ThornsRange = 2.5f;

        [Header("Optik")]
        public GameObject RootEffectPrefab;   // optional, pro gefangenem Gegner (RootDuration); leer = braune Laufzeit-Stacheln
        public GameObject HealSparklePrefab;  // optional, auf geheilten Zielen (2 s)
        public GameObject HealPulsePrefab;    // optional, Ring am Baum (Radius 1, wird skaliert; 2 s)

        private float _healTimer;
        private readonly List<EnemyBrain> _candidates = new List<EnemyBrain>();
        private static readonly List<PlayerAvatar> _avatarBuffer = new List<PlayerAvatar>();

        public int LastRooted { get; private set; }
        public float LastThornsDamage { get; private set; }

        public float EffectiveHealRadius => HealRadius > 0f ? HealRadius : EffectiveRange;

        protected override FusionElement DefaultElement => FusionElement.WorldTree;

        protected override void ApplyClassDefaults()
        {
            BaseMaxHP = 900f;
            DamageReduction = 0.25f;
        }

        protected override void Update()
        {
            base.Update();
            if (IsStunned) return;
            _healTimer += Time.deltaTime;
            if (_healTimer >= HealInterval)
            {
                _healTimer -= HealInterval;
                HealPulse(HealInterval);
            }
        }

        // Öffentlich für Tests
        public void HealPulse(float interval)
        {
            float radius = EffectiveHealRadius;
            Vector3 pos = transform.position;
            bool any = false;
            bool server = Net.IsServer;
            foreach (var b in Active)
            {
                if (b == null || b.IsDead || b.CurrentHP >= b.MaxHP) continue;
                Vector3 d = b.transform.position - pos;
                d.y = 0f;
                if (d.sqrMagnitude > radius * radius) continue;
                float amount = b.MaxHP * HealPercent + HealFlat;
                if (b == this) amount *= SelfHealFactor;
                if (!server || b.Heal(amount) > 0f)
                {
                    any = true;
                    SpawnVfx(HealSparklePrefab, b.transform.position + Vector3.up * 0.8f, 2f);
                }
            }

            // Spielfiguren im Heil-Radius (gleiche Formel wie für Buddies)
            PlayerAvatar.InRadius(pos, radius, _avatarBuffer);
            foreach (var avatar in _avatarBuffer)
            {
                var stats = avatar != null ? avatar.Stats : null;
                if (stats == null || stats.CurrentHP >= stats.MaxHP) continue;
                if (server) stats.Heal(stats.MaxHP * HealPercent + HealFlat);
                any = true;
                SpawnVfx(HealSparklePrefab, stats.transform.position + Vector3.up * 0.8f, 2f);
            }

            var nexus = Nexus.Instance;
            if (nexus != null && nexus.CurrentHP < nexus.MaxHP && nexus.GetDistanceFrom(pos) <= radius)
                if (!server || nexus.Heal(nexus.MaxHP * NexusHealPercentPerSecond * interval) > 0f) any = true;

            if (any && HealPulsePrefab != null)
            {
                var pulse = Instantiate(HealPulsePrefab, pos + Vector3.up * 0.1f, Quaternion.identity);
                pulse.transform.localScale = Vector3.Scale(pulse.transform.localScale, new Vector3(radius, 1f, radius));
                Destroy(pulse, 2f);
            }
        }

        protected override bool TryPerformAction()
        {
            _candidates.Clear();
            foreach (var e in FindEnemies(transform.position, EffectiveRange))
                if (e != null && !e.IsDead) _candidates.Add(e);
            if (_candidates.Count == 0) return false;

            // Dem Nexus am nächsten zuerst (ohne Nexus: dem Baum am nächsten)
            var nexus = Nexus.Instance;
            Vector3 self = transform.position;
            _candidates.Sort((a, b) => Score(a, nexus, self).CompareTo(Score(b, nexus, self)));

            float damage = EffectiveDamage;
            int n = Mathf.Min(_candidates.Count, Mathf.Max(1, MaxRootTargets));
            LastRooted = 0;
            for (int i = 0; i < n; i++)
            {
                var e = _candidates[i];
                Root(e, RootDuration);
                SpawnRootVisual(e);
                if (Net.IsServer) e.TakeDamage(damage);
                LastRooted++;
            }
            CurrentTarget = _candidates[0] != null ? _candidates[0].transform : null;
            GameAudio.Play(SfxId.StoneWall, self);
            return true;
        }

        private static float Score(EnemyBrain e, Nexus nexus, Vector3 self)
        {
            if (e == null) return float.MaxValue;
            return nexus != null ? nexus.GetDistanceFrom(e.transform.position) : (e.transform.position - self).sqrMagnitude;
        }

        private void SpawnRootVisual(EnemyBrain e)
        {
            if (e == null) return;
            if (RootEffectPrefab != null)
            {
                var vfx = Instantiate(RootEffectPrefab, e.transform.position, Quaternion.identity, e.transform);
                Destroy(vfx, RootDuration);
                return;
            }
            // Laufzeit-Platzhalter: drei braune Stacheln um die Füße
            var root = new GameObject("RootSpikes").transform;
            root.SetParent(e.transform, false);
            for (int i = 0; i < 3; i++)
            {
                var spike = CreatePrimitive(PrimitiveType.Cylinder, "Spike", new Color(0.4f, 0.26f, 0.12f), root);
                float a = i * Mathf.PI * 2f / 3f;
                spike.transform.localPosition = new Vector3(Mathf.Cos(a) * 0.35f, 0.35f, Mathf.Sin(a) * 0.35f);
                spike.transform.localRotation = Quaternion.Euler(Mathf.Sin(a) * 20f, 0f, -Mathf.Cos(a) * 20f);
                spike.transform.localScale = new Vector3(0.12f, 0.4f, 0.12f);
            }
            Destroy(root.gameObject, RootDuration);
        }

        // Dornen (Näherung): Der Angreifer ist nicht bekannt (IDamageable.TakeDamage hat keine Quelle). Stattdessen erhält der
        // nächste Gegner, der höchstens ThornsRange vom Baum-Collider entfernt steht (= Nahkampf-Abstand), ThornsFactor × Rohschaden.
        // Fernkämpfer weiter weg bleiben verschont; steht zufällig ein anderer Gegner näher, trifft es diesen.
        public override void TakeDamage(float amount)
        {
            if (!Net.IsServer || amount <= 0f || IsDead) return;
            LastThornsDamage = 0f;
            if (ThornsFactor > 0f)
            {
                EnemyBrain nearest = null;
                float best = float.MaxValue;
                foreach (var e in FindEnemies(transform.position, ThornsRange + 3f))
                {
                    if (e == null || e.IsDead) continue;
                    float d = CombatUtil.HorizontalDistance(e.transform.position, GetClosestPoint(e.transform.position));
                    if (d <= ThornsRange && d < best)
                    {
                        best = d;
                        nearest = e;
                    }
                }
                if (nearest != null)
                {
                    LastThornsDamage = amount * ThornsFactor;
                    nearest.TakeDamage(LastThornsDamage);
                }
            }
            base.TakeDamage(amount);
        }
    }
}
