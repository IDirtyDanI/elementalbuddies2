using UnityEngine;

namespace ElementalBuddies
{
    // Optik des Bannschilds (alle Rechner, Zustand über EnemyBrain.SetWard bzw. EnemyNet.Ward): Schale um den Gegner,
    // „Immun“-Funkeln bei geschluckten Treffern, Bruch-Effekt. Schale aus EnemyThemeSO.WardShellPrefab (Kind
    // „Ward_Shell“, Durchmesser 1 m um den Pivot; optional „Ward_Shards“ als Bruchstücke), sonst prozedurale Kugel.
    // Die Schale hängt nicht am Gegner (Elite-/VisualScale ändern die Wurzel-Skalierung erst nach dem Spawn) und folgt
    // ihm in LateUpdate; Größe aus den Renderer-Bounds, neu gemessen bei Skalierungswechsel.
    public class WardVisual : MonoBehaviour
    {
        private Transform _root;   // Instanz (Prefab oder Platzhalter)
        private Transform _shell;  // Ward_Shell
        private Renderer[] _shellRenderers;
        private Color _color;
        private float _diameter = 2f;
        private float _centerY = 1f;
        private float _measuredScale = -1f;
        private float _flash;
        private float _phase;
        private bool _hidden;
        private MaterialPropertyBlock _mpb;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        public static WardVisual Ensure(EnemyBrain enemy)
        {
            if (enemy == null) return null;
            var v = enemy.GetComponent<WardVisual>();
            if (v == null) v = enemy.gameObject.AddComponent<WardVisual>();
            v.Show();
            return v;
        }

        private void Show()
        {
            _hidden = false;
            if (_root != null) { _root.gameObject.SetActive(true); return; }
            _color = ThreatVisuals.WardColor;
            _phase = Random.value * 6f;
            var prefab = ThreatVisuals.WardShellPrefab;
            if (prefab != null)
            {
                _root = Instantiate(prefab).transform;
                _root.name = "Ward_" + name;
                _shell = ThreatVisuals.FindPart(_root, "Ward_Shell");
                if (_shell == null) _shell = _root;
                var shards = ThreatVisuals.FindPart(_root, "Ward_Shards");
                if (shards != null) shards.gameObject.SetActive(false);
            }
            else
            {
                _root = new GameObject("Ward_" + name).transform;
                Color c = _color;
                c.a = 0.28f;
                _shell = ThreatVisuals.Primitive(PrimitiveType.Sphere, "Ward_Shell", c, _root).transform;
            }
            _shellRenderers = _shell.GetComponentsInChildren<Renderer>(true);
            _measuredScale = -1f;
            LateUpdate();
        }

        // Größe aus den Gegner-Renderern (ohne die eigene Schale), Durchmesser = größte horizontale/vertikale Ausdehnung
        private void Measure()
        {
            _measuredScale = transform.lossyScale.y;
            bool any = false;
            Bounds b = default;
            foreach (var r in GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer || r is LineRenderer || r is SpriteRenderer) continue;
                if (any) b.Encapsulate(r.bounds); else { b = r.bounds; any = true; }
            }
            if (!any) { _diameter = 2f; _centerY = 1f; return; }
            float h = b.size.y, w = Mathf.Max(b.size.x, b.size.z);
            _diameter = Mathf.Clamp(Mathf.Max(h, w * 1.2f) * 1.1f, 1.2f, 6f);
            _centerY = b.center.y - transform.position.y;
        }

        // „Immun“-Funkeln: Schale blitzt auf und pulst, kleine Funken
        public void Flash()
        {
            _flash = 1f;
            if (_root == null) return;
            Vector3 p = transform.position + Vector3.up * _centerY;
            ThreatVisuals.Burst(p + Random.insideUnitSphere * _diameter * 0.3f, Color.Lerp(_color, Color.white, 0.5f), 5, 0.08f, 3f, 0.35f);
        }

        // Schildbruch: Schale weg, Scherben fliegen
        public void Break()
        {
            if (_root == null) return;
            Vector3 center = transform.position + Vector3.up * _centerY;
            var shards = ThreatVisuals.FindPart(_root, "Ward_Shards");
            if (shards != null)
            {
                // Weltgröße bleibt erhalten (Schale ist auf _diameter skaliert; Modell-Wrapper dürfen eine eigene Skalierung tragen)
                shards.SetParent(null, true);
                shards.gameObject.SetActive(true); // im Prefab/Show ausgeblendet – sonst läuft ShardBurst nie (und bleibt liegen)
                shards.gameObject.AddComponent<ShardBurst>().Init(5f, 1.1f);
            }
            else ThreatVisuals.Burst(center, _color, 10, Mathf.Max(0.15f, _diameter * 0.12f), 5f, 1.0f);
            Destroy(_root.gameObject);
            _root = null;
            _shell = null;
            _hidden = true;
        }

        void LateUpdate()
        {
            if (_root == null || _hidden) return;
            if (!Mathf.Approximately(transform.lossyScale.y, _measuredScale)) Measure();
            _phase += Time.deltaTime;
            _flash = Mathf.Max(0f, _flash - Time.deltaTime * 3f);
            float pulse = 1f + 0.03f * Mathf.Sin(_phase * 2.4f) + 0.12f * _flash;
            _root.position = transform.position + Vector3.up * _centerY;
            _root.rotation = Quaternion.Euler(0f, _phase * 25f, 0f);
            _root.localScale = Vector3.one * _diameter * pulse;
            if (_shellRenderers != null && _shellRenderers.Length > 0)
            {
                if (_mpb == null) _mpb = new MaterialPropertyBlock();
                foreach (var r in _shellRenderers)
                {
                    if (r == null) continue;
                    var mat = r.sharedMaterial;
                    if (mat == null) continue;
                    Color baseC = mat.HasProperty(BaseColorId) ? mat.GetColor(BaseColorId) : (mat.HasProperty(ColorId) ? mat.GetColor(ColorId) : _color);
                    Color c = Color.Lerp(baseC, Color.white, 0.6f * _flash);
                    c.a = Mathf.Clamp01(baseC.a * (1f + 1.5f * _flash) + 0.05f * Mathf.Sin(_phase * 3f));
                    r.GetPropertyBlock(_mpb);
                    if (mat.HasProperty(BaseColorId)) _mpb.SetColor(BaseColorId, c);
                    if (mat.HasProperty(ColorId)) _mpb.SetColor(ColorId, c);
                    r.SetPropertyBlock(_mpb);
                }
            }
        }

        void OnDestroy()
        {
            if (_root != null) Destroy(_root.gameObject);
        }
    }
}
