using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.CardData.SpellTemplates.Templates.Resource;
using Majik.Core.Game;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.SpellTemplates.Templates.Resource;

public class ResourceTemplateTests
{
    private static SpellBindContext Ctx(string text) =>
        new(new CardEntity { Name = "X", OracleText = text },
            new Player("A", 20), _ => _, null, null);

    [Fact]
    public void DrawCardsTemplate_MatchesDrawThreeCards()
    {
        new DrawCardsTemplate().TryBind(Ctx("Draw three cards."))
            .Should().NotBeNull();
    }

    /// <summary>
    /// Cantrip-factory-harvest pay-down: the shared "Draw N cards" prod cantrip
    /// path (DrawCardsTemplate → ResourceSpellFactory.DrawNSpell) must route its
    /// draws through the centralised <see cref="Majik.Core.Primitives.Fx.DrawCards"/>
    /// primitive so a draw that exhausts the library flags the draw-from-empty
    /// state-based loss (CR 120.3 / 704.5b) — exactly as Opt / Serum Visions /
    /// the JSON <c>draw_card</c> verb already do. The legacy hand-rolled draw
    /// loop silently returned without setting the flag, so a "Draw 2 cards"
    /// resolving against a one-card library would NOT mark the caster for the
    /// SBA loss.
    /// </summary>
    [Fact]
    public void DrawCardsTemplate_DrawingPastEmptyLibrary_FlagsDrawFromEmptySba()
    {
        var caster = new Player("A", 20);
        var only = new Majik.Core.Cards.Sorcery("Filler", "{1}");
        caster.Zones.Library.AddCard(only);
        only.SetZone(Majik.Core.Zones.ZoneType.Library);

        var def = new DrawCardsTemplate().TryBind(
            new SpellBindContext(
                new CardEntity { Name = "X", OracleText = "Draw two cards." },
                caster, _ => _, null, null));
        def.Should().NotBeNull();

        var chosen = new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: Array.Empty<IReadOnlyList<object>>(),
            Mana: Majik.Core.Players.Agents.ManaPayment.Empty);
        foreach (var fx in def!.EffectFactory(chosen)) fx.Execute();

        // The single available card was drawn; the second draw hit an empty
        // library and must flag the SBA loss.
        caster.Zones.Hand.GetCards().Should().Contain(only);
        caster.TriedToDrawFromEmptyLibrary.Should().BeTrue();
    }

    [Fact]
    public void DiscardTemplate_MatchesTargetPlayerDiscardsTwo()
    {
        new DiscardTemplate().TryBind(Ctx("Target player discards two cards."))
            .Should().NotBeNull();
    }

    [Fact]
    public void GainLifeTemplate_MatchesTargetPlayerGainsFive()
    {
        new GainLifeTemplate().TryBind(Ctx("Target player gains 5 life."))
            .Should().NotBeNull();
    }

    [Fact]
    public void YouGainLifeTemplate_MatchesYouGainThreeLife()
    {
        new YouGainLifeTemplate().TryBind(Ctx("You gain 3 life."))
            .Should().NotBeNull();
    }

    [Fact]
    public void YouLoseLifeTemplate_MatchesYouLoseTwoLife()
    {
        new YouLoseLifeTemplate().TryBind(Ctx("You lose 2 life."))
            .Should().NotBeNull();
    }

    [Fact]
    public void EachPlayerDrawsTemplate_MatchesEachPlayerDrawsACard()
    {
        new EachPlayerDrawsTemplate().TryBind(Ctx("Each player draws a card."))
            .Should().NotBeNull();
    }

    [Fact]
    public void TargetPlayerLosesLifeTemplate_MatchesTargetPlayerLosesFourLife()
    {
        new TargetPlayerLosesLifeTemplate().TryBind(Ctx("Target player loses 4 life."))
            .Should().NotBeNull();
    }
}
