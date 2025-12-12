using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace ElementalBuddies
{
    public class UpgradeCardUI : MonoBehaviour
    {
        public TextMeshProUGUI TitleText;
        public TextMeshProUGUI DescriptionText;
        public Image IconImage;
        public Button SelectButton;

        private UpgradeDefinitionSO _data;

        public void Setup(UpgradeDefinitionSO data)
        {
            _data = data;
            if (TitleText) TitleText.text = data.Title;
            if (DescriptionText) DescriptionText.text = data.Description;
            if (IconImage && data.Icon) IconImage.sprite = data.Icon;
            
            SelectButton.onClick.RemoveAllListeners();
            SelectButton.onClick.AddListener(OnClick);
        }

        private void OnClick()
        {
            UpgradeManager.Instance.SelectUpgrade(_data);
        }
    }
}