using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ElementalBuddies
{
    // Hauptmenü: Knopf „Tastaturbelegung“ im Einstellungs-Fenster (links neben „Zurück“) und eigene Seite als Kopie der
    // Erfolge-Seite (wie der Kodex in MainMenuMeta). Zur Laufzeit von MainMenuUI.Init angehängt – übersteht „Szene neu bauen“.
    public static class KeyBindingsMenu
    {
        private static GameObject _panel;
        private static KeyBindingsView _view;
        private static MainMenuUI _menu;

        public static bool IsOpen => _panel != null && _panel.activeSelf;

        public static void Attach(MainMenuUI menu)
        {
            if (menu == null || !Application.isPlaying) return;
            // Touch-Geräte (Paket U): keine Tastatur – kein Knopf, keine Belegungs-Seite
            if (MobilePlatform.TouchMode) { _menu = menu; _panel = null; return; }
            if (_panel != null && _menu == menu) return;
            _menu = menu;
            _panel = null;
            if (menu.SettingsPanel == null || menu.SettingsBackButton == null || menu.AchievementsPanel == null) return;

            // Knopf im Einstellungs-Fenster: „Zurück“ rückt nach rechts, „Tastaturbelegung“ links daneben
            var back = (RectTransform)menu.SettingsBackButton.transform;
            var bgo = Object.Instantiate(back.gameObject, back.parent, false);
            bgo.name = "KeyBindingsButton";
            var brt = (RectTransform)bgo.transform;
            back.anchoredPosition = new Vector2(back.anchoredPosition.x + 175f, back.anchoredPosition.y);
            brt.anchoredPosition = new Vector2(back.anchoredPosition.x - 350f, back.anchoredPosition.y);
            var button = bgo.GetComponent<Button>();
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(() => Show(true));
            var bl = bgo.GetComponentInChildren<TextMeshProUGUI>(true);
            if (bl != null)
            {
                bl.text = "Tastaturbelegung";
                bl.enableAutoSizing = true;
                bl.fontSizeMin = 18f;
                bl.fontSizeMax = bl.fontSize;
            }

            // Seite: Kopie der Erfolge-Seite, oberstes Kind des Canvas (liegt über dem Einstellungs-Fenster)
            var src = menu.AchievementsPanel;
            _panel = Object.Instantiate(src, src.transform.parent, false);
            _panel.name = "KeyBindingsPanel";
            _panel.transform.SetAsLastSibling();
            _panel.SetActive(false);
            var box = _panel.transform.Find("Box");
            var title = box != null ? box.Find("Ribbon/Title") : null;
            if (title != null) title.GetComponent<TextMeshProUGUI>().text = "Tastaturbelegung";
            foreach (var n in new[] { "Counter", "CounterIcon" })
            {
                var c = box != null ? box.Find(n) : null;
                if (c != null) c.gameObject.SetActive(false);
            }
            // Info-Zeile oben wird zur Meldungszeile (Tausch, Abbruch …)
            var info = box != null ? box.Find("Info") : null;
            var status = info != null ? info.GetComponent<TextMeshProUGUI>() : null;
            if (status != null)
            {
                var srt = status.rectTransform;
                srt.sizeDelta = new Vector2(((RectTransform)box).sizeDelta.x - 144f, srt.sizeDelta.y);
                status.textWrappingMode = TextWrappingModes.Normal;
                status.enableAutoSizing = true;
                status.fontSizeMin = 16f;
                status.fontSizeMax = Mathf.Min(status.fontSize, 26f);
                status.alignment = TextAlignmentOptions.Left;
                status.color = new Color(0.42f, 0.3f, 0.18f);
            }

            var scroll = _panel.GetComponentInChildren<ScrollRect>(true);
            var content = scroll != null ? scroll.content : null;
            var backBtn = box != null ? box.Find("BackButton") : null;
            if (backBtn != null)
            {
                var b = backBtn.GetComponent<Button>();
                b.onClick = new Button.ButtonClickedEvent();
                b.onClick.AddListener(() => Show(false));
                // „Alle zurücksetzen“ links neben „Zurück“
                var bb = (RectTransform)backBtn;
                var rgo = Object.Instantiate(backBtn.gameObject, box, false);
                rgo.name = "ResetAllButton";
                var rrt = (RectTransform)rgo.transform;
                bb.anchoredPosition = new Vector2(bb.anchoredPosition.x + 175f, bb.anchoredPosition.y);
                rrt.anchoredPosition = new Vector2(bb.anchoredPosition.x - 350f, bb.anchoredPosition.y);
                var rb = rgo.GetComponent<Button>();
                rb.onClick = new Button.ButtonClickedEvent();
                rb.onClick.AddListener(() => { if (_view != null) _view.ResetAll(); });
                var rl = rgo.GetComponentInChildren<TextMeshProUGUI>(true);
                if (rl != null)
                {
                    rl.text = "Alle zurücksetzen";
                    rl.enableAutoSizing = true;
                    rl.fontSizeMin = 18f;
                    rl.fontSizeMax = rl.fontSize;
                }
            }

            var label = menu.PlayButton != null ? menu.PlayButton.GetComponentInChildren<TextMeshProUGUI>(true) : null;
            TMP_FontAsset titleFont = title != null ? title.GetComponent<TextMeshProUGUI>().font : (label != null ? label.font : null);
            TMP_FontAsset bodyFont = menu.DifficultyDescription != null ? menu.DifficultyDescription.font : titleFont;
            Button template = menu.SettingsButton != null ? menu.SettingsButton : menu.SettingsBackButton;
            _view = KeyBindingsView.Create(_panel, content, template, titleFont, bodyFont, status);
        }

        public static void Show(bool show)
        {
            if (_panel == null) return;
            if (!show && KeyBindings.IsRebinding) KeyBindings.CancelRebind();
            _panel.SetActive(show);
            GameAudio.Play(SfxId.UiClick);
            if (show)
            {
                Canvas.ForceUpdateCanvases();
                var scroll = _panel.GetComponentInChildren<ScrollRect>(true);
                if (scroll != null) scroll.verticalNormalizedPosition = 1f;
            }
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        }

        // Von MainMenuUI.Update: true = Tasten gehören der Belegungs-Seite (bzw. dem Neubelegen)
        public static bool HandleKeys(Keyboard kb)
        {
            if (KeyBindingsView.Busy) return true;
            if (!IsOpen) return false;
            // Einstellungs-Fenster ging anderweitig zu (Lobby/Netz-Wechsel) → Seite mit schließen
            if (_menu != null && _menu.SettingsPanel != null && !_menu.SettingsPanel.activeSelf) { Show(false); return false; }
            if (kb != null && kb.escapeKey.wasPressedThisFrame) Show(false);
            return true;
        }
    }
}
