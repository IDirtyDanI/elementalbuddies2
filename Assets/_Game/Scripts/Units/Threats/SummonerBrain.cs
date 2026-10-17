using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace ElementalBuddies
{
    // Beschwörer (Rolle Summoner; Stadt: Knochenbeschwörer, Dorf: Goblin-Trommler) – Champion-Bedrohungen, Paket P1.
    // Server: wählt beim Spawn den freien, erreichbaren Ritualplatz (RitualSite), der am weitesten vom nächsten lebenden
    // Buddy entfernt ist (ohne Plätze: NavMesh-Punkt 15–25 m vom Spawn Richtung Nexus), läuft hin (greift nicht an),
    // errichtet den Ritualkreis und beschwört alle SummonInterval s eine Gruppe aus Läufern und Schwarm (Theme-Configs
    // über WaveManager.ResolveConfig) im Ring um den Kreis. Gruppe 2, +1 je zweiter Beschwörung, Deckel 8; höchstens
    // MaxAliveSummons lebende Beschworene. Beschworene zählen zur Welle und bringen nur SummonBountyFactor Kopfgeld.
    // Ritual-Vollendung (P4): ab Ritualbeginn läuft eine Uhr (RitualCompleteTime); bei Ablauf verliert der Nexus
    // RitualNexusDamageFraction seiner Max-HP (Fluchstrahl, Toast an alle), dann beginnt die Uhr neu. Fortschrittsring
    // über EnemyNet.ThreatProgress auch auf Clients.
    // Bannschild dauerhaft (Config.WardShield). Betäubung/Frost/Rückstoß des Champions halten Ritual und Uhr an.
    // Ersatz- und Festsitz-Ritualpunkte müssen vom Nexus aus vollständig erreichbar sein (der Champion muss hinkommen);
    // sonst anderer Punkt bzw. zurück ans Portal. Notbremse (ThreatBrain) setzt ihn bei unerreichbarem Stillstand zurück.
    // Optik: Ritual-Requisite (Teile Ritual_Base, Ritual_Runes; optional Ritual_Candle_*, Ritual_Fire), Austritts-
    // Requisite je Beschworenem (Rift_Ground, Rift_Hand – Hand fährt hoch, nach RiftDuration weg).
    // Animator (optional): Bool „Channel“ während des Rituals, Trigger „Summon“ bei jeder Beschwörung. Der Effekt (Klang,
    // Risse, Gegner) folgt SummonImpactDelay s nach dem Trigger – beim Stab-Aufschlag bzw. Trommel-Doppelschlag des Clips.
    public class SummonerBrain : ThreatBrain
    {
        public const byte FxSummon = 1;  // Beschwörung (Klang, Trigger) am Ritualkreis
        public const byte FxRift = 2;    // Austritt eines Beschworenen
        public const byte FxRitualComplete = 3; // Ritual vollendet: Fluchstrahl Kreis → Nexus

        [Header("Beschwörung")]
        [Tooltip("Kanonische Läufer-Config (Untote; das Map-Theme tauscht sie).")]
        public EnemyConfigSO RunnerConfig;
        [Tooltip("Kanonische Schwarm-Config (Untote; das Map-Theme tauscht sie).")]
        public EnemyConfigSO SwarmConfig;
        public float SummonInterval = 10f;
        [Tooltip("Erste Beschwörung so lange nach Ankunft (s).")]
        public float FirstSummonDelay = 3f;
        public int BaseGroupSize = 2;
        [Tooltip("+1 Gegner je so vielen Beschwörungen.")]
        public int GrowEvery = 2;
        public int MaxGroupSize = 8;
        [Tooltip("Höchstens so viele lebende Beschworene dieses Beschwörers.")]
        public int MaxAliveSummons = 24;
        [Tooltip("Beschworene erscheinen im Ring zwischen diesen Radien um den Kreis (m).")]
        public float RingMin = 2f;
        public float RingMax = 4f;
        [Tooltip("Kopfgeld-Faktor der Beschworenen (Anti-Farm).")]
        public float SummonBountyFactor = 0.25f;

        [Header("Ritual-Vollendung (Balancing-Modell P4)")]
        [Tooltip("Ritual-Uhr ab Ritualbeginn (s): bei Ablauf verliert der Nexus RitualNexusDamageFraction seiner Max-HP, dann " +
                 "beginnt die Uhr von vorn. Hält still, solange der Champion den Beschwörer betäubt/einfriert. 0 = aus.")]
        public float RitualCompleteTime = 45f;
        [Tooltip("Anteil der Nexus-Max-HP, den eine Vollendung kostet (Nexus.TakeDamage wie Gegnerschaden → Game Over möglich).")]
        public float RitualNexusDamageFraction = 0.1f;

        [Header("Weg")]
        [Tooltip("Ohne Ritualplätze: Ersatzpunkt so weit vom Spawn Richtung Nexus (m).")]
        public float FallbackMin = 15f;
        public float FallbackMax = 25f;
        [Tooltip("Ab diesem Abstand zum Platz beginnt das Ritual (m).")]
        public float ArriveDistance = 1.4f;
        [Tooltip("So lange ohne Fortschritt (< 1 m) → Ritual an Ort und Stelle (s).")]
        public float StuckTime = 10f;

        [Header("Optik / Klang")]
        [Tooltip("Ritual-Requisite (Teile Ritual_Base, Ritual_Runes). Leer = Platzhalter.")]
        public GameObject RitualPropPrefab;
        [Tooltip("Austritts-Requisite je Beschworenem (Teile Rift_Ground, Rift_Hand). Leer = Platzhalter.")]
        public GameObject RiftPropPrefab;
        public float RiftDuration = 1.5f;
        [Tooltip("Verzögerung Trigger „Summon“ → Beschwörung (Aufschlag im Clip; Knochenbeschwörer 0,6 s, Trommler 0,667 s).")]
        public float SummonImpactDelay = 0f;
        [Tooltip("Klang je Beschwörung (Stadt SummonCall, Dorf SummonDrum; ohne Clip Ersatzton).")]
        public SfxId SummonSfx = SfxId.SummonCall;
        public Color PlaceholderColor = new Color(0.66f, 0.42f, 1f);

        // Server
        private RitualSite _site;
        private Vector3 _dest;
        private bool _hasDest;
        private bool _channeling;
        private float _summonTimer;
        private float _pendingSummon = -1f; // > 0: Beschwörung läuft (Trigger gesendet), Rest bis zum Aufschlag
        private int _summonCount;
        private float _ritualElapsed;
        private int _ritualCompletions;
        private Vector3 _progressPos;
        private float _noProgress;
        private readonly List<EnemyBrain> _summons = new List<EnemyBrain>();
        private int _stuckRedirects;    // Festsitz → erreichbarer Nachbarpunkt (höchstens einmal, dann Portal)
        private RitualSite _avoidSite; // Platz, an dem er unerreichbar festsaß (Notbremse) – nicht erneut wählen

        // Alle Rechner
        private GameObject _ritualProp;
        private Transform _runes;

        public int SummonCount => _summonCount;
        public int RitualCompletions => _ritualCompletions;
        public float RitualElapsed => _ritualElapsed;
        public int AliveSummons
        {
            get
            {
                _summons.RemoveAll(e => e == null || e.IsDead);
                return _summons.Count;
            }
        }
        public RitualSite Site => _site;
        public bool IsChanneling => _channeling;

        protected override void Start()
        {
            base.Start();
            if (Net.IsServer) ChooseDestination();
        }

        // ---------------- Server ----------------

        private void ChooseDestination()
        {
            Vector3 me = transform.position;
            var sites = RitualSite.All;
            var buddyPos = new List<Vector3>();
            foreach (var b in ElementalBuddy.Active)
                if (b != null && b.isActiveAndEnabled && !b.IsDead) buddyPos.Add(b.transform.position);

            var candidates = new List<ChampionThreatRules.SiteCandidate>();
            var path = new NavMeshPath();
            for (int i = 0; i < sites.Count; i++)
            {
                var s = sites[i];
                bool reachable = false;
                bool free = s != null && s.IsFree && s != _avoidSite;
                if (free && NavMesh.SamplePosition(s.transform.position, out NavMeshHit hit, 2.5f, Filter))
                    reachable = NavMesh.CalculatePath(me, hit.position, Filter, path) && path.status == NavMeshPathStatus.PathComplete;
                candidates.Add(new ChampionThreatRules.SiteCandidate(free, reachable,
                    s != null ? ChampionThreatRules.NearestDistance(s.transform.position, buddyPos) : 0f,
                    s != null ? Horizontal(me, s.transform.position) : float.MaxValue));
            }
            int pick = ChampionThreatRules.PickRitualSite(candidates);
            if (pick >= 0 && sites[pick].TryReserve(Brain))
            {
                _site = sites[pick];
                NavMesh.SamplePosition(_site.transform.position, out NavMeshHit hit, 2.5f, Filter);
                _dest = hit.position;
                _hasDest = true;
            }
            else
            {
                // Ersatz: vom Spawn Richtung Nexus versetzt; der Punkt muss von hier und vom Nexus aus voll erreichbar sein
                if (FindFallbackPoint(me, path, out Vector3 p)) _dest = p;
                else if (ReachableFromNexus(me) && NavMesh.SamplePosition(me, out NavMeshHit here, 2f, Filter)) _dest = here.position;
                else if (Brain.WarpToRescue("fand keinen erreichbaren Ritualpunkt"))
                {
                    me = transform.position;
                    _dest = NavMesh.SamplePosition(me, out NavMeshHit portal, 2f, Filter) ? portal.position : me;
                }
                else _dest = me;
                _hasDest = true;
                Debug.Log($"SummonerBrain: kein freier Ritualplatz – Ersatzpunkt {_dest} ({Horizontal(me, _dest):0} m).");
            }
            _progressPos = transform.position;
            _noProgress = 0f;
            SetState(StateMoving, _dest);
        }

        // Ersatzpunkt FallbackMin..FallbackMax Richtung Nexus (leicht gestreut), von hier und vom Nexus aus voll erreichbar
        private bool FindFallbackPoint(Vector3 me, NavMeshPath path, out Vector3 point)
        {
            point = me;
            Vector3 dir = Nexus.Instance != null ? Nexus.Instance.transform.position - me : transform.forward;
            dir.y = 0f;
            dir = dir.sqrMagnitude > 0.01f ? dir.normalized : Vector3.forward;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                float d = Random.Range(FallbackMin, FallbackMax);
                float spread = attempt == 0 ? 0f : Random.Range(-40f, 40f);
                Vector3 p = me + Quaternion.Euler(0f, spread, 0f) * dir * d;
                if (!NavMesh.SamplePosition(p, out NavMeshHit hit, 6f, Filter)) continue;
                if (!NavMesh.CalculatePath(me, hit.position, Filter, path) || path.status != NavMeshPathStatus.PathComplete) continue;
                if (!ReachableFromNexus(hit.position)) continue;
                point = hit.position;
                return true;
            }
            return false;
        }

        // Festgesessen: Ritual hier, wenn vom Nexus aus erreichbar, sonst ein erreichbarer Punkt in der Nähe (bis 6 m);
        // false = nichts gefunden
        private bool FindStuckRitualPoint(out Vector3 point)
        {
            Vector3 me = transform.position;
            point = me;
            if (NavMesh.SamplePosition(me, out NavMeshHit hit, 2f, Filter) && ReachableFromNexus(hit.position))
            {
                point = hit.position;
                return true;
            }
            for (int r = 1; r <= 2; r++)
            {
                for (int k = 0; k < 8; k++)
                {
                    float ang = k * Mathf.PI / 4f;
                    Vector3 p = me + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * (3f * r);
                    if (!NavMesh.SamplePosition(p, out NavMeshHit h, 1.5f, Filter) || !ReachableFromNexus(h.position)) continue;
                    point = h.position;
                    return true;
                }
            }
            return false;
        }

        // Ursprünglichen Ritualplatz sofort freigeben (Ritual woanders oder zurück ans Portal)
        private void ReleaseSite()
        {
            if (_site != null) _site.Release(Brain);
            _site = null;
        }

        // Notbremse: zurück am Portal – Ritual abbrechen, Platz meiden und neu wählen
        protected override void OnRescued()
        {
            if (_site != null) _avoidSite = _site;
            ReleaseSite();
            _channeling = false;
            _pendingSummon = -1f;
            SetAnimBool("Channel", false);
            SetProgress(0f);
            ChooseDestination();
        }

        protected override void Tick(bool interrupted)
        {
            if (!_hasDest) ChooseDestination();
            if (!_channeling)
            {
                Brain.MoveTo(_dest);
                if (Horizontal(transform.position, _dest) <= ArriveDistance)
                {
                    // Kreis unter den Beschwörer legen (er steht mitten im Ritual, nicht am Rand); höchstens ArriveDistance vom Platz
                    Vector3 here = NavMesh.SamplePosition(transform.position, out NavMeshHit arrived, 1f, Filter) ? arrived.position : _dest;
                    BeginRitual(here);
                }
                else if ((transform.position - _progressPos).sqrMagnitude > 1f)
                {
                    _progressPos = transform.position;
                    _noProgress = 0f;
                }
                else if ((_noProgress += Time.deltaTime) >= StuckTime)
                {
                    // Hängt fest (z. B. Weg blockiert): Ritual hier bzw. an einem vom Nexus aus erreichbaren Punkt daneben;
                    // der reservierte Platz wird sofort frei. Nichts Erreichbares → zurück ans Portal und neu wählen.
                    _noProgress = 0f;
                    _progressPos = transform.position;
                    bool found = FindStuckRitualPoint(out Vector3 here);
                    if (found && _site != null && Horizontal(here, _site.transform.position) > ArriveDistance + 1f) ReleaseSite();
                    if (found && Horizontal(transform.position, here) <= ArriveDistance + 0.5f) BeginRitual(here);
                    else if (found && _stuckRedirects++ < 1)
                    {
                        // erreichbarer Punkt daneben: dorthin, Ankunft startet das Ritual
                        _dest = here;
                        SetState(StateMoving, _dest);
                    }
                    else
                    {
                        if (_site != null) _avoidSite = _site;
                        ReleaseSite();
                        _stuckRedirects = 0;
                        if (Brain.WarpToRescue("saß unerreichbar fest")) ChooseDestination();
                    }
                }
                return;
            }

            Brain.StopMoving();
            Brain.FacePoint(_dest + (Nexus.Instance != null ? (Nexus.Instance.transform.position - _dest).normalized : Vector3.forward));
            var wm = WaveManager.Instance;
            if (wm == null || !wm.IsWaveActive) return;
            TickRitualClock();
            // Takt läuft auch während der Aufschlag-Verzögerung weiter → Beschwörung genau alle SummonInterval s
            _summonTimer -= Time.deltaTime;
            if (_pendingSummon >= 0f)
            {
                _pendingSummon -= Time.deltaTime;
                if (_pendingSummon <= 0f)
                {
                    _pendingSummon = -1f;
                    Summon(wm);
                }
                return;
            }
            if (_summonTimer > 0f) return;
            _summonTimer += SummonInterval;
            StartSummon();
        }

        // Trigger zuerst (Ausholen), Beschwörung beim Aufschlag; ohne Platz für Beschworene keine Geste
        private void StartSummon()
        {
            int n = ChampionThreatRules.ClampToAliveCap(
                ChampionThreatRules.SummonGroupSize(_summonCount, BaseGroupSize, GrowEvery, MaxGroupSize), AliveSummons, MaxAliveSummons);
            if (n <= 0) return;
            ServerAnimTrigger("Summon");
            if (SummonImpactDelay <= 0f) Summon(WaveManager.Instance);
            else _pendingSummon = SummonImpactDelay;
        }

        // Ritual-Uhr: Fortschritt an die Clients, bei Vollendung Nexus-Schaden und Neustart
        private void TickRitualClock()
        {
            if (RitualCompleteTime <= 0f) return;
            _ritualElapsed += Time.deltaTime;
            SetProgress(ChampionThreatRules.RitualProgress(_ritualElapsed, RitualCompleteTime));
            if (!ChampionThreatRules.RitualCompleted(_ritualElapsed, RitualCompleteTime)) return;
            _ritualElapsed = 0f;
            _ritualCompletions++;
            SetProgress(0f);
            var nexus = Nexus.Instance;
            if (nexus != null)
            {
                float dmg = ChampionThreatRules.RitualNexusDamage(nexus.MaxHP, RitualNexusDamageFraction);
                nexus.TakeDamage(dmg);
                Debug.Log($"SummonerBrain: {Brain.DisplayName} hat sein Ritual vollendet – Nexus −{dmg:0} ({_ritualCompletions}×).");
            }
            ServerFx(FxRitualComplete, _dest);
            string msg = $"{Brain.DisplayName} hat sein Ritual vollendet!";
            foreach (ulong id in UpgradeManager.PlayerIds()) NetGame.SendToast(id, msg);
        }

        private void BeginRitual(Vector3 center)
        {
            _dest = center;
            _channeling = true;
            _ritualElapsed = 0f;
            SetProgress(0f);
            _summonTimer = FirstSummonDelay;
            _pendingSummon = -1f;
            Brain.StopMoving();
            SetState(StateChannel, center);
        }

        private void Summon(WaveManager wm)
        {
            int n = ChampionThreatRules.ClampToAliveCap(
                ChampionThreatRules.SummonGroupSize(_summonCount, BaseGroupSize, GrowEvery, MaxGroupSize), AliveSummons, MaxAliveSummons);
            if (n <= 0 || wm == null) return;
            _summonCount++;
            ServerFx(FxSummon, _dest);
            float start = Random.value * 360f;
            for (int i = 0; i < n; i++)
            {
                float ang = (start + i * 360f / n + Random.Range(-15f, 15f)) * Mathf.Deg2Rad;
                float r = Random.Range(RingMin, RingMax);
                Vector3 p = _dest + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * r;
                if (!NavMesh.SamplePosition(p, out NavMeshHit hit, 1.5f, Filter)) hit.position = _dest;
                // abwechselnd Läufer und Schwarm (Läufer zuerst)
                var cfg = i % 2 == 0 ? RunnerConfig : SwarmConfig;
                if (cfg == null) cfg = RunnerConfig != null ? RunnerConfig : SwarmConfig;
                if (cfg == null) continue;
                var e = wm.SpawnEnemyAt(cfg, hit.position, -1f, true);
                if (e == null) continue;
                e.SetBountyFactor(SummonBountyFactor);
                _summons.Add(e);
                ServerFx(FxRift, hit.position);
            }
        }

        // ---------------- Alle Rechner: Optik ----------------

        public override void PlayFx(byte id, Vector3 position)
        {
            if (id == FxSummon)
            {
                GameAudio.PlayOr(SummonSfx, position);
                if (_runes != null) _runesFlash = 1f;
            }
            else if (id == FxRift) SpawnRift(position);
            else if (id == FxRitualComplete) PlayRitualComplete(position);
        }

        // Fluchstrahl vom Kreis zum Nexus, lauter Klang (Beschwörungsklang + Einschlag)
        private void PlayRitualComplete(Vector3 from)
        {
            GameAudio.PlayOr(SummonSfx);           // 2D = überall gleich laut
            GameAudio.PlayOr(SfxId.HeavyImpact);
            if (_runes != null) _runesFlash = 1.5f;
            var nexus = Nexus.Instance;
            if (nexus == null) return;
            Vector3 to = nexus.GetClosestPoint(from) + Vector3.up * 1.5f;
            var go = new GameObject("RitualCurseBeam");
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.material = new Material(Shader.Find("Sprites/Default"));
            Color c = PlaceholderColor;
            lr.startColor = new Color(c.r, c.g, c.b, 1f);
            lr.endColor = new Color(1f, 0.25f, 0.2f, 1f);
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            // leicht gezackter Blitz
            const int n = 14;
            lr.positionCount = n;
            Vector3 a = from + Vector3.up * 1.2f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)(n - 1);
                Vector3 p = Vector3.Lerp(a, to, t) + Vector3.up * Mathf.Sin(t * Mathf.PI) * 6f;
                if (i > 0 && i < n - 1) p += Random.insideUnitSphere * 0.6f;
                lr.SetPosition(i, p);
            }
            go.AddComponent<FadingBeam>().Init(lr, 0.9f, 0.6f);
            ThreatVisuals.Burst(to, new Color(1f, 0.3f, 0.25f), 14, 0.25f, 6f, 0.9f);
            CameraFollow.AddTraumaAt(0.25f, to, 60f);
        }

        private float _runesFlash;

        protected override void OnStateShown(byte state, Vector3 point)
        {
            bool channel = state == StateChannel;
            SetAnimBool("Channel", channel);
            if (channel && _ritualProp == null) BuildRitualProp(point);
            else if (!channel && _ritualProp != null) DestroyProp();
        }

        protected override void Update()
        {
            base.Update();
            UpdateProgressRing();
            if (_runes == null) return;
            _runesFlash = Mathf.Max(0f, _runesFlash - Time.deltaTime * 1.5f);
            _runes.Rotate(0f, (12f + 90f * _runesFlash) * Time.deltaTime, 0f, Space.World);
            float s = 1f + 0.04f * Mathf.Sin(Time.time * 2f) + 0.1f * _runesFlash;
            _runes.localScale = _runesScale * s;
        }

        private Vector3 _runesScale = Vector3.one;

        // Fortschrittsring der Ritual-Uhr um den Kreis (alle Rechner): füllt sich im Uhrzeigersinn, Farbe → Rot
        private LineRenderer _ring;
        private const int RingSegments = 48;
        private const float RingRadius = 2.6f;

        private void UpdateProgressRing()
        {
            bool show = _ritualProp != null && State == StateChannel && RitualCompleteTime > 0f;
            if (!show)
            {
                if (_ring != null) _ring.enabled = false;
                return;
            }
            if (_ring == null)
            {
                var go = new GameObject("RitualProgress");
                go.transform.SetParent(_ritualProp.transform, false);
                _ring = go.AddComponent<LineRenderer>();
                _ring.useWorldSpace = true;
                _ring.material = new Material(Shader.Find("Sprites/Default"));
                _ring.widthMultiplier = 0.22f;
                _ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _ring.receiveShadows = false;
            }
            _ring.enabled = true;
            float p = Progress;
            int n = Mathf.Max(2, Mathf.CeilToInt(RingSegments * p) + 1);
            _ring.positionCount = n;
            Vector3 c = _ritualProp.transform.position + Vector3.up * 0.12f;
            for (int i = 0; i < n; i++)
            {
                float a = (90f - 360f * p * i / (n - 1)) * Mathf.Deg2Rad;
                _ring.SetPosition(i, c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * RingRadius);
            }
            Color col = Color.Lerp(PlaceholderColor, new Color(1f, 0.2f, 0.15f), p * p);
            if (p > 0.8f) col = Color.Lerp(col, Color.white, 0.35f * (0.5f + 0.5f * Mathf.Sin(Time.time * 12f)));
            _ring.startColor = _ring.endColor = col;
        }

        private void BuildRitualProp(Vector3 point)
        {
            if (RitualPropPrefab != null)
            {
                _ritualProp = Instantiate(RitualPropPrefab, point, Quaternion.identity);
            }
            else
            {
                _ritualProp = new GameObject("Ritual_Placeholder");
                _ritualProp.transform.position = point;
                var b = ThreatVisuals.Primitive(PrimitiveType.Cylinder, "Ritual_Base", new Color(0.18f, 0.16f, 0.2f), _ritualProp.transform);
                b.transform.localScale = new Vector3(4.2f, 0.04f, 4.2f);
                Color rc = PlaceholderColor;
                rc.a = 0.55f;
                var r = ThreatVisuals.Primitive(PrimitiveType.Cylinder, "Ritual_Runes", rc, _ritualProp.transform);
                r.transform.localScale = new Vector3(3.4f, 0.05f, 3.4f);
                r.transform.localPosition = Vector3.up * 0.03f;
            }
            _ritualProp.name = "Ritual_" + name;
            _runes = ThreatVisuals.FindPart(_ritualProp.transform, "Ritual_Runes");
            if (_runes != null) _runesScale = _runes.localScale;
        }

        private void DestroyProp()
        {
            if (_ring != null && _ring.material != null) Destroy(_ring.material);
            _ring = null;
            if (_ritualProp != null) Destroy(_ritualProp);
            _ritualProp = null;
            _runes = null;
        }

        private void SpawnRift(Vector3 pos)
        {
            GameObject rift;
            if (RiftPropPrefab != null) rift = Instantiate(RiftPropPrefab, pos, Quaternion.Euler(0f, Random.value * 360f, 0f));
            else
            {
                rift = new GameObject("Rift_Placeholder");
                rift.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, Random.value * 360f, 0f));
                var g = ThreatVisuals.Primitive(PrimitiveType.Cylinder, "Rift_Ground", new Color(0.22f, 0.17f, 0.12f), rift.transform);
                g.transform.localScale = new Vector3(1.3f, 0.06f, 1.3f);
                var h = ThreatVisuals.Primitive(PrimitiveType.Cube, "Rift_Hand", PlaceholderColor, rift.transform);
                h.transform.localScale = new Vector3(0.25f, 0.7f, 0.25f);
                h.transform.localPosition = Vector3.up * 0.35f;
            }
            rift.AddComponent<RiftProp>().Init(RiftDuration);
        }

        void OnDestroy()
        {
            if (_site != null) _site.Release(Brain);
            DestroyProp();
        }
    }

    // Kurzlebiger Strahl: hält hold s, blendet dann in fade s aus und zerstört sich (inkl. Material)
    public class FadingBeam : MonoBehaviour
    {
        private LineRenderer _lr;
        private float _hold, _fade, _t, _width;

        public void Init(LineRenderer lr, float hold, float fade)
        {
            _lr = lr;
            _hold = hold;
            _fade = Mathf.Max(0.05f, fade);
            _width = 0.35f;
            _lr.widthMultiplier = _width;
        }

        void Update()
        {
            _t += Time.deltaTime;
            if (_lr == null) { Destroy(gameObject); return; }
            float k = _t <= _hold ? 1f : 1f - (_t - _hold) / _fade;
            _lr.widthMultiplier = _width * Mathf.Max(0f, k) * (0.8f + 0.2f * Mathf.Sin(_t * 40f));
            if (k <= 0f)
            {
                if (_lr.material != null) Destroy(_lr.material);
                Destroy(gameObject);
            }
        }
    }

    // Austritts-Requisite: Rift_Hand fährt in 0,4 s aus dem Boden hoch und sinkt am Ende wieder ab, dann weg
    public class RiftProp : MonoBehaviour
    {
        private Transform _hand;
        private Vector3 _handPos;
        private float _life, _t;

        public void Init(float life)
        {
            _life = Mathf.Max(0.2f, life);
            _hand = ThreatVisuals.FindPart(transform, "Rift_Hand");
            if (_hand != null) _handPos = _hand.localPosition;
            Update();
        }

        void Update()
        {
            _t += Time.deltaTime;
            if (_hand != null)
            {
                float up = Mathf.Clamp01(_t / 0.4f) * Mathf.Clamp01((_life - _t) / 0.35f);
                _hand.localPosition = _handPos + Vector3.down * (1f - up) * 0.8f;
            }
            if (_t >= _life) Destroy(gameObject);
        }
    }
}
