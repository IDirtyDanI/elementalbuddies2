using UnityEngine;

namespace ElementalBuddies
{
    // Reine Rechenregeln der Mobil-UI (Paket U, Branch feature/mobile): Ziel-Skalierung der Canvases nach Bildschirm-DPI und
    // Geräteklasse, sicherer Bereich (Notch, Dynamic Island, Home-Indikator), Einpassen großer Panels und Breite der Top-Bar.
    // Eigene Assembly (ElementalBuddies.Rules), damit EditMode-Tests sie prüfen können. Gilt nur im Touch-Modus.
    //
    // Skalierungsregel: Das HUD ist für 2400 × 1350 Einheiten (Match 0,5) gebaut. Unitys CanvasScaler ergibt daraus die
    // Grund-Skalierung s0 (Pixel je Einheit). Für Touch soll eine Einheit physisch eine Mindestgröße haben (mm je Einheit,
    // Telefon kleiner als Tablet, weil man es näher hält – dazwischen weich überblendet nach Diagonale). Daraus folgt eine
    // Ziel-Skalierung sTarget = mm · dpi / 25,4. Sie wird begrenzt, damit der sichere Bereich mindestens
    // MinSafeHeightUnits × MinSafeWidthUnits Einheiten groß bleibt (sonst passen HUD-Layouts nicht mehr). Der
    // Verstärkungsfaktor boost = sTarget / s0 liegt zwischen 1 (nie kleiner als auf dem PC) und MaxBoost; der
    // CanvasScaler bekommt dann die Referenzauflösung / boost.
    //
    // Vorgerechnet (Insets iPhone quer ≈ 62 pt links/rechts, 21 pt unten bei 3×):
    //   iPhone 16 Pro Max 2868 × 1320, 460 dpi, sicher 2496 × 1257: s0 1,081, Diagonale 6,9″, sTarget 1,60,
    //     Layout-Grenze 1257 / 860 = 1,46 → s 1,46, boost 1,35 → Canvas 1962 × 903, sicher 1708 × 860 Einheiten,
    //     0,081 mm je Einheit (PC 24″ 1080p: 0,22 mm, iPhone ohne Anpassung: 0,060 mm), 48 dp ≈ 94 Einheiten.
    //   Lenovo Tab M11 1920 × 1200, ≈ 206 dpi: s0 0,843, Diagonale 11″, sTarget 0,93, boost 1,11 → Canvas 2058 × 1287,
    //     0,115 mm je Einheit, 48 dp ≈ 66 Einheiten. Meldet Android 240 dpi: boost 1,23 (Deckel 1,2…1,27 je Diagonale).
    public static class MobileUiMath
    {
        // Entwurfs-Referenz des HUD (Canvas.prefab)
        public const float DesignRefWidth = 2400f, DesignRefHeight = 1350f, DesignMatch = 0.5f;

        // Physische Zielgröße einer Canvas-Einheit (mm) – Telefon (≤ PhoneDiagonal) bzw. Tablet (≥ TabletDiagonal)
        public const float PhoneMmPerUnit = 0.085f, TabletMmPerUnit = 0.115f;
        public const float PhoneDiagonal = 6.5f, TabletDiagonal = 10f;
        public const float PhoneMaxBoost = 1.6f, TabletMaxBoost = 1.2f;

        // Mindestgröße des sicheren Bereichs in Canvas-Einheiten (HUD: Top-Bar + Knopf + zwei Ziel-Panels, Kartenladen)
        public const float MinSafeHeightUnits = 860f, MinSafeWidthUnits = 1600f;

        // Langes Drücken (Tooltips): Haltezeit und erlaubtes Verrutschen
        public const float LongPressSeconds = 0.4f;
        public const float LongPressSlopDp = 14f;

        // Unitys CanvasScaler (Scale With Screen Size, Expand/Shrink nicht): logarithmisch gemittelt
        public static float CanvasScale(float screenW, float screenH, float refW, float refH, float match)
        {
            if (screenW <= 0f || screenH <= 0f || refW <= 0f || refH <= 0f) return 1f;
            float logW = Mathf.Log(screenW / refW, 2f);
            float logH = Mathf.Log(screenH / refH, 2f);
            return Mathf.Pow(2f, Mathf.Lerp(logW, logH, Mathf.Clamp01(match)));
        }

        public static float DiagonalInches(float screenW, float screenH, float dpi)
        {
            if (dpi <= 1f) return 0f;
            float w = screenW / dpi, h = screenH / dpi;
            return Mathf.Sqrt(w * w + h * h);
        }

        // 0 = Telefon, 1 = Tablet (weich dazwischen)
        public static float TabletBlend(float diagonalInches) =>
            Mathf.InverseLerp(PhoneDiagonal, TabletDiagonal, diagonalInches);

        // Ziel-Skalierung (Pixel je Einheit der Entwurfs-Referenz) für Touch
        public static float TargetScale(float screenW, float screenH, float safeW, float safeH, float dpi)
        {
            float t = TabletBlend(DiagonalInches(screenW, screenH, dpi));
            float mm = Mathf.Lerp(PhoneMmPerUnit, TabletMmPerUnit, t);
            float target = mm * Mathf.Max(1f, dpi) / 25.4f;
            float layout = Mathf.Min(Mathf.Max(1f, safeH) / MinSafeHeightUnits, Mathf.Max(1f, safeW) / MinSafeWidthUnits);
            return Mathf.Min(target, layout);
        }

