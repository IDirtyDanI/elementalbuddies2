using UnityEngine;

namespace ElementalBuddies
{
    // Zeitkurven, die alle Filme teilen (Cutscene-Dienst): Kinobalken und Titel. Gleiche Formeln wie im Intro-Film
    // (IntroCutscenePath.BarsAt/TitleAlpha), nur mit freien Zeiten.
    public static class CutsceneTiming
    {
        // Kinobalken (0 = weg, 1 = ganz drin): Einfahren 0–barsIn, Ausfahren während der Rückblende
        public static float Bars(float t, float barsIn, float returnStart, float returnDuration)
        {
            if (t < returnStart) return IntroCutscenePath.Smooth01(t / Mathf.Max(0.01f, barsIn));
            return 1f - IntroCutscenePath.Smooth01((t - returnStart) / Mathf.Max(0.01f, returnDuration));
        }

        // Titel-Deckkraft (0..1): 0,4 s einblenden ab titleIn, 0,3 s ausblenden bis titleOut
        public static float TitleAlpha(float t, float titleIn, float titleOut)
        {
            float fadeIn = IntroCutscenePath.Smooth01((t - titleIn) / 0.4f);
            float fadeOut = 1f - IntroCutscenePath.Smooth01((t - (titleOut - 0.3f)) / 0.3f);
            return Mathf.Min(fadeIn, fadeOut);
        }
    }

    // Reine Bahn-Mathematik einer „Blick-Fahrt“: von der Spielkamera (Start) zu einem Weltpunkt fliegen, ihn aus
    // festem Abstand/fester Höhe ansehen (optional mit langsamem Bogen um den Punkt) und zurück zur Spielkamera gleiten.
    // Grundlage des Schrein-Films (ShrineCutscenePath) und der allgemeinen Cutscene.PlayFocusShot (z. B. Portal-Schwenk
    // beim Dimensionsriss). Ohne MonoBehaviour/Physik, damit EditMode-Tests sie prüfen können.
    // Zeit: 0 … FlyEnd Flug (löst sich langsam von der Figur, dann schnell), FlyEnd … HoldEnd Halten/Bogen,
    // danach Rückblende (ReturnDuration) – die macht der Cutscene-Dienst mit der dann gültigen Spiel-Lage (PoseAt).
    // Stetigkeit: Der Flug ist eine Überblendung von der Start-Lage auf die (für alle t definierte) Bogen-Lage, das
    // Gewicht läuft mit Steigung 0 in 0 und 1 ein – die Bahn hat also weder Sprung noch Knick.
    public sealed class FocusShotPath
    {
        public const float ReturnArc = 0.5f;  // Anheben in der Mitte der Rückblende (m)

        // ---------------- Zeitplan (s) ----------------
        public float FlyEnd = 1.6f;
        public float HoldEnd = 3.2f;
        public float ReturnDuration = 0.8f;
        public float Duration => HoldEnd + ReturnDuration;

        // ---------------- Kamera-Parameter ----------------
        public CutscenePose Start;            // Spielkamera beim Filmstart
        public Vector3 Target;                // gefilmter Punkt (Boden)
        public float Radius = 11f;            // waagerechter Abstand der Kamera zum Punkt
        public float Height = 7f;             // Kamerahöhe über dem Punkt
        public float StartAngle;              // Kompasswinkel der Kamera um den Punkt bei FlyEnd (Grad, 0 = +Z, 90 = +X)
        public float Sweep;                   // Bogen während des Haltens (Grad, Vorzeichen = Drehsinn)
        public float LookHeight = 1.5f;       // Blickpunkt so weit über dem Punkt
        public float Apex;                    // zusätzliche Flughöhe in der Mitte des Flugs (m)
        public float GroundY;                 // Bodenhöhe: die Kamera bleibt mindestens MinClearance darüber
        public float MinClearance = 1.5f;
        public float Fov;                     // Blickwinkel beim Halten (0 = Spielkamera unverändert)

        public FocusShotPath(CutscenePose start, Vector3 target)
        {
            Start = start;
            Target = target;
            GroundY = target.y;
        }

        // Kamera so, dass sie bei FlyEnd an target + offset steht (Abstand, Höhe und Winkel aus dem Versatz)
        public static FocusShotPath FromOffset(CutscenePose start, Vector3 target, Vector3 offset)
        {
            var path = new FocusShotPath(start, target);
            path.Radius = Mathf.Max(0.5f, new Vector2(offset.x, offset.z).magnitude);
            path.Height = offset.y;
            path.StartAngle = AngleOf(offset, 180f);
            return path;
        }

        // Kompasswinkel (Grad) eines waagerechten Versatzes; fallback bei (fast) senkrechtem Versatz
        public static float AngleOf(Vector3 flat, float fallback)
        {
            return flat.x * flat.x + flat.z * flat.z > 1e-6f ? Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg : fallback;
        }

        public Vector3 LookPoint => Target + Vector3.up * LookHeight;

        // Anteil des Flugs (0 = Spielkamera, 1 = Bogen-Lage): langsames Lösen, schnelle Mitte, weiches Ankommen
        public float FlyWeight(float t)
        {
            float u = Mathf.Clamp01(t / Mathf.Max(0.01f, FlyEnd));
            float q = Mathf.Pow(u, 1.4f);
            return q * q * q * (q * (q * 6f - 15f) + 10f);
        }

