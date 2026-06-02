using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="WarlordsFuryFactory"/>.
///
/// Card: Warlord's Fury — Sorcery {R} (Khans of Tarkir).
///   Oracle text (verified against Scryfall):
///     "Creatures you control gain first strike until end of turn.
///      Draw a card."
///
/// Covers:
///   - Identity: name, Sorcery type, Red colour, mana value 1.
///   - NamedCardFactory dispatch.
///   - SpellDefinition shape: no modes, no targets, no X.
///   - Resolve: grants First strike-until-EOT to every creature the caster
///     controls (asserted via ContinuousEffectsService); enemy creatures
///     are untouched. Then the caster draws a card.
///   - First strike grant expires at end of turn (CR 514.2).
/// </summary>
public class WarlordsFuryFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob   = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void WarlordsFury_Create_HasSorceryShape_Red()
    {
        var card = WarlordsFuryFactory.Create(_alice);

        card.Name.Should().Be("Warlord's Fury");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.Red);
        card.ManaCostValue.TotalValue.Should().Be(1, because: "{R} = mana value 1");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void WarlordsFury_NamedCardFactory_Dispatch()
    {
        var dispatched = NamedCardFactory.Create("Warlord's Fury", _alice);

        dispatched.Should().BeOfType<Sorcery>();
        dispatched.Name.Should().Be("Warlord's Fury");
        dispatched.HasType(CardType.Sorcery).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // SpellDefinition shape
    // -----------------------------------------------------------------------

    [Fact]
    public void WarlordsFury_BuildDefinition_NoModes_NoTargets_NoX()
    {
        var def = WarlordsFuryFactory.BuildDefinition(_alice);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().BeEmpty(because: "Warlord's Fury targets nothing");
    }

    // -----------------------------------------------------------------------
    // Resolve: grant first strike to controller's creatures + draw a card
    // -----------------------------------------------------------------------

    [Fact]
    public void WarlordsFury_Resolve_GrantsFirstStrikeToControllersCreatures_AndDraws()
    {
        var svc = new ContinuousEffectsService();

        // Alice controls a 2/2 creature.
        var ally = new Creature("Bear", "{1}{G}", 2, 2);
        ally.SetOwner(_alice);
        ally.SetController(_alice);
        ally.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(ally);
        ally.ActiveEffects = svc;

        // Bob controls a creature that should NOT get first strike.
        var enemy = new Creature("Goblin", "{R}", 1, 1);
        enemy.SetOwner(_bob);
        enemy.SetController(_bob);
        enemy.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(enemy);
        enemy.ActiveEffects = svc;

        // A card in Alice's library to draw.
        var libraryCard = new Sorcery("Filler", "{1}");
        libraryCard.SetOwner(_alice);
        libraryCard.SetController(_alice);
        libraryCard.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(libraryCard);

        var handBefore = _alice.Zones.Hand.GetCards().Count();

        var def = WarlordsFuryFactory.BuildDefinition(_alice, svc);

        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[0],
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        var effects = def.EffectFactory(chosen);
        foreach (var e in effects) e.Execute();

        // Alice's creature has First strike.
        CombatAbilities.HasFirstStrike(ally).Should().BeTrue(
            because: "Warlord's Fury grants first strike to all creatures Alice controls");

        // Bob's creature is not affected.
        CombatAbilities.HasFirstStrike(enemy).Should().BeFalse(
            because: "Warlord's Fury only affects creatures the caster controls");

        // The caster drew a card.
        _alice.Zones.Hand.GetCards().Count().Should().Be(handBefore + 1,
            because: "Warlord's Fury's rider draws a card for the caster");
        _alice.Zones.Hand.GetCards().Should().Contain(libraryCard);
    }

    [Fact]
    public void WarlordsFury_Resolve_FirstStrikeExpires_EndOfTurn()
    {
        var svc = new ContinuousEffectsService();

        var ally = new Creature("Bear", "{1}{G}", 2, 2);
        ally.SetOwner(_alice);
        ally.SetController(_alice);
        ally.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(ally);
        ally.ActiveEffects = svc;

        var def = WarlordsFuryFactory.BuildDefinition(_alice, svc);

        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[0],
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        foreach (var e in def.EffectFactory(chosen)) e.Execute();
        CombatAbilities.HasFirstStrike(ally).Should().BeTrue();

        // CR 514.2 — EOT cleanup expires the grant.
        svc.ExpireEndOfTurn();

        CombatAbilities.HasFirstStrike(ally).Should().BeFalse(
            because: "First strike grant expires at end of turn (CR 514.2)");
    }
}
