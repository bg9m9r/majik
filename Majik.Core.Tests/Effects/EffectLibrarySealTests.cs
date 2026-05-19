using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Effects;
using Xunit;

namespace Majik.Core.Tests.Effects;

/// <summary>
/// Verifies the post-Phase-2-slice-5 seal contract on EffectLibrary:
/// after any lookup, the catalog is frozen and further internal
/// registration throws.
/// </summary>
public class EffectLibrarySealTests
{
    [Fact]
    public void Lookup_TriggersSeal()
    {
        // Touch any read API.
        _ = EffectLibrary.GetEffect("damage_target");

        EffectLibrary.IsSealed.Should().BeTrue();
    }

    [Fact]
    public void Register_AfterSeal_Throws()
    {
        EffectLibrary.Initialize();
        EffectLibrary.IsSealed.Should().BeTrue();

        // Reflectively reach the internal Register (the friend assembly
        // grants access). It must reject post-seal mutations.
        var registerMethod = typeof(EffectLibrary)
            .GetMethod("Register", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        var act = () =>
        {
            try
            {
                registerMethod.Invoke(null, new object?[]
                {
                    "post_seal_test",
                    new Effect("x", () => { }),
                    new EffectMetadata("post_seal_test", "x", "x", EffectType.Other, new Dictionary<string, string>()),
                });
            }
            catch (System.Reflection.TargetInvocationException tie) when (tie.InnerException != null)
            {
                throw tie.InnerException;
            }
        };

        act.Should().Throw<InvalidOperationException>().WithMessage("*sealed*");
    }

    [Fact]
    public void BuiltIns_PresentAfterInit()
    {
        EffectLibrary.Initialize();
        EffectLibrary.IsRegistered("damage_target").Should().BeTrue();
        EffectLibrary.IsRegistered("gain_life").Should().BeTrue();
        EffectLibrary.IsRegistered("draw_cards").Should().BeTrue();
    }
}
