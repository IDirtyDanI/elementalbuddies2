using System.Collections.Generic;
using UnityEngine;

namespace ElementalBuddies
{
    // Fähigkeitstypen der Bosse (angelehnt an die Champion-Fähigkeiten)
    public enum BossAbilityKind
    {
        Slam,      // Sprung zum Ziel + Kreis am Landepunkt (wie Erdbeben)
        Whirl,     // Kreis um den Boss (wie Flammenwirbel), optional Rückstoß
        Cone,      // Kegel nach vorn (wie Flammenwelle)
        Nova,      // Kreis um den Boss, verlangsamt Spieler / betäubt Buddies (wie Frostnova)
        AllyHeal,  // heilt andere Gegner im Kreis (dunkler Heiliger Kreis)
        Rain,      // Kreis am Ziel, mehrere Schadens-Pulse (wie Feuerpfeil-Regen)
        Beam,      // Linie in Zielrichtung (wie Lichtpfeil)
        Throw      // Wurfgeschoss entlang einer Linie (Sichel, Axt): trifft nur, wen es im Flug berührt – ausweichbar
    }

    // Eine Boss-Fähigkeit: alle Parameter in einer Klasse, je nach Kind wird nur ein Teil genutzt.
    // Reihenfolge in BossBrain.Abilities = Priorität (erste bereite Fähigkeit gewinnt).
    [System.Serializable]
    public class BossAbility
    {
        public string Name = "Fähigkeit";
        public BossAbilityKind Kind = BossAbilityKind.Whirl;
        public bool Enabled = true;

        [Header("Timing")]
        public float Cooldown = 8f;
        [Tooltip("Ausholzeit: so lange füllt sich die Warnfläche (Slam: plus Sprungdauer).")]
        public float Windup = 1f;
        [Tooltip("Erholung nach dem Effekt (Boss steht noch still).")]
        public float Recovery = 0.5f;
        [Tooltip("Animator-Trigger beim Ausholen (nur wenn der Parameter existiert).")]
        public string AnimatorTrigger = "";

        [Header("Bedingung")]
        [Tooltip("Ziel (Spieler/Buddy) höchstens so weit entfernt. Whirl/Nova: irgendein Spieler/Buddy so nah. 0 = Flächengröße.")]
        public float TriggerRange = 8f;
        [Tooltip("Throw: Ziel mindestens so weit entfernt (Fernkampf-Druck; im Nahkampf bleiben die anderen Fähigkeiten).")]
        public float MinTriggerRange = 0f;

        [Header("Fläche")]
        [Tooltip("Kreis-Radius (Slam, Whirl, Nova, AllyHeal, Rain).")]
        public float Radius = 4f;
        [Tooltip("Kegel-Reichweite bzw. Linien-Länge (Cone, Beam).")]
        public float Length = 8f;
        [Tooltip("Volle Kegelöffnung in Grad (Cone).")]
        public float ConeAngle = 60f;
        [Tooltip("Linien-Breite (Beam).")]
        public float Width = 1.4f;
        [Tooltip("Rain: maximale Entfernung der Kreismitte vom Boss.")]
        public float MaxCastRange = 16f;
        [Tooltip("Beam: Wände (diese Layer) kürzen die Linie. 0 = keine Wandprüfung.")]
        public LayerMask ObstacleLayer = 1;

        [Header("Wirkung")]
        public float Damage = 30f;
        [Tooltip("Schadensfaktor gegen Buddies.")]
        public float BuddyDamageMultiplier = 1f;
        [Tooltip("Auch der Nexus nimmt Schaden, wenn er in der Fläche liegt.")]
        public bool HitsNexus = false;
        [Tooltip("Verlangsamung des Spielers (0.4 = 40 %).")]
        [Range(0f, 1f)] public float PlayerSlow = 0f;
        public float PlayerSlowDuration = 0f;
        public float PlayerKnockback = 0f;
        public float BuddyStunDuration = 0f;
        [Tooltip("Sterne über betäubten Buddies (leer = GlobalSettings.BuddyStunEffectPrefab).")]
        public GameObject StunVfxPrefab;

