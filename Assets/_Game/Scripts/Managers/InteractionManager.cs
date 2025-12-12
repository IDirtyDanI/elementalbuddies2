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

        private UnitConfigSO _selectedUnitConfig;
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

        void Update()
        {
            HandleInput();
            UpdateGhost();
        }

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
            if (_selectedUnitConfig != null && _currentGhost != null && _fireAction != null && _fireAction.WasPressedThisFrame())
            {
                TryBuild();
            }
        }

        private void SelectUnit(int index)
        {
            if (index < 0 || index >= UnitConfigs.Count) 
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
                Deselect();
                _selectedUnitConfig = config;
                CreateGhost();
            }
        }

        private void Deselect()
        {
            _selectedUnitConfig = null;
            if (_currentGhost != null) Destroy(_currentGhost);
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

        private bool ValidatePlacement(Vector3 position)
        {
            // Safety first
            if (_selectedUnitConfig == null) return false;
            if (EconomyManager.Instance == null) return false;

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

            if (EconomyManager.Instance.CurrentMana < cost) return false;

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
            if (EconomyManager.Instance.TrySpendMana(cost))
            {
                Instantiate(_selectedUnitConfig.Prefab, _currentGhost.transform.position, Quaternion.identity);
            }
        }
    }
}