using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace ElementalBuddies
{
    // Zentrale Game-Feel-Steuerung (Plan „Fesselung“, Paket A/B): hört auf Spiel-Ereignisse und löst lokal
    // Schadenszahlen, Aufblitzen, Treffer-Stopp, Kamera-Wackeln, Tod-Effekte, Warnungen, Banner und Sounds aus.
    // Rein kosmetisch und pro Rechner – verändert keinen Spielzustand. Liegt auf dem Managers-Objekt
    // (eingerichtet über BuddyTD/Game Feel/Einrichten).
    public class FeedbackDirector : MonoBehaviour
    {
        [Header("VFX (eigene Partikel, GameFeelSetup)")]
        public GameObject EnemyDeathVfx;
        public GameObject BossDeathVfx;
        public GameObject HitSparkVfx;
        [Tooltip("Splitterfrost-Karte: Eis zerspringt (Sprint 3).")]
        public GameObject FrostShatterVfx;
        [Tooltip("Höchstens so viele Gegner-Tod-Effekte pro Sekunde (Massenwellen).")]
        public float DeathVfxPerSecond = 24f;

        [Header("Schadenszahlen")]
        public Color ChampionColor = new Color(1f, 0.92f, 0.45f);
        public Color HeavyColor = new Color(1f, 0.55f, 0.15f);
        public Color TowerColor = new Color(0.93f, 0.9f, 0.82f);
        public float ChampionSize = 34f, HeavySize = 46f, TowerSize = 24f;
        [Tooltip("Turm-Treffer pro Gegner so lange sammeln, dann als eine Zahl zeigen.")]
        public float TowerAggregate = 0.3f;
        [Tooltip("Ab diesem Anteil des Max-Lebens gilt ein Champion-Treffer als schwer (Treffer-Stopp, große Zahl).")]
        public float HeavyFraction = 0.25f;

        [Header("Warnungen")]
        public float NexusAlarmCooldown = 10f;
        [Tooltip("Unter diesem Lebensanteil pulsiert die Vignette und der Herzschlag setzt ein.")]
        public float LowHealthFraction = 0.3f;

        [Header("Serien")]
        public float StreakWindow = 3f;
        public int[] StreakTiers = { 15, 30, 60 };
        public string[] StreakNames = { "Gemetzel!", "Blutbad!", "Vernichtung!" };
        public float StreakCooldown = 15f;

        private struct Pending { public float Amount, Since; public Vector3 Pos; }

        private FeedbackCanvas _fc;
        private readonly Dictionary<EnemyBrain, Pending> _pending = new Dictionary<EnemyBrain, Pending>();
        private readonly Dictionary<EnemyBrain, float> _lastTowerFlash = new Dictionary<EnemyBrain, float>();
        private readonly List<EnemyBrain> _flush = new List<EnemyBrain>();
        private readonly Queue<float> _kills = new Queue<float>();
        private int _streakTier;
        private float _nextStreak;
        private float _deathBudget, _sparkBudget;
        private float _lastHp = -1f, _nextHeartbeat;
        private float _lastNexusHp = -1f, _nextNexusAlarm;
        private bool _gameOver;

        // „+N“ am Splitter-Zähler
        private HUDManager _hud;
        private float _shardSum, _shardSince = -1f;
        private readonly List<(TextMeshProUGUI text, float start)> _shardPops = new List<(TextMeshProUGUI, float)>();

        private WaveManager _waves;
        private GameManager _game;
        private EconomyManager _eco;
        private Nexus _nexus;
        private UpgradeManager _upgrades;
        private MerchantManager _merchants;
        private FusionManager _fusion;

        void Start()
        {
            // Sprint 5: Tipps, Pings, Buddy-Emotes hängen am selben Objekt
            if (GetComponent<HintManager>() == null) gameObject.AddComponent<HintManager>();
            if (GetComponent<PingSystem>() == null) gameObject.AddComponent<PingSystem>();
            if (GetComponent<BuddyEmotes>() == null) gameObject.AddComponent<BuddyEmotes>();
            _hud = FindFirstObjectByType<HUDManager>();
            TMP_FontAsset font = _hud != null && _hud.ShardText != null ? _hud.ShardText.font : null;
            _fc = FeedbackCanvas.Create(font);
            _fc.transform.SetParent(transform, false);

            EnemyBrain.OnLocalHit += HandleHit;
            EnemyBrain.OnEnemyKilled += HandleKilled;
            EnemyBrain.OnEliteSpawned += HandleEliteSpawned;
            CardEffects.OnShatter += HandleShatter;
            GameManager.OnTeamWipe += HandleTeamWipe;

            _waves = WaveManager.Instance;
            if (_waves != null) { _waves.OnWaveStart += HandleWaveStart; _waves.OnWaveEnd += HandleWaveEnd; _waves.OnDawn += HandleDawn; }
            _game = GameManager.Instance;
            if (_game != null) _game.OnGameOver += HandleGameOver;
            _eco = EconomyManager.Instance;
            if (_eco != null) _eco.OnShardsEarned += HandleShardsEarned;
            _nexus = Nexus.Instance;
            if (_nexus != null) { _nexus.OnHealthChanged += HandleNexusHealth; _lastNexusHp = _nexus.CurrentHP; }
            _upgrades = UpgradeManager.Instance;
            if (_upgrades != null) { _upgrades.OnUpgradePicked += HandleUpgradePicked; _upgrades.OnUpgradesAvailable += HandleUpgradesOffered; }
            _merchants = MerchantManager.Instance;
            if (_merchants != null) { _merchants.OnCardsOffered += HandleCardsOffered; _merchants.OnCardPicked += HandleCardBought; }
            _fusion = FusionManager.Instance;
            if (_fusion != null) _fusion.OnFused += HandleFused;
        }

        void OnDestroy()
        {
            EnemyBrain.OnLocalHit -= HandleHit;
            EnemyBrain.OnEnemyKilled -= HandleKilled;
            EnemyBrain.OnEliteSpawned -= HandleEliteSpawned;
            CardEffects.OnShatter -= HandleShatter;
            GameManager.OnTeamWipe -= HandleTeamWipe;
            if (_waves != null) { _waves.OnWaveStart -= HandleWaveStart; _waves.OnWaveEnd -= HandleWaveEnd; _waves.OnDawn -= HandleDawn; }
            if (_game != null) _game.OnGameOver -= HandleGameOver;
            if (_eco != null) _eco.OnShardsEarned -= HandleShardsEarned;
            if (_nexus != null) _nexus.OnHealthChanged -= HandleNexusHealth;
            if (_upgrades != null) { _upgrades.OnUpgradePicked -= HandleUpgradePicked; _upgrades.OnUpgradesAvailable -= HandleUpgradesOffered; }
            if (_merchants != null) { _merchants.OnCardsOffered -= HandleCardsOffered; _merchants.OnCardPicked -= HandleCardBought; }
            if (_fusion != null) _fusion.OnFused -= HandleFused;
            TimeWarp.Cancel();
        }

        // ---------------- Eliten & Splitterfrost (Sprint 3) ----------------

        private readonly HashSet<EliteAffix> _eliteSeen = new HashSet<EliteAffix>();
        private float _nextEliteSound;

        // Server: Elite erschienen → Klang; jede Eigenschaft einmal pro Run erklärt
        private void HandleEliteSpawned(EnemyBrain e)
        {
            if (e == null) return;
            if (Time.time >= _nextEliteSound)
            {
                _nextEliteSound = Time.time + 1.5f;
                GameAudio.Play(SfxId.EliteSpawn, e.transform.position);
            }
            if (_eliteSeen.Add(e.Elite))
                ToastUI.Show($"Elite: <color=#{EliteInfo.Hex(e.Elite)}>{EliteInfo.Name(e.Elite)}</color> – {EliteInfo.Hint(e.Elite)}");
        }

        private float _nextShatterSound;

        private void HandleShatter(Vector3 pos, float radius)
        {
            if (FrostShatterVfx != null)
            {
                var go = Instantiate(FrostShatterVfx, pos + Vector3.up * 0.6f, Quaternion.identity);
                go.transform.localScale = Vector3.one * Mathf.Max(0.5f, radius / 3f);
                Destroy(go, 2.5f);
            }
            if (Time.time >= _nextShatterSound)
            {
                _nextShatterSound = Time.time + 0.12f;
                GameAudio.Play(SfxId.FrostShatter, pos);
            }
            CameraFollow.AddTraumaAt(0.12f, pos, 18f);
        }

        // ---------------- Treffer ----------------

        private void HandleHit(EnemyBrain e, float amount, bool fromPlayer, bool lethal)
        {
            if (e == null || amount <= 0f) return;
            Vector3 head = HeadPosition(e);
            bool heavy = fromPlayer && (lethal || (e.MaxHP > 0f && amount >= e.MaxHP * HeavyFraction));
            var mode = GameFeel.Numbers;

            if (fromPlayer)
            {
                if (mode != GameFeel.NumberMode.Off)
                    _fc.ShowNumber(head, amount, heavy ? HeavyColor : ChampionColor, heavy ? HeavySize : ChampionSize, heavy ? 1f : 0.8f);
                HitFlash.Flash(e.gameObject, 0.8f, heavy ? 0.1f : 0.07f);
                PlayChampionImpact(e.transform.position, lethal);
                if (HitSparkVfx != null && _sparkBudget >= 1f)
                {
                    _sparkBudget -= 1f;
                    Spawn(HitSparkVfx, Vector3.Lerp(e.transform.position, head, 0.55f), 1.2f);
                }
                if (heavy)
                {
                    TimeWarp.HitStop(lethal ? 0.06f : 0.04f);
                    CameraFollow.AddTraumaAt(lethal ? 0.14f : 0.09f, e.transform.position, 14f);
                }
            }
            else
            {
                if (mode == GameFeel.NumberMode.All)
                {
                    _pending.TryGetValue(e, out var p);
                    if (p.Amount <= 0f) p.Since = Time.unscaledTime;
                    p.Amount += amount;
                    p.Pos = head;
                    _pending[e] = p;
                    if (lethal) FlushNumber(e);
                }
                float now = Time.unscaledTime;
                if (!_lastTowerFlash.TryGetValue(e, out float last) || now - last > 0.22f)
                {
                    _lastTowerFlash[e] = now;
                    HitFlash.Flash(e.gameObject, 0.35f, 0.06f);
                }
            }
        }

        private void PlayChampionImpact(Vector3 pos, bool lethal)
        {
            var av = PlayerAvatar.Local;
            var champ = av != null ? av.Champion : GameSession.SelectedChampion;
            // Pfeil- und Arkanball-Einschläge haben eigene Sounds (ArcherKit/ArrowProjectile, ArcaneBall); das Schwert nicht
            if (champ == ChampionClass.Knight) GameAudio.Play(SfxId.SwordHit, pos);
            else if (champ == ChampionClass.Mage) GameAudio.Play(SfxId.EnemyHit, pos);
            if (lethal) GameAudio.Play(SfxId.HeavyImpact, pos);
        }

        private static Vector3 HeadPosition(EnemyBrain e)
        {
            var col = e.GetComponent<Collider>();
            if (col != null && col.enabled) return new Vector3(e.transform.position.x, col.bounds.max.y + 0.3f, e.transform.position.z);
            return e.transform.position + Vector3.up * 2.2f * e.transform.localScale.y;
        }

        private void FlushNumber(EnemyBrain e)
        {
            if (!_pending.TryGetValue(e, out var p)) return;
            _pending.Remove(e);
            if (p.Amount > 0f) _fc.ShowNumber(p.Pos, p.Amount, TowerColor, TowerSize, 0.75f);
        }

        // ---------------- Tod ----------------

        private void HandleKilled(EnemyBrain e)
        {
            if (e == null) return;
            FlushNumber(e);
            _lastTowerFlash.Remove(e);
            Vector3 pos = e.transform.position;

            if (e.IsBoss)
            {
                BossMoment(e, pos);
                return;
            }
            if (EnemyDeathVfx != null && _deathBudget >= 1f)
            {
                _deathBudget -= 1f;
                var fx = Spawn(EnemyDeathVfx, pos + Vector3.up * 0.4f, 2.5f);
                if (fx != null) fx.transform.localScale = Vector3.one * Mathf.Max(0.6f, e.transform.localScale.y);
            }
            CountKill();
        }

        private void BossMoment(EnemyBrain e, Vector3 pos)
        {
            TimeWarp.SlowMo(0.3f, 1.1f);
            CameraFollow.AddTrauma(0.65f);
            if (BossDeathVfx != null) Spawn(BossDeathVfx, pos + Vector3.up * 1f, 5f);
            GameAudio.Play(SfxId.BossDeath, pos);
            GameAudio.Play(SfxId.BossDefeated);
            _fc.ShowBanner(e.DisplayName + " besiegt!", "Die Stadt atmet auf", new Color(1f, 0.84f, 0.35f), 3f);
        }

        private void CountKill()
        {
            float now = Time.unscaledTime;
            _kills.Enqueue(now);
            while (_kills.Count > 0 && now - _kills.Peek() > StreakWindow) _kills.Dequeue();
            int count = _kills.Count;
            if (count < 5) _streakTier = 0;
            if (_streakTier < StreakTiers.Length && count >= StreakTiers[_streakTier])
            {
                int tier = _streakTier++;
                if (now >= _nextStreak && !_gameOver)
                {
                    _nextStreak = now + StreakCooldown;
                    GameAudio.Play(SfxId.Streak);
                    _fc.ShowBanner(StreakNames[Mathf.Min(tier, StreakNames.Length - 1)], count + " Gegner in " + StreakWindow.ToString("0") + " s", new Color(1f, 0.5f, 0.25f), 1.6f);
                }
            }
        }

        // ---------------- Team-Ausfall: Wiederbelebung mit Preis ----------------

        private float _wipeUntil = -1f;
        private float _wipeCost;

        private void HandleTeamWipe(int wipe, float cost, float delay)
        {
            _wipeCost = cost;
            _wipeUntil = Time.unscaledTime + delay + 0.3f;
            if (_fc != null) _fc.ShowBanner("Gefallen!", $"Der Nexus opfert {cost:0} Leben", new Color(0.95f, 0.3f, 0.25f), delay + 0.3f);
            CameraFollow.AddTrauma(0.35f);
            GameAudio.Play(SfxId.NexusAlarm);
            if (GameManager.Instance != null)
                ToastUI.Show($"Nächster Ausfall kostet {GameManager.Instance.NextWipeCost:0} Nexus-Leben");
        }

        // Countdown im Banner, solange die eigene Figur auf die Wiederbelebung wartet
        private void TickWipeBanner()
        {
            if (_wipeUntil < 0f || _fc == null) return;
            var me = PlayerAvatar.Local;
            if (Time.unscaledTime > _wipeUntil || me == null || !me.IsDowned) { _wipeUntil = -1f; return; }
            int left = Mathf.CeilToInt(me.RespawnRemaining);
            _fc.SetBannerSubtitle($"Der Nexus opfert {_wipeCost:0} Leben · Wiederbelebung in {left} s");
        }

        // ---------------- Spieler, Nexus, Splitter ----------------

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            TickWipeBanner();
            _deathBudget = Mathf.Min(12f, _deathBudget + DeathVfxPerSecond * dt);
            _sparkBudget = Mathf.Min(6f, _sparkBudget + 20f * dt);

            // Turm-Schadenszahlen ausgeben
            if (_pending.Count > 0)
            {
                float now = Time.unscaledTime;
                _flush.Clear();
                foreach (var kv in _pending)
                    if (kv.Key == null || now - kv.Value.Since >= TowerAggregate) _flush.Add(kv.Key);
                foreach (var e in _flush)
                {
                    if (e == null) { _pending.Remove(e); continue; }
                    FlushNumber(e);
                }
            }

            UpdatePlayerHealth();
            UpdateShardPops();
        }

        private void UpdatePlayerHealth()
        {
            var av = PlayerAvatar.Local;
            if (av == null || _gameOver) { _fc.SetLowHealth(0f); _lastHp = -1f; return; }
            float hp = av.Health, max = Mathf.Max(1f, av.MaxHealth);
            if (_lastHp >= 0f && hp < _lastHp - 0.01f)
            {
                float frac = (_lastHp - hp) / max;
                _fc.FlashVignette(0.35f + frac * 3f);
                CameraFollow.AddTrauma(Mathf.Min(0.45f, 0.12f + frac * 1.5f));
            }
            _lastHp = hp;

            bool alive = av.IsAlive && hp > 0f;
            float f = hp / max;
            if (alive && f < LowHealthFraction)
            {
                _fc.SetLowHealth(Mathf.Lerp(1f, 0.45f, f / LowHealthFraction));
                if (Time.unscaledTime >= _nextHeartbeat && !PauseManager.IsPaused)
                {
                    _nextHeartbeat = Time.unscaledTime + Mathf.Lerp(0.75f, 1.1f, f / LowHealthFraction);
                    GameAudio.Play(SfxId.LowHpHeartbeat);
                }
            }
            else _fc.SetLowHealth(0f);
        }

        private void HandleNexusHealth()
        {
            if (_nexus == null) return;
            float hp = _nexus.CurrentHP;
            if (_lastNexusHp >= 0f && hp < _lastNexusHp - 0.01f && !_gameOver && Time.unscaledTime >= _nextNexusAlarm)
            {
                _nextNexusAlarm = Time.unscaledTime + NexusAlarmCooldown;
                GameAudio.Play(SfxId.NexusAlarm);
                ToastUI.Show("Der Nexus wird angegriffen!");
            }
            _lastNexusHp = hp;
        }

        private void HandleShardsEarned(float amount)
        {
            if (amount <= 0f) return;
            if (_shardSince < 0f) _shardSince = Time.unscaledTime;
            _shardSum += amount;
        }

        private void UpdateShardPops()
        {
            float now = Time.unscaledTime;
            if (_shardSince >= 0f && now - _shardSince >= 0.35f)
            {
                SpawnShardPop(_shardSum);
                _shardSum = 0f;
                _shardSince = -1f;
            }
            for (int i = _shardPops.Count - 1; i >= 0; i--)
            {
                var (text, start) = _shardPops[i];
                float t = (now - start) / 1.1f;
                if (text == null || t >= 1f)
                {
                    if (text != null) Destroy(text.gameObject);
                    _shardPops.RemoveAt(i);
                    continue;
                }
                text.rectTransform.anchoredPosition = new Vector2(0f, -40f - 28f * t);
                var c = text.color;
                c.a = t > 0.6f ? Mathf.InverseLerp(1f, 0.6f, t) : 1f;
                text.color = c;
                text.transform.localScale = Vector3.one * (t < 0.1f ? Mathf.Lerp(1.4f, 1f, t / 0.1f) : 1f);
            }
        }

        private void SpawnShardPop(float amount)
        {
            if (_hud == null || _hud.ShardText == null || amount < 0.5f) return;
            var src = _hud.ShardText;
            var t = new GameObject("ShardPop", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            t.transform.SetParent(src.transform, false);
            t.rectTransform.anchorMin = t.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            t.rectTransform.sizeDelta = new Vector2(200f, 50f);
            t.font = src.font;
            t.fontSize = src.fontSize * 0.8f;
            t.fontStyle = FontStyles.Bold;
            t.alignment = TextAlignmentOptions.Center;
            t.raycastTarget = false;
            t.color = new Color(0.55f, 0.95f, 1f);
            t.outlineWidth = 0.2f;
            t.outlineColor = new Color32(20, 30, 50, 255);
            t.text = "+" + Mathf.RoundToInt(amount);
            _shardPops.Add((t, Time.unscaledTime));
        }

        // ---------------- Wellen, Karten, Fusion ----------------

        private void HandleWaveStart()
        {
            var atmo = SiegeProgression.Instance != null ? SiegeProgression.Instance.Atmosphere : null;
            if (atmo != null) atmo.SetDawn(false); // nach dem Morgengrauen: die nächste Nacht beginnt
            var ev = _waves != null ? _waves.ActiveEvent : WaveEvent.None;
            if (ev != WaveEvent.None) Codex.Discover(Codex.EventKey(ev));
            if (ev == WaveEvent.None)
            {
                GameAudio.Play(SfxId.WaveStart);
                return;
            }
            // Wellen-Ereignis: Banner, eigener Stinger, Färbung (Blutmond)
            GameAudio.Play(GameAudio.Has(SfxId.WaveEvent) ? SfxId.WaveEvent : SfxId.WaveStart);
            if (_fc != null) _fc.ShowBanner(WaveEvents.Name(ev) + "!", WaveEvents.Description(ev), WaveEvents.Color(ev), 3.2f);
            if (atmo != null && ev == WaveEvent.BloodMoon) atmo.SetEventTint(WaveEvents.Color(ev), 0.85f);
            if (atmo != null && ev == WaveEvent.SoulStorm) atmo.SetEventTint(WaveEvents.Color(ev), 0.4f);
        }

        private void HandleWaveEnd()
        {
            var atmo = SiegeProgression.Instance != null ? SiegeProgression.Instance.Atmosphere : null;
            if (atmo != null) atmo.SetEventTint(Color.white, 0f);
            if (!_gameOver) GameAudio.Play(SfxId.WaveClear);
        }

        // Morgengrauen: Sonnenaufgang, Fanfare, Banner (der Siegbildschirm kommt aus GameOverUI)
        private void HandleDawn()
        {
            var atmo = SiegeProgression.Instance != null ? SiegeProgression.Instance.Atmosphere : null;
            if (atmo != null) atmo.SetDawn(true);
            GameAudio.Play(GameAudio.Has(SfxId.Dawn) ? SfxId.Dawn : SfxId.BossDefeated);
            if (_fc != null) _fc.ShowBanner("Morgengrauen!", "Die Stadt hat die Nacht überstanden", new Color(1f, 0.78f, 0.42f), 4f);
        }

        private void HandleGameOver(string reason)
        {
            _gameOver = true;
            _fc.SetLowHealth(0f);
            TimeWarp.Cancel();
            GameAudio.Play(SfxId.GameOver);
        }

        private void HandleUpgradePicked(UpgradeDefinitionSO card)
        {
            GameAudio.Play(SfxId.CardPick);
            Codex.RecordTaken(card);
        }

        // Kodex: angebotene Karten gelten als gesehen
        private void HandleUpgradesOffered(System.Collections.Generic.List<UpgradeDefinitionSO> cards)
        {
            if (cards == null) return;
            foreach (var c in cards) Codex.Discover(Codex.CardKey(c));
        }
        private void HandleCardsOffered(Merchant m, List<MerchantCardSO> cards) => GameAudio.Play(SfxId.MerchantBell);
        private void HandleCardBought(MerchantCardSO card) => GameAudio.Play(SfxId.Coin);
        private void HandleFused(FusionBuddy f)
        {
            CameraFollow.AddTrauma(0.3f);
            if (f != null) Codex.Discover(Codex.FusionKey(f.Element));
        }

        // ---------------- Hilfen ----------------

        private static GameObject Spawn(GameObject prefab, Vector3 pos, float life)
        {
            if (prefab == null) return null;
            var go = Instantiate(prefab, pos, Quaternion.identity);
            Destroy(go, life);
            return go;
        }
    }
}