        [Header("Slam")]
        public float LeapDistance = 6f;
        public float LeapDuration = 0.45f;
        public float LeapHeight = 1.5f;

        [Header("AllyHeal")]
        [Tooltip("Heilung anderer Gegner in Prozent ihrer MaxHP.")]
        [Range(0f, 1f)] public float HealPercent = 0.2f;
        [Tooltip("Heilung des Bosses selbst in Prozent seiner MaxHP.")]
        [Range(0f, 1f)] public float SelfHealPercent = 0.05f;
        [Tooltip("Bedingung: mindestens MinAllies andere Gegner im Radius unter diesem HP-Anteil.")]
        [Range(0f, 1f)] public float HealThreshold = 0.8f;
        public int MinAllies = 2;

        [Header("Rain")]
        [Tooltip("Anzahl der Regenflächen je Wurf (1 = nur auf dem Ziel). Weitere Flächen liegen auf anderen Spielern in Reichweite, sonst zufällig RainSpread m um die erste; Schaden gilt je Fläche.")]
        public int RainAreas = 1;
        [Tooltip("Abstand weiterer Regenflächen von der ersten (min, max in m).")]
        public Vector2 RainSpread = new Vector2(4f, 7f);
        public int Pulses = 5;
        public float PulseDuration = 1.25f;
        [Tooltip("Fallende Pfeile pro Puls (nur Optik, braucht FallingArrowPrefab).")]
        public int ArrowsPerPulse = 4;
        public GameObject FallingArrowPrefab;
        [Tooltip("Einschlag pro Pfeil bzw. pro Puls (ohne Pfeil-Prefab).")]
        public GameObject PulseEffectPrefab;

        [Header("Throw")]
        [Tooltip("Fluggeschwindigkeit des Geschosses (m/s).")]
        public float ThrowSpeed = 20f;
        [Tooltip("Anzahl der Geschosse als Fächer (1 = eines aufs Ziel).")]
        public int ThrowCount = 1;
        [Tooltip("Winkel zwischen zwei Geschossen des Fächers (Grad).")]
        public float ThrowSpreadAngle = 15f;
        [Tooltip("Das Geschoss kehrt auf derselben Linie zurück (zweiter Durchgang, trifft erneut).")]
        public bool ThrowReturns = false;
        [Tooltip("Zielzeit am Anfang des Ausholens: so lange beobachtet der Boss die Laufrichtung, erst dann erscheint die Warnlinie.")]
        public float ThrowAimTime = 0.25f;
        [Tooltip("Vorhalten: 0 = auf die aktuelle Position, 1 = voll auf den Punkt, den das Ziel bei gleicher Laufrichtung erreicht.")]
        [Range(0f, 1f)] public float ThrowLead = 0.6f;
        [Tooltip("Drehung des Geschoss-Modells (Grad/s).")]
        public float ThrowSpin = 900f;
        [Tooltip("Modell des Geschosses (liegt flach, dreht um die Hochachse).")]
        public GameObject ProjectilePrefab;
        public SfxId HitSfx = SfxId.EnemyChop;

        [Header("Optik / Ton")]
        [Tooltip("Einschlag-Effekt (Mitte der Fläche), Skalierung = Flächengröße, wenn ScaleEffectBySize.")]
        public GameObject ImpactEffectPrefab;
        public float EffectLifetime = 3f;
        public bool ScaleEffectBySize = true;
        [Tooltip("Whirl: SlashArcFx-Prefab für den 360°-Bogen (Farbe = ThemeColor).")]
        public GameObject ArcFxPrefab;
        [Tooltip("Beam: BeamFx-Prefab (Farbe = ThemeColor).")]
        public GameObject BeamPrefab;
        public SfxId ImpactSfx = SfxId.StoneWall;
        public bool PlaySfx = true;

        // Laufzeit
        [System.NonSerialized] public float ReadyTime;

        // Steinwall blockt diese Fähigkeit (gerichtete Angriffe: Kegel, Strahl) – Kreise/Regen/Heilung nicht
        public bool BlockedByWalls => Kind == BossAbilityKind.Cone || Kind == BossAbilityKind.Beam || Kind == BossAbilityKind.Throw;

