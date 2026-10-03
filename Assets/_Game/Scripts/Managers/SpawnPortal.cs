using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

namespace ElementalBuddies
{
    // Gegner-Portal am Kartenrand. Öffnet sich ab Welle OpenFromWave (WaveManager öffnet vor jedem Wellenstart);
    // der WaveManager spawnt nur aus offenen Portalen. Ohne Portale in der Szene nutzt er seine SpawnPoints-Liste.
    public class SpawnPortal : MonoBehaviour
    {
        private static readonly List<SpawnPortal> _all = new List<SpawnPortal>();
        public static IReadOnlyList<SpawnPortal> All => _all;

        [Tooltip("Himmelsrichtung für Meldungen, z. B. „Westen“ → „Ein neues Portal öffnet sich im Westen!“")]
        public string DisplayName = "Westen";
        [Tooltip("Ab dieser Welle (1-basiert) ist das Portal offen.")]
        public int OpenFromWave = 1;
        [Tooltip("Spawn-Position (Fallback: dieses Transform).")]
        public Transform SpawnPoint;
        [Tooltip("Zufälliger Streuradius um den Spawnpunkt (auf NavMesh gesnappt).")]
        public float SpawnSpread = 1.5f;

        [Header("Visuals")]
        [Tooltip("Aktiv, solange das Portal offen ist (leuchtende Fläche, Partikel ...).")]
        public GameObject ActiveVisuals;
        [Tooltip("Optional: aktiv, solange das Portal geschlossen ist.")]
        public GameObject InactiveVisuals;

        public bool IsOpen { get; private set; }
        public event System.Action<SpawnPortal> OnOpenChanged;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRegistry() => _all.Clear();

        void Awake()
        {
            ApplyVisuals();
        }

        void OnEnable()
        {
            if (!_all.Contains(this)) _all.Add(this);
        }

        void OnDisable()
        {
            _all.Remove(this);
        }

        public void SetOpen(bool open)
        {
            if (IsOpen == open) return;
            IsOpen = open;
            ApplyVisuals();
            OnOpenChanged?.Invoke(this);
        }

        private void ApplyVisuals()
        {
            if (ActiveVisuals != null) ActiveVisuals.SetActive(IsOpen);
            if (InactiveVisuals != null) InactiveVisuals.SetActive(!IsOpen);
        }

        public Transform SpawnTransform => SpawnPoint != null ? SpawnPoint : transform;

        // Zufälliger Punkt um den Spawnpunkt, möglichst auf dem NavMesh
        public Vector3 GetSpawnPosition()
        {
            Vector3 basePos = SpawnTransform.position;
            Vector2 r = Random.insideUnitCircle * Mathf.Max(0f, SpawnSpread);
            Vector3 pos = basePos + new Vector3(r.x, 0f, r.y);
            if (NavMesh.SamplePosition(pos, out NavMeshHit hit, 3f, NavMesh.AllAreas)) return hit.position;
            if (NavMesh.SamplePosition(basePos, out hit, 3f, NavMesh.AllAreas)) return hit.position;
            return basePos;
        }

        void OnDrawGizmos()
        {
            Gizmos.color = IsOpen ? new Color(0.7f, 0.2f, 1f) : new Color(0.4f, 0.4f, 0.4f);
            Vector3 p = SpawnTransform.position;
            Gizmos.DrawWireSphere(p, Mathf.Max(0.5f, SpawnSpread));
        }
    }
}
