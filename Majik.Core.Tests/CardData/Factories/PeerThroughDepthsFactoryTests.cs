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
/// Tests for <see cref="PeerThroughDepthsFactory"/> (Champions of Kamigawa,
/// {1}{U}, Instant — Arcane).
///
/// Oracle text:
///   "Look at the top five cards of your library. You may reveal an instant or
///    sorcery card from among them and put it into your hand. Put the rest on
///    the bottom of your library in any order."
///
/// Covers:
/// - Identity: name, mana cost, Instant type, Arcane subtype, color (blue),
///   owner/controller.
/// - <see cref="NamedCardFactory"/> dispatcher entry.
/// - Resolve: instant in top 5 → goes to hand; rest go to bottom.
/// - Resolve: sorcery in top 5 → goes to hand; rest go to bottom.
/// - Resolve: no instant/sorcery in top 5 → nothing to hand; all go to bottom.
/// - Resolve: "may" decline (picker returns null) → nothing to hand; all bottom.
/// - Resolve: only looks at top 5 even with a deeper library.
/// - Resolve: short library (fewer than 5) — no throw, partial peek.
/// - Resolve: empty library — clean no-op.
/// </summary>
[Trait("Color", "U")]
public class PeerThroughDepthsFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // ── Identity ─────────────────────────────────────────────────────────────

    [Fact]
    public void Identity_Name_ManaCost_Type_Subtype()
    {
        var card = PeerThroughDepthsFactory.Create(_alice);

        card.Name.Should().Be("Peer Through Depths");
        card.ManaCost.Should().Be("{1}{U}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.HasSubtype(CardSubtype.Arcane).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
        card.Should().BeOfType<Instant>();
    }

    [Fact]
    public void NamedCardFactory_Dispatches_PeerThroughDepths()
    {
        var card = NamedCardFactory.Create("Peer Through Depths", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Peer Through Depths");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.HasSubtype(CardSubtype.Arcane).Should().BeTrue();
        card.ManaCost.Should().Be("{1}{U}");
    }

    // ── Resolve ──────────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_InstantInTopFive_GoesToHand_RestGoToBottom()
    {
        // Library top → bottom: creature, instant, land, land, sorcery, deep.
        var creature = new Creature("Bear", "{1}{G}", 2, 2);
        var instant = new Instant("Lightning Bolt", "{R}");
        var land1 = new Land("Island", new[] { CardSupertype.Basic }, new[] { CardSubtype.Island });
        var land2 = new Land("Plains", new[] { CardSupertype.Basic }, new[] { CardSubtype.Plains });
        var sorcery = new Sorcery("Ponder", "{U}");
        var deep = new Instant("Counterspell", "{U}{U}"); // below the look window

        SeedLibrary(_alice, creature, instant, land1, land2, sorcery, deep);

        // Deterministic pick: take the first eligible (the instant) to hand.
        var result = Resolve(eligible => eligible[0]);

        result.Peeked.Should().HaveCount(5, "only the top five were looked at");
        result.Eligible.Should().Equal(instant, sorcery);
        result.Picked.Should().BeSameAs(instant);

        _alice.Zones.Hand.GetCards().Should().ContainSingle().Which.Should().BeSameAs(instant);
        instant.Zone.Should().Be(ZoneType.Hand);

        // Four peeked non-picks rebottomed; deep stays above them.
        var lib = _alice.Zones.Library.GetCards().ToList();
        lib.Should().HaveCount(5);
        lib.Should().NotContain(instant);
        lib[0].Should().BeSameAs(deep, "deep was below the look window and untouched");
    }

    [Fact]
    public void Resolve_SorceryPicked_GoesToHand_RestGoToBottom()
    {
        var land1 = new Land("Island", new[] { CardSupertype.Basic }, new[] { CardSubtype.Island });
        var instant = new Instant("Bolt", "{R}");
        var sorcery = new Sorcery("Ponder", "{U}");
        var land2 = new Land("Plains", new[] { CardSupertype.Basic }, new[] { CardSubtype.Plains });
        var land3 = new Land("Forest", new[] { CardSupertype.Basic }, new[] { CardSubtype.Forest });

        SeedLibrary(_alice, land1, instant, sorcery, land2, land3);

        // Pick the sorcery specifically.
        var result = Resolve(eligible => eligible.First(c => ReferenceEquals(c, sorcery)));

        result.Eligible.Should().Equal(instant, sorcery);
        result.Picked.Should().BeSameAs(sorcery);

        _alice.Zones.Hand.GetCards().Should().ContainSingle().Which.Should().BeSameAs(sorcery);
        sorcery.Zone.Should().Be(ZoneType.Hand);

        var lib = _alice.Zones.Library.GetCards().ToList();
        lib.Should().HaveCount(4);
        lib.Should().NotContain(sorcery);
    }

    [Fact]
    public void Resolve_NoInstantOrSorceryInTopFive_NothingToHand_AllGoToBottom()
    {
        var l1 = new Land("Island", new[] { CardSupertype.Basic }, new[] { CardSubtype.Island });
        var l2 = new Land("Plains", new[] { CardSupertype.Basic }, new[] { CardSubtype.Plains });
        var l3 = new Land("Forest", new[] { CardSupertype.Basic }, new[] { CardSubtype.Forest });
        var c1 = new Creature("Bear", "{1}{G}", 2, 2);
        var c2 = new Creature("Ox", "{2}{W}", 1, 4);
        var deep = new Sorcery("Deep", "{B}"); // below window

        SeedLibrary(_alice, l1, l2, l3, c1, c2, deep);

        var result = Resolve(choosePick: null); // no eligible → no prompt fires

        result.Peeked.Should().HaveCount(5);
        result.Eligible.Should().BeEmpty();
        result.Picked.Should().BeNull();

        _alice.Zones.Hand.GetCards().Should().BeEmpty();

        var lib = _alice.Zones.Library.GetCards().ToList();
        lib.Should().HaveCount(6, "5 rebottomed + 1 deep untouched");
        lib.Should().Contain(new ICard[] { deep, l1, l2, l3, c1, c2 });
    }

    [Fact]
    public void Resolve_MayDeclined_NothingToHand_AllGoToBottom()
    {
        var instant = new Instant("Shock", "{R}");
        var l1 = new Land("Island", new[] { CardSupertype.Basic }, new[] { CardSubtype.Island });
        var l2 = new Land("Plains", new[] { CardSupertype.Basic }, new[] { CardSubtype.Plains });

        SeedLibrary(_alice, instant, l1, l2);

        var result = Resolve(_ => null); // explicitly decline the "may"

        result.Eligible.Should().ContainSingle().Which.Should().BeSameAs(instant);
        result.Picked.Should().BeNull("controller declined the may");

        _alice.Zones.Hand.GetCards().Should().BeEmpty();

        var lib = _alice.Zones.Library.GetCards().ToList();
        lib.Should().HaveCount(3, "all three peeked cards rebottomed");
        lib.Should().Contain(new ICard[] { instant, l1, l2 });
    }

    [Fact]
    public void Resolve_DefaultPicker_TakesFirstEligible()
    {
        // No choosePick override and no registered agent → deterministic
        // first-eligible fallback (CR 603.6c "may" resolved by the engine).
        var sorcery = new Sorcery("Ponder", "{U}");
        var instant = new Instant("Bolt", "{R}");

        SeedLibrary(_alice, sorcery, instant);

        var result = Resolve(choosePick: null);

        result.Picked.Should().BeSameAs(sorcery, "first eligible card is the deterministic fallback");
        _alice.Zones.Hand.GetCards().Should().ContainSingle().Which.Should().BeSameAs(sorcery);
    }

    [Fact]
    public void Resolve_ShortLibrary_OnlyOneCard_NoThrow()
    {
        var instant = new Instant("Bolt", "{R}");
        SeedLibrary(_alice, instant);

        var result = Resolve(eligible => eligible[0]);

        result.Peeked.Should().HaveCount(1);
        result.Picked.Should().BeSameAs(instant);
        _alice.Zones.Hand.GetCards().Should().ContainSingle().Which.Should().BeSameAs(instant);
        _alice.Zones.Library.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void Resolve_EmptyLibrary_CleanNoOp()
    {
        var result = Resolve(eligible => eligible.FirstOrDefault());

        result.Peeked.Should().BeEmpty();
        result.Eligible.Should().BeEmpty();
        result.Picked.Should().BeNull();
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Zones.Library.GetCards().Should().BeEmpty();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Run the resolve effect synchronously (legacy <see cref="IEffect.Execute"/>
    /// path → no live agent) with the supplied picker, returning the observed
    /// <see cref="PeerThroughDepthsFactory.Result"/>.
    /// </summary>
    private PeerThroughDepthsFactory.Result Resolve(
        System.Func<IReadOnlyList<ICard>, ICard?>? choosePick)
    {
        PeerThroughDepthsFactory.Result? captured = null;
        var effects = PeerThroughDepthsFactory.BuildResolveEffect(
            _alice, choosePick, r => captured = r);

        foreach (var e in effects) e.Execute();

        captured.Should().NotBeNull();
        return captured!;
    }

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
}
