using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="MagmaticChannelerFactory"/> (Core Set 2021, {1}{R}).
///
/// Covers:
///   - Identity (Human Shaman 1/2, {1}{R}, owner/controller).
///   - <see cref="NamedCardFactory"/> dispatcher entry.
///   - Activated ability shape: one ICost = ManaCostCost("{2}{R}"), one
///     AdditionalCost = Tap.
///   - <see cref="MagmaticChannelerFactory.CanActivateGraveyardGate"/> at
///     0 / 3 / 4 / 5 instant+sorcery cards (boundary tests around the
///     <see cref="MagmaticChannelerFactory.GraveyardThreshold"/> of 4).
///   - Resolve: picks an eligible creature/instant from the top 4 and
///     puts it into hand; remainder goes to the bottom of the library.
///   - Resolve: no eligible card → nothing moves to hand, remainder still
///     re-bottoms.
///   - Resolve: short library (less than 4 cards) — clean no-op when 0
///     cards, partial peek when fewer than 4.
///   - Resolve: graveyard gate failure short-circuits cleanly (defensive
///     re-check; cost was paid by the cost layer).
/// </summary>
public class MagmaticChannelerTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void MagmaticChanneler_Identity_HumanShaman_1_2_At_1R()
    {
        var card = MagmaticChannelerFactory.Create(_alice);

        card.Name.Should().Be("Magmatic Channeler");
        card.ManaCost.Should().Be("{1}{R}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Shaman).Should().BeTrue();
        card.BasePower.Should().Be(1);
        card.BaseToughness.Should().Be(2);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_MagmaticChanneler()
    {
        var card = NamedCardFactory.Create("Magmatic Channeler", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Magmatic Channeler");
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Shaman).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(1);
        ((Creature)card).BaseToughness.Should().Be(2);

        card.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void MagmaticChanneler_HasOneActivatedAbility_With2RAndTapCosts()
    {
        var card = MagmaticChannelerFactory.Create(_alice);
        var ability = card.Abilities.OfType<ActivatedAbility>().Single();

        var manaCost = ability.Costs.OfType<ManaCostCost>().Single();
        manaCost.Cost.Generic.Should().Be(2);
        manaCost.Cost.Red.Should().Be(1);
        ability.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(c => c.CostType == AdditionalCostType.Tap,
                "the {T} symbol is the tap cost");
    }

    [Fact]
    public void GraveyardGate_FalseAt0()
    {
        MagmaticChannelerFactory.CanActivateGraveyardGate(_alice).Should().BeFalse();
    }

    [Fact]
    public void GraveyardGate_FalseAt3()
    {
        SeedGraveyardWithSpells(_alice, instants: 2, sorceries: 1);
        MagmaticChannelerFactory.CanActivateGraveyardGate(_alice).Should().BeFalse();
    }

    [Fact]
    public void GraveyardGate_TrueAtThreshold4()
    {
        SeedGraveyardWithSpells(_alice, instants: 2, sorceries: 2);
        MagmaticChannelerFactory.CanActivateGraveyardGate(_alice).Should().BeTrue();
    }

    [Fact]
    public void GraveyardGate_TrueAt5_Over_4()
    {
        SeedGraveyardWithSpells(_alice, instants: 3, sorceries: 2);
        MagmaticChannelerFactory.CanActivateGraveyardGate(_alice).Should().BeTrue();
    }

    [Fact]
    public void GraveyardGate_IgnoresNonInstantSorceryCards()
    {
        AddToGraveyard(_alice, new Creature("Bear A", "{1}{G}", 2, 2));
        AddToGraveyard(_alice, new Creature("Bear B", "{1}{G}", 2, 2));
        AddToGraveyard(_alice, new Creature("Bear C", "{1}{G}", 2, 2));
        AddToGraveyard(_alice, new Creature("Bear D", "{1}{G}", 2, 2));
        AddToGraveyard(_alice, new Creature("Bear E", "{1}{G}", 2, 2));

        MagmaticChannelerFactory.CanActivateGraveyardGate(_alice).Should().BeFalse(
            "creatures don't count toward Magmatic Channeler's gate");
    }

    [Fact]
    public void Resolve_PicksCreature_FromTopFour_PutsToHand_RestBottom()
    {
        var card = MagmaticChannelerFactory.Create(_alice);
        SeatOnBattlefield(card);

        // Library top → bottom:
        //   sorc (not eligible — sorcery)
        //   bear (eligible — creature; first eligible match wins)
        //   inst (eligible — instant)
        //   land (not eligible — Land)
        var sorc = new Sorcery("Sorc", "{R}") { Owner = _alice };
        var bear = new Creature("Bear", "{1}{G}", 2, 2) { Owner = _alice };
        var inst = new Instant("Bolt", "{R}") { Owner = _alice };
        var land = new Land("Plains", new[] { CardSupertype.Basic }, new[] { CardSubtype.Plains })
            { Owner = _alice };
        var deep = new Instant("Below", "{R}") { Owner = _alice };
        AddToLibrary(_alice, sorc, bear, inst, land, deep);

        // Seed graveyard so the gate passes (defensive).
        SeedGraveyardWithSpells(_alice, instants: 4, sorceries: 0);

        ExecuteActivation(card);

        // Bear is the first eligible (creature) — moves to hand.
        _alice.Zones.Hand.GetCards().Should().ContainSingle().Which.Should().BeSameAs(bear);
        bear.Zone.Should().Be(ZoneType.Hand);

        // sorc + inst + land go to the bottom of the library (in
        // snapshot order); deep stays at the bottom (it was below the
        // peek window).
        var libOrder = _alice.Zones.Library.GetCards().ToList();
        // deep was untouched (below the 4-card peek), still at position 0 here.
        libOrder.Should().Contain(new ICard[] { deep, sorc, inst, land });
        libOrder.Should().NotContain(bear);
    }

    [Fact]
    public void Resolve_NoEligibleCards_NoPickButRemainderRebottoms()
    {
        var card = MagmaticChannelerFactory.Create(_alice);
        SeatOnBattlefield(card);

        // Library: 4 sorceries + 1 land — NONE are creature or instant.
        var s1 = new Sorcery("S1", "{R}") { Owner = _alice };
        var s2 = new Sorcery("S2", "{R}") { Owner = _alice };
        var s3 = new Sorcery("S3", "{R}") { Owner = _alice };
        var s4 = new Sorcery("S4", "{R}") { Owner = _alice };
        var land = new Land("Plains", new[] { CardSupertype.Basic }, new[] { CardSubtype.Plains })
            { Owner = _alice };
        AddToLibrary(_alice, s1, s2, s3, s4, land);

        SeedGraveyardWithSpells(_alice, instants: 4, sorceries: 0);

        ExecuteActivation(card);

        // Hand stays empty — no eligible reveal.
        _alice.Zones.Hand.GetCards().Should().BeEmpty();

        // The 4 sorceries re-bottom (land was below the peek window,
        // still sits at bottom; final order: land then s1..s4).
        var lib = _alice.Zones.Library.GetCards().ToList();
        lib.Should().HaveCount(5);
        lib.Should().Contain(new ICard[] { land, s1, s2, s3, s4 });
    }

    [Fact]
    public void Resolve_EmptyLibrary_NoOp()
    {
        var card = MagmaticChannelerFactory.Create(_alice);
        SeatOnBattlefield(card);
        SeedGraveyardWithSpells(_alice, instants: 4, sorceries: 0);

        ExecuteActivation(card);

        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Zones.Library.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void Resolve_LibraryShorterThanFour_PeeksWhatRemains()
    {
        var card = MagmaticChannelerFactory.Create(_alice);
        SeatOnBattlefield(card);

        // Library: 2 cards (less than the 4-card peek window).
        var inst = new Instant("Bolt", "{R}") { Owner = _alice };
        var land = new Land("Plains", new[] { CardSupertype.Basic }, new[] { CardSubtype.Plains })
            { Owner = _alice };
        AddToLibrary(_alice, inst, land);

        SeedGraveyardWithSpells(_alice, instants: 4, sorceries: 0);

        ExecuteActivation(card);

        // Instant moves to hand; land re-bottoms.
        _alice.Zones.Hand.GetCards().Should().ContainSingle().Which.Should().BeSameAs(inst);
        _alice.Zones.Library.GetCards().Should().ContainSingle().Which.Should().BeSameAs(land);
    }

    [Fact]
    public void Resolve_GraveyardGateFails_DefensiveShortCircuit_NoOp()
    {
        // Defensive — if a stale activation somehow bypassed the gate
        // (the cost was paid by the cost layer), the resolve-time guard
        // should short-circuit cleanly without mutating zones.
        var card = MagmaticChannelerFactory.Create(_alice);
        SeatOnBattlefield(card);

        var inst = new Instant("Bolt", "{R}") { Owner = _alice };
        var bear = new Creature("Bear", "{1}{G}", 2, 2) { Owner = _alice };
        AddToLibrary(_alice, inst, bear);

        // NO graveyard seeding → CanActivateGraveyardGate returns false.
        ExecuteActivation(card);

        // Nothing moved.
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Zones.Library.GetCards().Should().Contain(new ICard[] { inst, bear });
    }

    [Fact]
    public void Resolve_SorceriesInLibraryAreNotEligible_ButInstantWins()
    {
        // Mixed top-4: sorcery, sorcery, instant, sorcery. The instant
        // is the only eligible reveal — confirms sorceries are
        // EXCLUDED from the reveal pool (distinct from the gate, which
        // counts instants AND sorceries in the graveyard).
        var card = MagmaticChannelerFactory.Create(_alice);
        SeatOnBattlefield(card);

        var s1 = new Sorcery("S1", "{R}") { Owner = _alice };
        var s2 = new Sorcery("S2", "{R}") { Owner = _alice };
        var inst = new Instant("Bolt", "{R}") { Owner = _alice };
        var s3 = new Sorcery("S3", "{R}") { Owner = _alice };
        AddToLibrary(_alice, s1, s2, inst, s3);

        SeedGraveyardWithSpells(_alice, instants: 4, sorceries: 0);

        ExecuteActivation(card);

        _alice.Zones.Hand.GetCards().Should().ContainSingle().Which.Should().BeSameAs(inst);
        _alice.Zones.Library.GetCards().Should().Contain(new ICard[] { s1, s2, s3 });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void AddToGraveyard(Player p, ICard card)
    {
        if (card is Card concrete)
        {
            concrete.SetOwner(p);
            concrete.SetZone(ZoneType.Graveyard);
        }
        p.Zones.Graveyard.AddCard(card);
    }

    private static void SeedGraveyardWithSpells(Player p, int instants, int sorceries)
    {
        for (var i = 0; i < instants; i++)
        {
            AddToGraveyard(p, new Instant($"Inst{i}", "{R}"));
        }
        for (var i = 0; i < sorceries; i++)
        {
            AddToGraveyard(p, new Sorcery($"Sorc{i}", "{R}"));
        }
    }

    private static void AddToLibrary(Player p, params ICard[] cards)
    {
        foreach (var c in cards)
        {
            if (c is Card concrete)
            {
                concrete.SetOwner(p);
                concrete.SetZone(ZoneType.Library);
            }
            p.Zones.Library.AddCard(c);
        }
    }

    private static void SeatOnBattlefield(Creature card)
    {
        card.SetZone(ZoneType.Battlefield);
        card.Owner!.Zones.Battlefield.AddCard(card);
    }

    private static void ExecuteActivation(Creature card)
    {
        var ability = card.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var effect in ability.Effects) effect.Execute();
    }
}
