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

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Cathartic Pyre (Innistrad: Midnight Hunt, {1}{R}, Instant).
///
/// Oracle text (verified against Scryfall):
///   "Choose one —
///     • Cathartic Pyre deals 3 damage to target creature or planeswalker.
///     • Discard up to two cards, then draw that many cards."
///
/// CR 700.2d — modal "Choose one —" spell with 2 modes.
///   Mode 0: 3 damage to target creature or planeswalker.
///   Mode 1: discard up to two cards, then draw that many cards.
///
/// Covers:
///   - Card shape + dispatch ({1}{R}, Red, Instant).
///   - SpellDefinition shape: 2 modes, 2 MinTargets=0 target requests.
///   - Mode 0 deals 3 damage to a target creature.
///   - Mode 0 removes 3 loyalty from a target planeswalker (CR 306.7).
///   - Mode 0 no-op against a non-creature/planeswalker target (CR 608.2b).
///   - Mode 1 discards then draws the same number of cards.
///   - Mode 1 with an empty hand discards nothing, draws nothing.
/// </summary>
public class CatharticPyreFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Card identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void CatharticPyre_HasInstantShape_Red_AtCost1R()
    {
        var card = CatharticPyreFactory.Create(_alice);

        card.Name.Should().Be("Cathartic Pyre");
        card.ManaCost.Should().Be("{1}{R}");
        card.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.Red);
        card.ManaCostValue.TotalValue.Should().Be(2, because: "{1}{R} = mana value 2");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsCatharticPyreShape()
    {
        var dispatched = NamedCardFactory.Create("Cathartic Pyre", _alice);

        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("Cathartic Pyre");
        dispatched.ManaCost.Should().Be("{1}{R}");
    }

    [Fact]
    public void SpellDefinition_ExposesTwoModes_AndTwoOptionalTargetRequests()
    {
        var def = CatharticPyreFactory.BuildDefinition(_alice, o => o);

        def.Modes.Should().HaveCount(2);
        def.Modes[CatharticPyreFactory.ModeDamage].Should().Contain("damage");
        def.Modes[CatharticPyreFactory.ModeRummage].Should().Contain("Discard");

        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(2);
        def.TargetRequests[CatharticPyreFactory.ModeDamage].MinTargets.Should().Be(0);
        def.TargetRequests[CatharticPyreFactory.ModeDamage].Description.Should().Contain("creature or planeswalker");
        def.TargetRequests[CatharticPyreFactory.ModeRummage].MinTargets.Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // Mode 0 — 3 damage to target creature or planeswalker
    // -----------------------------------------------------------------------

    [Fact]
    public void Mode0_Deals3DamageToTargetCreature()
    {
        var creature = NewControlledPermanent<Creature>(_bob, "Grizzly Bears", "{1}{G}", 2, 2);

        var effects = ResolveMode(CatharticPyreFactory.ModeDamage,
            new IReadOnlyList<object>[]
            {
                new object[] { creature }, // mode 0 target
                Array.Empty<object>(),     // mode 1 (unused)
            });

        effects.Should().HaveCount(1);
        creature.Damage.Should().Be(3,
            because: "mode 0 deals 3 damage to the target creature");
    }

    [Fact]
    public void Mode0_Removes3LoyaltyFromTargetPlaneswalker()
    {
        var pw = NewControlledPlaneswalker(_bob, "Liliana of the Veil", "{1}{B}{B}", startingLoyalty: 3);

        ResolveMode(CatharticPyreFactory.ModeDamage,
            new IReadOnlyList<object>[]
            {
                new object[] { pw },
                Array.Empty<object>(),
            });

        pw.Loyalty.Should().Be(0,
            because: "mode 0 removes 3 loyalty from the target planeswalker (CR 306.7)");
    }

    [Fact]
    public void Mode0_TargetPlayer_DoesNothing()
    {
        // A player is not a legal target for the damage mode (creature or
        // planeswalker only). If somehow resolved against one, CR 608.2b → no-op.
        var beforeLife = _bob.LifeTotal;

        ResolveMode(CatharticPyreFactory.ModeDamage,
            new IReadOnlyList<object>[]
            {
                new object[] { _bob },
                Array.Empty<object>(),
            });

        _bob.LifeTotal.Should().Be(beforeLife,
            because: "mode 0 only hits creatures/planeswalkers (CR 608.2b)");
    }

    // -----------------------------------------------------------------------
    // Mode 1 — discard up to two, then draw that many
    // -----------------------------------------------------------------------

    [Fact]
    public void Mode1_DiscardsTwo_ThenDrawsTwo()
    {
        // 5 cards in hand, 3 in library.
        for (var i = 0; i < 5; i++) AddToHand(_alice, $"Hand {i}");
        for (var i = 0; i < 3; i++) AddToLibrary(_alice, $"Lib {i}");

        var startHand = _alice.Zones.Hand.GetCards().Count();
        var startLib = _alice.Zones.Library.GetCards().Count();

        var effects = ResolveMode(CatharticPyreFactory.ModeRummage,
            new IReadOnlyList<object>[]
            {
                Array.Empty<object>(), // mode 0 (unused)
                Array.Empty<object>(), // mode 1 (no target)
            });

        effects.Should().HaveCount(1);

        // Discard up to two, then draw that many: -2 hand from discard,
        // +2 hand from draw → net 0; graveyard +2; library -2.
        _alice.Zones.Hand.GetCards().Count().Should().Be(startHand,
            because: "discard 2 then draw 2 → net zero change in hand size");
        _alice.Zones.Library.GetCards().Count().Should().Be(startLib - 2,
            because: "drew two cards from the library");
        _alice.Zones.Graveyard.GetCards().Count().Should().Be(2,
            because: "two cards were discarded to the graveyard");
    }

    [Fact]
    public void Mode1_EmptyHand_DiscardsNothing_DrawsNothing()
    {
        for (var i = 0; i < 3; i++) AddToLibrary(_alice, $"Lib {i}");
        var startLib = _alice.Zones.Library.GetCards().Count();

        ResolveMode(CatharticPyreFactory.ModeRummage,
            new IReadOnlyList<object>[]
            {
                Array.Empty<object>(),
                Array.Empty<object>(),
            });

        // "Discard up to two" with empty hand → discard zero → draw zero.
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Zones.Library.GetCards().Count().Should().Be(startLib,
            because: "nothing was discarded, so nothing is drawn");
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private IReadOnlyList<Majik.Core.Abilities.IEffect> ResolveMode(
        int mode, IReadOnlyList<object>[] targets)
    {
        var def = CatharticPyreFactory.BuildDefinition(_alice, o => o);

        var chosen = new ChosenSpellParams(
            ModeIndex: mode,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        var effects = def.EffectFactory(chosen);
        foreach (var fx in effects)
        {
            fx.Execute();
        }
        return effects;
    }

    private static void AddToHand(Player owner, string name)
    {
        var card = new Instant(name, "{R}");
        card.SetOwner(owner);
        card.SetController(owner);
        card.SetZone(ZoneType.Hand);
        owner.Zones.Hand.AddCard(card);
    }

    private static void AddToLibrary(Player owner, string name)
    {
        var card = new Instant(name, "{R}");
        card.SetOwner(owner);
        card.SetController(owner);
        card.SetZone(ZoneType.Library);
        owner.Zones.Library.AddCard(card);
    }

    private static T NewControlledPermanent<T>(Player owner, string name, string cost,
        int power = 0, int toughness = 0)
        where T : ICard
    {
        T card;
        if (typeof(T) == typeof(Creature))
        {
            card = (T)(ICard)new Creature(name, cost, power, toughness);
        }
        else
        {
            throw new InvalidOperationException($"Unsupported type {typeof(T)}");
        }

        ((Card)(ICard)card).SetOwner(owner);
        ((Card)(ICard)card).SetController(owner);
        ((Card)(ICard)card).SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(card);
        return card;
    }

    private static Planeswalker NewControlledPlaneswalker(
        Player owner, string name, string cost, int startingLoyalty)
    {
        var pw = new Planeswalker(name, cost, startingLoyalty);
        pw.SetOwner(owner);
        pw.SetController(owner);
        pw.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(pw);
        return pw;
    }
}
