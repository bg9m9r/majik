using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.CardData.SpellTemplates.Templates.Damage;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Xunit;

namespace Majik.Core.Tests.CardData.SpellTemplates.Templates.Damage;

/// <summary>
/// Spell-source damage (Lightning Bolt-class effects) must publish
/// <see cref="DamageDealtEvent"/> with <see cref="DamageType.Spell"/>
/// so the portal can animate the spell ping separately from combat
/// strikes. Mirrors CR 119.1 (sources of damage).
/// </summary>
public class SpellDamageEventTests
{
    private static ChosenSpellParams Targeting(object target) =>
        new(
            ModeIndex: null, X: null,
            Targets: new IReadOnlyList<object>[] { new[] { target } },
            Mana: ManaPayment.Empty);

    [Fact]
    public void DamageCreatureSpell_PublishesDamageDealt_WithSpellType()
    {
        var caster = new Player("Caster", 20);
        var target = new Creature("victim", "", 4, 4);
        var bus = new EventBus();
        var events = new List<DamageDealtEvent>();
        bus.Subscribe<DamageDealtEvent>(events.Add);

        var spell = DamageSpellFactory.DamageCreatureSpell(
            3, _ => target, replacements: null, caster: caster, bus: bus);

        foreach (var fx in spell.EffectFactory(Targeting(target))) fx.Execute();

        target.Damage.Should().Be(3);
        events.Should().ContainSingle();
        var e = events[0];
        e.Amount.Should().Be(3);
        e.DamageType.Should().Be(DamageType.Spell);
        e.TargetIsPlayer.Should().BeFalse();
        e.TargetInstanceId.Should().Be(target.InstanceId);
        e.SourceInstanceId.Should().Be(caster.Id, "spell-source identity falls back to caster until ICard threading lands");
    }

    [Fact]
    public void DamagePlayerSpell_PublishesDamageDealt_TargetIsPlayer()
    {
        var caster = new Player("Caster", 20);
        var victim = new Player("Victim", 20);
        var bus = new EventBus();
        var events = new List<DamageDealtEvent>();
        bus.Subscribe<DamageDealtEvent>(events.Add);

        var spell = DamageSpellFactory.DamagePlayerSpell(
            3, _ => victim, replacements: null, caster: caster, bus: bus);

        foreach (var fx in spell.EffectFactory(Targeting(victim))) fx.Execute();

        victim.LifeTotal.Should().Be(17);
        events.Should().ContainSingle();
        var e = events[0];
        e.Amount.Should().Be(3);
        e.DamageType.Should().Be(DamageType.Spell);
        e.TargetIsPlayer.Should().BeTrue();
        e.TargetInstanceId.Should().Be(victim.Id);
    }

    [Fact]
    public void DamageSpell_NoBus_DoesNotThrow_AndDoesNotEmit()
    {
        var target = new Creature("victim", "", 4, 4);
        var spell = DamageSpellFactory.DamageCreatureSpell(2, _ => target);

        var action = () =>
        {
            foreach (var fx in spell.EffectFactory(Targeting(target))) fx.Execute();
        };

        action.Should().NotThrow("legacy callers that don't pass a bus still work");
        target.Damage.Should().Be(2);
    }
}
