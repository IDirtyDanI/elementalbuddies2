using UnityEngine;
using UnityEngine.UI;

namespace ElementalBuddies
{
    // HUD-Layout im Touch-Modus (Paket U). Die unteren Ecken gehören der Touch-Steuerung (TouchZones: links Bewegungs-
    // Stick, rechts Angriffs-Cluster), unten mittig liegt die Bau-Leiste. Deshalb, zur Laufzeit und ohne Prefab-Änderung:
    //  - Pause-Knopf (≥ 48 dp Trefferfläche) rechts in der Top-Bar; der Wellen-Text rückt dafür nach links.
    //  - Top-Bar: passt ihr Inhalt (Leben, Mana, Splitter, Gold | Nexus | Welle + Pause) nicht in die Breite des sicheren
    //    Bereichs (Telefon), wird sie als Ganzes verkleinert und füllt trotzdem die volle Breite.
    //  - „Welle starten“ wandert von unten rechts (Angriffs-Cluster) nach oben rechts direkt unter die Top-Bar; die
    //    Ziel-Panels (Schrein/Händler) rücken darunter. Langes Drücken auf den Knopf zeigt die Wellenvorschau
    //    (WavePreviewUI.TouchPeek), kurzes Tippen startet wie bisher.
    // Angelegt von HUDManager.Start (vor der Wellenvorschau). Auf dem PC nie vorhanden.
    public class MobileUiHud : MonoBehaviour
    {
        public float PauseSize = 76f;          // Optik in Top-Bar-Einheiten (Trefferfläche wächst über MobileUiTouchTargets)
        public float PauseRightMargin = 16f;
        public float WaveButtonGap = 12f;      // unter der Top-Bar
        public float ObjectiveGap = 14f;       // Ziel-Panels unter dem Knopf
        public float RightMargin = 24f;
        public float TopBarSideInset = 24f;    // TopBar: sizeDelta.x −24 im Prefab
        public float TopBarGroupGap = 16f;

        public static MobileUiHud Instance { get; private set; }

        private HUDManager _hud;
        private RectTransform _content, _topBar, _waveButton, _objective, _pause;
        private WavePreviewUI _preview;
        private MobileUiLongPress _waveLongPress;
        private CanvasGroup _waveGroup;
        private MerchantScreenUI _merchantUi;
        private UpgradeScreenUI _upgradeUi;
        private float _nextModalLookup;
        private static readonly Vector3[] _corners = new Vector3[4];

        public RectTransform PauseButton => _pause;

