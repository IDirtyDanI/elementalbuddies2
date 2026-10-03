using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

namespace ElementalBuddies
{
    public class InteractionManager : MonoBehaviour
    {
        public static InteractionManager Instance { get; private set; }

        [Header("Configs")]
        public List<UnitConfigSO> UnitConfigs; 

        [Header("Settings")]
        public LayerMask FloorLayer;
        public LayerMask ObstacleLayer; 
        public Material ValidMat;
        public Material InvalidMat;
        public LayerMask BuddyLayer; // Für Auswahl/Verkaufen; leer = Layer "Buddy" bzw. alle Layer

        [Header("Range Indicator")]
        public Material RangeIndicatorMaterial; // Optional; leer = Laufzeit-Material mit "Sprites/Default"
        public float RangeIndicatorAlpha = 0.7f;

        // Ausgewählter (platzierter) Buddy für Info-Panel / Aufwerten
        public ElementalBuddy SelectedBuddy { get; private set; }
        public event System.Action<ElementalBuddy> OnBuddySelected; // null = abgewählt

        private bool _hasBuddySelection;
        private RangeIndicator _selectedRange;
        private RangeIndicator _ghostRange;

        private UnitConfigSO _selectedUnitConfig;
        private int _selectedIndex = -1;

        // Public API für die klickbare Bauleiste (Index = Hotkey - 1)
        public IReadOnlyList<UnitConfigSO> Configs => UnitConfigs;
        public int SelectedIndex => _selectedIndex;
        public event System.Action OnSelectionChanged;
        private GameObject _currentGhost;
        private Camera _mainCamera;
        
        [SerializeField] private InputActionAsset inputAsset;
        private InputAction _fireAction;
        
        private InputAction[] _buildActions;

        void Awake()
        {
            if (Instance != null && Instance != this) Destroy(gameObject);
            else Instance = this;

            _mainCamera = Camera.main;

            if (inputAsset != null)
            {
                var map = inputAsset.FindActionMap("Player");
                if (map != null)
                {
                    _fireAction = map.FindAction("Fire");
                    _buildActions = new InputAction[]
                    {
                        map.FindAction("Build1"),
                        map.FindAction("Build2"),
                        map.FindAction("Build3"),
                        map.FindAction("Build4")
                    };
                }
            }
        }

        void Start()
        {
            _selectedRange = RangeIndicator.Create("RangeIndicator (Selected)", RangeIndicatorMaterial);
            _ghostRange = RangeIndicator.Create("RangeIndicator (Ghost)", RangeIndicatorMaterial);
            _selectedRange.transform.SetParent(transform, true);
            _ghostRange.transform.SetParent(transform, true);
        }

        void OnDestroy()
        {
            if (SelectedBuddy != null) SelectedBuddy.OnLevelChanged -= RefreshSelectedRange;
        }

        void Update()
        {
            // Pause-Menü offen -> keine Eingaben (ESC übernimmt der PauseManager)
            if (PauseManager.IsPaused) return;

            // Ausgewählter Buddy zerstört (Verkauf, Tod) oder Game Over -> abwählen
            if (_hasBuddySelection && (SelectedBuddy == null || IsGameOver)) DeselectBuddy();

            HandleInput();
            UpdateGhost();
        }

        private bool IsGameOver => GameManager.Instance != null && GameManager.Instance.CurrentState == GameState.GameOver;

