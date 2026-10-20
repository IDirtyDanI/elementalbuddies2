using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace ElementalBuddies
{
    // Touch-Modus im Editor erzwingen (ohne Device Simulator), z. B. um das Mobil-HUD im Game-View zu prüfen.
    // „Gerät nachbilden“: Game-View auf die Geräteauflösung (Querformat) stellen und DPI + Safe Area für MobilePlatform
    // simulieren (nur im Touch-Modus wirksam, nur diese Editor-Sitzung). Für echte Notch-/Insel-Masken und Touch per Maus
    // den Device Simulator mit den Profilen in Editor/Devices nutzen.
    public static class MobileEditorMenu
    {
        private const string MenuPath = "BuddyTD/Mobile/Touch-Modus im Editor erzwingen";
        private const string SimRoot = "BuddyTD/Mobile/Gerät nachbilden/";

        [MenuItem(MenuPath)]
        private static void Toggle()
        {
            bool on = !EditorPrefs.GetBool(MobilePlatform.ForceEditorPrefKey, false);
            EditorPrefs.SetBool(MobilePlatform.ForceEditorPrefKey, on);
        }

        [MenuItem(MenuPath, true)]
        private static bool ToggleValidate()
        {
            Menu.SetChecked(MenuPath, EditorPrefs.GetBool(MobilePlatform.ForceEditorPrefKey, false));
            return true;
        }

        // Lenovo Tab M11: 1920 × 1200, ~207 dpi, keine Notch → volle Fläche
        [MenuItem(SimRoot + "Lenovo Tab M11 (1920×1200)")]
        private static void SimTabM11() => Simulate("Tab M11", 1920, 1200, 207f, Vector4.zero);

        // iPhone 16 Pro Max quer: 2868 × 1320, 460 ppi, @3x; Insets links/rechts 62 pt, unten 21 pt (Home-Indikator)
        [MenuItem(SimRoot + "iPhone 16 Pro Max (2868×1320)")]
        private static void SimIPhone() => Simulate("iPhone 16 Pro Max", 2868, 1320, 460f, new Vector4(186f, 63f, 186f, 0f));

        [MenuItem(SimRoot + "Aus (PC-Werte)")]
        private static void SimOff() => MobilePlatform.ClearSimulation();

        public static void Simulate(string label, int width, int height, float dpi, Vector4 insets)
        {
            MobilePlatform.SetSimulation(dpi, insets);
            if (!SetGameViewSize(label, width, height))
                Debug.LogWarning($"[Mobile] Game-View-Größe {width}×{height} konnte nicht gesetzt werden – bitte von Hand wählen.");
        }

        // Game-View: feste Auflösung (legt sie bei Bedarf als eigene Größe an). Interne Editor-API per Reflection.
        public static bool SetGameViewSize(string label, int width, int height)
        {
            try
            {
                const BindingFlags bf = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                var gvType = Type.GetType("UnityEditor.GameView,UnityEditor");
                var sizesType = Type.GetType("UnityEditor.GameViewSizes,UnityEditor");
                var sizeType = Type.GetType("UnityEditor.GameViewSize,UnityEditor");
                var sizeKind = Type.GetType("UnityEditor.GameViewSizeType,UnityEditor");
                if (gvType == null || sizesType == null || sizeType == null || sizeKind == null) return false;

                var gv = EditorWindow.GetWindow(gvType, false, null, false);
                var inst = typeof(ScriptableSingleton<>).MakeGenericType(sizesType).GetProperty("instance", bf).GetValue(null);
                var groupType = gvType.GetProperty("currentSizeGroupType", bf).GetValue(gv);
                var group = sizesType.GetMethod("GetGroup", bf).Invoke(inst, new[] { groupType });
                var texts = (string[])group.GetType().GetMethod("GetDisplayTexts", bf).Invoke(group, null);
                int idx = Array.FindIndex(texts, t => t.StartsWith(label + " ("));
                if (idx < 0)
                {
                    var ctor = sizeType.GetConstructor(new[] { sizeKind, typeof(int), typeof(int), typeof(string) });
                    var size = ctor.Invoke(new object[] { Enum.Parse(sizeKind, "FixedResolution"), width, height, label });
                    group.GetType().GetMethod("AddCustomSize", bf).Invoke(group, new[] { size });
                    texts = (string[])group.GetType().GetMethod("GetDisplayTexts", bf).Invoke(group, null);
                    idx = Array.FindIndex(texts, t => t.StartsWith(label + " ("));
                }
                if (idx < 0) return false;
                gvType.GetMethod("SizeSelectionCallback", bf).Invoke(gv, new object[] { idx, null });
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Mobile] Game-View-Größe: " + e.Message);
                return false;
            }
        }
    }
}
