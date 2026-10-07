using UnityEngine;
using System.Collections;
using System.Collections.Generic;

namespace ElementalBuddies
{
    // Licht-Buddy: Lichtstrahl gegen Gegner + regelmäßiger Segen, der alle Spielfiguren, Buddies und (gedrosselt) den Nexus
    // im Wirkbereich heilt. Heilung/Schaden nur auf dem Server; Clients zeigen Strahl und Funkeln.
    public class HealerBuddy : ElementalBuddy
    {
        [Header("Lichtstrahl")]
        public Transform FirePoint;
        public Color BeamColor = new Color(1f, 0.9f, 0.45f, 1f);
        public float BeamWidth = 0.14f;
        public float BeamDuration = 0.18f;
        public GameObject HitEffectPrefab; // optional, am Ziel abgespielt

        [Header("Segen (Heil-Puls)")]
        public float BlessInterval = 4f;
        public float BlessHeal = 10f;               // wird mit der Stufe skaliert (wie Schaden)
        [Range(0f, 1f)] public float NexusHealCapPercent = 0.01f; // max. 1 % der Nexus-HP pro Puls
        public GameObject BlessPulsePrefab;         // optional, Ring-Effekt in Reichweite
        public GameObject HealSparklePrefab;        // optional, Funkeln auf geheilten Zielen

        [Header("Stufe 4 – Sonnenerzengel")]
        public float Stage4HealMultiplier = 2f;
        [Tooltip("Schild für geheilte Buddies (absorbiert Schaden zuerst).")]
        public float Stage4ShieldAmount = 20f;
        public float Stage4ShieldDuration = 4f;

        private float _nextBlessTime;
        private static readonly List<PlayerAvatar> _avatarBuffer = new List<PlayerAvatar>();
        private LineRenderer _beam;

        protected override void Start()
        {
            base.Start();
            _nextBlessTime = Time.time + BlessInterval * 0.5f;
            CreateBeam();
        }

        protected override void Update()
        {
            base.Update();
            if (Config == null || IsStunned || Time.time < _nextBlessTime) return;
            if (GameManager.Instance != null && GameManager.Instance.CurrentState == GameState.GameOver) return;

            _nextBlessTime = Time.time + BlessInterval;
            Bless();
        }

        // ---------------- Lichtstrahl ----------------

        protected override bool TryPerformAction()
        {
            Collider[] hits = Physics.OverlapSphere(transform.position, EffectiveRange);
            Transform bestTarget = null;
            float closestDist = float.MaxValue;

            foreach (var hit in hits)
            {
                if (!hit.CompareTag("Enemy")) continue;
                float d = Vector3.Distance(transform.position, hit.transform.position);
                if (d < closestDist)
                {
                    closestDist = d;
                    bestTarget = hit.transform;
                }
            }

            if (bestTarget == null) return false;

            CurrentTarget = bestTarget;
            var damageable = bestTarget.GetComponent<IDamageable>();
            Vector3 hitPoint = bestTarget.position + Vector3.up * 0.9f;
            if (damageable != null && Net.IsServer) damageable.TakeDamage(EffectiveDamage);

            StopCoroutine(nameof(ShowBeam));
            StartCoroutine(ShowBeam(hitPoint));
            if (HitEffectPrefab != null) Destroy(Instantiate(HitEffectPrefab, hitPoint, Quaternion.identity), 2f);
            return true;
        }

        private void CreateBeam()
        {
            var go = new GameObject("LightBeam");
            go.transform.SetParent(transform, false);
            _beam = go.AddComponent<LineRenderer>();
            _beam.positionCount = 2;
            _beam.useWorldSpace = true;
            _beam.numCapVertices = 4;
            _beam.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _beam.receiveShadows = false;
            _beam.material = new Material(Shader.Find("Sprites/Default"));
            _beam.enabled = false;
        }

        private IEnumerator ShowBeam(Vector3 target)
        {
            if (_beam == null) yield break;
            Vector3 origin = FirePoint != null ? FirePoint.position : transform.position + Vector3.up;
            _beam.enabled = true;

            float t = 0f;
            while (t < BeamDuration)
            {
                float k = 1f - t / BeamDuration;
                _beam.SetPosition(0, FirePoint != null ? FirePoint.position : origin);
                _beam.SetPosition(1, target);
                _beam.startWidth = BeamWidth * k;
                _beam.endWidth = BeamWidth * 0.5f * k;
                var c = BeamColor; c.a = k;
                _beam.startColor = c;
                _beam.endColor = new Color(1f, 1f, 1f, k);
                t += Time.deltaTime;
                yield return null;
            }
            _beam.enabled = false;
        }

        // ---------------- Segen ----------------

        public float EffectiveBlessHeal => GetBlessHealAtLevel(Level);
        public float GetBlessHealAtLevel(int level) =>
            BlessHeal * LevelMultiplier(DamageBonusPerLevel, level) * (level >= PerkLevel ? Stage4HealMultiplier : 1f);

        private void Bless()
        {
            float range = EffectiveRange;
            float heal = EffectiveBlessHeal;
            bool healedAny = false;
            // Überall simulieren, Server entscheidet: Clients heilen nicht, zeigen aber dieselbe Optik (gespiegelte HP)
            bool server = Net.IsServer;

            // Alle Spielfiguren im Wirkbereich
            PlayerAvatar.InRadius(transform.position, range, _avatarBuffer);
            foreach (var avatar in _avatarBuffer)
            {
                var stats = avatar != null ? avatar.Stats : null;
                if (stats == null || stats.CurrentHP >= stats.MaxHP) continue;
                if (server) stats.Heal(heal);
                Sparkle(stats.transform.position);
                healedAny = true;
            }

            // Buddies (inkl. sich selbst)
            foreach (var buddy in ElementalBuddy.Active)
            {
                if (buddy == null || buddy.CurrentHP >= buddy.MaxHP) continue;
                if (Vector3.Distance(transform.position, buddy.transform.position) > range) continue;
                if (!server || buddy.Heal(heal) > 0f)
                {
                    if (server && HasPerk) buddy.AddShield(Stage4ShieldAmount, Stage4ShieldDuration);
                    Sparkle(buddy.transform.position);
                    healedAny = true;
                }
            }

            // Nexus (gedrosselt)
            var nexus = Nexus.Instance;
            if (nexus != null && nexus.CurrentHP < nexus.MaxHP && nexus.GetDistanceFrom(transform.position) <= range)
            {
                float capped = Mathf.Min(heal, nexus.MaxHP * NexusHealCapPercent);
                if (!server || nexus.Heal(capped) > 0f)
                {
                    Sparkle(nexus.GetClosestPoint(transform.position));
                    healedAny = true;
                }
            }

            // Ring nur zeigen, wenn wirklich jemand geheilt wurde (sonst flackert es ständig)
            if (healedAny && BlessPulsePrefab != null)
            {
                var pulse = Instantiate(BlessPulsePrefab, transform.position + Vector3.up * 0.1f, Quaternion.identity);
                pulse.transform.localScale = Vector3.one * Mathf.Max(0.1f, range / 3f);
                Destroy(pulse, 3f);
            }
        }

        private void Sparkle(Vector3 position)
        {
            if (HealSparklePrefab == null) return;
            Destroy(Instantiate(HealSparklePrefab, position + Vector3.up * 0.8f, Quaternion.identity), 2f);
        }
    }
}
