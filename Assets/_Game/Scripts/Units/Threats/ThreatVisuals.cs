using System.Collections.Generic;
using UnityEngine;

namespace ElementalBuddies
{
    // Champion-Bedrohungen: gemeinsame Optik-Helfer (alle Rechner). Requisiten kommen aus Prefab-Feldern (Platzhalter
    // aus Editor/ChampionThreatSetup, später die echten Modelle aus P5); fehlt ein Prefab, entsteht ein prozeduraler
    // Platzhalter aus Primitiven. Teile werden über feste Namen gefunden (Ward_Shell, Ritual_Runes, Rift_Hand …).
    public static class ThreatVisuals
    {
        private static readonly Color DefaultWard = new Color(0.66f, 0.42f, 1f);

        // Schildfarbe des aktiven Gegner-Themes (Stadt violett, Dorf bernstein)
        public static Color WardColor
        {
            get
            {
                var wm = WaveManager.Instance;
                var theme = wm != null ? wm.ActiveTheme : null;
                return theme != null ? theme.WardColor : DefaultWard;
            }
        }

        public static GameObject WardShellPrefab
        {
            get
            {
                var wm = WaveManager.Instance;
                var theme = wm != null ? wm.ActiveTheme : null;
                return theme != null ? theme.WardShellPrefab : null;
            }
        }

        // Teil per Name (rekursiv, auch inaktive); null, wenn es fehlt
        public static Transform FindPart(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;
            foreach (Transform c in root)
            {
                var r = FindPart(c, name);
                if (r != null) return r;
            }
            return null;
        }

        // Alle Teile, deren Name mit prefix beginnt (z. B. Bind_Chain_1..3)
        public static List<Transform> FindParts(Transform root, string prefix)
        {
            var list = new List<Transform>();
            if (root == null) return list;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith(prefix)) list.Add(t);
            return list;
        }

        // Primitive ohne Collider/Schatten in einer Farbe (Platzhalter)
        public static GameObject Primitive(PrimitiveType type, string name, Color color, Transform parent)
        {
            return SuperBuddy.CreatePrimitive(type, name, color, parent);
        }

        // Kleine Funken-/Scherbenwolke (Würfel fliegen nach außen und schrumpfen) – ohne Partikelsystem
        public static void Burst(Vector3 pos, Color color, int count, float size, float speed, float life)
        {
            var root = new GameObject("ThreatBurst");
            root.transform.position = pos;
            for (int i = 0; i < count; i++)
            {
                var c = Primitive(PrimitiveType.Cube, "Bit", color, root.transform);
                c.transform.localScale = Vector3.one * size * Random.Range(0.7f, 1.3f);
                c.transform.localRotation = Random.rotation;
            }
            root.AddComponent<ShardBurst>().Init(speed, life);
        }
    }

    // Scherben/Funken: alle Kinder fliegen vom Mittelpunkt weg (Richtung = Welt-Versatz zum Mittelpunkt, sonst zufällig),
    // fallen, drehen sich, schrumpfen und blenden aus (Alpha über MaterialPropertyBlock, nur transparente Materialien), dann weg
    public class ShardBurst : MonoBehaviour
    {
        private readonly List<Transform> _parts = new List<Transform>();
        private readonly List<Vector3> _vel = new List<Vector3>();
        private readonly List<Vector3> _scale = new List<Vector3>();
        private readonly List<Renderer> _fade = new List<Renderer>();
        private readonly List<Color> _fadeColor = new List<Color>();
        private MaterialPropertyBlock _mpb;
        private float _life = 1f, _t;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        public void Init(float speed, float life)
        {
            _life = Mathf.Max(0.05f, life);
            foreach (Transform c in transform)
            {
                c.gameObject.SetActive(true);
                Vector3 off = c.position - transform.position;
                if (off.sqrMagnitude < 0.0001f)
                {
                    // Pivot im Mittelpunkt (z. B. Modell-Scherben ohne eigenen Ursprung): Mitte der Renderer-Bounds
                    var r = c.GetComponentInChildren<Renderer>();
                    if (r != null) off = r.bounds.center - transform.position;
                }
                Vector3 dir = off.sqrMagnitude > 0.0001f ? off.normalized : Random.onUnitSphere;
                dir = (dir + Random.insideUnitSphere * 0.35f + Vector3.up * 0.4f).normalized;
                _parts.Add(c);
                _vel.Add(dir * speed * Random.Range(0.6f, 1.2f));
                _scale.Add(c.localScale);
                foreach (var r in c.GetComponentsInChildren<Renderer>())
                {
                    var m = r.sharedMaterial;
                    if (m == null || !m.HasProperty(BaseColorId) || m.renderQueue < 2900) continue;
                    _fade.Add(r);
                    _fadeColor.Add(m.GetColor(BaseColorId));
                }
            }
        }

        void Update()
        {
            _t += Time.deltaTime;
            float k = Mathf.Clamp01(_t / _life);
            for (int i = 0; i < _parts.Count; i++)
            {
                if (_parts[i] == null) continue;
                _vel[i] += Vector3.down * 9f * Time.deltaTime;
                _parts[i].position += _vel[i] * Time.deltaTime;
                _parts[i].Rotate(200f * Time.deltaTime, 90f * Time.deltaTime, 0f, Space.Self);
                _parts[i].localScale = _scale[i] * (1f - k * k);
            }
            if (_fade.Count > 0)
            {
                if (_mpb == null) _mpb = new MaterialPropertyBlock();
                for (int i = 0; i < _fade.Count; i++)
                {
                    if (_fade[i] == null) continue;
                    Color c = _fadeColor[i];
                    c.a *= 1f - k;
                    _fade[i].GetPropertyBlock(_mpb);
                    _mpb.SetColor(BaseColorId, c);
                    _fade[i].SetPropertyBlock(_mpb);
                }
            }
            if (_t >= _life) Destroy(gameObject);
        }
    }
}