        private void HandleInput()
        {
            if (inputAsset == null || _buildActions == null) return;

            // Check Build Keys
            for (int i = 0; i < _buildActions.Length; i++)
            {
                if (_buildActions[i] != null && _buildActions[i].WasPressedThisFrame())
                {
                    // Debug.Log($"Build Key {i+1} pressed");
                    SelectUnit(i);
                }
            }

            // Check Click to Build
            // Klicks auf UI (z. B. Bauleiste) bauen nicht in die Welt
            bool pointerOverUI = UnityEngine.EventSystems.EventSystem.current != null && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();
            if (!pointerOverUI && _selectedUnitConfig != null && _currentGhost != null && _fireAction != null && _fireAction.WasPressedThisFrame())
            {
                _leftClickConsumedFrame = Time.frameCount;
                TryBuild();
            }
            // Linksklick ohne Ghost: Buddy auswählen (leerer Boden = abwählen)
            else if (!pointerOverUI && _currentGhost == null && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                TrySelectBuddy();
            }

            // Rechtsklick bricht nur das Platzieren ab (sonst gehört er dem Blink); Verkaufen läuft über das Info-Panel
            if (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame && _currentGhost != null)
            {
                _rightClickConsumedFrame = Time.frameCount;
                Deselect();
            }

            // ESC: erst Platzieren abbrechen, dann Buddy abwählen; sonst öffnet der PauseManager das Pause-Menü
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                if (_currentGhost != null)
                {
                    _escapeConsumedFrame = Time.frameCount;
                    Deselect();
                }
                else if (_hasBuddySelection)
                {
                    _escapeConsumedFrame = Time.frameCount;
                    DeselectBuddy();
                }
            }
        }

        // ESC wurde in diesem Frame schon verbraucht (PauseManager prüft das in LateUpdate)
        private int _escapeConsumedFrame = -1;
        public bool EscapeConsumedThisFrame => _escapeConsumedFrame == Time.frameCount;

        // ---------------- Maus-Teilung mit den Spieler-Zaubern ----------------
        // Linksklick = Arcane Ball, außer er wird hier gebraucht (UI, Platzieren, Buddy anklicken)
        private int _leftClickConsumedFrame = -1;
        private int _rightClickConsumedFrame = -1;

        private static bool PointerOverUI =>
            UnityEngine.EventSystems.EventSystem.current != null && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();

        public bool WouldConsumeLeftClick()
        {
            if (_leftClickConsumedFrame == Time.frameCount || PointerOverUI || _currentGhost != null) return true;
            return RaycastBuddy() != null;
        }

        // Rechtsklick = Blink, außer beim Platzieren (Abbrechen) oder über UI
        public bool WouldConsumeRightClick()
        {
            return _rightClickConsumedFrame == Time.frameCount || PointerOverUI || _currentGhost != null;
        }

        // Buddy unter dem Mauszeiger (gleiche Layer-Logik wie beim Verkaufen)
        private ElementalBuddy RaycastBuddy()
        {
            if (Mouse.current == null) return null;
            if (_mainCamera == null) _mainCamera = Camera.main;
            if (_mainCamera == null) return null;

            int mask = BuddyLayer.value;
            if (mask == 0) mask = LayerMask.GetMask("Buddy");
            if (mask == 0) mask = Physics.AllLayers;

            Ray ray = _mainCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
            if (!Physics.Raycast(ray, out RaycastHit hit, 100f, mask, QueryTriggerInteraction.Collide)) return null;

            return hit.collider.GetComponentInParent<ElementalBuddy>();
        }

        private void TrySelectBuddy()
        {
            // Auswahl in Bau- und Kampfphase, nicht bei Game Over / Pause / Upgrade-Screen
            if (IsGameOver || Time.timeScale <= 0f) return;

            var buddy = RaycastBuddy();
            if (buddy != null) SelectBuddy(buddy);
            else DeselectBuddy();
        }

        public void SelectBuddy(ElementalBuddy buddy)
        {
            if (buddy == null)
            {
                DeselectBuddy();
                return;
            }
            if (buddy == SelectedBuddy) return;

            if (SelectedBuddy != null) SelectedBuddy.OnLevelChanged -= RefreshSelectedRange;
            SelectedBuddy = buddy;
            _hasBuddySelection = true;
            SelectedBuddy.OnLevelChanged += RefreshSelectedRange;
            RefreshSelectedRange();
            OnBuddySelected?.Invoke(SelectedBuddy);
        }

        public void DeselectBuddy()
        {
            if (!_hasBuddySelection) return;
            if (SelectedBuddy != null) SelectedBuddy.OnLevelChanged -= RefreshSelectedRange;
            SelectedBuddy = null;
            _hasBuddySelection = false;
            if (_selectedRange != null) _selectedRange.Hide();
            OnBuddySelected?.Invoke(null);
        }

