using System.Collections.Generic;
using UnityEngine;

namespace ElementalBuddies
{
    // Fähigkeiten-Satz eines Champions. Liegt (pro Klasse eine Komponente) auf dem Player; PlayerAbilities
    // aktiviert nur das Kit der gewählten Klasse und kümmert sich um Eingabe, Abklingzeiten, Aufladungen,
    // Mana und Freischaltung. Das Kit liefert Werte, Texte, Icons und führt die Fähigkeiten aus.
    public abstract class ChampionKit : MonoBehaviour
    {
        [System.Serializable]
        public class AbilityDisplay
        {
            public AbilityId Id;
            public string Name;
            [Tooltip("Icon in der Fähigkeitenleiste (leer = Icon aus der ChampionDefinition, sonst Platzhalter der Leiste).")]
            public Sprite Icon;
        }

        [Header("Anzeige")]
        public string DisplayName;
        [Tooltip("Optional: Menü-Daten (Porträt, Fähigkeiten-Icons als Fallback).")]
        public ChampionDefinitionSO Definition;
        [Tooltip("Name + Icon pro Fähigkeit – die eine Stelle für Leisten-Icons dieses Champions.")]
        public List<AbilityDisplay> Abilities = new List<AbilityDisplay>();

        protected PlayerAbilities Owner;
        protected PlayerController Controller;
        protected CharacterController Character;
        protected PlayerStats Stats;

        public abstract ChampionClass Class { get; }

        // Welche Fähigkeit liegt auf welcher Taste
        public abstract AbilityId GetAbility(AbilitySlot slot);

        public abstract float GetCooldown(AbilityId id);
        public abstract float GetManaCost(AbilityId id);

        // Beschreibung mit Live-Werten (Rich-Text, Hervorhebung über Hi()). dm = Schadens-Multiplikator.
        public abstract string Describe(AbilityId id, float dm);

        // Fähigkeit ausführen. Kosten und Abklingzeit hat PlayerAbilities schon verbucht.
        public abstract void Cast(AbilityId id, SpellCastContext ctx);

        // ---------------- Optionale Erweiterungen ----------------

        // > 1: Fähigkeit mit Aufladungen (z. B. Rolle). Abklingzeit = Wiederaufladezeit pro Ladung.
        public virtual int GetMaxCharges(AbilityId id) => 1;
        // Kurze Sperre zwischen zwei Ladungen
        public virtual float GetChargeLockout(AbilityId id) => 0.3f;

        // Gehaltene Fähigkeit (z. B. Schildblock): Cast = Beginn, SetHeld(false) beim Loslassen
        public virtual bool IsHoldAbility(AbilityId id) => false;
        public virtual void SetHeld(AbilityId id, bool held) { }
        // Für die Leiste: Fähigkeit gerade aktiv (gehalten / läuft)
        public virtual bool IsActive(AbilityId id) => false;

        // Primärangriff wiederholt sich, solange die Taste gehalten wird
        public virtual bool AutoRepeatPrimary => false;

        // Zusätzliche Bedingung (z. B. kein Angriff während des Blocks / der Rolle)
        public virtual bool CanCast(AbilityId id) => true;

        // Mindest-Mana zum Auslösen (Standard = Kosten)
        public virtual float GetRequiredMana(AbilityId id) => GetManaCost(id);

        // Text unter dem Slot (Standard: Mana-Kosten, leer bei 0)
        public virtual string GetManaLabel(AbilityId id)
        {
            float cost = GetManaCost(id);
            return cost > 0f ? Mathf.CeilToInt(cost).ToString() : "";
        }

        // Zeile „Mana • Abklingzeit" im Tooltip
        public virtual string GetCostLine(AbilityId id)
        {
            float cost = GetManaCost(id);
            string mana = cost > 0f ? $"{Mathf.CeilToInt(cost)} Mana" : "kein Mana";
            float cd = Owner != null ? Owner.GetCooldownDuration(id) : GetCooldown(id);
            return $"{ManaColor}{mana}</color>   •   {Fmt(cd)} s Abklingzeit";
        }

        // Eingehenden Schaden verändern (Block, Schadensreduktion). Rückgabe = verbleibender Schaden.
        public virtual float ModifyIncomingDamage(float amount, Vector3 sourcePosition, bool hasSource) => amount;

        // Animator-Trigger für eine eben gewirkte Fähigkeit (null = keiner)
        public virtual string GetAnimTrigger(AbilityId id) => null;
        // Laufende Animator-Zustände (z. B. Bool "Block") setzen
        public virtual void UpdateAnimator(Animator animator, ChampionVisual visual) { }

        // Bewegungs-Modifikator (Block = langsamer)
        public virtual float MoveSpeedMultiplier => 1f;

        // Aufruf durch PlayerAbilities beim Aktivieren / Deaktivieren (Klassenwechsel)
        public virtual void Initialize(PlayerAbilities owner)
        {
            Owner = owner;
            Controller = owner.GetComponent<PlayerController>();
            Character = owner.GetComponent<CharacterController>();
            Stats = owner.GetComponent<PlayerStats>();
        }

        public virtual void OnActivated() { }
        public virtual void OnDeactivated() { }

        // ---------------- Anzeige-Helfer ----------------

        public virtual string GetName(AbilityId id)
        {
            foreach (var a in Abilities)
                if (a != null && a.Id == id && !string.IsNullOrEmpty(a.Name)) return a.Name;
            return id.ToString();
        }

        public virtual Sprite GetIcon(AbilityId id)
        {
            foreach (var a in Abilities)
                if (a != null && a.Id == id && a.Icon != null) return a.Icon;
            if (Definition != null)
            {
                string key = KeyOf(id);
                foreach (var info in Definition.Abilities)
                    if (info != null && info.Icon != null && (info.Name == GetName(id) || info.Key == key)) return info.Icon;
            }
            return null;
        }

        // Kurzbeschriftung der Taste, auf der die Fähigkeit liegt
        public string KeyOf(AbilityId id)
        {
            for (int i = 0; i < AbilitySlots.Count; i++)
                if (GetAbility((AbilitySlot)i) == id) return AbilitySlots.ShortKey((AbilitySlot)i);
            return "";
        }

        public AbilitySlot SlotOf(AbilityId id)
        {
            for (int i = 0; i < AbilitySlots.Count; i++)
                if (GetAbility((AbilitySlot)i) == id) return (AbilitySlot)i;
            return AbilitySlot.Primary;
        }

        protected const string ManaColor = "<color=#2f5f9e>";
        protected const string HiOpen = "<color=#8a2a12><b>";
        protected const string HiClose = "</b></color>";
        public static string Hi(float value) => HiOpen + Fmt(value) + HiClose;
        public static string Fmt(float v)
        {
            return Mathf.Approximately(v, Mathf.Round(v)) ? Mathf.RoundToInt(v).ToString() : v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        }

        // ---------------- Gemeinsame Helfer für Kits ----------------

        protected Vector3 Ground(Vector3 p)
        {
            p.y = Owner != null ? Owner.GetGroundHeight(p, p.y) : p.y;
            return p;
        }
    }
}
