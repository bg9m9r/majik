using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Murderous Rider // Swift End (Throne of Eldraine, {1}{B}{B}).
///
/// Covers:
///   - Card identity (name, type, subtypes, P/T, mana cost).
///   - Lifelink keyword presence (CR 702.15).
///   - NamedCardFactory dispatch.
///   - Swift End helper structural shape — one "creature or planeswalker"
///     TargetRequest, fixed-cost (no X), no modes.
///   - Swift End resolve: destroys a target creature (CR 701.7) and
///     caster loses 2 life (CR 119.3).
///   - Swift End resolve: destroys a target planeswalker.
///   - Swift End resolve on an illegal at-resolution target (non-permanent)
///     still costs the caster 2 life (printed wording is two consecutive
///     sentences with no conditional gate).
///
/// Adventure cast-from-hand-to-exile (CR 715) + the printed
/// "when this dies, exile it" self-exile LTB clause are deferred — see
/// <see cref="MurderousRiderFactory"/> XML doc.
/// </summary>
public class MurderousRiderTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void MurderousRider_IsCreature_ZombieKnight_2_3_AtCost1BB()
    {
        var card = MurderousRiderFactory.Create(_alice);

        card.Name.Should().Be("Murderous Rider");
        card.ManaCost.Should().Be("{1}{B}{B}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Zombie).Should().BeTrue();
        card.HasSubtype(CardSubtype.Knight).Should().BeTrue();
        card.BasePower.Should().Be(2);
        card.BaseToughness.Should().Be(3);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void MurderousRider_HasLifelink()
    {
        var card = MurderousRiderFactory.Create(_alice);

        card.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).Should().Contain("Lifelink");
    }

    [Fact]
    public void NamedCardFactory_Dispatches_MurderousRider()
    {
        var card = NamedCardFactory.Create("Murderous Rider", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Murderous Rider");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Zombie).Should().BeTrue();
        card.HasSubtype(CardSubtype.Knight).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(2);
        ((Creature)card).BaseToughness.Should().Be(3);
        card.Owner.Should().Be(_alice);

        card.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).Should().Contain("Lifelink");
    }

    // -----------------------------------------------------------------------
    // Swift End helper — structural shape
    // -----------------------------------------------------------------------

    [Fact]
    public void SwiftEnd_Helper_HasSingleCreatureOrPlaneswalkerTarget()
    {
        var def = MurderousRiderFactory.BuildAdventureSpell(_alice, o => o);

        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
        def.TargetRequests.Should().HaveCount(1);

        var tr = def.TargetRequests[0];
        tr.MinTargets.Should().Be(1);
        tr.MaxTargets.Should().Be(1);
        tr.Description.Should().Contain("creature or planeswalker");
        tr.Intent.Should().Be(BotIntent.Removal);
    }

    // -----------------------------------------------------------------------
    // Swift End helper — resolve
    // -----------------------------------------------------------------------

    [Fact]
    public void SwiftEnd_Resolve_DestroysCreature_AndCasterLoses2Life()
    {
        // Bob has a creature on the battlefield — legal target.
        var goblin = new Creature("Goblin Guide", "{R}", power: 2, toughness: 2)
        {
            Owner = _bob,
            Controller = _bob,
        };
        _bob.Zones.Battlefield.AddCard(goblin);
        goblin.SetZone(ZoneType.Battlefield);

        var aliceLifeBefore = _alice.LifeTotal;

        var def = MurderousRiderFactory.BuildAdventureSpell(_alice, o => o);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new[] { new object[] { goblin } },
            Mana: ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        // CR 701.7 — destroyed creature is in its owner's graveyard.
        goblin.Zone.Should().Be(ZoneType.Graveyard);
        _bob.Zones.Graveyard.GetCards().Should().Contain(goblin);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(goblin);

        // CR 119.3 — caster loses 2 life.
        _alice.LifeTotal.Should().Be(aliceLifeBefore - 2);
    }

    [Fact]
    public void SwiftEnd_Resolve_DestroysPlaneswalker()
    {
        var pw = new Planeswalker(
            name: "Liliana, the Last Hope",
            manaCost: "{1}{B}{B}",
            startingLoyalty: 3,
            subtypes: new[] { CardSubtype.Liliana })
        {
            Owner = _bob,
            Controller = _bob,
        };
        _bob.Zones.Battlefield.AddCard(pw);
        pw.SetZone(ZoneType.Battlefield);

        var aliceLifeBefore = _alice.LifeTotal;

        var def = MurderousRiderFactory.BuildAdventureSpell(_alice, o => o);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new[] { new object[] { pw } },
            Mana: ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        // CR 701.7 — destroyed planeswalker goes to its owner's graveyard.
        pw.Zone.Should().Be(ZoneType.Graveyard);
        _bob.Zones.Graveyard.GetCards().Should().Contain(pw);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(pw);

        // CR 119.3 — caster still loses 2 life.
        _alice.LifeTotal.Should().Be(aliceLifeBefore - 2);
    }

    [Fact]
    public void SwiftEnd_Resolve_IllegalTarget_StillCostsCasterTwoLife()
    {
        // An artifact (not creature, not planeswalker) is an illegal
        // target at resolution per CR 608.2b — the destroy half fizzles
        // but the caster still pays the 2-life clause (printed as a
        // separate sentence).
        var artifact = new Artifact("Sol Ring", "{1}")
        {
            Owner = _bob,
            Controller = _bob,
        };
        _bob.Zones.Battlefield.AddCard(artifact);
        artifact.SetZone(ZoneType.Battlefield);

        var aliceLifeBefore = _alice.LifeTotal;

        var def = MurderousRiderFactory.BuildAdventureSpell(_alice, o => o);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new[] { new object[] { artifact } },
            Mana: ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        // Artifact untouched.
        artifact.Zone.Should().Be(ZoneType.Battlefield);
        _bob.Zones.Battlefield.GetCards().Should().Contain(artifact);

        // Caster still loses 2 life.
        _alice.LifeTotal.Should().Be(aliceLifeBefore - 2);
    }
}