        // Größe der Fläche (für Effekt-Skalierung und Default-Bedingung)
        public float AreaSize => Kind == BossAbilityKind.Cone || Kind == BossAbilityKind.Beam || Kind == BossAbilityKind.Throw ? Length : Radius;
    }

    // Treffer-Helfer der Boss-Fähigkeiten: alle Spielfiguren, Buddies, optional Nexus – nie andere Gegner. Nur auf dem Server.
    public static class BossCombat
    {
        private const float PlayerRadius = 0.4f;
        private static readonly List<ElementalBuddy> _buddies = new List<ElementalBuddy>();
        private static readonly List<PlayerStats> _players = new List<PlayerStats>();

        // Alle Spielfiguren (Mehrspieler: PlayerAvatar.All; ohne Netz-Figuren Fallback auf das Player-Tag)
        public static List<PlayerStats> CollectPlayers(List<PlayerStats> result)
        {
            result.Clear();
            var all = PlayerAvatar.All;
            if (all.Count > 0)
            {
                for (int i = 0; i < all.Count; i++)
                    if (all[i] != null && all[i].Stats != null) result.Add(all[i].Stats);
            }
            else if (Player != null) result.Add(Player);
            return result;
        }

        // Wand-Prüfung in fester Höhe über dem Boden (Boss-Pivots schweben je nach baseOffset unterschiedlich hoch;
        // der Steinwall ist 2,5 m hoch)
        public const float WallCheckHeight = 1.2f;
        public static Vector3 WallCheckPoint(Vector3 p) => new Vector3(p.x, AoeTelegraph.GroundY(p) + WallCheckHeight, p.z);

        // Steht ein Steinwall zwischen Quelle und Ziel?
        public static bool WallBetween(Vector3 source, Vector3 target) =>
            StoneWall.AnyActive && StoneWall.Blocks(WallCheckPoint(source), WallCheckPoint(target));

        public static bool IsAlive(PlayerStats p) => p != null && p.isActiveAndEnabled && p.CurrentHP > 0f && !p.IsDead;

        private static PlayerStats _player;
        // Veraltet (Einzelspieler-Fallback): erste Figur mit Player-Tag. Spiellogik nutzt CollectPlayers.
        public static PlayerStats Player
        {
            get
            {
                if (_player == null)
                {
                    var go = GameObject.FindGameObjectWithTag("Player");
                    if (go != null) _player = go.GetComponent<PlayerStats>();
                }
                return _player;
            }
        }

        // Schaden + Effekte in der Fläche. source = Herkunft für den Schildblock (Boss bzw. Einschlag)
        public static void Apply(AoeShape shape, BossAbility a, Vector3 source, float damage)
        {
            if (!Net.IsServer) return;
            bool walls = a.BlockedByWalls && StoneWall.AnyActive;

            // Alle Spielfiguren in der Fläche (Slow/Rückstoß leitet PlayerController an den Besitzer weiter)
            CollectPlayers(_players);
            foreach (var player in _players)
            {
                if (!IsAlive(player) || !shape.Contains(player.transform.position, PlayerRadius)) continue;
                if (walls && WallBetween(source, player.transform.position)) continue;
                bool immune = player.IsInvulnerable;
                if (damage > 0f) player.TakeDamage(damage, source);
                if (immune) continue;
                var pc = player.GetComponent<PlayerController>();
                if (pc == null) continue;
                if (a.PlayerSlow > 0f && a.PlayerSlowDuration > 0f) pc.ApplySlow(a.PlayerSlow, a.PlayerSlowDuration);
                if (a.PlayerKnockback > 0f)
                    pc.ApplyKnockback(CombatUtil.FlatDirection(source, player.transform.position, shape.Forward), a.PlayerKnockback, 0.25f);
            }
            _players.Clear();

            // Kopie: sterbende Buddies verlassen die Registry sofort
            _buddies.Clear();
            _buddies.AddRange(ElementalBuddy.Active);
            foreach (var b in _buddies)
            {
                if (b == null || b.IsDead || !b.isActiveAndEnabled) continue;
                Vector3 p = b.GetClosestPoint(shape.Origin);
                if (!shape.Contains(p, 0.1f) && !shape.Contains(b.transform.position, 0.4f)) continue;
                if (walls && WallBetween(source, b.transform.position) && WallBetween(source, p)) continue;
                if (a.BuddyStunDuration > 0f) b.Stun(a.BuddyStunDuration, a.StunVfxPrefab);
                if (damage > 0f) b.TakeDamage(damage * a.BuddyDamageMultiplier);
            }
            _buddies.Clear();

            if (a.HitsNexus && damage > 0f && Nexus.Instance != null)
            {
                Vector3 p = Nexus.Instance.GetClosestPoint(shape.Origin);
                if (shape.Contains(p, 0.1f) && !(walls && WallBetween(source, p))) Nexus.Instance.TakeDamage(damage);
            }
        }