        // Kompasswinkel der Kamera um den Punkt: läuft schon während des Flugs gleichmäßig, bremst zum Ende des
        // Bogens auf 0 (die Rückblende beginnt ohne Ruck)
        public float AngleAt(float t)
        {
            float v = (t - FlyEnd) / Mathf.Max(0.01f, HoldEnd - FlyEnd);
            v = Mathf.Min(v, 1f);
            float g = v < 0f ? 1.5f * v : 1.5f * v - 0.5f * v * v * v;
            return StartAngle + Sweep * g;
        }

        // Kamera-Position auf dem Bogen (für alle t definiert)
        public Vector3 OrbitPosition(float t)
        {
            float a = AngleAt(t) * Mathf.Deg2Rad;
            float y = Mathf.Max(Target.y + Height, GroundY + MinClearance);
            return new Vector3(Target.x + Mathf.Sin(a) * Radius, y, Target.z + Mathf.Cos(a) * Radius);
        }

        // Kamera-Lage zur Filmzeit t (0 … HoldEnd; danach bleibt die Endlage – die Rückblende liefert PoseAt)
        public CutscenePose Evaluate(float t)
        {
            t = Mathf.Clamp(t, 0f, HoldEnd);
            float w = FlyWeight(t);
            Vector3 orbit = OrbitPosition(t);
            Vector3 pos = Vector3.Lerp(Start.Position, orbit, w) + Vector3.up * (Apex * Mathf.Sin(w * Mathf.PI));
            pos.y = Mathf.Max(pos.y, Mathf.Min(Start.Position.y, GroundY + MinClearance));
            Quaternion look = LookRotation(LookPoint - pos);
            Quaternion rot = Quaternion.Slerp(Start.Rotation, look, IntroCutscenePath.Smooth01(w * 1.5f));
            var pose = new CutscenePose(pos, rot);
            pose.Fov = Fov > 0f && Start.Fov > 0f ? Mathf.Lerp(Start.Fov, Fov, w) : 0f;
            return pose;
        }

        // Ganzer Film inkl. Rückblende auf die Spiel-Lage game (t ≥ Duration → genau game)
        public CutscenePose PoseAt(float t, CutscenePose game)
        {
            if (t <= HoldEnd) return Evaluate(t);
            float k = (t - HoldEnd) / Mathf.Max(0.01f, ReturnDuration);
            var pose = IntroCutscenePath.Blend(Evaluate(HoldEnd), game, k, ReturnArc);
            pose.Fov = game.Fov;
            return pose;
        }

        private static Quaternion LookRotation(Vector3 dir)
        {
            if (dir.sqrMagnitude < 1e-6f) dir = Vector3.forward;
            return Quaternion.LookRotation(dir.normalized, Vector3.up);
        }
    }

    // Schrein-Film (wiki: Design/Dimensionsriss-Plan, Abschnitt A): ≈ 5 s beim ersten Erwachen eines Schreins.
    // 0–0,6 s Kinobalken fahren ein, die Kamera löst sich von der Figur; 0,6–2,0 s Flug zum Schrein (hoch, schnell);
    // 2,0–4,2 s langsamer Bogen (≈ 40°) um den Schrein mit der Lichtsäule im Bild, dazu der Titel; 4,2–5,0 s Rückblende.
    public static class ShrineCutscenePath
    {
        public const float Duration = 5f;
        public const float BarsIn = 0.6f;
        public const float FlyEnd = 2f;
        public const float ArcEnd = 4.2f;
        public const float ReturnDuration = Duration - ArcEnd;
        public const float SweepDegrees = 40f;
        public const float TitleIn = 1.9f, TitleOut = 4.2f;
        public const float SoundTime = 1.6f;      // Ton kurz vor der Ankunft
        public const float FocusFrom = 0.9f;      // ab hier gilt der Schrein als gefilmter Punkt (Verdeckungs-Fader)

        public const float DefaultRadius = 11f;
        public const float DefaultHeight = 6.5f;
        public const float LookHeight = 2.2f;

        // midAngle: Kompasswinkel (Grad), um den der Bogen liegt – die Kamera fährt von midAngle − 20° nach + 20°
        // (sign < 0: umgekehrt). start: Spielkamera beim Filmstart.
        public static FocusShotPath Create(CutscenePose start, Vector3 shrine, float midAngle, float sign = 1f,
            float radius = DefaultRadius, float height = DefaultHeight)
        {
            float s = sign < 0f ? -1f : 1f;
            var path = new FocusShotPath(start, shrine)
            {
                FlyEnd = FlyEnd,
                HoldEnd = ArcEnd,
                ReturnDuration = ReturnDuration,
                Radius = radius,
                Height = height,
                Sweep = s * SweepDegrees,
                StartAngle = midAngle - s * SweepDegrees * 0.5f,
                LookHeight = LookHeight,
            };
            path.Apex = ApexFor(start.Position, shrine);
            return path;
        }

        // Anflug „hoch“: je weiter der Weg, desto höher der Bogen (gedeckelt)
        public static float ApexFor(Vector3 from, Vector3 to)
        {
            float d = new Vector2(to.x - from.x, to.z - from.z).magnitude;
            return Mathf.Clamp(d * 0.12f, 0f, 6f);
        }

        // Winkel, aus dem die Spielkamera auf den Schrein blickt (Kamera steht in dieser Richtung vom Schrein aus)
        public static float ApproachAngle(CutscenePose start, Vector3 shrine)
        {
            Vector3 back = -(start.Rotation * Vector3.forward);
            return FocusShotPath.AngleOf(start.Position - shrine, FocusShotPath.AngleOf(back, 180f));
        }
    }
}
