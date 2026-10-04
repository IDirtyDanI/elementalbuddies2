using System.Collections.Generic;
using UnityEngine;

namespace ElementalBuddies
{
    // Sturmwolke des Sturmfürsten: eigenständig (läuft weiter, wenn der Buddy stirbt; Werte beim Erzeugen festgehalten).
    // Driftet zur Mitte der Gegner unter ihr, macht sie alle WetInterval s nass und schlägt alle StrikeInterval s
    // mit einem Blitz auf einen zufälligen Gegner unter ihr ein (Nass ×2) + 1 Kettensprung.
    public class StormCloud : MonoBehaviour
    {
        private float _radius, _lifetime, _height, _drift, _wetInterval, _wetDuration, _strikeInterval;
        private float _damage, _wetMult, _chainRange, _chainFactor;
        private GameObject _strikeVfx, _wetVfx;
        private Material _boltMat;
        private Color _boltColor;
        private float _age, _wetTimer, _strikeTimer;
        private Vector3 _ground;
        private Vector3 _baseScale;

        public bool IsExpired => _age >= _lifetime;
        public float Radius => _radius;
        public int Strikes { get; private set; }
        public Vector3 GroundPosition => _ground;

        private readonly List<EnemyBrain> _under = new List<EnemyBrain>();
        private const float FadeTime = 0.5f;

        public static StormCloud Spawn(StormLordBuddy owner, Vector3 targetPos)
        {
            Vector3 ground = SuperBuddy.GroundPoint(targetPos);
            Vector3 pos = ground + Vector3.up * owner.CloudHeight;
            GameObject go;
            if (owner.StormCloudPrefab != null)
            {
                go = Instantiate(owner.StormCloudPrefab, pos, Quaternion.identity);
            }
            else
            {
                go = new GameObject("StormCloud");
                go.transform.position = pos;
                // Platzhalter: drei dunkle, halbtransparente Scheiben
                for (int i = 0; i < 3; i++)
                {
                    var disc = SuperBuddy.CreatePrimitive(PrimitiveType.Cylinder, "Puff" + i, new Color(0.18f, 0.18f, 0.25f, 0.75f), go.transform);
                    float r = owner.CloudRadius * (1f - i * 0.2f) * 2f;
                    disc.transform.localPosition = new Vector3((i - 1) * 0.6f, i * 0.25f, (i % 2) * 0.5f);
                    disc.transform.localScale = new Vector3(r, 0.15f, r);
                }
            }
            go.name = "StormCloud";

            var cloud = go.AddComponent<StormCloud>();
            cloud._radius = owner.CloudRadius;
            cloud._lifetime = Mathf.Max(0.1f, owner.CloudLifetime);
            cloud._height = owner.CloudHeight;
            cloud._drift = owner.CloudDriftSpeed;
            cloud._wetInterval = Mathf.Max(0.05f, owner.WetInterval);
            cloud._wetDuration = owner.WetDuration;
            cloud._strikeInterval = Mathf.Max(0.05f, owner.StrikeInterval);
            cloud._damage = owner.EffectiveDamage;
            cloud._wetMult = owner.WetMultiplier;
            cloud._chainRange = owner.ChainRange;
            cloud._chainFactor = owner.ChainFactor;
            cloud._strikeVfx = owner.StrikeEffectPrefab;
            cloud._wetVfx = owner.WetVfxPrefab;
            cloud._boltMat = owner.BoltMaterial;
            cloud._boltColor = owner.BoltColor;
            cloud._ground = ground;
            cloud._baseScale = go.transform.localScale;
            cloud._strikeTimer = cloud._strikeInterval * 0.5f; // erster Blitz kurz nach dem Erscheinen
            return cloud;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            _age += dt;
            if (_age >= _lifetime)
            {
                Destroy(gameObject);
                return;
            }

            _under.Clear();
            LavaPuddle.CollectEnemies(_ground, _radius, _under);
            Drift(dt);

            _wetTimer += dt;
            if (_wetTimer >= _wetInterval)
            {
                _wetTimer -= _wetInterval;
                foreach (var e in _under)
                    if (e != null && !e.IsDead) e.ApplyWet(_wetDuration, _wetVfx);
            }

            _strikeTimer += dt;
            if (_strikeTimer >= _strikeInterval && _under.Count > 0)
            {
                _strikeTimer -= _strikeInterval;
                Strike(_under[Random.Range(0, _under.Count)]);
            }

            // Ausklingen
            float remaining = _lifetime - _age;
            float k = remaining < FadeTime ? Mathf.Clamp01(remaining / FadeTime) : 1f;
            transform.localScale = _baseScale * k;
        }

        // Zur Mitte der Gegner unter der Wolke treiben
        private void Drift(float dt)
        {
            if (_under.Count == 0 || _drift <= 0f) return;
            Vector3 centroid = Vector3.zero;
            int n = 0;
            foreach (var e in _under)
            {
                if (e == null) continue;
                centroid += e.transform.position;
                n++;
            }
            if (n == 0) return;
            centroid /= n;
            Vector3 d = centroid - _ground;
            d.y = 0f;
            float step = _drift * dt;
            if (d.magnitude > step) d = d.normalized * step;
            _ground += d;
            // Höhe vom echten Boden, nicht vom Gegner-Pivot (NavMesh-BaseOffset)
            _ground.y = SuperBuddy.GroundPoint(_ground).y;
            transform.position = _ground + Vector3.up * _height;
        }

        // Öffentlich für Tests
        public void Strike(EnemyBrain target)
        {
            if (target == null || target.IsDead) return;
            Strikes++;
            Vector3 from = transform.position;
            Vector3 to = target.transform.position + Vector3.up * 0.9f;
            SpawnBolt(from, to);
            if (_strikeVfx != null) Destroy(Instantiate(_strikeVfx, SuperBuddy.GroundPoint(target.transform.position), Quaternion.identity), 1f);

            Vector3 origin = target.transform.position;
            target.TakeDamage(_damage * (target.IsWet ? _wetMult : 1f));

            // 1 Kettensprung zum nächsten anderen Gegner
            EnemyBrain next = null;
            float bestD = float.MaxValue;
            foreach (var e in CombatUtil.FindEnemies(origin, _chainRange))
            {
                if (e == null || e == target || e.IsDead) continue;
                float d = (e.transform.position - origin).sqrMagnitude;
                if (d < bestD)
                {
                    bestD = d;
                    next = e;
                }
            }
            if (next != null)
            {
                SpawnBolt(to, next.transform.position + Vector3.up * 0.9f);
                next.TakeDamage(_damage * _chainFactor * (next.IsWet ? _wetMult : 1f));
            }
            GameAudio.Play(SfxId.ArcaneBallHit, origin);
        }

        private void SpawnBolt(Vector3 from, Vector3 to)
        {
            const int points = 10;
            var p = new Vector3[points];
            for (int i = 0; i < points; i++)
            {
                float t = i / (float)(points - 1);
                p[i] = Vector3.Lerp(from, to, t);
                if (i > 0 && i < points - 1) p[i] += Random.insideUnitSphere * 0.35f;
            }
            FusionLineFx.Spawn(p, _boltMat, _boltColor, Color.white, 0.12f, 0.05f, 0.15f, "StormBolt");
        }
    }
}
