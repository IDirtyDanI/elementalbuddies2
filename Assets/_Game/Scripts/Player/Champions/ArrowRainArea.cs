using UnityEngine;

namespace ElementalBuddies
{
    // Laufzeit-Objekt des Feuerpfeil-Regens: verteilt die Schadens-Wellen über die Dauer und lässt
    // brennende Pfeile (nur Optik) ins Zielgebiet fallen. Zerstört sich danach selbst.
    public class ArrowRainArea : MonoBehaviour
    {
        private FireArrowRainSpell _spell;
        private float _dm = 1f;
        private float _t;
        private int _wavesDone;

        public int WavesDone => _wavesDone;

        public void Setup(FireArrowRainSpell spell, float damageMultiplier)
        {
            _spell = spell;
            _dm = damageMultiplier;
        }

        void Update()
        {
            Tick(Time.deltaTime);
        }

        // Öffentlich für synchrone Tests
        public void Tick(float dt)
        {
            if (_spell == null) { Destroy(gameObject); return; }
            _t += dt;
            int waves = Mathf.Max(1, _spell.Waves);
            float interval = _spell.RainDuration / waves;
            // Erste Welle nach einem halben Intervall (Pfeile sind dann gerade gelandet)
            while (_wavesDone < waves && _t >= interval * (_wavesDone + 0.5f))
            {
                _wavesDone++;
                DoWave();
            }
            if (_wavesDone >= waves && _t >= _spell.RainDuration + 0.3f) Destroy(gameObject);
        }

        private void DoWave()
        {
            Vector3 c = transform.position;
            for (int i = 0; i < _spell.ArrowsPerWave; i++)
            {
                Vector2 r = Random.insideUnitCircle * _spell.Radius;
                Vector3 land = c + new Vector3(r.x, 0f, r.y);
                if (_spell.FallingArrowPrefab != null)
                {
                    // Pfeil startet schräg über dem Landepunkt und fällt in ~0.2 s hinein
                    Vector3 from = land + new Vector3(-1.2f, 6f, -0.6f);
                    var arrow = Instantiate(_spell.FallingArrowPrefab, from, Quaternion.LookRotation(land - from));
                    var fall = arrow.GetComponent<FallingArrow>();
                    if (fall == null) fall = arrow.AddComponent<FallingArrow>();
                    fall.Setup(land, 0.18f + Random.value * 0.06f, _spell.ImpactFxPrefab);
                }
                else if (_spell.ImpactFxPrefab != null)
                {
                    CombatUtil.SpawnFx(_spell.ImpactFxPrefab, land, Quaternion.identity, 1.5f);
                }
            }

            foreach (var enemy in CombatUtil.FindEnemies(c, _spell.Radius))
            {
                if (enemy == null) continue;
                if (_spell.BurnDps > 0f && _spell.BurnDuration > 0f)
                    BurnEffect.Apply(enemy.gameObject, _spell.BurnDps * _dm, _spell.BurnDuration, _spell.BurnVfxPrefab);
                enemy.TakeDamage(_spell.DamagePerWave * _dm);
            }
            if (_wavesDone == 1 || _wavesDone % 2 == 0) GameAudio.Play(SfxId.FireWave, c);
        }
    }

    // Optik eines fallenden Pfeils: fliegt in duration zum Landepunkt, bleibt kurz stecken, Einschlag-Funken
    public class FallingArrow : MonoBehaviour
    {
        private Vector3 _from, _to;
        private float _duration, _t;
        private GameObject _impact;
        private bool _landed;

        public void Setup(Vector3 land, float duration, GameObject impactFx)
        {
            _from = transform.position;
            _to = land;
            _duration = Mathf.Max(0.02f, duration);
            _impact = impactFx;
        }

        void Update()
        {
            if (_landed) return;
            _t += Time.deltaTime;
            float k = Mathf.Clamp01(_t / _duration);
            transform.position = Vector3.Lerp(_from, _to, k);
            if (k >= 1f)
            {
                _landed = true;
                if (_impact != null) CombatUtil.SpawnFx(_impact, _to, Quaternion.identity, 1.5f);
                foreach (var ps in GetComponentsInChildren<ParticleSystem>()) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                Destroy(gameObject, 0.6f);
            }
        }
    }
}
