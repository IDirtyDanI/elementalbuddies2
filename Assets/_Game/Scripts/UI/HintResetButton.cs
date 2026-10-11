using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ElementalBuddies
{
    // Knopf „Tipps zurücksetzen“ (Bekannte Probleme #127) rechts neben dem Schalter „Tipps anzeigen“ – im Pause-Menü
    // und in den Einstellungen des Hauptmenüs. Geklont aus einem vorhandenen Knopf, damit der Stil passt.
    public static class HintResetButton
    {
        private const string Label = "Tipps zurücksetzen";
        private const string DoneLabel = "Zurückgesetzt";

        // row: Zeile mit Label + Toggle; template: beliebiger Pergament-Knopf als Stilvorlage
        public static Button Attach(Transform row, Button template)
        {
            if (row == null || template == null) return null;
            var go = Object.Instantiate(template.gameObject, row, false);
            go.name = "HintResetButton";
            go.SetActive(true);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(400f, 0f);
            rt.sizeDelta = new Vector2(300f, 50f);
            var le = go.GetComponent<LayoutElement>();
            if (le != null) le.ignoreLayout = true;

            var button = go.GetComponent<Button>();
            button.onClick = new Button.ButtonClickedEvent();
            var text = go.GetComponentInChildren<TextMeshProUGUI>(true);
            if (text != null)
            {
                text.text = Label;
                text.enableAutoSizing = true;
                text.fontSizeMin = 18f;
                text.fontSizeMax = 28f;
            }
            button.onClick.AddListener(() =>
            {
                HintManager.ResetAll();
                if (text != null) text.text = DoneLabel;
                GameAudio.Play(GameAudio.Has(SfxId.Hint) ? SfxId.Hint : SfxId.UiClick);
            });
            return button;
        }

        // Beim Öffnen der Einstellungen wieder die normale Beschriftung zeigen
        public static void ResetLabel(Button button)
        {
            if (button == null) return;
            var text = button.GetComponentInChildren<TextMeshProUGUI>(true);
            if (text != null) text.text = Label;
        }
    }
}
