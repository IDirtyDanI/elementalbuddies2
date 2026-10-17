using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace ElementalBuddies
{
    // Bannwirker (Rolle Hexer; Stadt: Grabhexe, Dorf: Dornenhexe) – Champion-Bedrohungen, Paket P1.
    // Server: sucht den erreichbaren Buddy mit den meisten Buddies im Radius ClusterRadius (sonst den nächstbesten mit
    // einem erreichbaren Standpunkt in CastRange), läuft zu diesem Standpunkt bzw. bis CastRange heran,
    // wirkt CastTime s (Telegraph: Bodenrunen am Ziel, Klang) und bannt dann bis zu MaxVictims Buddies im Radius
    // HexRadius (ElementalBuddy.SetHexed: keine Aktionen, Fusion/Aufwerten bleiben erlaubt). Der Bann hält, solange der
    // Bannwirker lebt und höchstens BreakDistance entfernt ist; sind alle Gebannten weg, wählt er ein neues Ziel.
    // Greift selbst nie an, Bannschild dauerhaft. Betäubung/Frost/Rückstoß des Champions brechen das Wirken ab (explizites
    // Unterbrechungs-Flag aus EnemyBrain); der Bann selbst endet auch während einer Betäubung, wenn er zu weit wegfliegt.
    // Ohne erreichbaren Buddy läuft er zum Nexus (greift nicht an).
    // Optik: Telegraph-Prefab (Durchmesser 1 m, wird auf 2 × HexRadius skaliert), Fessel am Buddy (BindPropPrefab,
    // Teile Bind_Ring, Bind_Chain_*) + Strahl zum Bannwirker (BuddyBindVisual).
    // Animator (optional): Trigger „Cast“ beim Wirken, Bool „Channel“ solange der Bann gehalten wird.
    public class HexerBrain : ThreatBrain
    {
        public const byte FxTelegraph = 1;
        public const byte FxCancel = 2;
        public const byte FxHex = 3;

        [Header("Bann")]
        [Tooltip("Zielwahl: Buddies im Umkreis dieses Radius um einen Buddy zählen zur Gruppe (m).")]
        public float ClusterRadius = 5f;
        [Tooltip("Bleibt in diesem Abstand zum Ziel stehen und wirkt (m).")]
        public float CastRange = 11f;
        public float CastTime = 1.5f;
        [Tooltip("Bannt Buddies in diesem Radius um das Ziel (m).")]
        public float HexRadius = 5f;
        public int MaxVictims = 4;
        [Tooltip("Bann endet, wenn der Bannwirker weiter entfernt ist (m).")]
        public float BreakDistance = 18f;
        [Tooltip("Neue Zielwahl höchstens so oft (s).")]
        public float RetargetInterval = 1f;
        [Tooltip("Pause nach einem abgebrochenen Wirken (s).")]
        public float CancelCooldown = 1f;

        [Header("Optik / Klang")]
        [Tooltip("Fessel am gebannten Buddy (Teile Bind_Ring, Bind_Chain_1..3). Leer = Platzhalter.")]
        public GameObject BindPropPrefab;
        [Tooltip("Bodenrunen während des Wirkens (Durchmesser 1 m). Leer = Platzhalter.")]
        public GameObject TelegraphPrefab;
        [Tooltip("Strahl/Fessel-Farbe (Platzhalter). Standard: Schildfarbe des Themes.")]
        public Color BindColor = new Color(0.66f, 0.42f, 1f);
        public SfxId CastSfx = SfxId.HexCast;

        // Server
        private ElementalBuddy _target;
        private float _nextRetarget;
        private float _castEnd;
        private float _cooldownUntil;
        private Vector3 _castCenter;
        private Vector3 _castSpot;
        private bool _hasCastSpot;
        private NavMeshPath _path;
        private const int MaxTargetAttempts = 8;  // Kandidaten-Buddies je Zielwahl
        private const int MaxPathQueries = 32;    // CalculatePath-Aufrufe je Zielwahl (höchstens 1/RetargetInterval)
        private const float NexusArriveDistance = 2.5f;
        private readonly List<ElementalBuddy> _victims = new List<ElementalBuddy>();

        // Alle Rechner
        private GameObject _telegraph;
        private float _telegraphEnd;

        public IReadOnlyList<ElementalBuddy> Victims => _victims;
        public ElementalBuddy Target => _target;

        protected override void Tick(bool interrupted)
        {
            byte s = State;
            if (s == StateCasting)
            {
                if (interrupted) { CancelCast(); return; }
                Brain.StopMoving();
                Brain.FacePoint(_castCenter);
                if (Time.time >= _castEnd) ReleaseCast();
                return;
            }
            if (s == StateChannel)
            {
                Brain.StopMoving();
                if (!PruneOrEndChannel()) return;
                Brain.FacePoint(_victims[0].transform.position);
                return;
            }
            Seek();
        }

        // Blockiert (betäubt, eingefroren, zurückgestoßen): Tod und Abstand der Gebannten trotzdem prüfen
        protected override void BlockedTick()
        {
            if (State == StateChannel) PruneOrEndChannel();
        }

        // false = keine Gebannten mehr → Bann beendet, neue Zielwahl
        private bool PruneOrEndChannel()
        {
            PruneVictims();
            if (_victims.Count > 0) return true;
            SetAnimBool("Channel", false);
            SetState(StateMoving, transform.position);
            _nextRetarget = 0f;
            return false;
        }

        // Notbremse hat ihn an ein Portal gesetzt: Wirken/Bann beenden, neu zielen
        protected override void OnRescued()
        {
            if (State == StateCasting) ServerFx(FxCancel, _castCenter);
            ReleaseAll();
            SetAnimBool("Channel", false);
            SetState(StateMoving, transform.position);
            _target = null;
            _hasCastSpot = false;
            _nextRetarget = 0f;
        }

        // ---------------- Zielwahl und Annäherung ----------------

        private static bool ValidVictim(ElementalBuddy b) =>
            b != null && b.isActiveAndEnabled && !b.IsDead && b.CurrentHP > 0f && !b.IsBanished;

        private void Seek()
        {
            if (Time.time >= _nextRetarget || !ValidVictim(_target))
            {
                _nextRetarget = Time.time + RetargetInterval;
                _target = PickTarget(out _castSpot);
                _hasCastSpot = _target != null;
            }
            if (_target == null)
            {
                // Kein (erreichbarer) Buddy: Richtung Nexus weiterlaufen statt stehen zu bleiben (greift nicht an)
                if (Nexus.Instance != null)
                {
                    Vector3 n = Nexus.Instance.GetClosestPoint(transform.position);
                    if (Horizontal(transform.position, n) > NexusArriveDistance) Brain.MoveTo(n);
                    else Brain.StopMoving();
                }
                SetState(StateMoving, transform.position);
                return;
            }
            Vector3 tp = _target.transform.position;
            bool atSpot = _hasCastSpot && Horizontal(transform.position, _castSpot) <= 1f;
            if ((Horizontal(transform.position, tp) <= CastRange || atSpot) && Time.time >= _cooldownUntil)
            {
                BeginCast(tp);
                return;
            }
            if (_hasCastSpot) Brain.MoveTo(_castSpot);
            else Brain.MoveTo(NavMesh.SamplePosition(_target.GetClosestPoint(transform.position), out NavMeshHit hit, 2f, Filter) ? hit.position : tp);
            SetState(StateMoving, tp);
        }

        // Größte Buddy-Gruppe mit erreichbarem Standpunkt in CastRange; unerreichbare Kandidaten werden verworfen und der
        // nächstbeste (kleinere Gruppe, dann näher) versucht – höchstens MaxTargetAttempts Buddies/MaxPathQueries Pfade
        private ElementalBuddy PickTarget(out Vector3 spot)
        {
            spot = transform.position;
            var buddies = new List<ElementalBuddy>();
            var pos = new List<Vector3>();
            var valid = new List<bool>();
            foreach (var b in ElementalBuddy.Active)
            {
                if (!ValidVictim(b)) continue;
                buddies.Add(b);
                pos.Add(b.transform.position);
                valid.Add(true);
            }
            if (_path == null) _path = new NavMeshPath();
            int budget = MaxPathQueries;
            for (int attempt = 0; attempt < MaxTargetAttempts && budget > 0; attempt++)
            {
                int i = ChampionThreatRules.PickHexTarget(pos, valid, ClusterRadius, transform.position);
                if (i < 0) return null;
                if (FindCastSpot(buddies[i], ref budget, out spot)) return buddies[i];
                valid[i] = false;
            }
            return null;
        }

        // Erreichbarer Standpunkt höchstens CastRange (horizontal) vom Buddy: zuerst der Punkt vor dem Buddy (voller Pfad
        // oder Teilpfad, der in Wirkreichweite endet), sonst Ringpunkte um den Buddy (nächster voll erreichbarer)
        private bool FindCastSpot(ElementalBuddy b, ref int budget, out Vector3 spot)
        {
            Vector3 me = transform.position;
            Vector3 bp = b.transform.position;
            spot = me;
            if (Horizontal(me, bp) <= CastRange) return true; // schon in Reichweite

            if (budget > 0 && NavMesh.SamplePosition(b.GetClosestPoint(me), out NavMeshHit hit, CastRange * 0.5f, Filter))
            {
                budget--;
                if (NavMesh.CalculatePath(me, hit.position, Filter, _path) && _path.corners.Length > 0)
                {
                    Vector3 end = _path.corners[_path.corners.Length - 1];
                    if (_path.status == NavMeshPathStatus.PathComplete && Horizontal(hit.position, bp) <= CastRange) { spot = hit.position; return true; }
                    if (Horizontal(end, bp) <= CastRange * 0.9f) { spot = end; return true; }
                }
            }

            // Ringpunkte (zwei Radien × 8 Richtungen, vom Bannwirker aus zuerst die zugewandte Seite)
            Vector3 toMe = me - bp;
            toMe.y = 0f;
            float baseAng = toMe.sqrMagnitude > 0.01f ? Mathf.Atan2(toMe.z, toMe.x) : 0f;
            float bestLen = float.MaxValue;
            bool found = false;
            float[] radii = { CastRange * 0.55f, CastRange * 0.85f };
            for (int r = 0; r < radii.Length; r++)
            {
                for (int k = 0; k < 8 && budget > 0; k++)
                {
                    // 0, +45°, −45°, +90° … (zugewandte Seite zuerst)
                    int step = (k + 1) / 2;
                    float ang = baseAng + (k % 2 == 1 ? 1f : -1f) * step * Mathf.PI / 4f;
                    Vector3 p = bp + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * radii[r];
                    if (!NavMesh.SamplePosition(p, out NavMeshHit rh, 2f, Filter) || Horizontal(rh.position, bp) > CastRange * 0.95f) continue;
                    budget--;
                    if (!NavMesh.CalculatePath(me, rh.position, Filter, _path) || _path.status != NavMeshPathStatus.PathComplete) continue;
                    float len = PathLength(_path);
                    if (len < bestLen)
                    {
                        bestLen = len;
                        spot = rh.position;
                        found = true;
                    }
                }
                if (found) return true; // innerer Ring genügt
            }
            return found;
        }

        private static float PathLength(NavMeshPath path)
        {
            float len = 0f;
            var c = path.corners;
            for (int i = 1; i < c.Length; i++) len += Vector3.Distance(c[i - 1], c[i]);
            return len;
        }

        // ---------------- Wirken und Bann ----------------

        private void BeginCast(Vector3 center)
        {
            _castCenter = center;
            _castEnd = Time.time + CastTime;
            Brain.StopMoving();
            SetState(StateCasting, center);
            ServerAnimTrigger("Cast");
            ServerFx(FxTelegraph, center);
        }

        private void CancelCast()
        {
            ServerFx(FxCancel, _castCenter);
            _cooldownUntil = Time.time + CancelCooldown;
            SetState(StateMoving, transform.position);
        }

        private void ReleaseCast()
        {
            var buddies = new List<ElementalBuddy>();
            var pos = new List<Vector3>();
            var valid = new List<bool>();
            foreach (var b in ElementalBuddy.Active)
            {
                if (b == null) continue;
                buddies.Add(b);
                pos.Add(b.transform.position);
                valid.Add(ValidVictim(b));
            }
            _victims.Clear();
            foreach (int i in ChampionThreatRules.SelectHexVictims(pos, valid, _castCenter, HexRadius, MaxVictims))
            {
                buddies[i].SetHexed(Brain);
                _victims.Add(buddies[i]);
            }
            if (_victims.Count == 0)
            {
                SetState(StateMoving, transform.position);
                _nextRetarget = 0f;
                return;
            }
            ServerFx(FxHex, _castCenter);
            SetAnimBool("Channel", true);
            SetState(StateChannel, _castCenter);
        }

        // Ungültige, fremd gebannte oder zu weit entfernte Opfer freigeben
        private void PruneVictims()
        {
            for (int i = _victims.Count - 1; i >= 0; i--)
            {
                var b = _victims[i];
                bool alive = b != null && b.isActiveAndEnabled && !b.IsDead;
                bool mine = alive && b.HexSource == Brain;
                float d = alive ? Horizontal(transform.position, b.transform.position) : float.MaxValue;
                if (mine && ChampionThreatRules.HexHolds(!Brain.IsDead, d, BreakDistance)) continue;
                if (alive) b.ClearHex(Brain);
                _victims.RemoveAt(i);
            }
        }

        private void ReleaseAll()
        {
            if (!Net.IsServer) return;
            foreach (var b in _victims)
                if (b != null) b.ClearHex(Brain);
            _victims.Clear();
        }

        // ---------------- Alle Rechner: Optik ----------------

        public override void PlayFx(byte id, Vector3 position)
        {
            if (id == FxTelegraph)
            {
                GameAudio.PlayOr(CastSfx, position);
                ShowTelegraph(position);
            }
            else if (id == FxCancel || id == FxHex)
            {
                if (_telegraph != null) Destroy(_telegraph);
                _telegraph = null;
                if (id == FxHex) ThreatVisuals.Burst(position + Vector3.up * 0.3f, BindColor, 10, 0.12f, 4f, 0.6f);
            }
        }

        protected override void OnStateShown(byte state, Vector3 point)
        {
            SetAnimBool("Channel", state == StateChannel);
        }

        private void ShowTelegraph(Vector3 center)
        {
            if (_telegraph != null) Destroy(_telegraph);
            if (TelegraphPrefab != null) _telegraph = Instantiate(TelegraphPrefab, center, Quaternion.identity);
            else
            {
                _telegraph = new GameObject("HexTelegraph_Placeholder");
                _telegraph.transform.position = center;
                Color c = BindColor;
                c.a = 0.35f;
                var d = ThreatVisuals.Primitive(PrimitiveType.Cylinder, "Hex_Runes", c, _telegraph.transform);
                d.transform.localScale = new Vector3(1f, 0.02f, 1f);
                d.transform.localPosition = Vector3.up * 0.05f;
            }
            _telegraph.transform.localScale = Vector3.one * HexRadius * 2f;
            _telegraphEnd = Time.time + CastTime + 0.2f;
        }

        protected override void Update()
        {
            base.Update();
            if (_telegraph == null) return;
            float k = 1f - Mathf.Clamp01((_telegraphEnd - Time.time) / (CastTime + 0.2f));
            _telegraph.transform.localScale = Vector3.one * HexRadius * 2f * (0.6f + 0.4f * k);
            _telegraph.transform.Rotate(0f, 60f * Time.deltaTime, 0f);
            if (Time.time >= _telegraphEnd)
            {
                Destroy(_telegraph);
                _telegraph = null;
            }
        }

        void OnDestroy()
        {
            ReleaseAll();
            if (_telegraph != null) Destroy(_telegraph);
        }
    }
}
