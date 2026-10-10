using System.Collections.Generic;
using UnityEngine;

namespace ElementalBuddies
{
    // Elite-Gegner (Plan „Fesselung“ C4): ab Welle 12 bekommen einzelne Normalgegner eine Eigenschaft, die eine
    // Einseitigkeit bestraft (z. B. Feuer-Monokultur, Bekannte-Probleme #77). Nur hinten anhängen (byte, synchronisiert).
    public enum EliteAffix : byte
    {
        None,
        Fireproof,    // −60 % Schaden von Feuer-Buddies
        Frostguard,   // immun gegen Verlangsamung und Einfrieren
        Swift,        // +40 % Tempo
        Shieldbearer, // Gegner in der Nähe nehmen −30 % Schaden
        ShardThief,   // schnell, bringt viele Splitter
        Ram           // Belagerungsramme (Wellen-Ereignis): viel Leben, nur der Nexus zählt – nie zufällig gewürfelt
    }

    public static class EliteInfo
    {
        public const int Count = 5; // zufällig würfelbare Eigenschaften (ohne Ramme)

        // Grundwerte jeder Elite
        public const float HpFactor = 2.5f;
        public const float BountyFactor = 3f;
        public const float ScaleFactor = 1.2f;
        public const float FireproofTaken = 0.4f;
        public const float ShieldRadius = 5f;
        public const float ShieldTaken = 0.7f;

        public static string Name(EliteAffix a)
        {
            switch (a)
            {
                case EliteAffix.Fireproof: return "Feuerfest";
                case EliteAffix.Frostguard: return "Frostgepanzert";
                case EliteAffix.Swift: return "Flink";
                case EliteAffix.Shieldbearer: return "Schildträger";
                case EliteAffix.ShardThief: return "Splitterdieb";
                case EliteAffix.Ram: return "Belagerungsramme";
                default: return "";
            }
        }

        public static string Hint(EliteAffix a)
        {
            switch (a)
            {
                case EliteAffix.Fireproof: return "nimmt kaum Feuerschaden";
                case EliteAffix.Frostguard: return "lässt sich nicht verlangsamen";
                case EliteAffix.Swift: return "sehr schnell";
                case EliteAffix.Shieldbearer: return "schützt Gegner in der Nähe";
                case EliteAffix.ShardThief: return "flink, bringt viele Splitter";
                case EliteAffix.Ram: return "marschiert stur zum Nexus – aufhalten!";
                default: return "";
            }
        }

        public static Color Color(EliteAffix a)
        {
            switch (a)
            {
                case EliteAffix.Fireproof: return new Color(1f, 0.45f, 0.12f);
                case EliteAffix.Frostguard: return new Color(0.45f, 0.85f, 1f);
                case EliteAffix.Swift: return new Color(0.55f, 1f, 0.4f);
                case EliteAffix.Shieldbearer: return new Color(0.95f, 0.9f, 0.55f);
                case EliteAffix.ShardThief: return new Color(0.75f, 0.45f, 1f);
                case EliteAffix.Ram: return new Color(0.85f, 0.12f, 0.08f);
                default: return UnityEngine.Color.white;
            }
        }

        public static string Hex(EliteAffix a) => ColorUtility.ToHtmlStringRGB(Color(a));
        // dunklere Variante für Text auf Pergament (Wellenvorschau)
        public static string DarkHex(EliteAffix a)
        {
            UnityEngine.Color.RGBToHSV(Color(a), out float h, out float sat, out float v);
            return ColorUtility.ToHtmlStringRGB(UnityEngine.Color.HSVToRGB(h, Mathf.Min(1f, sat + 0.25f), 0.5f));
        }

        public static float SpeedFactor(EliteAffix a) =>
            a == EliteAffix.Swift ? 1.4f : a == EliteAffix.ShardThief ? 1.25f : a == EliteAffix.Ram ? 0.8f : 1f;

        public static float Bounty(EliteAffix a) =>
            a == EliteAffix.None ? 1f : a == EliteAffix.ShardThief ? BountyFactor * 2f : a == EliteAffix.Ram ? 12f : BountyFactor;

        public static float Hp(EliteAffix a) => a == EliteAffix.Ram ? 6f : HpFactor; // Ramme 6× (Modell: 10× machte die Morgengrauen-Welle W20 zu schwer)
        public static float Scale(EliteAffix a) => a == EliteAffix.Ram ? 1.7f : ScaleFactor;
        // Ramme schlägt hart auf den Nexus
        public static float Damage(EliteAffix a) => a == EliteAffix.Ram ? 3f : 1f;
    }

    // Optik einer Elite (alle Rechner): farbiger Aura-Ring + Eigenschafts-Symbol über dem Kopf
    public class EliteVisual : MonoBehaviour
    {
        private Transform _icon;
        private Camera _cam;

        public static void Attach(EnemyBrain enemy, EliteAffix affix)
        {
            if (enemy == null || affix == EliteAffix.None || enemy.GetComponent<EliteVisual>() != null) return;
            var v = enemy.gameObject.AddComponent<EliteVisual>();
            v.Build(enemy, affix);
        }

        private void Build(EnemyBrain enemy, EliteAffix affix)
        {
            var wm = WaveManager.Instance;
            Color c = EliteInfo.Color(affix);

            // Höhe aus den Renderern (wie die HP-Leiste)
            float height = 2f;
            bool any = false;
            Bounds b = default;
            foreach (var r in GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer) continue;
                if (any) b.Encapsulate(r.bounds); else { b = r.bounds; any = true; }
            }
            if (any) height = b.max.y - transform.position.y;

            if (wm != null && wm.EliteAuraPrefab != null)
            {
                var aura = Instantiate(wm.EliteAuraPrefab, transform);
                aura.transform.localPosition = Vector3.up * 0.05f;
                aura.transform.localRotation = Quaternion.identity;
                foreach (var ps in aura.GetComponentsInChildren<ParticleSystem>())
                {
                    var main = ps.main;
                    Color sc = c;
                    sc.a = main.startColor.color.a;
                    main.startColor = sc;
                }
            }

            var light = new GameObject("EliteLight").AddComponent<Light>();
            light.transform.SetParent(transform, false);
            light.transform.localPosition = Vector3.up * 1.2f;
            light.type = LightType.Point;
            light.color = c;
            light.range = 3.5f;
            light.intensity = 2.2f;
            light.shadows = LightShadows.None;

            int idx = (int)affix - 1;
            if (wm != null && wm.EliteIcons != null && idx >= 0 && idx < wm.EliteIcons.Length && wm.EliteIcons[idx] != null)
            {
                var go = new GameObject("EliteIcon");
                go.transform.SetParent(transform, false);
                // über der HP-Leiste (EnemyHealthBar: Höhe + 0,35)
                float s = transform.lossyScale.y > 0.01f ? transform.lossyScale.y : 1f;
                go.transform.localPosition = Vector3.up * ((height + 0.85f) / s);
                go.transform.localScale = Vector3.one * (0.42f / s);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = wm.EliteIcons[idx];
                sr.sortingOrder = 50;
                _icon = go.transform;
            }
        }

        void LateUpdate()
        {
            if (_icon == null) return;
            if (_cam == null) _cam = Camera.main;
            if (_cam != null) _icon.rotation = _cam.transform.rotation;
        }
    }
}
