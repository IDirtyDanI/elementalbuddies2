using UnityEngine;
using System.Collections.Generic;

namespace ElementalBuddies
{
    public class UpgradeScreenUI : MonoBehaviour
    {
        public GameObject Panel; // The whole popup
        public Transform CardsContainer;
        public GameObject CardPrefab;

        void Start()
        {
            if (UpgradeManager.Instance != null)
            {
                UpgradeManager.Instance.OnUpgradesAvailable += ShowUpgrades;
                UpgradeManager.Instance.OnUpgradeSelected += HideUpgrades;
            }
            if (Panel != null) Panel.SetActive(false);
        }

        void OnDestroy()
        {
             if (UpgradeManager.Instance != null)
            {
                UpgradeManager.Instance.OnUpgradesAvailable -= ShowUpgrades;
                UpgradeManager.Instance.OnUpgradeSelected -= HideUpgrades;
            }
        }

        private void ShowUpgrades(List<UpgradeDefinitionSO> upgrades)
        {
            if (Panel != null) Panel.SetActive(true);
            
            // Clear old cards
            foreach (Transform child in CardsContainer)
            {
                Destroy(child.gameObject);
            }

            // Create new cards
            foreach (var up in upgrades)
            {
                GameObject go = Instantiate(CardPrefab, CardsContainer);
                var ui = go.GetComponent<UpgradeCardUI>();
                if (ui != null) ui.Setup(up);
            }
        }

        private void HideUpgrades()
        {
            if (Panel != null) Panel.SetActive(false);
        }
    }
}