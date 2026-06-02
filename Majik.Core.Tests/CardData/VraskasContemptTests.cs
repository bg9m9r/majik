using FluentAssertions;
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

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Vraska's Contempt (Ixalan, {2}{B}{B}, Instant).
///
/// Oracle text (verified against Scryfall 2026-06-01):
///   "Exile target creature or planeswalker. You gain 2 life."
///
/// Vraska's Contempt is the exile cousin of Hero's Downfall — same
/// creature-or-planeswalker target shape, but it exiles (CR 701.21) instead
/// of destroying, and adds a fixed "You gain 2 life" rider (CR 119.3).
///
/// Covers:
///   - Card identity (Instant, {2}{B}{B}, owner / controller).
///   - NamedCardFactory dispatch.
///   - SpellDefinition shape — single 1..1 creature-or-PW target request,
///     no modes, no variable X, BotIntent.Removal.
///   - Resolve: exiles a creature + caster gains 2 life (CR 701.21 / 119.3).
///   - Resolve: exiles a planeswalker + caster gains 2 life.
///   - Resolve: artifact target (not creature, not PW) is illegal → not
///     exiled, but the caster still gains 2 life (CR 608.2b / 608.2c).
///   - Resolve: off-battlefield target → not exiled, caster still gains
///     2 life.
/// </summary>
public class VraskasContemptTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void VraskasContempt_IsInstant_AtCost2BB()
    {
        var card = VraskasContemptFactory.Create(_alice);

        card.Name.Should().Be("Vraska's Contempt");
        card.ManaCost.Should().Be("{2}{B}{B}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_VraskasContempt()
    {
        var card = NamedCardFactory.Create("Vraska's Contempt", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Vraska's Contempt");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{2}{B}{B}");
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // SpellDefinition — structural shape
    // -----------------------------------------------------------------------

    [Fact]
    public void VraskasContempt_Definition_HasSingleCreatureOrPlaneswalkerTarget()
    {
        var def = VraskasContemptFactory.BuildDefinition(o => o, _alice);

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
    // Resolve — exiles creature / planeswalker + gains 2 life
    // -----------------------------------------------------------------------

    [Fact]
    public void VraskasContempt_ExilesCreature_AndGainsLife()
    {
        var goblin = NewControlledCreature(_bob, "Goblin Guide", "{R}");

        Resolve(goblin);

        goblin.Zone.Should().Be(ZoneType.Exile,
            "Vraska's Contempt exiles the targeted creature (CR 701.21)");
        _bob.Zones.Exile.GetCards().Should().Contain(goblin);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(goblin);
        _alice.LifeTotal.Should().Be(22, "the caster gains 2 life (CR 119.3)");
    }

    [Fact]
    public void VraskasContempt_ExilesPlaneswalker_AndGainsLife()
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

        Resolve(pw);

        pw.Zone.Should().Be(ZoneType.Exile,
            "Vraska's Contempt exiles the targeted planeswalker (CR 701.21)");
        _bob.Zones.Exile.GetCards().Should().Contain(pw);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(pw);
        _alice.LifeTotal.Should().Be(22, "the caster gains 2 life (CR 119.3)");
    }

    // -----------------------------------------------------------------------
    // Resolve — illegal targets still grant life (CR 608.2c)
    // -----------------------------------------------------------------------

    [Fact]
    public void VraskasContempt_ArtifactTarget_NotExiled_ButGainsLife()
    {
        // Pure artifact (not creature, not PW) — illegal exile target.
        var artifact = new Artifact("Sol Ring", "{1}")
        {
            Owner = _bob,
            Controller = _bob,
        };
        _bob.Zones.Battlefield.AddCard(artifact);
        artifact.SetZone(ZoneType.Battlefield);

        Resolve(artifact);

        artifact.Zone.Should().Be(ZoneType.Battlefield,
            "Vraska's Contempt can only exile creatures or planeswalkers (CR 608.2b)");
        _bob.Zones.Battlefield.GetCards().Should().Contain(artifact);
        _alice.LifeTotal.Should().Be(22,
            "the untargeted life-gain clause still resolves (CR 608.2c)");
    }

    [Fact]
    public void VraskasContempt_TargetNotOnBattlefield_NotExiled_ButGainsLife()
    {
        var creature = NewControlledCreature(_bob, "Tarmogoyf", "{1}{G}");

        // Simulate the target leaving the battlefield before resolution.
        _bob.Zones.Battlefield.RemoveCard(creature);
        creature.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(creature);

        Resolve(creature);

        // Zone unchanged by the resolve — CR 608.2b illegal target → no exile.
        creature.Zone.Should().Be(ZoneType.Graveyard);
        _alice.LifeTotal.Should().Be(22,
            "the untargeted life-gain clause still resolves (CR 608.2c)");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private void Resolve(object targetToken)
    {
        var def = VraskasContemptFactory.BuildDefinition(
            targetResolver: t => t, caster: _alice);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { targetToken } },
            Mana: ManaPayment.Empty);

        foreach (var fx in def.EffectFactory(chosen))
        {
            fx.Execute();
        }
    }

    private static Creature NewControlledCreature(Player owner, string name, string cost)
    {
        var c = new Creature(name, cost, 1, 1);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(c);
        return c;
    }
}
