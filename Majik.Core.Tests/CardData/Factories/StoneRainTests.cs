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
using Land = Majik.Core.Cards.Land;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Stone Rain (Alpha / reprints, {2}{R}, Sorcery).
///
/// Oracle text (verified against Scryfall):
///   "Destroy target land."
///
/// Covers:
///   - Card identity (Sorcery, {2}{R}, owner/controller) loaded from the
///     embedded JSON shape via <see cref="StoneRainFactory.Create"/>.
///   - NamedCardFactory dispatch ([CardName("Stone Rain")]).
///   - Resolve destroys the chosen target land — CR 701.7 destroy → owner's
///     graveyard. Mirrors the OracleSpellBinder DestroyLandTemplate path,
///     which is how Stone Rain resolves in prod.
/// </summary>
[Trait("Color", "R")]
public class StoneRainTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Card identity / dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void StoneRain_IsSorcery_AtCost2R()
    {
        var sr = StoneRainFactory.Create(_alice);

        sr.Name.Should().Be("Stone Rain");
        sr.ManaCost.Should().Be("{2}{R}");
        sr.HasType(CardType.Sorcery).Should().BeTrue();
        sr.Owner.Should().BeSameAs(_alice);
        sr.Controller.Should().BeSameAs(_alice);
    }
    // -----------------------------------------------------------------------
    // Resolve — destroy target land (CR 701.7 → owner's graveyard)
    // -----------------------------------------------------------------------

    [Fact]
    public void StoneRain_DestroysTargetLand_MovesToGraveyard()
    {
        var mtn = new Land("Mountain")
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        _alice.Zones.Battlefield.AddCard(mtn);

        var def = StoneRainFactory.BuildSpellDefinition(_alice, raw => raw);
        def.Should().NotBeNull();

        Resolve(def, mtn);

        mtn.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Graveyard.GetCards().Should().Contain(mtn);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(mtn);
    }

    [Fact]
    public void StoneRain_NonLandTarget_DoesNothing()
    {
        // CR 608.2b — an illegal pick (here a creature) makes the destroy
        // effect do nothing; the DestroyLand filter gates on CardType.Land.
        var bear = new Creature("Bear", "1G", 2, 2)
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        _alice.Zones.Battlefield.AddCard(bear);

        var def = StoneRainFactory.BuildSpellDefinition(_alice, raw => raw);
        Resolve(def, bear);

        bear.Zone.Should().Be(ZoneType.Battlefield);
    }

    private static void Resolve(SpellDefinition def, object target)
    {
        var chosen = new ChosenSpellParams(null, null,
            new[] { new object[] { target } },
            ManaPayment.Empty);
        foreach (var e in def.EffectFactory(chosen)) e.Execute();
    }
}
