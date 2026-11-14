using UnityEngine;

namespace ElementalBuddies
{
    // Eine spielbare Map (Walddorf-Plan, Paket P0). Ersetzt die früheren Stadt-Konstanten im Laufzeitcode
    // (Mauer ±35, „Stadtmauer“-Texte, Minimap-Mitte/-Radius, Morgengrauen-Sonne, Szenenname „test“).
    // Die Feld-Standardwerte entsprechen exakt der Stadt – MapRegistry erzeugt daraus einen Ersatz, falls keine
    // Assets geladen werden können. Assets: Resources/Maps/Map_*.asset (Map_City = Stadt).
    // Lichtstimmung einer Dimension (Dimensionsriss). SiegeAtmosphere blendet sie lokal über die Belagerungs-Stimmung,
    // solange die eigene Figur in der Arena steht. Mobil-Low hat kein Post-Processing → der Look entsteht nur aus
    // Sonne, Umgebungslicht (Trilight), Nebel und Himmel. Standardwerte = Totenreich (Stadt).
    [System.Serializable]
    public class DimensionAtmosphere
    {
        [Header("Sonne / Mond")]
        public Vector3 SunEuler = new Vector3(52f, 215f, 0f);
        public Color SunColor = new Color(0.55f, 0.85f, 0.72f);
        public float SunIntensity = 1.15f;
        [Range(0f, 1f)] public float ShadowStrength = 0.75f;
        [Header("Umgebungslicht (Trilight)")]
        public Color AmbientSky = new Color(0.42f, 0.36f, 0.66f);
        public Color AmbientEquator = new Color(0.28f, 0.42f, 0.42f);
        public Color AmbientGround = new Color(0.10f, 0.13f, 0.14f);
        [Header("Nebel (Exponential²)")]
        public Color FogColor = new Color(0.10f, 0.16f, 0.17f);
        public float FogDensity = 0.016f;
        [Header("Himmel (Skybox/Procedural)")]
        public Color SkyTint = new Color(0.34f, 0.22f, 0.55f);
        public Color SkyGround = new Color(0.05f, 0.08f, 0.09f);
        public float SkyExposure = 0.32f;
        public float SkyAtmosphere = 1.4f;
        [Header("Überblendung")]
        [Tooltip("Sekunden für das Ein-/Ausblenden (die Schwarzblende des Portals verdeckt den Sprung).")]
        public float BlendTime = 0.5f;
    }

    [CreateAssetMenu(fileName = "Map_", menuName = "Elemental Buddies/Map/Map-Definition")]
    public class MapDefinitionSO : ScriptableObject
    {
        public const string CityId = "city";

        [Header("Identität")]
        [Tooltip("Stabile Id (PlayerPrefs „map“, Lobby). Nie umbenennen.")]
        public string Id = CityId;
        public string DisplayName = "Stadt";
        [Tooltip("Pfad der Spielszene (Build Settings). Der Szenenname zum Laden ergibt sich daraus.")]
        public string ScenePath = "Assets/test.unity";
        [TextArea] public string Tagline = "";
        public Sprite Preview;
        [Tooltip("Reihenfolge in Auswahl und Build Settings (aufsteigend).")]
        public int Order;
        [Tooltip("Erfolgs-Id, deren Erreichen (irgendein Rang) die Map freischaltet; leer = immer frei. Auswertung: Hauptmenü (Paket C-Menü).")]
        public string UnlockAchievementId = "";
        [Tooltip("Gegner-Theme (leer/Identität = kanonische Configs der Wellen-Assets).")]
        public EnemyThemeSO Theme;
        [Tooltip("Präfix für map-eigene Rekord-Schlüssel in PlayerPrefs (Stadt: leer = alte Schlüssel).")]
        public string RecordKeyPrefix = "";

        [Header("Geometrie")]
        [Tooltip("Halbe Kantenlänge des Mauerquadrats um den Ursprung (innen = Siedlung).")]
        public float WallHalfSize = 35f;
        [Tooltip("Tore (Welt-x/z).")]
        public Vector2[] Gates = { new Vector2(-35f, 0f), new Vector2(0f, 35f), new Vector2(0f, -35f) };
        [Tooltip("Zentraler Platz (Welt-x/z).")]
        public Vector2 Plaza = new Vector2(26f, 0f);
        [Tooltip("Welt-x/z der Minimap-Mitte.")]
        public Vector2 MinimapCenter = Vector2.zero;
        [Tooltip("Weltmeter von der Mitte bis zum Minimap-Rand.")]
        public float MinimapRadius = 68f;
        [Tooltip("Sonnenrichtung im Morgengrauen (SiegeAtmosphere).")]
        public Vector3 DawnSunEuler = new Vector3(9f, 95f, 0f);

