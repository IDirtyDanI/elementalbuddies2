using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace ElementalBuddies.EditorTools
{
    // Kontaktbogen der Posen eines Champions (Editor-Sampling mit allen Layern + ChampionWeaponHold) – zum Prüfen von
    // Waffen-Haltung und -Ausrichtung ohne Play-Mode. Zeilen = Posen, Spalten = Zeitpunkte, je Zelle zwei Ansichten
    // (schräg vorne / Seite). API: Render(visual, labels, times, pfad). Menü: BuddyTD → Champions → Posen-Kontaktbogen.
    public static class ChampionPoseSheet
    {
        public const int Cell = 220;

        [MenuItem("BuddyTD/Champions/Posen-Kontaktbogen")]
        public static void RenderPlayerPrefab()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Prefabs/Player.prefab");
            foreach (var cv in prefab.GetComponentsInChildren<ChampionVisual>(true))
                Render(cv, null, new[] { 0f, 0.25f, 0.5f, 0.75f }, "Captures/Sheets/" + cv.name + ".png");
        }

        // labels = null: alle Posen des Controllers (ChampionPoseSampler.Enumerate)
        public static string Render(ChampionVisual source, string[] labels, float[] times, string path, float yaw0 = 35f, float yaw1 = 100f)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var camGo = new GameObject("__SheetCam") { hideFlags = HideFlags.HideAndDontSave };
            var lightGo = new GameObject("__SheetLight") { hideFlags = HideFlags.HideAndDontSave };
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            lightGo.transform.rotation = Quaternion.Euler(35f, 160f, 0f);
            var log = new System.Text.StringBuilder();
            using (var s = new ChampionPoseSampler(source, new Vector3(0f, -500f, 0f)))
            {
                var ac = ChampionPoseSampler.ControllerOf(s.Animator);
                var all = ChampionPoseSampler.Enumerate(ac);
                var poses = new List<PoseSample>();
                if (labels == null) poses = all;
                else foreach (var l in labels) { var p = all.Find(x => x.Label == l); if (p != null) poses.Add(p); }
                int cols = times.Length * 2, rows = poses.Count;
                int w = cols * Cell, h = rows * Cell;
                var rt = new RenderTexture(w, h, 24);
                var cam = camGo.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.82f, 0.8f, 0.76f);
                cam.fieldOfView = 28f;
                cam.nearClipPlane = 0.05f;
                cam.targetTexture = rt;
                // mehrere Renders pro Editor-Frame: Skinning bei jedem Render neu berechnen (sonst hinkt das Mesh eine Pose hinterher)
                foreach (var smr in s.Root.GetComponentsInChildren<SkinnedMeshRenderer>(true)) smr.forceMatrixRecalculationPerRender = true;
                for (int r = 0; r < rows; r++)
                    for (int c = 0; c < times.Length; c++)
                    {
                        float t = times[c];
                        if (poses[r].Layer > 0) t *= 0.98f;
                        s.Sample(poses[r], t);
                        Vector3 center = s.Animator.GetBoneTransform(HumanBodyBones.Hips).position + Vector3.up * 0.1f;
                        for (int k = 0; k < 2; k++)
                        {
                            float yaw = (k == 0 ? yaw0 : yaw1) * Mathf.Deg2Rad;
                            cam.rect = new Rect((c * 2 + k) / (float)cols, 1f - (r + 1) / (float)rows, 1f / cols, 1f / rows);
                            cam.transform.position = center + new Vector3(Mathf.Sin(yaw), 0.12f, Mathf.Cos(yaw)) * 4.6f;
                            cam.transform.LookAt(center);
                            cam.Render();
                        }
                    }
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                RenderTexture.active = null;
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                cam.targetTexture = null;
                rt.Release();
                Object.DestroyImmediate(rt);
                foreach (var p in poses) log.Append(p.Label).Append(" | ");
            }
            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(lightGo);
            return log.ToString();
        }
    }
}
