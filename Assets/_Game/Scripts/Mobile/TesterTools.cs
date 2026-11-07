using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

namespace ElementalBuddies
{
    // Tester-Hilfen (2026-11-03, nach Spielstand-Verlust eines Testers durch Deinstallieren):
    //  - Versionsanzeige oben in der Mitte des Hauptmenüs (nur Touch-Geräte): „v0.1.3“ – jeder Tester-Build zählt die Version hoch
    //    (Editor/TesterBuild), dann installiert Android die neue APK als Update über die alte und der Spielstand bleibt.
    //  - Nur im Tester-Build (Define EB_TESTER): Knopf „Tester: alles freischalten“ daneben, zweimal tippen = alle Erfolge
    //    auf Schwer (Champions, Stufe 4, Fusionen, Walddorf, Belagerung) und alle Belagerungsstufen auf allen Karten.
    //    Gilt wie jede Freischaltung ab dem nächsten Spiel. Im normalen Build gibt es den Knopf nicht.
    // Im Editor lässt sich der Knopf über EditorPrefs „BuddyTD.TesterTools“ zum Prüfen einschalten.
    public class TesterTools : MonoBehaviour
    {
        private const string MenuSceneName = "MainMenu";
        private GameObject _root;
        private TextMeshProUGUI _unlockLabel;
        private bool _armed;

        public static bool UnlockEnabled
        {
            get
            {
#if EB_TESTER
                return true;
#elif UNITY_EDITOR
                return UnityEditor.EditorPrefs.GetBool("BuddyTD.TesterTools", false);
#else
                return false;
#endif
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (!MobilePlatform.TouchMode) return;
            // #532 (Tester 2026-11-04): Development-Builds öffnen per Drei-Finger-Doppeltipp Unitys Rendering Debugger –
            // beim Bauen/Zoomen mit mehreren Fingern passiert das versehentlich, und Tester finden nicht wieder heraus
            if (Debug.isDebugBuild && !Application.isEditor)
                UnityEngine.Rendering.DebugManager.instance.enableRuntimeUI = false;
            var go = new GameObject("TesterTools");
            DontDestroyOnLoad(go);
            go.AddComponent<TesterTools>();
        }

        void OnEnable() { SceneManager.sceneLoaded += HandleScene; Build(SceneManager.GetActiveScene()); }
        void OnDisable() => SceneManager.sceneLoaded -= HandleScene;

        private void HandleScene(Scene s, LoadSceneMode m) => Build(s);

        private void Build(Scene s)
        {
            if (_root != null) Destroy(_root);
            _root = null;
            _armed = false;
            if (s.name != MenuSceneName) return;

            _root = new GameObject("TesterToolsCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = _root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30000;
            var scaler = _root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            string version = "v" + Application.version + (UnlockEnabled ? " · Tester" : "");
            // Oben in der Mitte: zwischen Titel und Champion-Panel ist im Hauptmenü frei (unten links sitzen Knöpfe)
            var v = NewText(_root.transform, "Version", version, 22f, new Vector2(0f, -8f), new Vector2(360f, 32f));
            v.alignment = TextAlignmentOptions.Top;

            if (!UnlockEnabled) return;
            var img = new GameObject("UnlockAll", typeof(Image), typeof(Button)).GetComponent<Image>();
            img.transform.SetParent(_root.transform, false);
            img.color = new Color(0.2f, 0.13f, 0.08f, 0.75f);
            var rt = (RectTransform)img.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -44f);
            rt.sizeDelta = new Vector2(330f, 56f);
            var b = img.GetComponent<Button>();
            b.targetGraphic = img;
            b.onClick.AddListener(OnUnlockClicked);
            _unlockLabel = NewText(img.transform, "Label", "Tester: alles freischalten", 22f, Vector2.zero, rt.sizeDelta);
            var lrt = _unlockLabel.rectTransform;
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one; lrt.offsetMin = lrt.offsetMax = Vector2.zero;
            _unlockLabel.alignment = TextAlignmentOptions.Center;
            _unlockLabel.color = new Color(1f, 0.93f, 0.8f);
        }

        private void OnUnlockClicked()
        {
            if (!_armed)
            {
                _armed = true;
                _unlockLabel.text = "Wirklich? Nochmal tippen";
                return;
            }
            UnlockAll();
            _armed = false;
            _unlockLabel.text = "Freigeschaltet!";
        }

        // Alle Erfolge auf Schwer (zählt für alle Stufen) + alle Belagerungsstufen auf allen Karten
        public static void UnlockAll()
        {
            Progression.UnlockAllPersistent(Progression.RankHard);
            foreach (var map in MapRegistry.All)
                if (map != null) SiegeLevels.RecordDawn(SiegeLevels.Max - 1, map);
            Debug.Log("[TesterTools] alles freigeschaltet");
        }

        private static TextMeshProUGUI NewText(Transform parent, string name, string text, float size, Vector2 pos, Vector2 box)
        {
            var go = new GameObject(name, typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = box;
            var t = go.GetComponent<TextMeshProUGUI>();
            foreach (var f in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
                if (f != null && f.name.StartsWith("AlegreyaSans-Regular")) { t.font = f; break; }
            t.text = text;
            t.fontSize = size;
            t.color = new Color(1f, 1f, 1f, 0.7f);
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.raycastTarget = false;
            return t;
        }
    }
}