        public static MobileUiHud Attach(HUDManager hud)
        {
            if (hud == null || !MobilePlatform.TouchMode) return null;
            var m = hud.GetComponent<MobileUiHud>();
            if (m != null) return m;
            m = hud.gameObject.AddComponent<MobileUiHud>();
            m.Build(hud);
            return m;
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        private void Build(HUDManager hud)
        {
            Instance = this;
            _hud = hud;
            var canvas = hud.GetComponentInParent<Canvas>();
            if (canvas == null) return;
            canvas = canvas.rootCanvas;
            SafeAreaFitter.Attach(canvas, true, true);
            _content = SafeAreaFitter.ContentOf(canvas.transform);
            _topBar = SafeAreaFitter.FindInCanvas(canvas.transform, "TopBar") as RectTransform;

            if (_topBar != null)
            {
                BuildPauseButton();
                // Wellen-Text endet links vom Pause-Knopf (HUDManager.FitWaveText liest den Wert beim ersten Aufbau)
                hud.WaveTextRightInset = Mathf.Max(hud.WaveTextRightInset, PauseRightMargin + PauseSize + 18f);
            }

            if (hud.StartWaveButton != null)
            {
                _waveButton = (RectTransform)hud.StartWaveButton.transform;
                _waveButton.anchorMin = _waveButton.anchorMax = new Vector2(1f, 1f);
                _waveButton.pivot = new Vector2(1f, 1f);
                _waveLongPress = MobileUiLongPress.Attach(hud.StartWaveButton.gameObject);
                _waveLongPress.OnLongPress += HandleWaveLongPress;
            }

            var obj = FindFirstObjectByType<ObjectiveUI>(FindObjectsInactive.Include);
            if (obj != null)
            {
                var rt = obj.transform as RectTransform;
                // Nur das oben rechts verankerte Ziel-Panel verschieben
                if (rt != null && rt.anchorMin == rt.anchorMax && rt.anchorMin == Vector2.one) _objective = rt;
            }
            LateUpdate();
        }

        private void HandleWaveLongPress(bool held)
        {
            if (_preview == null && _hud != null) _preview = _hud.GetComponent<WavePreviewUI>();
            if (_preview != null) _preview.TouchPeek = held;
        }

        private void BuildPauseButton()
        {
            var go = new GameObject("PauseButton", typeof(RectTransform), typeof(Image), typeof(Button));
            _pause = (RectTransform)go.transform;
            _pause.SetParent(_topBar, false);
            _pause.anchorMin = _pause.anchorMax = new Vector2(1f, 0.5f);
            _pause.pivot = new Vector2(1f, 0.5f);
            _pause.anchoredPosition = new Vector2(-PauseRightMargin, 0f);
            _pause.sizeDelta = new Vector2(PauseSize, PauseSize);

            var bg = go.GetComponent<Image>();
            var src = _hud.StartWaveButton != null ? _hud.StartWaveButton.image : null;
            if (src != null && src.sprite != null)
            {
                bg.sprite = src.sprite;
                bg.type = src.sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
                bg.color = src.color;
            }
            else bg.color = new Color(0.36f, 0.23f, 0.12f, 0.95f);

            // Zwei helle Balken (gezeichnet, keine Schrift nötig)
            var cream = new Color(0.98f, 0.93f, 0.8f);
            for (int i = 0; i < 2; i++)
            {
                var bar = new GameObject(i == 0 ? "BarL" : "BarR", typeof(RectTransform), typeof(Image));
                var brt = (RectTransform)bar.transform;
                brt.SetParent(_pause, false);
                brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0.5f);
                brt.pivot = new Vector2(0.5f, 0.5f);
                brt.sizeDelta = new Vector2(PauseSize * 0.14f, PauseSize * 0.44f);
                brt.anchoredPosition = new Vector2((i == 0 ? -1f : 1f) * PauseSize * 0.12f, 0f);
                var img = bar.GetComponent<Image>();
                img.color = cream;
                img.raycastTarget = false;
            }

            var btn = go.GetComponent<Button>();
            btn.targetGraphic = bg;
            btn.navigation = new Navigation { mode = Navigation.Mode.None };
            btn.onClick.AddListener(() =>
            {
                var pm = PauseManager.Instance;
                if (pm != null) pm.TogglePause();
            });
        }

        void LateUpdate()
        {
            if (_content == null) return;
            FitTopBar();
            HideWaveButtonUnderModal();
            float y = TopBarBottom() - WaveButtonGap;
            if (_waveButton != null)
            {
                var pos = new Vector2(-RightMargin, y);
                if ((_waveButton.anchoredPosition - pos).sqrMagnitude > 0.01f) _waveButton.anchoredPosition = pos;
                y -= _waveButton.rect.height + ObjectiveGap;
            }
            if (_objective != null)
            {
                var pos = new Vector2(_objective.anchoredPosition.x, y);
                if ((_objective.anchoredPosition - pos).sqrMagnitude > 0.01f) _objective.anchoredPosition = pos;
            }
        }

