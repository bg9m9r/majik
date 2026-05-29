using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="OverlordOfTheBalemurkFactory"/> — Overlord of the
/// Balemurk (Duskmourn: House of Horror, {3}{B}{B}). Enchantment Creature —
/// Avatar Horror 5/5.
///
/// Covers:
///   - Card shape (name, types Creature + Enchantment, Avatar + Horror
///     subtypes, {3}{B}{B}, 5/5).
///   - Impending 5 marker keyword (mechanic deferred; marker present).
///   - Two enters-or-attacks triggered abilities (ETB + attack).
///   - NamedCardFactory dispatch.
///   - Trigger body: mill four, then optionally return a non-Avatar
///     creature / planeswalker from the whole graveyard to hand.
///   - non-Avatar filter excludes Avatar creatures.
///   - "you may" decline (selector returns null) is a no-op.
///   - Empty / no-eligible graveyard → clean no-op (still mills).
/// </summary>
public class OverlordOfTheBalemurkTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Card identity / types
    // -----------------------------------------------------------------------

    [Fact]
    public void Overlord_IsEnchantmentCreature_AvatarHorror_FiveFive()
    {
        var c = OverlordOfTheBalemurkFactory.Create(_alice);

        c.Name.Should().Be("Overlord of the Balemurk");
        c.ManaCost.Should().Be("{3}{B}{B}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasType(CardType.Enchantment).Should().BeTrue();
        c.HasSubtype(CardSubtype.Avatar).Should().BeTrue();
        c.HasSubtype(CardSubtype.Horror).Should().BeTrue();
        c.BasePower.Should().Be(5);
        c.BaseToughness.Should().Be(5);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Overlord_HasImpendingMarker_WithCount5()
    {
        var c = OverlordOfTheBalemurkFactory.Create(_alice);

        var impending = c.Abilities.OfType<KeywordAbility>()
            .SingleOrDefault(k => k.Keyword == "Impending");
        impending.Should().NotBeNull();
        impending!.Arg.Should().Be(5);
    }

    [Fact]
    public void Overlord_HasTwoTriggers_EntersAndAttacks()
    {
        var c = OverlordOfTheBalemurkFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2,
            "Overlord prints one ability that triggers on enters OR attacks "
            + "— modelled as two TriggeredAbility instances sharing an effect.");
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Overlord()
    {
        var card = NamedCardFactory.Create("Overlord of the Balemurk", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Overlord of the Balemurk");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasType(CardType.Enchantment).Should().BeTrue();
        card.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).Should().Contain("Impending");
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2);
    }

    // -----------------------------------------------------------------------
    // Trigger body: mill 4, then may return non-Avatar creature / planeswalker
    // -----------------------------------------------------------------------

    [Fact]
    public void Trigger_MillsFour_FromLibrary()
    {
        SeedLibrary(_alice, 10);

        var overlord = OverlordOfTheBalemurkFactory.Create(
            _alice, triggers: null, returnSelector: _ => null);
        ResolveFirstTrigger(overlord);

        _alice.Zones.Library.GetCards().Should().HaveCount(6);
        _alice.Zones.Graveyard.GetCards().Should().HaveCount(4);
    }

    [Fact]
    public void Trigger_ReturnsSelectedNonAvatarCreature_ToHand()
    {
        // Seed graveyard with an eligible creature, a planeswalker, an Avatar
        // creature (ineligible), and a non-creature non-walker (ineligible).
        var beast = NewCreature("Beast", subtypes: new[] { CardSubtype.Beast });
        var walker = NewPlaneswalker("Planey");
        var avatar = NewCreature("Avatar-Guy", subtypes: new[] { CardSubtype.Avatar });
        var sorcery = NewCard("Some-Sorcery", CardType.Sorcery);
        foreach (var card in new ICard[] { beast, walker, avatar, sorcery })
            PutInGraveyard(_alice, card);

        // Empty library so the mill adds nothing new to the graveyard.
        var overlord = OverlordOfTheBalemurkFactory.Create(
            _alice,
            triggers: null,
            returnSelector: candidates => candidates.First(c => ReferenceEquals(c, beast)));

        ResolveFirstTrigger(overlord);

        _alice.Zones.Hand.GetCards().Should().Contain(beast);
        beast.Zone.Should().Be(ZoneType.Hand);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(beast);
        // Ineligible cards stay put.
        _alice.Zones.Graveyard.GetCards().Should().Contain(new ICard[] { walker, avatar, sorcery });
    }

    [Fact]
    public void Trigger_AvatarCreature_IsNotEligible()
    {
        var avatar = NewCreature("Avatar-Guy", subtypes: new[] { CardSubtype.Avatar });
        PutInGraveyard(_alice, avatar);

        OverlordOfTheBalemurkFactory.IsEligibleReturn(avatar).Should().BeFalse();

        // With only an Avatar creature in the yard, the may-return picks
        // nothing even when the selector would accept the first candidate.
        var overlord = OverlordOfTheBalemurkFactory.Create(
            _alice,
            triggers: null,
            returnSelector: candidates => candidates.Count > 0 ? candidates[0] : null);
        ResolveFirstTrigger(overlord);

        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Zones.Graveyard.GetCards().Should().Contain(avatar);
    }

    [Fact]
    public void Trigger_PlaneswalkerIsEligible()
    {
        var walker = NewPlaneswalker("Planey");
        OverlordOfTheBalemurkFactory.IsEligibleReturn(walker).Should().BeTrue();
    }

    [Fact]
    public void Trigger_DeclineToReturn_IsNoOp_ButStillMills()
    {
        var beast = NewCreature("Beast", subtypes: new[] { CardSubtype.Beast });
        PutInGraveyard(_alice, beast);
        SeedLibrary(_alice, 5);

        var overlord = OverlordOfTheBalemurkFactory.Create(
            _alice, triggers: null, returnSelector: _ => null);
        ResolveFirstTrigger(overlord);

        // Declined — Beast stays in the graveyard.
        _alice.Zones.Hand.GetCards().Should().NotContain(beast);
        _alice.Zones.Graveyard.GetCards().Should().Contain(beast);
        // Still milled four.
        _alice.Zones.Library.GetCards().Should().HaveCount(1);
    }

    [Fact]
    public void Trigger_EmptyGraveyardAfterMill_IsCleanNoOp()
    {
        // Empty library + empty graveyard. Resolving must not throw and
        // must leave hand empty.
        var overlord = OverlordOfTheBalemurkFactory.Create(_alice);

        Action resolve = () => ResolveFirstTrigger(overlord);
        resolve.Should().NotThrow();
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void ResolveFirstTrigger(Creature overlord)
    {
        var trigger = overlord.Abilities.OfType<TriggeredAbility>().First();
        foreach (var eff in trigger.Effects)
            eff.Execute();
    }

    private static void SeedLibrary(Player p, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var card = new Card($"Lib-{i}", "");
            card.SetOwner(p);
            card.SetZone(ZoneType.Library);
            p.Zones.Library.AddCard(card);
        }
    }

    private static Creature NewCreature(string name, IEnumerable<CardSubtype>? subtypes = null)
        => new(name, "{1}{B}", 2, 2, subtypes: subtypes);

    private static Planeswalker NewPlaneswalker(string name)
        => new(name, "{2}{B}", 3);

    private static Card NewCard(string name, CardType type)
    {
        var c = new Card(name, "{1}");
        c.AddCardType(type);
        return c;
    }

    private static void PutInGraveyard(Player p, ICard card)
    {
        card.SetOwner(p);
        card.SetZone(ZoneType.Graveyard);
        p.Zones.Graveyard.AddCard(card);
    }
}
