using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for the combined transforming-DFC name registration
/// "Fable of the Mirror-Breaker // Reflection of Kiki-Jiki"
/// (Kamigawa: Neon Dynasty, {2}{R}).
///
/// Both faces already ship as fully-built factories
/// (<see cref="FableOfTheMirrorBreakerFactory"/> front +
/// <see cref="ReflectionOfKikiJikiFactory"/> back). This combined-name
/// factory is the transforming-DFC alias precedent — the same posture as
/// <c>[CardName("Thing in the Ice // Awoken Horror")]</c> — so the printed
/// Scryfall name (which the embedded seed keys on) resolves through
/// <see cref="NamedCardFactory"/> dispatch and flips <c>IsImplemented</c>.
///
/// The combined factory hands back the FRONT face (the Saga) — exactly what
/// a transforming DFC enters the battlefield as (CR 712.4: it starts on its
/// front face). Behavioural coverage of the chapters / transform / copy
/// ability lives in <c>FableOfTheMirrorBreakerTests</c>; this suite only
/// asserts the combined-name dispatch + base front-face identity.
/// </summary>
public class FableOfTheMirrorBreakerReflectionOfKikiJikiFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private const string CombinedName =
        "Fable of the Mirror-Breaker // Reflection of Kiki-Jiki";

    [Fact]
    public void Create_BuildsFrontFace_RedEnchantmentSaga_AtCost2R()
    {
        var card = FableOfTheMirrorBreakerReflectionOfKikiJikiFactory.Create(_alice);

        // CR 712.4 — a transforming DFC enters on its front face (the Saga).
        card.Name.Should().Be("Fable of the Mirror-Breaker");
        card.ManaCost.Should().Be("{2}{R}");
        card.HasType(CardType.Enchantment).Should().BeTrue();
        card.HasSubtype(CardSubtype.Saga).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.Red);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Create_AttachesMdfcState_WithBothFaceNames()
    {
        var card = FableOfTheMirrorBreakerReflectionOfKikiJikiFactory.Create(_alice);

        // CR 712 — the DFC face tracker is present so the transform target
        // (Reflection of Kiki-Jiki) is observable; starts front-face up.
        card.MdfcState.Should().NotBeNull();
        card.MdfcState!.FrontFaceName.Should().Be("Fable of the Mirror-Breaker");
        card.MdfcState.BackFaceName.Should().Be("Reflection of Kiki-Jiki");
        card.MdfcState.IsBackFace.Should().BeFalse();
    }

    [Fact]
    public void NamedCardFactory_Dispatches_CombinedName_ToFrontFace()
    {
        var card = NamedCardFactory.Create(CombinedName, _alice);

        // The seed keys this card on its combined printed name; dispatch must
        // resolve it (not fall through to the vanilla unknown-name shell).
        card.Should().BeOfType<Enchantment>();
        card.Name.Should().Be("Fable of the Mirror-Breaker");
        card.HasSubtype(CardSubtype.Saga).Should().BeTrue();
    }

    [Fact]
    public void CombinedName_IsImplemented()
    {
        // Registering the combined-name [CardName] factory must flip the
        // seed's IsImplemented for the printed combined name.
        ImplementedCardNames.All.Should().Contain(CombinedName);
    }
}
