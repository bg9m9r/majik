using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Pays down the <c>manland-color-identity-layer5</c> deferral: when the
/// <see cref="ManlandBinder"/> animates a creature-land, the colour words it
/// already parses out of the "becomes a N/N &lt;colours&gt; &lt;Subtype&gt;
/// creature" body are wired into a Layer-5 <see cref="SetColorsEffect"/>
/// (CR 613.1e) attached alongside the existing Layer-4 type grant + Layer-7b
/// P/T set. Before this, the colours lived only in the effect-name string and
/// the animated body computed as colourless — wrong for "is/becomes &lt;colour&gt;"
/// interactions (protection, Painter, devotion off the animated body, etc.).
///
/// <para>The colour effect expires at end of turn like the rest of the
/// animation (CR 514.2), so the land reverts to its printed colourless state.</para>
/// </summary>
public class ManlandBinderColorIdentityTests
{
    private readonly Player _alice = new("Alice", 20);

    // Creeping Tar Pit — "{1}{U}{B}: Until end of turn, this land becomes a 3/2
    // blue and black Elemental creature. It's still a land. It can't be blocked
    // this turn." (Scryfall exact, 2026-06-13.)
    private static CardEntity CreepingTarPitEntity() => new()
    {
        Name = "Creeping Tar Pit",
        TypeLine = "Land",
        OracleText =
            "This land enters tapped.\n" +
            "{T}: Add {U} or {B}.\n" +
            "{1}{U}{B}: Until end of turn, this land becomes a 3/2 blue and black " +
            "Elemental creature. It's still a land. It can't be blocked this turn.",
    };

    // Celestial Colonnade — "{3}{W}{U}: Until end of turn, this land becomes a
    // 4/4 white and blue Elemental creature with flying and vigilance. It's
    // still a land." (Scryfall exact, 2026-06-13.)
    private static CardEntity CelestialColonnadeEntity() => new()
    {
        Name = "Celestial Colonnade",
        TypeLine = "Land",
        OracleText =
            "This land enters tapped.\n" +
            "{T}: Add {W} or {U}.\n" +
            "{3}{W}{U}: Until end of turn, this land becomes a 4/4 white and blue " +
            "Elemental creature with flying and vigilance. It's still a land.",
    };

    private Land Animate(CardEntity entity, ContinuousEffectsService effects)
    {
        var land = new Land(entity.Name) { Owner = _alice, Controller = _alice };
        ManlandBinder.Bind(land, entity, _alice, effects).Should().BeTrue();
        land.SetZone(ZoneType.Battlefield);

        var animate = land.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Cast<ActivatedAbility>()
            .Single();
        animate.Resolve();
        return land;
    }

    [Fact]
    public void Animate_RegistersLayer5ColorEffect_TarPit_BlueAndBlack()
    {
        var effects = new ContinuousEffectsService();
        var land = Animate(CreepingTarPitEntity(), effects);

        var colorEffect = GetRegisteredEffects(effects)
            .OfType<SetColorsEffect>()
            .SingleOrDefault(e => ReferenceEquals(e.Source, land));
        colorEffect.Should().NotBeNull("the binder must wire the parsed colours into a Layer-5 effect");
        colorEffect!.Layer.Should().Be(Layer.Color);
        colorEffect.ExpiresAtEndOfTurn.Should().BeTrue("the colour change ends at cleanup (CR 514.2)");
    }

    [Fact]
    public void Animate_ComputesBlueAndBlack_TarPit()
    {
        var effects = new ContinuousEffectsService();
        var land = Animate(CreepingTarPitEntity(), effects);

        var chars = effects.Compute(land);
        chars.Colors.Should().BeEquivalentTo(new[] { ManaColor.Blue, ManaColor.Black });
    }

    [Fact]
    public void Animate_ComputesWhiteAndBlue_Colonnade()
    {
        var effects = new ContinuousEffectsService();
        var land = Animate(CelestialColonnadeEntity(), effects);

        var chars = effects.Compute(land);
        chars.Colors.Should().BeEquivalentTo(new[] { ManaColor.White, ManaColor.Blue });
    }

    [Fact]
    public void Animate_EndOfTurn_RevertsToColorless()
    {
        var effects = new ContinuousEffectsService();
        var land = Animate(CreepingTarPitEntity(), effects);

        effects.Compute(land).Colors.Should().NotBeEmpty();

        // CR 514.2 — "until end of turn" effects end during the cleanup step.
        effects.ExpireEndOfTurn();

        GetRegisteredEffects(effects)
            .OfType<SetColorsEffect>()
            .Where(e => ReferenceEquals(e.Source, land))
            .Should().BeEmpty();
        effects.Compute(land).Colors.Should().BeEmpty(
            "a printed manland is colourless once the animation wears off");
    }

    private static IEnumerable<ContinuousEffect> GetRegisteredEffects(ContinuousEffectsService svc)
    {
        var field = typeof(ContinuousEffectsService).GetField(
            "_effects",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var list = (System.Collections.IEnumerable)field!.GetValue(svc)!;
        foreach (var e in list) yield return (ContinuousEffect)e;
    }
}
