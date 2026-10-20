using UnityEngine;
using UnityEngine.UI;

namespace ElementalBuddies
{
    // Touch-Skalierung eines Wurzel-Canvas (Paket U). Merkt sich die Referenzauflösung des CanvasScaler (Scale With Screen
    // Size) und setzt sie auf Referenz / MobileUi.Boost – damit werden Texte und Knöpfe physisch so groß, wie es
    // MobileUiMath für Telefon bzw. Tablet vorgibt. Folgt Auflösungs-/Orientierungswechseln (jedes Bild geprüft, nur bei
    // Änderung gesetzt). Canvases ohne CanvasScaler (z. B. FeedbackCanvas der Schadenszahlen) bekommen einen mit
    // konstanter Pixelgröße und Faktor Boost. Wird von MobileUiRuntime an alle passenden Canvases gehängt; auf dem PC
    // nie aktiv.
    //
    // Für eigene Touch-Canvases, die ihre Größe selbst in dp rechnen: MobileUiScaler.Exclude(canvas) aufrufen (oder
    // ConstantPhysicalSize/ConstantPixelSize benutzen) – dann bleibt der Canvas unangetastet.
    [DisallowMultipleComponent]
    public class MobileUiScaler : MonoBehaviour
    {
        [Tooltip("Nicht skalieren (eigene Touch-Canvases).")]
        public bool Excluded;
        [Tooltip("Höchste Verstärkung für diesen Canvas (Hauptmenü: 1, weil die Komposition die Höhe füllt).")]
        public float MaxBoost = 10f;

        private CanvasScaler _scaler;
        private Vector2 _baseReference;
        private float _baseFactor = 1f;
        private bool _pixelMode;      // ConstantPixelSize (selbst angelegt oder vorhanden)
        private bool _ready;
        private float _applied = -1f;

        public float AppliedBoost => _applied > 0f ? _applied : 1f;

        public static void Exclude(Canvas canvas)
        {
            if (canvas == null) return;
            var s = canvas.GetComponent<MobileUiScaler>();
            if (s == null) s = canvas.gameObject.AddComponent<MobileUiScaler>();
            s.SetExcluded(true);
        }

        public static MobileUiScaler Attach(Canvas canvas, bool addScalerIfMissing, float maxBoost)
        {
            if (canvas == null) return null;
            var s = canvas.GetComponent<MobileUiScaler>();
            if (s != null) return s;
            s = canvas.gameObject.AddComponent<MobileUiScaler>();
            s.MaxBoost = maxBoost;
            s.Init(addScalerIfMissing);
            return s;
        }

        private void Init(bool addScalerIfMissing)
        {
            _scaler = GetComponent<CanvasScaler>();
            if (_scaler == null && addScalerIfMissing)
            {
                _scaler = gameObject.AddComponent<CanvasScaler>();
                _scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
                _scaler.scaleFactor = 1f;
            }
            if (_scaler == null) { Excluded = true; return; }
            _pixelMode = _scaler.uiScaleMode == CanvasScaler.ScaleMode.ConstantPixelSize;
            if (!_pixelMode && _scaler.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize) { Excluded = true; return; }
            _baseReference = _scaler.referenceResolution;
            _baseFactor = _scaler.scaleFactor;
            _ready = true;
            Apply();
        }

        private void SetExcluded(bool on)
        {
            if (on && _ready && _scaler != null)
            {
                // Ursprungswerte zurück
                if (_pixelMode) _scaler.scaleFactor = _baseFactor;
                else _scaler.referenceResolution = _baseReference;
                _applied = -1f;
            }
            Excluded = on;
        }

        void LateUpdate() => Apply();

        private void Apply()
        {
            if (!_ready || Excluded || _scaler == null || !MobilePlatform.TouchMode) return;
            float k = Mathf.Clamp(MobileUi.Boost, 1f, Mathf.Max(1f, MaxBoost));
            if (Mathf.Abs(k - _applied) < 0.0005f) return;
            _applied = k;
            if (_pixelMode) _scaler.scaleFactor = _baseFactor * k;
            else _scaler.referenceResolution = MobileUiMath.BoostedReference(_baseReference, k);
        }
    }
}