        // Einzeltreffer eines Wurfgeschosses auf eine Spielfigur (Schaden, Verlangsamung, Rückstoß wie in Apply)
        public static void HitPlayer(PlayerStats player, BossAbility a, Vector3 source, Vector3 forward, float damage)
        {
            if (!Net.IsServer || !IsAlive(player)) return;
            bool immune = player.IsInvulnerable;
            if (damage > 0f) player.TakeDamage(damage, source);
            if (immune) return;
            var pc = player.GetComponent<PlayerController>();
            if (pc == null) return;
            if (a.PlayerSlow > 0f && a.PlayerSlowDuration > 0f) pc.ApplySlow(a.PlayerSlow, a.PlayerSlowDuration);
            if (a.PlayerKnockback > 0f) pc.ApplyKnockback(forward, a.PlayerKnockback, 0.25f);
        }

        public const float PlayerHitRadius = PlayerRadius;

        // Effekt-Prefab (null-sicher), optional nach Flächengröße skaliert. Auf den Boden gesetzt: der Boss-Pivot
        // liegt um NavMeshAgent.baseOffset × Skalierung über dem Boden (bei Bossen ~2 m).
        public static void SpawnEffect(BossAbility a, GameObject prefab, Vector3 pos, Quaternion rot)
        {
            if (prefab == null) return;
            pos.y = AoeTelegraph.GroundY(pos);
            CombatUtil.SpawnFx(prefab, pos, rot, a.EffectLifetime, a.ScaleEffectBySize ? Mathf.Max(0.1f, a.AreaSize) : 1f);
        }
    }

    // Laufzeit-Objekt des Pfeilhagels: Schadens-Pulse über PulseDuration, fallende Pfeile (Optik), Warnfläche bleibt
    // bis zum Ende stehen. Läuft unabhängig vom Boss weiter (auch wenn er stirbt).
    // Mehrspieler: Server mit Schaden; Clients spielen eine reine Optik-Kopie (visualOnly, Warnfläche endet per Server-RPC).
    public class BossRainArea : MonoBehaviour
    {
        private AoeShape _shape;
        private BossAbility _ability;
        private AoeTelegraph _telegraph;
        private float _damageMultiplier = 1f;
        private float _t;
        private int _done;
        private bool _visualOnly;

        public void SetupVisual(AoeShape shape, BossAbility ability)
        {
            Setup(shape, ability, null, 0f);
            _visualOnly = true;
        }

        public void Setup(AoeShape shape, BossAbility ability, AoeTelegraph telegraph, float damageMultiplier = 1f)
        {
            _damageMultiplier = damageMultiplier;
            _shape = shape;
            _ability = ability;
            _telegraph = telegraph;
            transform.position = shape.Origin;
        }

        void Update()
        {
            if (_ability == null) { Destroy(gameObject); return; }
            _t += Time.deltaTime;
            int pulses = Mathf.Max(1, _ability.Pulses);
            float interval = Mathf.Max(0.01f, _ability.PulseDuration) / pulses;
            // Erster Puls nach einem halben Intervall (Pfeile gerade gelandet)
            while (_done < pulses && _t >= interval * (_done + 0.5f))
            {
                _done++;
                Pulse();
            }
            if (_done >= pulses)
            {
                if (_telegraph != null) _telegraph.Finish(0.25f);
                Destroy(gameObject);
            }
        }

