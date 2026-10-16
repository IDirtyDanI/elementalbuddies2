using UnityEngine;

namespace ElementalBuddies
{
    // Map der laufenden Spielszene (eine Komponente je Spielszene, Wurzelobjekt „MapContext“). Laufzeitcode liest
    // map-abhängige Werte und Texte über MapContext.Current – ohne Komponente (Hauptmenü, Tests) gilt MapRegistry.Default.
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    public class MapContext : MonoBehaviour
    {
        [Tooltip("Map-Definition dieser Szene.")]
        public MapDefinitionSO Map;

        private static MapContext _instance;
        private static int _searchedFrame = -1;

        // Komponente der aktiven Spielszene (null im Menü). Sucht einmal pro Frame nach, falls Awake noch nicht lief.
        public static MapContext Instance
        {
            get
            {
                if (_instance == null && _searchedFrame != Time.frameCount)
                {
                    _searchedFrame = Time.frameCount;
                    _instance = FindAnyObjectByType<MapContext>();
                }
                return _instance;
            }
        }

        // Map der laufenden Szene, sonst die Standard-Map (Stadt)
        public static MapDefinitionSO Current
        {
            get
            {
                var c = Instance;
                return c != null && c.Map != null ? c.Map : MapRegistry.Default;
            }
        }

        void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Debug.LogWarning("MapContext: mehr als eine Komponente in der Szene – " + name + " wird ignoriert.");
                return;
            }
            _instance = this;
            if (Map == null) Debug.LogWarning("MapContext: keine Map-Definition gesetzt – es gilt " + MapRegistry.Default.Id + ".");
        }

        void OnDestroy()
        {
            if (_instance == this) _instance = null;
            _searchedFrame = -1;
        }
    }
}
