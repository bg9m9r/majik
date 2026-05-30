using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Effects;

/// <summary>
/// CR 105.3 / CR 613.1e — Layer 5 colour-changing effects. A permanent's
/// effective colour is computed by seeding the printed/static colour
/// (mana cost pips + colour indicator + token override + Devoid) and then
/// applying any active Layer-5 SET or ADD colour effects in timestamp
/// order. <see cref="SetColorsEffect"/> overwrites the colour set;
/// <see cref="AddColorsEffect"/> unions onto it.
/// </summary>
public class Layer5ColorEffectTests
{
    [Fact]
    public void Compute_NoColorEffect_SeedsStaticColorsFromManaCost()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2);

        var chars = svc.Compute((Permanent)bear);

        chars.Colors.Should().BeEquivalentTo(new[] { ManaColor.Green });
    }

    [Fact]
    public void Compute_SetColorsEffect_OverwritesPrintedColor()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2) { Zone = ZoneType.Battlefield }; // green printed
        svc.Register(new SetColorsEffect(
            source: bear,
            scope: p => ReferenceEquals(p, bear),
            colors: new[] { ManaColor.Blue }));

        var chars = svc.Compute((Permanent)bear);

        // CR 613.1e — SET replaces; the green pip no longer contributes.
        chars.Colors.Should().BeEquivalentTo(new[] { ManaColor.Blue });
    }

    [Fact]
    public void Compute_SetAllColors_ReportsEveryColor()
    {
        var svc = new ContinuousEffectsService();
        var artifact = new Artifact("Etched Champion", "3") { Zone = ZoneType.Battlefield }; // colorless
        svc.Register(SetColorsEffect.AllColors(
            source: artifact,
            scope: p => ReferenceEquals(p, artifact)));

        var chars = svc.Compute((Permanent)artifact);

        chars.Colors.Should().BeEquivalentTo(new[]
        {
            ManaColor.White, ManaColor.Blue, ManaColor.Black,
            ManaColor.Red, ManaColor.Green,
        });
    }

    [Fact]
    public void Compute_AddColorsEffect_UnionsOntoPrintedColor()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2) { Zone = ZoneType.Battlefield }; // green
        svc.Register(new AddColorsEffect(
            source: bear,
            scope: p => ReferenceEquals(p, bear),
            colors: new[] { ManaColor.White }));

        var chars = svc.Compute((Permanent)bear);

        // CR 613.1e — ADD keeps existing colours.
        chars.Colors.Should().BeEquivalentTo(new[] { ManaColor.Green, ManaColor.White });
    }

    [Fact]
    public void Compute_RemovingEffect_RevertsToStaticColor()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2) { Zone = ZoneType.Battlefield };
        var effect = new SetColorsEffect(
            source: bear,
            scope: p => ReferenceEquals(p, bear),
            colors: new[] { ManaColor.Blue });
        svc.Register(effect);

        svc.Compute((Permanent)bear).Colors.Should().BeEquivalentTo(new[] { ManaColor.Blue });

        svc.Unregister(effect);

        svc.Compute((Permanent)bear).Colors.Should().BeEquivalentTo(new[] { ManaColor.Green });
    }

    [Fact]
    public void Compute_SetThenAdd_AppliesInTimestampOrder()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2) { Zone = ZoneType.Battlefield }; // green
        // SET to blue (earlier), then ADD red (later) → {Blue, Red}.
        svc.Register(new SetColorsEffect(
            source: bear, scope: p => ReferenceEquals(p, bear),
            colors: new[] { ManaColor.Blue }));
        svc.Register(new AddColorsEffect(
            source: bear, scope: p => ReferenceEquals(p, bear),
            colors: new[] { ManaColor.Red }));

        var chars = svc.Compute((Permanent)bear);

        chars.Colors.Should().BeEquivalentTo(new[] { ManaColor.Blue, ManaColor.Red });
    }

    [Fact]
    public void Compute_OnNonCreaturePermanent_AppliesColorEffect()
    {
        var svc = new ContinuousEffectsService();
        var enchantment = new Enchantment("Test Enchantment", "1W") { Zone = ZoneType.Battlefield }; // white
        svc.Register(SetColorsEffect.AllColors(
            source: enchantment,
            scope: p => ReferenceEquals(p, enchantment)));

        var chars = svc.Compute((Permanent)enchantment);

        chars.Should().NotBeOfType<CreatureCharacteristics>();
        chars.Colors.Should().HaveCount(5);
    }

    [Fact]
    public void GetEffectiveColors_ConsultsLayerService_WhenActiveEffectsSet()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2) { Zone = ZoneType.Battlefield };
        bear.ActiveEffects = svc;
        svc.Register(SetColorsEffect.AllColors(
            source: bear, scope: p => ReferenceEquals(p, bear)));

        bear.GetEffectiveColors().Should().HaveCount(5);
    }

    [Fact]
    public void GetEffectiveColors_FallsBackToStatic_WhenNoActiveEffects()
    {
        var bear = new Creature("Bear", "1G", 2, 2); // ActiveEffects null

        bear.GetEffectiveColors().Should().BeEquivalentTo(new[] { ManaColor.Green });
    }

    [Fact]
    public void GetEffectiveColors_FallsBackToStatic_WhenServiceHasNoColorEffect()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2);
        bear.ActiveEffects = svc;

        bear.GetEffectiveColors().Should().BeEquivalentTo(new[] { ManaColor.Green });
    }
}