        private void Pulse()
        {
            Vector3 c = _shape.Origin;
            int arrows = Mathf.Max(0, _ability.ArrowsPerPulse);
            for (int i = 0; i < arrows; i++)
            {
                Vector2 r = Random.insideUnitCircle * _shape.Radius;
                Vector3 land = c + new Vector3(r.x, 0f, r.y);
                if (_ability.FallingArrowPrefab != null)
                {
                    Vector3 from = land + new Vector3(-1.2f, 6f, -0.6f);
                    var arrow = Instantiate(_ability.FallingArrowPrefab, from, Quaternion.LookRotation(land - from));
                    var fall = arrow.GetComponent<FallingArrow>();
                    if (fall == null) fall = arrow.AddComponent<FallingArrow>();
                    fall.Setup(land, 0.18f + Random.value * 0.06f, _ability.PulseEffectPrefab);
                }
                else if (_ability.PulseEffectPrefab != null)
                    CombatUtil.SpawnFx(_ability.PulseEffectPrefab, land, Quaternion.identity, 1.5f);
            }
            if (!_visualOnly && Net.IsServer) BossCombat.Apply(_shape, _ability, c, _ability.Damage * _damageMultiplier);
            if (_ability.PlaySfx && (_done == 1 || _done % 2 == 0)) GameAudio.Play(_ability.ImpactSfx, c);
            CameraFollow.AddTraumaAt(0.18f, c, 16f);
        }
    }

    // Wurfgeschoss eines Bosses (BossAbilityKind.Throw): fliegt entlang der Warnlinie, optional zurück. Trifft jede Figur
    // höchstens einmal je Durchgang – und nur, wenn das Geschoss sie wirklich berührt: Wer die Linie verlässt oder im
    // Moment der Berührung unverwundbar ist (Ausweichrolle, Blinzeln), bleibt heil. Ein Steinwall hält das Geschoss auf.
    // Die Warnlinie bleibt bis zum Ende stehen (der Rückweg ist genauso gefährlich).
    // Mehrspieler: Server mit Schaden; Clients fliegen eine reine Optik-Kopie (visualOnly), die Warnlinie endet per Server-RPC.
    public class BossThrowProjectile : MonoBehaviour
    {
        public const float FlightHeight = 1.1f;
        private const float StartOffset = 1.2f; // vor dem Boss, nicht in seiner Mitte

        public static readonly List<BossThrowProjectile> Active = new List<BossThrowProjectile>();

        private AoeShape _line;
        private BossAbility _ability;
        private AoeTelegraph _telegraph;
        private float _damage, _dist, _groundY;
        private bool _visualOnly, _returning;
        private Transform _visual;
        private readonly HashSet<Object> _hit = new HashSet<Object>();
        private static readonly List<PlayerStats> _players = new List<PlayerStats>();
        private static readonly List<ElementalBuddy> _buddies = new List<ElementalBuddy>();

        public bool Returning => _returning;
        public int Hits { get; private set; }
        // Zähler für das Messwerkzeug (DimensionFightBench): geworfene Geschosse und Treffer auf Spielfiguren (nur Server)
        public static int TotalThrown, TotalPlayerHits;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { Active.Clear(); TotalThrown = 0; TotalPlayerHits = 0; }

        public static BossThrowProjectile Spawn(AoeShape line, BossAbility ability, AoeTelegraph telegraph, float damage, Color theme, bool visualOnly)
        {
            var go = new GameObject("BossThrow" + (visualOnly ? " (Optik)" : ""));
            var p = go.AddComponent<BossThrowProjectile>();
            p._line = line;
            p._ability = ability;
            p._telegraph = telegraph;
            p._damage = damage;
            p._visualOnly = visualOnly;
            if (!visualOnly) TotalThrown++;
            p._groundY = AoeTelegraph.GroundY(line.Origin);
            p._dist = Mathf.Min(StartOffset, line.Length);
            go.transform.SetPositionAndRotation(p.PointAt(p._dist), Quaternion.LookRotation(line.Forward));
            if (ability.ProjectilePrefab != null)
            {
                var v = Instantiate(ability.ProjectilePrefab, go.transform);
                v.transform.localPosition = Vector3.zero;
                v.transform.localRotation = Quaternion.identity;
                p._visual = v.transform;
                foreach (var tr in v.GetComponentsInChildren<TrailRenderer>(true)) tr.Clear();
            }
            return p;
        }

