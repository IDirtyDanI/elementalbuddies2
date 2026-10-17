using System.Collections.Generic;
using UnityEngine;

namespace ElementalBuddies
{
    // Ritualplatz eines Beschwörers (Champion-Bedrohungen): 6–8 je Map abseits der Gegnerwege, vom Nexus aus zu Fuß
    // erreichbar (Editor/ChampionThreatSetup legt sie an). Statische Registry; ein Platz trägt höchstens einen Beschwörer
    // (Belegung nur auf dem Server, SummonerBrain reserviert beim Spawn und gibt beim Tod frei).
    public class RitualSite : MonoBehaviour
    {
        private static readonly List<RitualSite> _all = new List<RitualSite>();
        public static IReadOnlyList<RitualSite> All => _all;

        public EnemyBrain Occupant { get; private set; }
        public bool IsFree => Occupant == null || Occupant.IsDead;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _all.Clear();

        void OnEnable()
        {
            if (!_all.Contains(this)) _all.Add(this);
        }

        void OnDisable()
        {
            _all.Remove(this);
            Occupant = null;
        }

        public bool TryReserve(EnemyBrain e)
        {
            if (e == null || !IsFree) return false;
            Occupant = e;
            return true;
        }

        public void Release(EnemyBrain e)
        {
            if (Occupant == e) Occupant = null;
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.66f, 0.42f, 1f, 0.8f);
            Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.1f, 2f);
            Gizmos.DrawLine(transform.position, transform.position + Vector3.up * 2.5f);
        }
    }
}
