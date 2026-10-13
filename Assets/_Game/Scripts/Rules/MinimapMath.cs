using UnityEngine;

namespace ElementalBuddies
{
    // Reine Rechenregeln der Minimap (Welt → Karte). Eigene Assembly (ElementalBuddies.Rules), damit EditMode-Tests
    // sie ohne Assembly-CSharp prüfen können. Konvention: Norden = Welt +Z = oben auf der Karte, Osten = +X = rechts;
    // die Karte dreht sich nicht mit.
    public static class MinimapMath
    {
        // Weltposition (x/z) → Kartenpunkt relativ zur Kartenmitte (UI-Einheiten).
        // worldCenter = Welt-x/z der Kartenmitte, worldRadius = Weltmeter vom Mittelpunkt bis zum Kartenrand,
        // mapRadius = Radius der Karte in UI-Einheiten.
        public static Vector2 WorldToMap(Vector3 world, Vector2 worldCenter, float worldRadius, float mapRadius)
        {
            if (worldRadius <= 0f) return Vector2.zero;
            float s = mapRadius / worldRadius;
            return new Vector2((world.x - worldCenter.x) * s, (world.z - worldCenter.y) * s);
        }

        // Kartenpunkt → Weltposition (y = 0); Umkehrung von WorldToMap
        public static Vector3 MapToWorld(Vector2 map, Vector2 worldCenter, float worldRadius, float mapRadius)
        {
            if (mapRadius <= 0f) return new Vector3(worldCenter.x, 0f, worldCenter.y);
            float s = worldRadius / mapRadius;
            return new Vector3(worldCenter.x + map.x * s, 0f, worldCenter.y + map.y * s);
        }

        // Punkt auf den Kreis mit Radius radius begrenzen (Marker außerhalb der Karte bleiben am Rand sichtbar)
        public static Vector2 ClampToCircle(Vector2 p, float radius, out bool clamped)
        {
            radius = Mathf.Max(0f, radius);
            float m = p.magnitude;
            clamped = m > radius;
            if (!clamped) return p;
            return m > 1e-6f ? p * (radius / m) : Vector2.zero;
        }

        // Blickrichtung (Welt) → Z-Rotation eines nach oben zeigenden Pfeil-Sprites in Grad
        // (Norden 0, Osten −90, Süden ±180, Westen 90). Ohne horizontale Richtung → 0.
        public static float HeadingToRotation(Vector3 forward)
        {
            if (forward.x * forward.x + forward.z * forward.z < 1e-8f) return 0f;
            return -Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
        }

        // Weltmeter, die die Karte vom Mittelpunkt bis zum Rand zeigen muss, damit alle Punkte (x/z) inklusive
        // margin Metern Rand hineinpassen
        public static float RadiusToFit(Vector2 worldCenter, Vector2[] points, float margin)
        {
            float r = 0f;
            if (points != null)
                foreach (var p in points) r = Mathf.Max(r, (p - worldCenter).magnitude);
            return r + Mathf.Max(0f, margin);
        }
    }
}
