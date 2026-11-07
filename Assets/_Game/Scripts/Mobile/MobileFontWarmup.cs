using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace ElementalBuddies
{
    // Schrift-Atlanten vorfüllen (Tab M11, 2026-11-02): Die Spielschriften (Alegreya Sans, MedievalSharp) sind dynamische
    // TMP-Schriften und fast leer. Zeigt ein Text ein Zeichen zum ersten Mal, fügt TextMeshPro die Glyphe hinzu und liest
    // dabei die OpenType-Tabellen (GPOS/Ligaturen) – laut Profiler 189 ms pro Fall auf dem Gerät (Hänger mitten in der
    // Welle). Deshalb beim Laden jeder Szene alle geladenen dynamischen Schriften einmal mit dem üblichen Zeichensatz füllen.
    // Nur Touch-Modus auf dem Gerät (MobileQuality); im Editor nicht, sonst landen die Glyphen in den Font-Assets.
    public static class MobileFontWarmup
    {
        // ASCII, Latin-1 (Umlaute, ß, °, ×, ·) und die Typografie der Spieltexte (deutsche Anführungszeichen, Striche,
        // Minuszeichen, Pfeile, Auslassung, schmale Leerzeichen)
        private const string Extra = "„“‚‘’”–—−…•→←↑↓€‰  ";

        private static readonly HashSet<int> _done = new HashSet<int>();
        private static uint[] _charset;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _done.Clear();

        public static void Run()
        {
            if (Application.isEditor) return;
            if (_charset == null) _charset = BuildCharset();
            float t0 = Time.realtimeSinceStartup;
            int fonts = 0;
            foreach (var f in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
            {
                if (f == null || f.atlasPopulationMode != AtlasPopulationMode.Dynamic || !_done.Add(f.GetInstanceID())) continue;
                f.TryAddCharacters(_charset, out _, true);
                fonts++;
            }
            if (fonts > 0) Debug.Log($"[MobileFontWarmup] {fonts} Schriften mit {_charset.Length} Zeichen gefüllt ({(Time.realtimeSinceStartup - t0) * 1000f:0} ms)");
        }

        private static uint[] BuildCharset()
        {
            var list = new List<uint>();
            for (uint c = 0x20; c <= 0x7E; c++) list.Add(c);
            for (uint c = 0xA0; c <= 0xFF; c++) list.Add(c);
            foreach (char c in Extra) list.Add(c);
            return list.ToArray();
        }
    }
}
