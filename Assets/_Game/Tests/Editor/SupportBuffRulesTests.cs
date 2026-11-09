using NUnit.Framework;

namespace ElementalBuddies.Tests
{
    // Unterstützungs-Buffs des Schamanen (Konzept §2 „Stapeln“): stärkster gilt, Dauer erneuern, nicht stapeln
    public class SupportBuffRulesTests
    {
        private const float Eps = 0.0001f;

        [Test]
        public void NeuerBuff_GiltBisEnde()
        {
            var b = SupportBuffRules.Apply(default, 0.25f, 10f, 5f);
            Assert.AreEqual(0.25f, SupportBuffRules.Current(b, 5f), Eps);
            Assert.AreEqual(0.25f, SupportBuffRules.Current(b, 14.9f), Eps);
            Assert.AreEqual(0f, SupportBuffRules.Current(b, 15f), Eps);
        }

        [Test]
        public void GleicherBuff_StapeltNicht_ErneuertDauer()
        {
            var b = SupportBuffRules.Apply(default, 0.25f, 1f, 0f);
            b = SupportBuffRules.Apply(b, 0.25f, 1f, 0.5f);
            Assert.AreEqual(0.25f, SupportBuffRules.Current(b, 0.6f), Eps, "kein Stapeln");
            Assert.AreEqual(1.5f, b.Until, Eps, "Dauer erneuert");
        }

        [Test]
        public void StaerkererBuff_ErsetztSchwaecheren()
        {
            var b = SupportBuffRules.Apply(default, 0.10f, 5f, 0f);
            b = SupportBuffRules.Apply(b, 0.30f, 2f, 1f);
            Assert.AreEqual(0.30f, SupportBuffRules.Current(b, 1.5f), Eps);
            Assert.AreEqual(5f, b.Until, Eps, "längere Restdauer bleibt erhalten");
        }

        [Test]
        public void SchwaechererBuff_AendertNichts()
        {
            var b = SupportBuffRules.Apply(default, 0.30f, 2f, 0f);
            var after = SupportBuffRules.Apply(b, 0.25f, 10f, 1f);
            Assert.AreEqual(0.30f, after.Value, Eps);
            Assert.AreEqual(2f, after.Until, Eps, "schwächerer verlängert den stärkeren nicht");
        }

        [Test]
        public void NachAblauf_GiltAuchSchwaechererWieder()
        {
            var b = SupportBuffRules.Apply(default, 0.30f, 2f, 0f);
            b = SupportBuffRules.Apply(b, 0.10f, 3f, 2.5f);
            Assert.AreEqual(0.10f, SupportBuffRules.Current(b, 3f), Eps);
        }

        [Test]
        public void UngueltigeWerte_WerdenIgnoriert()
        {
            var b = SupportBuffRules.Apply(default, 0.2f, 1f, 0f);
            Assert.AreEqual(b.Value, SupportBuffRules.Apply(b, 0f, 5f, 0.5f).Value, Eps);
            Assert.AreEqual(b.Until, SupportBuffRules.Apply(b, 0.5f, 0f, 0.5f).Until, Eps);
            Assert.AreEqual(b.Until, SupportBuffRules.Apply(b, float.NaN, 5f, 0.5f).Until, Eps);
        }

        [Test]
        public void Faktoren()
        {
            Assert.AreEqual(1.3f, SupportBuffRules.SpeedFactor(0.3f), Eps);
            Assert.AreEqual(1f / 1.25f, SupportBuffRules.CooldownFactor(0.25f), Eps);
            Assert.AreEqual(0.7f, SupportBuffRules.DamageTakenFactor(0.3f), Eps);
            Assert.AreEqual(1f, SupportBuffRules.DamageTakenFactor(-1f), Eps);
            Assert.AreEqual(1f - SupportBuffRules.MaxDamageReduction, SupportBuffRules.DamageTakenFactor(2f), Eps, "Obergrenze");
        }

        [Test]
        public void Satz_TrenntArten()
        {
            var set = new SupportBuffSet();
            set.Apply(SupportBuffKind.MoveSpeed, 0.3f, 3f, 0f);
            set.Apply(SupportBuffKind.DamageReduction, 0.3f, 2f, 0f);
            Assert.AreEqual(1.3f, set.MoveSpeedFactor(1f), Eps);
            Assert.AreEqual(0.7f, set.DamageTakenFactor(1f), Eps);
            Assert.AreEqual(1f, set.AttackCooldownFactor(1f), Eps, "kein Angriffstempo-Buff");
            Assert.AreEqual(1f, set.DamageTakenFactor(2.5f), Eps, "abgelaufen");
            Assert.AreEqual(2f, set.Remaining(SupportBuffKind.MoveSpeed, 1f), Eps);
            set.Clear();
            Assert.AreEqual(1f, set.MoveSpeedFactor(1f), Eps);
        }

        [Test]
        public void Satz_ZweiSchamanen_StaerksterGilt()
        {
            var set = new SupportBuffSet();
            set.Apply(SupportBuffKind.AttackSpeed, 0.25f, 1f, 0f);
            set.Apply(SupportBuffKind.AttackSpeed, 0.30f, 1f, 0.2f); // Kriegstotem mit Händlerkarte
            set.Apply(SupportBuffKind.AttackSpeed, 0.25f, 1f, 0.4f);
            Assert.AreEqual(1f / 1.3f, set.AttackCooldownFactor(0.5f), Eps);
        }
    }
}
