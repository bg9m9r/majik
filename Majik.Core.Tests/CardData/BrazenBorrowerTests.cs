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
/// Tests for Brazen Borrower // Petty Theft (Throne of Eldraine, {1}{U}{U}).
///
/// Covers:
///   - Card identity (name, type, subtypes, P/T, mana cost) materialised from
///     the embedded JSON definition.
///   - Flash + Flying keyword presence (CR 702.8 / 702.9).
///   - NamedCardFactory dispatch.
///   - Petty Theft helper structural shape — one "nonland permanent an
///     opponent controls" TargetRequest, fixed-cost (no X), no modes, Bounce
///     intent.
///   - Petty Theft resolve: returns an opponent's nonland permanent to its
///     owner's hand (CR 701.10).
///   - Petty Theft resolve: leaves a land target untouched (CR 305 — nonland
///     restriction).
///   - Petty Theft candidate gathering excludes the caster's own permanents
///     (CR 109.5 — "an opponent").
///
/// Adventure cast-from-hand-to-exile (CR 715) routing is exercised by the
/// cast-pipeline suite; the printed "This creature can block only creatures
/// with flying" rider is deferred — see <see cref="BrazenBorrowerFactory"/>
/// XML doc.
/// </summary>
public class BrazenBorrowerTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void BrazenBorrower_IsCreature_FaerieRogue_3_1_AtCost1UU()
    {
        var card = BrazenBorrowerFactory.Create(_alice);

        card.Name.Should().Be("Brazen Borrower");
        card.ManaCost.Should().Be("{1}{U}{U}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Faerie).Should().BeTrue();
        card.HasSubtype(CardSubtype.Rogue).Should().BeTrue();
        card.BasePower.Should().Be(3);
        card.BaseToughness.Should().Be(1);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void BrazenBorrower_HasFlashAndFlying()
    {
        var card = BrazenBorrowerFactory.Create(_alice);

        var keywords = card.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword)
            .ToList();
        keywords.Should().Contain("Flash");
        keywords.Should().Contain("Flying");
    }

    [Fact]
    public void NamedCardFactory_Dispatches_BrazenBorrower()
    {
        var card = NamedCardFactory.Create("Brazen Borrower", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Brazen Borrower");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Faerie).Should().BeTrue();
        card.HasSubtype(CardSubtype.Rogue).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(3);
        ((Creature)card).BaseToughness.Should().Be(1);
        card.Owner.Should().Be(_alice);

        card.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).Should().Contain(new[] { "Flash", "Flying" });
    }

    // -----------------------------------------------------------------------
    // Petty Theft helper — structural shape
    // -----------------------------------------------------------------------

    [Fact]
    public void PettyTheft_Helper_HasSingleNonlandOpponentPermanentTarget()
    {
        var def = BrazenBorrowerFactory.BuildAdventureSpell(_alice, o => o);

        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
        def.TargetRequests.Should().HaveCount(1);

        var tr = def.TargetRequests[0];
        tr.MinTargets.Should().Be(1);
        tr.MaxTargets.Should().Be(1);
        tr.Description.Should().Contain("nonland permanent");
        tr.Description.Should().Contain("opponent controls");
        tr.Intent.Should().Be(BotIntent.Bounce);
    }

    // -----------------------------------------------------------------------
    // Petty Theft helper — resolve
    // -----------------------------------------------------------------------

    [Fact]
    public void PettyTheft_Resolve_ReturnsOpponentPermanentToOwnersHand()
    {
        // Bob controls a creature — a legal target for Alice's Petty Theft.
        var goblin = new Creature("Goblin Guide", "{R}", power: 2, toughness: 2)
        {
            Owner = _bob,
            Controller = _bob,
        };
        _bob.Zones.Battlefield.AddCard(goblin);
        goblin.SetZone(ZoneType.Battlefield);

        var def = BrazenBorrowerFactory.BuildAdventureSpell(_alice, o => o);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new[] { new object[] { goblin } },
            Mana: ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        // CR 701.10 — returned to its owner's hand.
        goblin.Zone.Should().Be(ZoneType.Hand);
        _bob.Zones.Hand.GetCards().Should().Contain(goblin);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(goblin);
        goblin.Controller.Should().BeSameAs(_bob, "control resets to owner in hand");
    }

    [Fact]
    public void PettyTheft_Resolve_LandTarget_IsUntouched()
    {
        // A Land is not a legal target (CR 305 — nonland restriction). Even if
        // forced through the resolver, the resolve guard leaves it in place.
        var land = new Land("Island")
        {
            Owner = _bob,
            Controller = _bob,
        };
        _bob.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var def = BrazenBorrowerFactory.BuildAdventureSpell(_alice, o => o);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new[] { new object[] { land } },
            Mana: ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        land.Zone.Should().Be(ZoneType.Battlefield);
        _bob.Zones.Battlefield.GetCards().Should().Contain(land);
    }

    [Fact]
    public void PettyTheft_CandidateGatherer_ExcludesCastersOwnPermanents()
    {
        // Alice (the caster) controls her own creature; Bob controls one too.
        var aliceCreature = new Creature("Llanowar Elves", "{G}", power: 1, toughness: 1)
        {
            Owner = _alice,
            Controller = _alice,
        };
        _alice.Zones.Battlefield.AddCard(aliceCreature);
        aliceCreature.SetZone(ZoneType.Battlefield);

        var bobCreature = new Creature("Goblin Guide", "{R}", power: 2, toughness: 2)
        {
            Owner = _bob,
            Controller = _bob,
        };
        _bob.Zones.Battlefield.AddCard(bobCreature);
        bobCreature.SetZone(ZoneType.Battlefield);

        var land = new Land("Island")
        {
            Owner = _bob,
            Controller = _bob,
        };
        _bob.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var def = BrazenBorrowerFactory.BuildAdventureSpell(_alice, o => o);
        var tr = def.TargetRequests[0];

        var ctx = new GameContext(
            self: _alice,
            allPlayers: new[] { _alice, _bob },
            activePlayer: _alice,
            turnNumber: 1,
            currentPhase: null,
            stack: new Majik.Core.Stack.Stack(new Majik.Core.Events.EventBus()));

        var candidates = tr.CandidateGatherer!(ctx).ToList();

        // CR 109.5 — only the opponent's nonland permanent is a candidate.
        candidates.Should().Contain(bobCreature);
        candidates.Should().NotContain(aliceCreature, "Alice's own permanents aren't 'an opponent's'");
        candidates.Should().NotContain(land, "lands are excluded (CR 305)");
    }
}
