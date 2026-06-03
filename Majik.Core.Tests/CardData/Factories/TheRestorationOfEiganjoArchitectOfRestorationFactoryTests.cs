using FluentAssertions;
using Majik.Core.Abilities;
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
/// "The Restoration of Eiganjo // Architect of Restoration"
/// (Kamigawa: Neon Dynasty, {2}{W}).
///
/// Both faces ship as fully-built factories
/// (<see cref="TheRestorationOfEiganjoFactory"/> front +
/// <see cref="ArchitectOfRestorationFactory"/> back). This combined-name
/// factory is the transforming-DFC alias precedent — the seed keys the card on
/// its printed combined name, which must resolve through
/// <see cref="NamedCardFactory"/> dispatch and flip <c>IsImplemented</c>.
/// Behavioural coverage of the chapters / transform lives in
/// <c>RestorationOfEiganjoSagaTests</c>; this suite asserts dispatch +
/// base front/back identity.
/// </summary>
public class TheRestorationOfEiganjoArchitectOfRestorationFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private const string CombinedName =
        "The Restoration of Eiganjo // Architect of Restoration";

    [Fact]
    public void Create_BuildsFrontFace_WhiteEnchantmentSaga_AtCost2W()
    {
        var card = TheRestorationOfEiganjoArchitectOfRestorationFactory.Create(_alice);

        // CR 712.4 — a transforming DFC enters on its front face (the Saga).
        card.Name.Should().Be("The Restoration of Eiganjo");
        card.ManaCost.Should().Be("{2}{W}");
        card.HasType(CardType.Enchantment).Should().BeTrue();
        card.HasSubtype(CardSubtype.Saga).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.White);
    }

    [Fact]
    public void Create_AttachesMdfcState_WithBothFaceNames()
    {
        var card = TheRestorationOfEiganjoArchitectOfRestorationFactory.Create(_alice);

        card.MdfcState.Should().NotBeNull();
        card.MdfcState!.FrontFaceName.Should().Be("The Restoration of Eiganjo");
        card.MdfcState.BackFaceName.Should().Be("Architect of Restoration");
        card.MdfcState.IsBackFace.Should().BeFalse();
    }

    [Fact]
    public void NamedCardFactory_Dispatches_CombinedName_ToFrontFace()
    {
        var card = NamedCardFactory.Create(CombinedName, _alice);

        card.Should().BeOfType<Enchantment>();
        card.Name.Should().Be("The Restoration of Eiganjo");
        card.HasSubtype(CardSubtype.Saga).Should().BeTrue();
    }

    [Fact]
    public void CombinedName_IsImplemented()
    {
        ImplementedCardNames.All.Should().Contain(CombinedName);
    }

    [Fact]
    public void Architect_BackFace_Is3x4WhiteFoxMonk_WithVigilance()
    {
        var architect = ArchitectOfRestorationFactory.Create(_alice);

        architect.Name.Should().Be("Architect of Restoration");
        architect.Power.Should().Be(3);
        architect.Toughness.Should().Be(4);
        architect.HasType(CardType.Enchantment).Should().BeTrue();
        architect.HasSubtype(CardSubtype.Fox).Should().BeTrue();
        architect.HasSubtype(CardSubtype.Monk).Should().BeTrue();
        CardColors.GetColors(architect).Should().Contain(ManaColor.White);
        architect.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "Vigilance").Should().BeTrue();
        // CR 712.4 — the back face is pre-flipped.
        architect.MdfcState!.IsBackFace.Should().BeTrue();
    }
}