        private void RefreshSelectedRange()
        {
            if (_selectedRange == null || SelectedBuddy == null) return;
            _selectedRange.Show(SelectedBuddy.transform, SelectedBuddy.EffectiveRange,
                RangeIndicator.ElementColor(SelectedBuddy.ElementIndex, RangeIndicatorAlpha));
        }

        // Verkaufen nur in der Bauphase (und nicht während Pause/Upgrade-Screen)
        public bool CanSellNow =>
            (GameManager.Instance == null || GameManager.Instance.CurrentState == GameState.Building) && Time.timeScale > 0f;

        // Rückerstattung in Seelensplittern für einen Buddy (inkl. Aufwertungen)
        public float GetSellRefund(ElementalBuddy buddy)
        {
            if (buddy == null) return 0f;
            // Vorplatzierte Buddies ohne PaidCost: Basis-Kosten als Grundlage
            float paid = buddy.PaidCost;
            if (paid <= 0f && buddy.Config != null) paid = buddy.Config.CostOutCombat;
            return EconomyManager.Instance != null ? EconomyManager.Instance.GetRefundAmount(paid) : paid * 0.7f;
        }

        private void TrySell()
        {
            if (!CanSellNow) return;
            SellBuddy(RaycastBuddy());
        }

        private bool SellBuddy(ElementalBuddy buddy)
        {
            if (buddy == null || !CanSellNow) return false;

            if (EconomyManager.Instance != null) EconomyManager.Instance.AddShards(GetSellRefund(buddy));
            if (buddy == SelectedBuddy) DeselectBuddy();
            Destroy(buddy.gameObject);
            return true;
        }

        // UI-Helfer für das Buddy-Info-Panel
        public void SellSelected()
        {
            SellBuddy(SelectedBuddy);
        }

        public bool UpgradeSelected()
        {
            return SelectedBuddy != null && SelectedBuddy.TryUpgrade();
        }

        // Für UI-Buttons: gleiches Verhalten wie Hotkey (erneute Auswahl = abwählen)
        public void SelectUnitByIndex(int index)
        {
            SelectUnit(index);
        }

        // Aktuelle Baukosten in Seelensplittern (inkl. Kampf-Aufschlag); -1 bei ungültigem Index
        public float GetCurrentCost(int index)
        {
            if (UnitConfigs == null || index < 0 || index >= UnitConfigs.Count || UnitConfigs[index] == null) return -1f;
            float baseCost = UnitConfigs[index].CostOutCombat;
            return EconomyManager.Instance != null ? EconomyManager.Instance.GetBuildingCost(baseCost) : baseCost;
        }

        private void SelectUnit(int index)
        {
            if (UnitConfigs == null || index < 0 || index >= UnitConfigs.Count) 
            {
                Debug.LogWarning($"InteractionManager: Index {index} invalid or UnitConfigs list empty/too short!");
                return;
            }
            
            var config = UnitConfigs[index];
            if (config == null)
            {
                Debug.LogError($"InteractionManager: UnitConfig at index {index} is NULL! Assign it in Inspector.");
                return;
            }

            if (_selectedUnitConfig == config)
            {
                Deselect();
            }
            else
            {
                // Debug.Log($"Selected Unit: {config.name}");
                DeselectBuddy(); // Bauen beginnt -> Buddy-Auswahl aufheben
                ClearSelection();
                _selectedUnitConfig = config;
                _selectedIndex = index;
                CreateGhost();
                OnSelectionChanged?.Invoke();
            }
        }

        private void Deselect()
        {
            bool hadSelection = _selectedUnitConfig != null;
            ClearSelection();
            if (hadSelection) OnSelectionChanged?.Invoke();
        }

        private void ClearSelection()
        {
            _selectedUnitConfig = null;
            _selectedIndex = -1;
            if (_currentGhost != null) Destroy(_currentGhost);
            if (_ghostRange != null) _ghostRange.Hide();
        }

