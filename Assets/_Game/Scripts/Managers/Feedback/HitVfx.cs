using System.Collections.Generic;
using UnityEngine;

namespace ElementalBuddies
{
    // Treffer-Effekte je Fähigkeit am Gegner: sitzt auf Körperhöhe, wächst mit der Gegnergröße, Budget gegen Massen-Treffer.
    // Zwei Wege, beide rein kosmetisch:
    //  - Kits, deren Treffer auf allen Rechnern gerechnet werden (Schwert, Axt, Pfeile, Lichtpfeil), rufen Show direkt auf
    //    und geben dem Schaden hitFx = false mit;
    //  - alle anderen Treffer entscheidet der Server: EnemyBrain.DealPlayerDamage(…, AbilityId) → EnemyBrain.OnAbilityHit
    //    → FeedbackDirector zeigt den Effekt und schickt ihn per EnemyNet-RPC an die Clients (die feuern OnAbilityHit erneut).
    // DoT-Ticks (Brennen) tragen keine Fähigkeit und zeigen nur ihr Status-VFX.
    public static class HitVfx
    {
        public const string ResourcePath = "HitVfxTable";

        // Alle Fähigkeiten, die Gegnern Schaden zufügen (je eine Tabellenzeile Pflicht, HitVfxTableTests).
        // Nicht dabei: Blink, Schildblock, Rolle, Steinwall, Heiliger Kreis, Lichtschwur, Lichtrausch (kein eigener Schaden).
        public static readonly AbilityId[] DamagingAbilities =
        {
            AbilityId.ArcaneBall, AbilityId.FireWave, AbilityId.FrostNova,
            AbilityId.SwordSlash, AbilityId.FlameWhirl, AbilityId.FrostStrike, AbilityId.Earthquake,
            AbilityId.ArrowShot, AbilityId.FireArrowRain, AbilityId.FrostArrow, AbilityId.ThornTrap, AbilityId.LightArrow,
            AbilityId.AxeCleave, AbilityId.BerserkCharge, AbilityId.EmberStorm, AbilityId.IceBreaker, AbilityId.Landslide
        };

        // Fallback, falls die Tabelle fehlt (FeedbackDirector setzt den alten HitSpark)
        public static GameObject Fallback;

        private static HitVfxTableSO _table;
        private static bool _loaded;
        private static HitVfxBudget _budget;
        private static readonly List<ParticleSystem> _ps = new List<ParticleSystem>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _table = null;
            _loaded = false;
            _budget = null;
            Fallback = null;
        }

        public static HitVfxTableSO Table
        {
            get
            {
                if (!_loaded)
                {
                    _loaded = true;
                    _table = Resources.Load<HitVfxTableSO>(ResourcePath);
                }
                return _table;
            }
        }

        public static bool IsDamaging(AbilityId id) => System.Array.IndexOf(DamagingAbilities, id) >= 0;

        // Effekt der Fähigkeit am Gegner zeigen (lokal). dir = Trefferrichtung (waagerecht, optional), scale = Zusatzfaktor
        // (z. B. Kombo-Abschluss). legacy = altes Kit-Prefab, nur falls die Tabelle keinen Eintrag hat.
        // Rückgabe: Effekt erschienen (false = Budget erschöpft / nichts zugeordnet).
        public static bool Show(EnemyBrain enemy, AbilityId id, Vector3 dir = default, float scale = 1f, GameObject legacy = null)
        {
            if (enemy == null) return false;
            var table = Table;
            var entry = table != null ? table.Find(id) : null;
            GameObject prefab = entry != null && entry.Prefab != null ? entry.Prefab : legacy != null ? legacy : Fallback;
            if (prefab == null) return false;

            if (_budget == null)
                _budget = table != null ? new HitVfxBudget(table.MaxPerTrigger, table.Burst, table.PerSecond) : new HitVfxBudget();
            float minInterval = entry != null ? entry.MinInterval : 0.2f;
            if (!_budget.TryShow(enemy.GetInstanceID(), (int)id, minInterval, Time.unscaledTime, Time.frameCount)) return false;

            Vector3 pos = BodyPoint(enemy, entry != null ? entry.Height : 0.4f, out float bodyScale);
            // vor den Körper Richtung Kamera ziehen, sonst verdeckt der Gegner den Effekt (Draufsicht, Tiefentest)
            var cam = Camera.main;
            if (cam != null)
            {
                Vector3 to = cam.transform.position - pos;
                float d = to.magnitude;
                if (d > 0.01f) pos += to / d * Mathf.Min(d * 0.5f, enemy.BodyRadius + 0.5f);
            }
            dir.y = 0f;
            Quaternion rot = dir.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(dir.normalized)
                : Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            var fx = Object.Instantiate(prefab, pos, rot);
            float k = bodyScale * Mathf.Max(0.05f, scale) * (entry != null ? entry.Scale : 1f);
            if (!Mathf.Approximately(k, 1f)) fx.transform.localScale = prefab.transform.localScale * k;
            if (entry != null && entry.Tint != Color.white) Tint(fx, entry.Tint);
            Object.Destroy(fx, table != null ? table.Lifetime : 1.2f);
            return true;
        }

        // Trefferpunkt am Körper (Höhe zwischen Füßen und Kollider-Oberkante) und Größenfaktor: Normalgegner (~2 m hoch) = 1,
        // große Gegner (Bosse, Belagerungsramme) bis 2,5 – wie die Status-VFX (BodyFootprint), aber auch für hohe Gegner
        public static Vector3 BodyPoint(EnemyBrain enemy, float height01, out float scale)
        {
            Vector3 feet = enemy.transform.position;
            float top = feet.y + 2f * Mathf.Abs(enemy.transform.lossyScale.y);
            var col = enemy.GetComponent<Collider>();
            if (col != null && col.enabled) top = col.bounds.max.y;
            float h = Mathf.Max(0.4f, top - feet.y);
            scale = Mathf.Clamp(Mathf.Max(h / 2f, BodyFootprint.StatusVfxFactor(enemy.gameObject)), 0.6f, 2.5f);
            return new Vector3(feet.x, feet.y + h * Mathf.Clamp01(height01), feet.z);
        }

        // Startfarben aller Partikel der Instanz tönen (Konstante bzw. zwei Farben)
        private static void Tint(GameObject fx, Color tint)
        {
            fx.GetComponentsInChildren(true, _ps);
            foreach (var ps in _ps)
            {
                var main = ps.main;
                var c = main.startColor;
                switch (c.mode)
                {
                    case ParticleSystemGradientMode.Color:
                        main.startColor = c.color * tint;
                        break;
                    case ParticleSystemGradientMode.TwoColors:
                        main.startColor = new ParticleSystem.MinMaxGradient(c.colorMin * tint, c.colorMax * tint);
                        break;
                }
            }
            _ps.Clear();
            foreach (var l in fx.GetComponentsInChildren<Light>(true)) l.color *= tint;
        }
    }
}
