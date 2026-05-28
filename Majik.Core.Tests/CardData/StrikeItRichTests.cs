using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="StrikeItRichFactory"/>.
///
/// Card: Strike It Rich — Sorcery {R} (Streets of New Capenna).
///   "Create a Treasure token.
///    Flashback {2}{R}."
///
/// Covers:
///   - Identity + <see cref="NamedCardFactory"/> dispatch.
///   - Flashback alt-cost surfaced as {2}{R} via the oracle binder
///     (<see cref="FlashbackOracleParser"/>).
///   - Resolve: creates exactly one Treasure token under the caster's
///     control; token is an artifact, colourless, on the battlefield.
///   - Treasure token shape: HasType(Artifact), IsToken, five ManaAbility
///     options (any-colour sac-for-mana, CR 111.10).
///   - Flashback cast from graveyard: same resolve effect; cost's
///     <c>OnResolved</c> exiles the card (CR 702.33b).
///   - Flashback gating: cannot cast from hand or battlefield.
/// </summary>
public class StrikeItRichTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void StrikeItRich_Identity()
    {
        var c = StrikeItRichFactory.Create(_alice);

        c.Name.Should().Be("Strike It Rich");
        c.ManaCost.Should().Be("{R}");
        c.HasType(CardType.Sorcery).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_StrikeItRich()
    {
        var card = NamedCardFactory.Create("Strike It Rich", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Strike It Rich");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.Should().Be("{R}");
        card.Owner.Should().BeSameAs(_alice);
    }

    [Fact]
    public void FlashbackCost_ParsedFromOracle_Is2R()
    {
        var fb = StrikeItRichFactory.BuildFlashbackCost();

        fb.AlternativeManaCost.Should().Be(ManaCost.Parse("2R"));
        fb.Description.Should().Contain("Flashback");
    }

    // -----------------------------------------------------------------------
    // Resolve: create exactly one Treasure token
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_CreatesExactlyOneTreasureToken_OnBattlefield()
    {
        // Alice controls nothing before resolve.
        _alice.Zones.Battlefield.GetCards().Should().BeEmpty();

        var effects = StrikeItRichFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        // Exactly one new permanent appears on Alice's battlefield.
        var battlefield = _alice.Zones.Battlefield.GetCards().ToList();
        battlefield.Should().HaveCount(1,
            "Strike It Rich creates exactly one Treasure token (CR 111.10)");

        var token = battlefield[0];
        token.Name.Should().Be("Treasure");
        token.HasType(CardType.Artifact).Should().BeTrue(
            "Treasure is an artifact (CR 111.10)");
        token.Zone.Should().Be(ZoneType.Battlefield);
        token.Owner.Should().BeSameAs(_alice);
        token.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Resolve_TreasureToken_IsToken()
    {
        var effects = StrikeItRichFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        var token = _alice.Zones.Battlefield.GetCards().Single();
        (token as Permanent)?.IsToken.Should().BeTrue(
            "Treasure is a token permanent (CR 111)");
    }

    [Fact]
    public void Resolve_TreasureToken_IsColourless()
    {
        // CR 111.10 — Treasure tokens are colourless artifacts.
        var effects = StrikeItRichFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        var token = _alice.Zones.Battlefield.GetCards().Single() as Card;
        token.Should().NotBeNull();
        // ColourIdentity for a colourless token should expose no colour pips.
        // Verified via CardColors helper used by TokenFactory.CreateTreasure.
        var colors = CardColors.GetColors(token!);
        colors.Should().BeEmpty("Treasure tokens are colourless (CR 111.10)");
    }

    [Fact]
    public void Resolve_TreasureToken_HasFiveManaAbilities_AnyColour()
    {
        // CR 111.10 — "{T}, Sacrifice this artifact: Add one mana of any
        // color." Bound as five ManaAbility options (W/U/B/R/G) so the bot's
        // mana picker can satisfy any colour pip.
        var effects = StrikeItRichFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        var token = _alice.Zones.Battlefield.GetCards().Single();
        var manaAbilities = token.Abilities
            .OfType<ManaAbility>()
            .ToList();

        manaAbilities.Should().HaveCount(5,
            "Treasure token encodes one ManaAbility per colour (W/U/B/R/G) " +
            "so the bot can satisfy any pip (TokenFactory.CreateTreasure)");
    }

    [Fact]
    public void Resolve_MultipleResolutions_CreateMultipleTokens()
    {
        // Each cast of Strike It Rich (or each flashback) creates one token.
        // Two sequential effects should yield two Treasures.
        foreach (var e in StrikeItRichFactory.BuildResolveEffect(_alice)) e.Execute();
        foreach (var e in StrikeItRichFactory.BuildResolveEffect(_alice)) e.Execute();

        _alice.Zones.Battlefield.GetCards().Should().HaveCount(2,
            "each resolution creates an independent Treasure token");
    }

    // -----------------------------------------------------------------------
    // Flashback cast: from graveyard, paying {2}{R}, then exile.
    // -----------------------------------------------------------------------

    [Fact]
    public void FlashbackCast_FromGraveyard_CreatesToken_ThenExiles()
    {
        // Strike It Rich is in Alice's graveyard (cast from grave via
        // flashback alt-cost).
        var sir = StrikeItRichFactory.Create(_alice);
        _alice.Zones.Graveyard.AddCard(sir);
        sir.SetZone(ZoneType.Graveyard);

        // Sanity: flashback cost legal here.
        var fb = StrikeItRichFactory.BuildFlashbackCost();
        fb.CanCastFor(sir, _alice).Should().BeTrue();
        fb.AlternativeManaCost.Should().Be(ManaCost.Parse("2R"));

        // Resolve side-effect: create the Treasure token.
        foreach (var e in StrikeItRichFactory.BuildResolveEffect(_alice)) e.Execute();

        _alice.Zones.Battlefield.GetCards().Should().HaveCount(1,
            "flashback resolution creates the same Treasure token as printed cast");

        // Then flashback's post-resolve hook fires — card exiles from
        // graveyard (CR 702.33b). Simulate what SpellCastFlow does in prod.
        fb.OnResolved(sir, _alice);

        sir.Zone.Should().Be(ZoneType.Exile);
        _alice.Zones.Exile.GetCards().Should().Contain(sir);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(sir);
    }

    [Fact]
    public void FlashbackCost_CannotCast_FromHandOrBattlefield()
    {
        // CR 702.33 — flashback is only legal from graveyard.
        var sir = StrikeItRichFactory.Create(_alice);
        sir.SetZone(ZoneType.Hand);

        var fb = StrikeItRichFactory.BuildFlashbackCost();
        fb.CanCastFor(sir, _alice).Should().BeFalse();
    }
}