        [Header("Texte")]
        [Tooltip("Ortsangabe innerhalb der Mauer.")]
        public string InsidePhrase = "in der Stadt";
        [Tooltip("Mauer als Ganzes, nach „vor der …“.")]
        public string WallNoun = "Stadtmauer";
        [Tooltip("Endung der Mauerseite, nach „vor der West…“ (Westmauer, Westpalisade).")]
        public string WallSideSuffix = "mauer";
        [Tooltip("Siedlung mit Artikel, klein (\"die Stadt\", \"das Dorf\").")]
        public string SettlementNoun = "die Stadt";
        [Tooltip("Banner nach einem Bosssieg.")]
        public string BossRelief = "Die Stadt atmet auf";
        [Tooltip("Morgengrauen-Sieg (Banner/Spielende, ohne Punkt).")]
        public string DawnVictoryLine = "Die Stadt hat die Nacht überstanden";
        [Tooltip("Ruf eines Buddys zum Wellenstart.")]
        public string BattleCry = "Für die Stadt!";
        [Tooltip("Optional: eigene Namen der Belagerungsstufen 0..Max (leer = Standardnamen).")]
        public string[] SiegeLevelNames = new string[0];

        [Header("Dimensionsriss (Boss-Event ab Welle 20, DimensionEvent)")]
        [Tooltip("Name der Dimension hinter den Portalen (Stadt: Totenreich, Walddorf: Kriegslager der Orks).")]
        public string DimensionName = "Totenreich";
        [Tooltip("Farbe des Risses: begehbare Portale (Fläche, Partikel, Bodenring, Lichtsäule), Randpfeil, Ziel-Panel. " +
                 "Soll sich klar vom normalen Portal-Violett abheben.")]
        public Color DimensionColor = new Color(0.3f, 1f, 0.72f);
        [Tooltip("Boss der Dimension (Rolle DimensionBoss, IsBoss, Prefab mit EnemyBrain/EnemyNet/BossBrain in der Netz-Prefab-Liste). " +
                 "Leer = erster Boss aus den Zusatzgruppen des WaveManagers (über das Map-Theme aufgelöst).")]
        public EnemyConfigSO DimensionBoss;
        [Tooltip("Arena-Prefab (Vertrag siehe DimensionArena). Leer = Platzhalter-Arena aus Grundkörpern.")]
        public GameObject DimensionArenaPrefab;
        [Tooltip("Lichtstimmung in der Arena (nur lokal für Spieler, die drin sind).")]
        public DimensionAtmosphere DimensionAtmosphere = new DimensionAtmosphere();

        // Name der Szene zum Laden (SceneManager/NGO)
        public string SceneName => System.IO.Path.GetFileNameWithoutExtension(ScenePath ?? "");

        // Siedlung groß geschrieben am Satzanfang („Die Stadt“)
        public string SettlementNounCapitalized =>
            string.IsNullOrEmpty(SettlementNoun) ? "" : char.ToUpper(SettlementNoun[0]) + SettlementNoun.Substring(1);

        public bool IsInside(Vector3 pos) => Mathf.Max(Mathf.Abs(pos.x), Mathf.Abs(pos.z)) < WallHalfSize;

        // Ortsangabe relativ zur Mauer: „in der Stadt“ oder „vor der West-/Nord-/Süd-/Ostmauer“ (dominante Achse)
        public string LocationPhrase(Vector3 pos)
        {
            if (IsInside(pos)) return InsidePhrase;
            if (Mathf.Abs(pos.x) >= Mathf.Abs(pos.z)) return pos.x < 0f ? "vor der West" + WallSideSuffix : "vor der Ost" + WallSideSuffix;
            return pos.z > 0f ? "vor der Nord" + WallSideSuffix : "vor der Süd" + WallSideSuffix;
        }

        public string OutsideWallPhrase => "vor der " + WallNoun;

        // Name einer Belagerungsstufe (map-eigen oder Standard); null = Standard verwenden
        public string SiegeLevelName(int level)
        {
            if (SiegeLevelNames == null || level < 0 || level >= SiegeLevelNames.Length) return null;
            string n = SiegeLevelNames[level];
            return string.IsNullOrEmpty(n) ? null : n;
        }
    }
}