        private void CreateGhost()
        {
            if (_selectedUnitConfig == null || _selectedUnitConfig.Prefab == null) return;
            
            _currentGhost = Instantiate(_selectedUnitConfig.Prefab);
            
            // Disable logic components on ghost
            var behaviors = _currentGhost.GetComponentsInChildren<MonoBehaviour>();
            foreach (var b in behaviors) b.enabled = false;
            
            // Disable colliders on ghost
            var colliders = _currentGhost.GetComponentsInChildren<Collider>();
            foreach (var c in colliders) c.enabled = false;
            
            // Disable NavMeshAgent if present
            var agents = _currentGhost.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>();
            foreach (var a in agents) a.enabled = false;
        }

        private void UpdateGhost()
        {
            if (_currentGhost == null) return;

            UpdateGhostRange();

            // Safe check if Main Camera is lost
            if (_mainCamera == null) _mainCamera = Camera.main;
            if (_mainCamera == null) return;

            Vector2 mousePos = Mouse.current.position.ReadValue();
            Ray ray = _mainCamera.ScreenPointToRay(mousePos);
            
            if (Physics.Raycast(ray, out RaycastHit hit, 100f, FloorLayer))
            {
                // Snap to Grid (1m)
                Vector3 pos = hit.point;
                pos.x = Mathf.Round(pos.x);
                pos.z = Mathf.Round(pos.z);
                pos.y = hit.point.y; // Keep Y (assuming flat floor at 0 mostly, but safe to keep hit y)

                _currentGhost.transform.position = pos;

                bool isValid = ValidatePlacement(pos);
                UpdateGhostVisuals(isValid);
            }
        }

        // Reichweite des Ghosts (Stufe 1); Ghost-Buddy ist deaktiviert, die Werte-Methoden funktionieren trotzdem
        private void UpdateGhostRange()
        {
            if (_ghostRange == null) return;
            var buddy = _currentGhost.GetComponentInChildren<ElementalBuddy>(true);
            float range = buddy != null ? buddy.GetRangeAtLevel(1) : _selectedUnitConfig.Range;
            int element = buddy != null ? buddy.ElementIndex : (int)_selectedUnitConfig.Type;
            if (range > 0f) _ghostRange.Show(_currentGhost.transform, range, RangeIndicator.ElementColor(element, RangeIndicatorAlpha * 0.6f));
            else _ghostRange.Hide();
        }

        private bool ValidatePlacement(Vector3 position)
        {
            // Safety first
            if (_selectedUnitConfig == null) return false;
            if (EconomyManager.Instance == null) return false;

            // Check Buddy Slots
            if (BuddySlotManager.Instance != null && !BuddySlotManager.Instance.HasFreeSlot) return false;

            // Check Overlap
            if (Physics.CheckSphere(position, 0.45f, ObstacleLayer)) return false;

            // Check Cost
            // Ensure we catch any internal errors in GetBuildingCost too, though unlikely
            float cost = 0f;
            try
            {
                 cost = EconomyManager.Instance.GetBuildingCost(_selectedUnitConfig.CostOutCombat); 
            }
            catch (System.Exception e)
            {
                Debug.LogError($"InteractionManager: Error calculating cost: {e.Message}");
                return false;
            }

            if (!EconomyManager.Instance.CanAfford(cost)) return false;

            return true;
        }
        
        private void UpdateGhostVisuals(bool isValid)
        {
             var renderers = _currentGhost.GetComponentsInChildren<Renderer>();
             Material mat = isValid ? ValidMat : InvalidMat;
             
             if (mat != null)
             {
                 foreach(var r in renderers)
                 {
                     r.material = mat; 
                 }
             }
        }

        private void TryBuild()
        {
            if (!ValidatePlacement(_currentGhost.transform.position)) return;

            float cost = EconomyManager.Instance.GetBuildingCost(_selectedUnitConfig.CostOutCombat);
            if (EconomyManager.Instance.TrySpendShards(cost))
            {
                var go = Instantiate(_selectedUnitConfig.Prefab, _currentGhost.transform.position, Quaternion.identity);
                var buddy = go.GetComponentInChildren<ElementalBuddy>();
                if (buddy != null) buddy.PaidCost = cost;
            }
        }
    }
}