        // Händler/Kartenwahl: deren Gold-/Kopfzeile liegt oben rechts genau dort, wo „Welle starten“ im Touch-Layout sitzt
        // (am PC unten rechts, dort stört er nicht) → solange ausblenden
        private void HideWaveButtonUnderModal()
        {
            if (_waveButton == null) return;
            if ((_merchantUi == null || _upgradeUi == null) && Time.unscaledTime >= _nextModalLookup)
            {
                _nextModalLookup = Time.unscaledTime + 2f;
                if (_merchantUi == null) _merchantUi = FindFirstObjectByType<MerchantScreenUI>(FindObjectsInactive.Include);
                if (_upgradeUi == null) _upgradeUi = FindFirstObjectByType<UpgradeScreenUI>(FindObjectsInactive.Include);
            }
            bool modal = _merchantUi != null && _merchantUi.Panel != null && _merchantUi.Panel.activeInHierarchy ||
                         _upgradeUi != null && _upgradeUi.Panel != null && _upgradeUi.Panel.activeInHierarchy;
            if (_waveGroup == null)
            {
                if (!modal) return;
                _waveGroup = _waveButton.GetComponent<CanvasGroup>();
                if (_waveGroup == null) _waveGroup = _waveButton.gameObject.AddComponent<CanvasGroup>();
            }
            _waveGroup.alpha = modal ? 0f : 1f;
            _waveGroup.blocksRaycasts = !modal;
            _waveGroup.interactable = !modal;
        }

        // Unterkante der Top-Bar relativ zur Oberkante des sicheren Bereichs (≤ 0)
        private float TopBarBottom()
        {
            if (_topBar == null || !_topBar.gameObject.activeInHierarchy) return -10f;
            _topBar.GetWorldCorners(_corners);
            return _content.InverseTransformPoint(_corners[0]).y - _content.rect.yMax;
        }

        // Breite des Inhalts messen (bei Skalierung 1) und die Leiste bei Bedarf verkleinern
        private void FitTopBar()
        {
            if (_topBar == null) return;
            float left = 0f, cMin = 0f, cMax = 0f, right = 0f;
            bool anyCenter = false;
            for (int i = 0; i < _topBar.childCount; i++)
            {
                var c = _topBar.GetChild(i) as RectTransform;
                if (c == null || !c.gameObject.activeSelf || c.anchorMin.x != c.anchorMax.x) continue;
                float w = c.rect.width * Mathf.Abs(c.localScale.x);
                float x0 = c.anchoredPosition.x - c.pivot.x * w, x1 = x0 + w;
                if (c.anchorMin.x <= 0.01f) left = Mathf.Max(left, x1);
                else if (c.anchorMin.x >= 0.99f) right = Mathf.Max(right, -x0);
                else
                {
                    if (!anyCenter) { cMin = x0; cMax = x1; anyCenter = true; }
                    else { cMin = Mathf.Min(cMin, x0); cMax = Mathf.Max(cMax, x1); }
                }
            }
            // Der Wellen-Text darf bis WaveTextMaxWidth wachsen (HUDManager.FitWaveText)
            if (_hud != null && _hud.WaveText != null)
                right += Mathf.Max(0f, _hud.WaveTextMaxWidth - _hud.WaveText.rectTransform.rect.width);

            float required = MobileUiMath.BarRequiredWidth(left, cMin, cMax, right, TopBarGroupGap);
            float parentW = _content.rect.width;
            float avail = parentW - TopBarSideInset;
            float f = MobileUiMath.BarFit(required, avail);
            if (Mathf.Abs(_topBar.localScale.x - f) > 0.001f) _topBar.localScale = new Vector3(f, f, 1f);
            // Gestreckt (Anker 0…1): nach dem Verkleinern wieder volle Breite
            if (_topBar.anchorMin.x <= 0.01f && _topBar.anchorMax.x >= 0.99f)
            {
                float sx = avail / f - parentW;
                if (Mathf.Abs(_topBar.sizeDelta.x - sx) > 0.05f) _topBar.sizeDelta = new Vector2(sx, _topBar.sizeDelta.y);
            }
        }
    }
}
