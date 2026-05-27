using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="AugurOfBolasFactory"/> ({1}{U}, Creature — Merfolk Wizard 1/3).
///
/// Oracle text:
///   "When this creature enters, look at the top three cards of your library.
///    You may reveal an instant or sorcery card from among them and put it
///    into your hand. Put the rest on the bottom of your library in any order."
///
/// Covers:
/// - Identity: name, mana cost, type, subtypes (Merfolk Wizard), P/T 1/3,
///   color (blue), owner/controller.
/// - <see cref="NamedCardFactory"/> dispatcher entry.
/// - Exactly one ETB <see cref="TriggeredAbility"/> active on the Battlefield.
/// - ETB: instant in top 3 → goes to hand; other two go to bottom of library.
/// - ETB: sorcery in top 3 → goes to hand; others go to bottom of library.
/// - ETB: no instant/sorcery in top 3 → nothing to hand; all three go to bottom.
/// - ETB: "may" decline (picker returns null) → nothing to hand; all to bottom.
/// - ETB: short library (fewer than 3 cards) — no throw, partial peek.
/// - ETB: empty library — clean no-op.
/// </summary>
public class AugurOfBolasFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // ── Identity ─────────────────────────────────────────────────────────────

    [Fact]
    public void Identity_Name_ManaCost_Type_Subtypes_PT()
    {
        var card = AugurOfBolasFactory.Create(_alice);

        card.Name.Should().Be("Augur of Bolas");
        card.ManaCost.Should().Be("{1}{U}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Merfolk).Should().BeTrue();
        card.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);

        var creature = card.Should().BeOfType<Creature>().Subject;
        creature.BasePower.Should().Be(1);
        creature.BaseToughness.Should().Be(3);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_AugurOfBolas()
    {
        var card = NamedCardFactory.Create("Augur of Bolas", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Augur of Bolas");
        card.HasSubtype(CardSubtype.Merfolk).Should().BeTrue();
        card.HasSubtype(CardSubtype.Wizard).Should().BeTrue();

        var creature = (Creature)card;
        creature.BasePower.Should().Be(1);
        creature.BaseToughness.Should().Be(3);
    }

    [Fact]
    public void Card_HasExactlyOneEtbTriggeredAbility_ActiveOnBattlefield()
    {
        var card = AugurOfBolasFactory.Create(_alice);

        var etb = card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "Augur of Bolas has one triggered ability — the ETB look-at-top-3 clause.")
            .And.Subject.Single();

        etb.ActiveZones.Should().Contain(ZoneType.Battlefield,
            "the ETB trigger is only active while the permanent is on the Battlefield");
    }

    // ── ETB resolution ───────────────────────────────────────────────────────

    [Fact]
    public void Etb_InstantInTopThree_GoesToHand_OthersTwoGoToBottom()
    {
        // Library top → bottom:
        //   creature (ineligible)
        //   instant  (eligible — first eligible instant/sorcery)
        //   land     (ineligible)
        //   deep     (below look window — untouched)
        var creature = new Creature("Bear", "{1}{G}", 2, 2);
        var instant = new Instant("Lightning Bolt", "{R}");
        var land = new Land("Plains", new[] { CardSupertype.Basic }, new[] { CardSubtype.Plains });
        var deep = new Sorcery("Deep", "{B}");

        SeedLibrary(_alice, creature, instant, land, deep);

        AugurOfBolasFactory.Result? result = null;
        var card = AugurOfBolasFactory.Create(_alice, triggers: null, choosePick: null, onEtbResolved: r => result = r);

        FireEtb(card);

        result.Should().NotBeNull();
        result!.Peeked.Should().HaveCount(3, "only the top three were looked at");
        result.Eligible.Should().ContainSingle().Which.Should().BeSameAs(instant);
        result.Picked.Should().BeSameAs(instant);

        // Instant is now in hand.
        _alice.Zones.Hand.GetCards().Should().ContainSingle().Which.Should().BeSameAs(instant);
        instant.Zone.Should().Be(ZoneType.Hand);

        // Creature and land go to the bottom; deep remains above them.
        var lib = _alice.Zones.Library.GetCards().ToList();
        lib.Should().HaveCount(3, "creature + land rebottomed, deep was untouched");
        lib.Should().NotContain(instant);
    }

    [Fact]
    public void Etb_SorceryInTopThree_GoesToHand_OthersGoToBottom()
    {
        var land1 = new Land("Island", new[] { CardSupertype.Basic }, new[] { CardSubtype.Island });
        var sorcery = new Sorcery("Ponder", "{U}");
        var land2 = new Land("Plains", new[] { CardSupertype.Basic }, new[] { CardSubtype.Plains });

        SeedLibrary(_alice, land1, sorcery, land2);

        AugurOfBolasFactory.Result? result = null;
        var card = AugurOfBolasFactory.Create(_alice, triggers: null, choosePick: null, onEtbResolved: r => result = r);
        FireEtb(card);

        result!.Picked.Should().BeSameAs(sorcery);
        _alice.Zones.Hand.GetCards().Should().ContainSingle().Which.Should().BeSameAs(sorcery);
        sorcery.Zone.Should().Be(ZoneType.Hand);

        var lib = _alice.Zones.Library.GetCards().ToList();
        lib.Should().HaveCount(2);
        lib.Should().NotContain(sorcery);
    }

    [Fact]
    public void Etb_NoInstantOrSorceryInTopThree_NothingToHand_AllGoToBottom()
    {
        // Library top → bottom: 3 lands, 1 deep creature (below window).
        var l1 = new Land("Island", new[] { CardSupertype.Basic }, new[] { CardSubtype.Island });
        var l2 = new Land("Plains", new[] { CardSupertype.Basic }, new[] { CardSubtype.Plains });
        var l3 = new Land("Forest", new[] { CardSupertype.Basic }, new[] { CardSubtype.Forest });
        var deep = new Creature("Bear", "{1}{G}", 2, 2);

        SeedLibrary(_alice, l1, l2, l3, deep);

        AugurOfBolasFactory.Result? result = null;
        var card = AugurOfBolasFactory.Create(_alice, triggers: null, choosePick: null, onEtbResolved: r => result = r);
        FireEtb(card);

        result!.Eligible.Should().BeEmpty();
        result.Picked.Should().BeNull();

        _alice.Zones.Hand.GetCards().Should().BeEmpty();

        // All three go to bottom; deep was untouched.
        var lib = _alice.Zones.Library.GetCards().ToList();
        lib.Should().HaveCount(4, "3 re-bottomed + 1 deep untouched");
        lib.Should().Contain(new ICard[] { deep, l1, l2, l3 });
    }

    [Fact]
    public void Etb_MayDeclined_NothingToHand_AllThreeGoToBottom()
    {
        // Eligible card present but picker returns null (controller declines).
        var instant = new Instant("Shock", "{R}");
        var l1 = new Land("Island", new[] { CardSupertype.Basic }, new[] { CardSubtype.Island });
        var l2 = new Land("Plains", new[] { CardSupertype.Basic }, new[] { CardSubtype.Plains });

        SeedLibrary(_alice, instant, l1, l2);

        AugurOfBolasFactory.Result? result = null;
        var card = AugurOfBolasFactory.Create(
            _alice, triggers: null,
            choosePick: _ => null,   // explicitly decline the "may"
            onEtbResolved: r => result = r);
        FireEtb(card);

        result!.Eligible.Should().ContainSingle().Which.Should().BeSameAs(instant);
        result.Picked.Should().BeNull("controller declined the may");

        _alice.Zones.Hand.GetCards().Should().BeEmpty();

        var lib = _alice.Zones.Library.GetCards().ToList();
        lib.Should().HaveCount(3, "all three re-bottomed");
        lib.Should().Contain(new ICard[] { instant, l1, l2 });
    }

    [Fact]
    public void Etb_ShortLibrary_OnlyOneCard_NoThrow()
    {
        var instant = new Instant("Bolt", "{R}");
        SeedLibrary(_alice, instant);

        AugurOfBolasFactory.Result? result = null;
        var card = AugurOfBolasFactory.Create(_alice, triggers: null, choosePick: null, onEtbResolved: r => result = r);
        FireEtb(card);

        result!.Peeked.Should().HaveCount(1);
        result.Picked.Should().BeSameAs(instant);

        _alice.Zones.Hand.GetCards().Should().ContainSingle().Which.Should().BeSameAs(instant);
        _alice.Zones.Library.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void Etb_EmptyLibrary_CleanNoOp()
    {
        var card = AugurOfBolasFactory.Create(_alice);
        FireEtb(card);

        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Zones.Library.GetCards().Should().BeEmpty();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void SeedLibrary(Player p, params ICard[] cards)
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

    private static void FireEtb(Creature card)
    {
        var etb = card.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in etb.Effects) e.Execute();
    }
}
