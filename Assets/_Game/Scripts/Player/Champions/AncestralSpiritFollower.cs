using System.Collections.Generic;
using UnityEngine;

namespace ElementalBuddies
{
    // Ahnengeist des Schamanen (V): Geisterwolf, der dem Schamanen schwebend folgt und alle TickInterval s Spieler und
    // Buddies im Radius heilt, den Nexus gedrosselt (Anteil der Max-LP je NexusInterval, wie der Licht-Buddy).
    // Entsteht im Cast auf allen Rechnern: Folgen und Funkeln überall, Heilung nur mit Net.IsServer.
    public class AncestralSpiritFollower : MonoBehaviour
    {
        [Tooltip("Abstand hinter/neben dem Schamanen (lokal zur Blickrichtung des Schamanen).")]
        public Vector3 FollowOffset = new Vector3(1.1f, 0.6f, -0.9f);
        public float FollowSharpness = 6f;
        public float BobAmplitude = 0.15f;
        public float BobSpeed = 2.2f;

        public ShamanKit Owner { get; private set; }
        public float Remaining => _ending ? 0f : Mathf.Max(0f, _duration - _age);

        private AncestralSpiritSpell _spell;
        private float _duration, _age, _nextTick, _nextNexus, _endT;
        private bool _ending;
        private static readonly List<PlayerAbilities> _players = new List<PlayerAbilities>();

        private const float FadeTime = 0.5f;

        public static AncestralSpiritFollower Spawn(GameObject prefab, ShamanKit owner, AncestralSpiritSpell spell, Vector3 position)
        {
            Quaternion rot = owner != null ? owner.transform.rotation : Quaternion.identity;
            GameObject go = prefab != null ? Instantiate(prefab, position, rot) : new GameObject("AncestralSpirit");
            go.transform.SetPositionAndRotation(position, rot);
            var f = go.GetComponent<AncestralSpiritFollower>();
            if (f == null) f = go.AddComponent<AncestralSpiritFollower>();
            if (prefab == null) f.BuildPlaceholder();
            f.Setup(owner, spell);
            return f;
        }

        public void Setup(ShamanKit owner, AncestralSpiritSpell spell)
        {
            Owner = owner;
            _spell = spell;
            _duration = spell != null ? Mathf.Max(0.1f, spell.Duration) : 1f;
            _age = 0f;
            _nextTick = spell != null ? spell.TickInterval : 1f; // erster Heil-Tick nach einem Intervall (7 LP/s × 8 s = 56)
            _nextNexus = 0f;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            Follow(dt);
            if (_ending)
            {
                _endT += dt;
                if (_endT >= FadeTime) Destroy(gameObject);
                return;
            }
            _age += dt;
            if (_spell != null)
            {
                if (_age >= _nextTick && _age <= _duration + 0.01f)
                {
                    _nextTick += Mathf.Max(0.1f, _spell.TickInterval);
                    HealTick();
                }
                if (_age >= _nextNexus && _age <= _duration)
                {
                    _nextNexus += Mathf.Max(0.1f, _spell.NexusInterval);
                    NexusTick();
                }
            }
            // Besitzer weg oder gefallen → vorzeitig beenden
            bool ownerGone = Owner == null || !Owner.isActiveAndEnabled || (Owner.OwnerStats != null && Owner.OwnerStats.IsDead);
            if (_age >= _duration || ownerGone) End();
        }

        private void Follow(float dt)
        {
            if (Owner == null) return;
            Transform t = Owner.transform;
            Vector3 target = t.position + t.rotation * FollowOffset + Vector3.up * (Mathf.Sin(Time.time * BobSpeed) * BobAmplitude);
            Vector3 prev = transform.position;
            transform.position = Vector3.Lerp(prev, target, 1f - Mathf.Exp(-FollowSharpness * dt));
            Vector3 move = transform.position - prev;
            move.y = 0f;
            Vector3 face = move.sqrMagnitude > 0.0004f ? move : t.forward;
            face.y = 0f;
            if (face.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(face.normalized), 1f - Mathf.Exp(-8f * dt));
        }

        // Heilung im Radius um den Schamanen: Spieler (inkl. Schamane) und Buddies
        private void HealTick()
        {
            if (Owner == null) return;
            Vector3 center = Owner.transform.position;
            float heal = _spell.HealPerSecond * Mathf.Max(0.1f, _spell.TickInterval);
            bool server = Net.IsServer;

            ShamanKit.CollectPlayers(center, _spell.Radius, Owner.OwnerAbilities, _players);
            foreach (var p in _players)
            {
                var stats = p != null ? p.GetComponent<PlayerStats>() : null;
                if (stats == null || stats.IsDead || stats.CurrentHP >= stats.MaxHP) continue;
                if (server) stats.Heal(heal);
                Sparkle(stats.transform.position);
            }

            var buddies = ElementalBuddy.Active;
            for (int i = buddies.Count - 1; i >= 0; i--)
            {
                var b = buddies[i];
                if (b == null || b.IsDead || b.CurrentHP >= b.MaxHP) continue;
                if (CombatUtil.HorizontalDistance(center, b.transform.position) > _spell.Radius) continue;
                if (server) b.Heal(heal);
                Sparkle(b.transform.position);
            }
        }

        // Nexus gedrosselt: NexusHealPercent der Max-LP je NexusInterval (wie der Segen des Licht-Buddys)
        private void NexusTick()
        {
            var nexus = Nexus.Instance;
            if (Owner == null || nexus == null || nexus.CurrentHP >= nexus.MaxHP) return;
            if (nexus.GetDistanceFrom(Owner.transform.position) > _spell.Radius) return;
            float amount = nexus.MaxHP * Mathf.Clamp01(_spell.NexusHealPercent);
            if (Net.IsServer) nexus.Heal(amount);
            Sparkle(nexus.GetClosestPoint(Owner.transform.position));
        }

        private void Sparkle(Vector3 position)
        {
            if (_spell == null || _spell.HealFxPrefab == null) return;
            CombatUtil.SpawnFx(_spell.HealFxPrefab, position + Vector3.up * 0.8f, Quaternion.identity, 2f);
        }

        public void End()
        {
            if (_ending) return;
            _ending = true;
            _endT = 0f;
            foreach (var ps in GetComponentsInChildren<ParticleSystem>()) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        // Platzhalter ohne Modell: halbtransparenter Cyan-Körper mit Kopf
        private void BuildPlaceholder()
        {
            var c = new Color(0.4f, 0.95f, 1f, 0.45f);
            var body = SuperBuddy.CreatePrimitive(PrimitiveType.Capsule, "Body", c, transform);
            body.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            body.transform.localScale = new Vector3(0.45f, 0.6f, 0.45f);
            var head = SuperBuddy.CreatePrimitive(PrimitiveType.Sphere, "Head", c, transform);
            head.transform.localPosition = new Vector3(0f, 0.15f, 0.65f);
            head.transform.localScale = Vector3.one * 0.35f;
        }
    }
}
