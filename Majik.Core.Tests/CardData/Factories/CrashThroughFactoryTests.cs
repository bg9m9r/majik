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
/// Unit tests for <see cref="CrashThroughFactory"/>.
///
/// Card: Crash Through — Sorcery {R} (Dominaria).
///   Oracle text (verified against Scryfall):
///     "Creatures you control gain trample until end of turn. (Each of those
///      creatures can deal excess combat damage to the player or planeswalker
///      it's attacking.)
///      Draw a card."
///
/// Covers:
///   - Identity: name, Sorcery type, Red colour, mana value 1.
///   - NamedCardFactory dispatch.
///   - SpellDefinition shape: no modes, no targets, no X.
///   - Resolve: grants Trample-until-EOT to every creature the caster
///     controls (asserted via ContinuousEffectsService); enemy creatures
///     are untouched. Then the caster draws a card.
///   - Trample grant expires at end of turn (CR 514.2).
/// </summary>
[Trait("Color", "R")]
public class CrashThroughFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob   = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void CrashThrough_Create_HasSorceryShape_Red()
    {
        var card = CrashThroughFactory.Create(_alice);

        card.Name.Should().Be("Crash Through");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.Red);
        card.ManaCostValue.TotalValue.Should().Be(1, because: "{R} = mana value 1");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }
    // -----------------------------------------------------------------------
    // SpellDefinition shape
    // -----------------------------------------------------------------------

    [Fact]
    public void CrashThrough_BuildDefinition_NoModes_NoTargets_NoX()
    {
        var def = CrashThroughFactory.BuildDefinition(_alice);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().BeEmpty(because: "Crash Through targets nothing");
    }

    // -----------------------------------------------------------------------
    // Resolve: grant trample to controller's creatures + draw a card
    // -----------------------------------------------------------------------

    [Fact]
    public void CrashThrough_Resolve_GrantsTrampleToControllersCreatures_AndDraws()
    {
        var svc = new ContinuousEffectsService();

        // Alice controls a 2/2 creature.
        var ally = new Creature("Bear", "{1}{G}", 2, 2);
        ally.SetOwner(_alice);
        ally.SetController(_alice);
        ally.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(ally);
        ally.ActiveEffects = svc;

        // Bob controls a creature that should NOT get trample.
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

        var def = CrashThroughFactory.BuildDefinition(_alice, svc);

        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[0],
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        var effects = def.EffectFactory(chosen);
        foreach (var e in effects) e.Execute();

        // Alice's creature has Trample.
        CombatAbilities.HasTrample(ally).Should().BeTrue(
            because: "Crash Through grants trample to all creatures Alice controls");

        // Bob's creature is not affected.
        CombatAbilities.HasTrample(enemy).Should().BeFalse(
            because: "Crash Through only affects creatures the caster controls");

        // The caster drew a card.
        _alice.Zones.Hand.GetCards().Count().Should().Be(handBefore + 1,
            because: "Crash Through's rider draws a card for the caster");
        _alice.Zones.Hand.GetCards().Should().Contain(libraryCard);
    }

    [Fact]
    public void CrashThrough_Resolve_TrampleExpires_EndOfTurn()
    {
        var svc = new ContinuousEffectsService();

        var ally = new Creature("Bear", "{1}{G}", 2, 2);
        ally.SetOwner(_alice);
        ally.SetController(_alice);
        ally.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(ally);
        ally.ActiveEffects = svc;

        var def = CrashThroughFactory.BuildDefinition(_alice, svc);

        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[0],
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        foreach (var e in def.EffectFactory(chosen)) e.Execute();
        CombatAbilities.HasTrample(ally).Should().BeTrue();

        // CR 514.2 — EOT cleanup expires the grant.
        svc.ExpireEndOfTurn();

        CombatAbilities.HasTrample(ally).Should().BeFalse(
            because: "Trample grant expires at end of turn (CR 514.2)");
    }
}
