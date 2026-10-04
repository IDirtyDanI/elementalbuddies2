using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace ElementalBuddies
{
    // Boss-HP-Leiste im HUD: zeigt den ältesten lebenden Boss (EnemyBrain.ActiveBosses), stirbt er, den nächsten.
    // Ohne Boss ist Root ausgeblendet. Fill: Image (Filled oder per anchorMax), DamageChip: verzögerte Leiste.
    public class BossHealthBarUI : MonoBehaviour
    {
        [Tooltip("Wird ein-/ausgeblendet (nicht dieses Objekt selbst, sonst läuft Update nicht).")]
        public GameObject Root;
        public TMP_Text NameText;
        [Tooltip("HP-Füllung: Image-Typ Filled (fillAmount) oder ein gestrecktes Image (anchorMax.x).")]
        public Image Fill;
        [Tooltip("Optional: verzögerte helle Leiste hinter der Füllung (anchorMax.x).")]
        public RectTransform DamageChip;
        [Tooltip("Optional: Rahmen, eingefärbt mit Config.ThemeColor.")]
        public Image Frame;
        [Tooltip("Füllung zusätzlich mit ThemeColor färben.")]
        public bool TintFill = false;
        public float ChipDelay = 0.4f;
        public float ChipSpeed = 0.8f;

        private EnemyBrain _boss;
        private float _chip = 1f, _last = 1f, _chipHoldUntil;

        void OnEnable()
        {
            EnemyBrain.OnBossSpawned += HandleBossChanged;
            EnemyBrain.OnEnemyKilled += HandleBossChanged;
            Refresh();
        }

        void OnDisable()
        {
            EnemyBrain.OnBossSpawned -= HandleBossChanged;
            EnemyBrain.OnEnemyKilled -= HandleBossChanged;
        }

        private void HandleBossChanged(EnemyBrain e)
        {
            if (e == null || e.IsBoss) Refresh();
        }

        private EnemyBrain FindBoss()
        {
            var list = EnemyBrain.ActiveBosses;
            for (int i = 0; i < list.Count; i++)
                if (list[i] != null && !list[i].IsDead) return list[i];
            return null;
        }

        private void Refresh()
        {
            var boss = FindBoss();
            if (boss != _boss)
            {
                _boss = boss;
                _chip = _last = 1f;
                if (_boss != null) Bind(_boss);
            }
            if (Root != null) Root.SetActive(_boss != null);
        }

        private void Bind(EnemyBrain boss)
        {
            if (NameText != null) NameText.text = boss.DisplayName;
            Color theme = boss.Config != null ? boss.Config.ThemeColor : Color.white;
            if (Frame != null) Frame.color = theme;
            if (TintFill && Fill != null) Fill.color = theme;
            if (boss.MaxHP > 0f) _chip = _last = Mathf.Clamp01(boss.CurrentHP / boss.MaxHP);
            SetFill(_last);
            SetChip(_chip);
        }

        void Update()
        {
            if (_boss == null || _boss.IsDead) { Refresh(); if (_boss == null) return; }

            float hp01 = _boss.MaxHP > 0f ? Mathf.Clamp01(_boss.CurrentHP / _boss.MaxHP) : 1f;
            if (hp01 < _last) _chipHoldUntil = Time.time + ChipDelay;
            _last = hp01;
            if (Time.time >= _chipHoldUntil) _chip = Mathf.MoveTowards(_chip, hp01, ChipSpeed * Time.deltaTime);
            if (_chip < hp01) _chip = hp01;
            SetFill(hp01);
            SetChip(_chip);
        }

        private void SetFill(float v)
        {
            if (Fill == null) return;
            if (Fill.type == Image.Type.Filled) Fill.fillAmount = v;
            else Fill.rectTransform.anchorMax = new Vector2(v, Fill.rectTransform.anchorMax.y);
        }

        private void SetChip(float v)
        {
            if (DamageChip != null) DamageChip.anchorMax = new Vector2(v, DamageChip.anchorMax.y);
        }
    }
}