        // Verstärkung gegenüber der normalen Canvas-Skalierung (1 = wie auf dem PC)
        public static float Boost(float screenW, float screenH, float safeW, float safeH, float dpi)
        {
            if (screenW <= 0f || screenH <= 0f) return 1f;
            float s0 = CanvasScale(screenW, screenH, DesignRefWidth, DesignRefHeight, DesignMatch);
            float t = TabletBlend(DiagonalInches(screenW, screenH, dpi));
            float maxBoost = Mathf.Lerp(PhoneMaxBoost, TabletMaxBoost, t);
            float s = TargetScale(screenW, screenH, safeW, safeH, dpi);
            return Mathf.Clamp(s / Mathf.Max(0.0001f, s0), 1f, maxBoost);
        }

        // Referenzauflösung für den CanvasScaler: kleiner = alles größer
        public static Vector2 BoostedReference(Vector2 reference, float boost)
        {
            float b = Mathf.Max(0.01f, boost);
            return new Vector2(reference.x / b, reference.y / b);
        }

        // Ränder des sicheren Bereichs in Pixeln: x = links, y = unten, z = rechts, w = oben (nie negativ)
        public static Vector4 Insets(float screenW, float screenH, Rect safe)
        {
            if (safe.width <= 0f || safe.height <= 0f) return Vector4.zero;
            return new Vector4(
                Mathf.Max(0f, safe.xMin),
                Mathf.Max(0f, safe.yMin),
                Mathf.Max(0f, screenW - safe.xMax),
                Mathf.Max(0f, screenH - safe.yMax));
        }

        // Faktor, mit dem ein mittig verankerter Inhalt (halbe Ausdehnung need) in den Platz avail passt (≤ 1, ≥ min)
        public static float FitScale(Vector2 needHalf, Vector2 availHalf, float min = 0.5f)
        {
            float f = 1f;
            if (needHalf.x > 0.01f) f = Mathf.Min(f, availHalf.x / needHalf.x);
            if (needHalf.y > 0.01f) f = Mathf.Min(f, availHalf.y / needHalf.y);
            return Mathf.Clamp(f, Mathf.Min(min, 1f), 1f);
        }

        // Nötige Breite einer Leiste mit drei Gruppen (links verankert bis leftExtent, mittig von centerMin bis centerMax
        // relativ zur Mitte, rechts verankert bis rightExtent vom rechten Rand), damit sich keine Gruppen berühren
        public static float BarRequiredWidth(float leftExtent, float centerMin, float centerMax, float rightExtent, float gap)
        {
            float a = 2f * (leftExtent + gap - centerMin);
            float b = 2f * (centerMax + gap + rightExtent);
            return Mathf.Max(0f, Mathf.Max(a, b));
        }

        // Skalierung der Leiste, damit sie in available passt (≤ 1)
        public static float BarFit(float required, float available, float min = 0.6f)
        {
            if (required <= 0.01f || available >= required) return 1f;
            return Mathf.Clamp(available / required, Mathf.Min(min, 1f), 1f);
        }

        // dp → Canvas-Einheiten bei gegebener Canvas-Skalierung (Pixel je Einheit)
        public static float DpToUnits(float dp, float dpi, float canvasScale) =>
            dp * Mathf.Max(1f, dpi) / 160f / Mathf.Max(0.0001f, canvasScale);

        // Langes Drücken erkannt? (Haltezeit erreicht, Finger kaum bewegt)
        public static bool IsLongPress(float heldSeconds, float movedPixels, float dpi) =>
            heldSeconds >= LongPressSeconds && movedPixels <= LongPressSlopDp * Mathf.Max(1f, dpi) / 160f;

        public static bool MovedTooFar(float movedPixels, float dpi) =>
            movedPixels > LongPressSlopDp * Mathf.Max(1f, dpi) / 160f;

        // Touch-Trefferfläche: wie weit darf ein Rechteck r (Pixel) je Seite wachsen, um mindestens minSize groß zu werden,
        // ohne in andere Bedienelemente zu wachsen? Jede Seite nimmt höchstens die halbe Lücke zum Nachbarn, damit sich zwei
        // vergrößerte Nachbarn nie überschneiden. Rückgabe: x = links, y = unten, z = rechts, w = oben (≥ 0).
        // Rechtecke, die r überlappen (z. B. Eltern/Kinder), begrenzen nicht.
        public static Vector4 TouchPadding(Rect r, Vector2 minSize, Rect[] others, int count)
        {
            float gx = Mathf.Max(0f, (minSize.x - r.width) * 0.5f);
            float gy = Mathf.Max(0f, (minSize.y - r.height) * 0.5f);
            if (gx <= 0f && gy <= 0f) return Vector4.zero;
            float left = gx, right = gx, down = gy, up = gy;
            if (others != null)
            {
                count = Mathf.Min(count, others.Length);
                for (int i = 0; i < count; i++)
                {
                    Rect q = others[i];
                    if (q.width <= 0f || q.height <= 0f || q.Overlaps(r)) continue;
                    bool rowOverlap = q.yMax > r.yMin - gy && q.yMin < r.yMax + gy;
                    bool colOverlap = q.xMax > r.xMin - gx && q.xMin < r.xMax + gx;
                    if (rowOverlap)
                    {
                        if (q.xMax <= r.xMin) left = Mathf.Min(left, (r.xMin - q.xMax) * 0.5f);
                        else if (q.xMin >= r.xMax) right = Mathf.Min(right, (q.xMin - r.xMax) * 0.5f);
                    }
                    if (colOverlap)
                    {
                        if (q.yMax <= r.yMin) down = Mathf.Min(down, (r.yMin - q.yMax) * 0.5f);
                        else if (q.yMin >= r.yMax) up = Mathf.Min(up, (q.yMin - r.yMax) * 0.5f);
                    }
                }
            }
            return new Vector4(Mathf.Max(0f, left), Mathf.Max(0f, down), Mathf.Max(0f, right), Mathf.Max(0f, up));
        }
    }
}
