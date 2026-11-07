using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace ElementalBuddies
{
    // Einwilligung in personalisierte Werbung (DSGVO, Stufe 3 Werbung-Plan, 2026-11-02). Werbung im Spiel ist ohnehin
    // freiwillig; gefragt wird nur, ob die Werbenetzwerke (ironSource, Unity Ads) die Werbe-ID für personalisierte Anzeigen
    // nutzen dürfen. Beim ersten Start einmal fragen (vor dem Start von LevelPlay), Antwort in PlayerPrefs; ändern über
    // Pausenmenü → Einstellungen → „Datenschutz (Werbung)“. Ohne Google-Werbung im Mediationsmix ist keine Google-CMP nötig.
    public static class AdConsent
    {
        private const string PrefKey = "ads_consent"; // -1 = noch nicht gefragt, 0 = nein, 1 = ja

        public static bool Asked => PlayerPrefs.GetInt(PrefKey, -1) >= 0;
        public static bool Granted => PlayerPrefs.GetInt(PrefKey, -1) == 1;

        // Gespeicherte Antwort liefern oder (erster Start) die Frage zeigen
        public static void Ensure(Action<bool> done)
        {
            if (Asked) { done(Granted); return; }
            Ask(done);
        }

        // Frage zeigen (auch zum Ändern); die Antwort wird gespeichert und an LevelPlay weitergegeben
        public static void Ask(Action<bool> done = null)
        {
            var root = new GameObject("AdConsent", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            UnityEngine.Object.DontDestroyOnLoad(root);
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32001;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var dim = NewImage(root.transform, "Dim", new Color(0f, 0f, 0f, 0.75f));
            var drt = (RectTransform)dim.transform;
            drt.anchorMin = Vector2.zero;
            drt.anchorMax = Vector2.one;
            drt.offsetMin = drt.offsetMax = Vector2.zero;

            var box = NewImage(root.transform, "Box", new Color(0.93f, 0.87f, 0.74f, 1f));
            ((RectTransform)box.transform).sizeDelta = new Vector2(1100f, 560f);

            var title = NewText(box.transform, new Vector2(0f, 200f), new Vector2(1000f, 80f), 46f, FontStyles.Bold);
            title.text = "Werbung und Datenschutz";
            var body = NewText(box.transform, new Vector2(0f, 40f), new Vector2(1000f, 240f), 28f, FontStyles.Normal);
            body.text = "Elemental Buddies ist kostenlos. Wer möchte, kann <b>freiwillig</b> kurze Videos ansehen und bekommt dafür " +
                        "z. B. einen Neuwurf – Pflicht ist das nie.\n\nDürfen die Werbepartner (ironSource, Unity Ads) dafür deine " +
                        "Werbe-ID nutzen, um <b>personalisierte</b> Werbung zu zeigen? Sonst siehst du allgemeine Werbung. " +
                        "Ändern kannst du das jederzeit im Pausenmenü unter Einstellungen.";

            NewButton(box.transform, new Vector2(-260f, -190f), "Personalisiert erlauben", () => Answer(root, true, done));
            NewButton(box.transform, new Vector2(260f, -190f), "Nur allgemeine Werbung", () => Answer(root, false, done));
        }

        private static void Answer(GameObject root, bool consent, Action<bool> done)
        {
            PlayerPrefs.SetInt(PrefKey, consent ? 1 : 0);
            PlayerPrefs.Save();
            UnityEngine.Object.Destroy(root);
            LevelPlayAds.ApplyConsent(consent);
            Debug.Log("[AdConsent] personalisierte Werbung: " + (consent ? "ja" : "nein"));
            done?.Invoke(consent);
        }

        // Test-Fernsteuerung: Antwort vergessen (nächster Start fragt neu)
        public static void Reset()
        {
            PlayerPrefs.DeleteKey(PrefKey);
            PlayerPrefs.Save();
        }

        // Spielschrift der Oberfläche, falls geladen (sonst TMP-Standard)
        private static TMP_FontAsset GameFont()
        {
            foreach (var f in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
                if (f != null && f.name.StartsWith("AlegreyaSans-Regular")) return f;
            return null;
        }

        private static Image NewImage(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            return img;
        }

        private static TextMeshProUGUI NewText(Transform parent, Vector2 pos, Vector2 size, float fontSize, FontStyles style)
        {
            var go = new GameObject("Text", typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var t = go.GetComponent<TextMeshProUGUI>();
            var font = GameFont();
            if (font != null) t.font = font;
            t.fontSize = fontSize;
            t.fontStyle = style;
            t.color = new Color(0.22f, 0.14f, 0.08f);
            t.alignment = TextAlignmentOptions.Center;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.raycastTarget = false;
            return t;
        }

        private static void NewButton(Transform parent, Vector2 pos, string label, Action onClick)
        {
            var img = NewImage(parent, label, new Color(0.45f, 0.27f, 0.14f, 1f));
            var rt = (RectTransform)img.transform;
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(460f, 96f);
            var b = img.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            b.onClick.AddListener(() => onClick());
            var t = NewText(img.transform, Vector2.zero, new Vector2(440f, 90f), 30f, FontStyles.Bold);
            t.color = Color.white;
            t.text = label;
        }
    }
}