        void OnEnable() => Active.Add(this);
        void OnDisable() => Active.Remove(this);

        private Vector3 PointAt(float dist)
        {
            Vector3 p = _line.Origin + _line.Forward * dist;
            p.y = _groundY + FlightHeight;
            return p;
        }

        void Update()
        {
            if (_ability == null) { End(); return; }
            float step = Mathf.Max(1f, _ability.ThrowSpeed) * Time.deltaTime;
            Vector3 from = transform.position;
            bool done = false;
            if (!_returning)
            {
                _dist += step;
                if (_dist >= _line.Length)
                {
                    _dist = _line.Length;
                    if (_ability.ThrowReturns) { _returning = true; _hit.Clear(); }
                    else done = true;
                }
            }
            else
            {
                _dist -= step;
                if (_dist <= 0f) { _dist = 0f; done = true; }
            }
            Vector3 to = PointAt(_dist);
            transform.position = to;
            if (_visual != null) _visual.Rotate(0f, _ability.ThrowSpin * Time.deltaTime, 0f, Space.Self);

            // Steinwall hält das Geschoss auf (auch die Optik-Kopie)
            if (StoneWall.AnyActive && StoneWall.Blocks(BossCombat.WallCheckPoint(from), BossCombat.WallCheckPoint(to)))
            {
                if (_ability.ImpactEffectPrefab != null) CombatUtil.SpawnFx(_ability.ImpactEffectPrefab, to, Quaternion.identity, 1.5f);
                End();
                return;
            }

            if (!_visualOnly && Net.IsServer) Sweep(from, to);
            if (done) End();
        }

        // Alle Figuren, die das Geschoss in diesem Bild überstrichen hat (einmal je Durchgang)
        private void Sweep(Vector3 from, Vector3 to)
        {
            float half = _ability.Width * 0.5f;
            Vector3 flight = _returning ? -_line.Forward : _line.Forward;
            BossCombat.CollectPlayers(_players);
            foreach (var player in _players)
            {
                if (!BossCombat.IsAlive(player) || _hit.Contains(player)) continue;
                if (DimensionEventRules.FlatDistanceToSegment(player.transform.position, from, to) > half + BossCombat.PlayerHitRadius) continue;
                _hit.Add(player);
                Hits++;
                if (!player.IsInvulnerable) TotalPlayerHits++;
                BossCombat.HitPlayer(player, _ability, to, flight, _damage);
                if (_ability.ImpactEffectPrefab != null)
                    CombatUtil.SpawnFx(_ability.ImpactEffectPrefab, player.transform.position + Vector3.up * FlightHeight, Quaternion.identity, 1.5f);
                if (_ability.PlaySfx) GameAudio.Play(_ability.HitSfx, to);
                CameraFollow.AddTraumaAt(0.3f, to, 16f);
            }
            _players.Clear();

            _buddies.Clear();
            _buddies.AddRange(ElementalBuddy.Active);
            foreach (var b in _buddies)
            {
                if (b == null || b.IsDead || !b.isActiveAndEnabled || _hit.Contains(b)) continue;
                if (DimensionEventRules.FlatDistanceToSegment(b.transform.position, from, to) > half + 0.5f) continue;
                _hit.Add(b);
                if (_damage > 0f) b.TakeDamage(_damage * _ability.BuddyDamageMultiplier);
            }
            _buddies.Clear();
        }

        private void End()
        {
            if (_telegraph != null) _telegraph.Finish(0.2f);
            _telegraph = null;
            Destroy(gameObject);
        }
    }
}
