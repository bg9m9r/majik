using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Rules;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="BraveTheElementsFactory"/>.
///
/// Card: Brave the Elements — Instant {W} (Magic 2010 / Magic Origins).
///   Oracle text (verified against Scryfall):
///     "Choose a color. White creatures you control gain protection from the
///      chosen color until end of turn."
///
/// Covers:
///   - Identity: name, Instant type, White colour, mana value 1, owner /
///     controller wired — loaded from the embedded JSON def.
///   - NamedCardFactory dispatch returns the Instant shape.
///   - SpellDefinition shape: five choose-one colour modes (WUBRG), no targets,
///     no X (CR 700.2 modal analogue for "Choose a color", CR 601.2b).
///   - Resolve: only the caster's WHITE creatures gain protection from the
///     chosen colour (CR 702.16); non-white allies and enemy creatures are
///     untouched.
///   - The granted protection is the CHOSEN colour only (the other four colours
///     are not granted).
///   - The grant expires at end of turn (CR 514.2).
/// </summary>
[Trait("Color", "W")]
public class BraveTheElementsFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_HasInstantShape_White_AtCostW()
    {
        var card = BraveTheElementsFactory.Create(_alice);

        card.Name.Should().Be("Brave the Elements");
        card.ManaCost.Should().Be("{W}");
        card.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.White);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_BraveTheElements()
    {
        var card = NamedCardFactory.Create("Brave the Elements", _alice);

        card.Name.Should().Be("Brave the Elements");
        card.HasType(CardType.Instant).Should().BeTrue(
            because: "the [CardName] factory dispatch returns the Instant shape, not a vanilla shell");
    }

    // -----------------------------------------------------------------------
    // SpellDefinition shape
    // -----------------------------------------------------------------------

    [Fact]
    public void BuildDefinition_HasFiveColourModes_NoTargets_NoX()
    {
        var def = BraveTheElementsFactory.BuildDefinition(_alice);

        def.Modes.Should().HaveCount(5, because: "the colour pick is five choose-one modes (WUBRG)");
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().BeEmpty(because: "Brave the Elements targets nothing");
    }

    // -----------------------------------------------------------------------
    // Resolve: grant protection-from-chosen-colour to caster's white creatures
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_GrantsProtectionFromChosenColour_ToCastersWhiteCreaturesOnly()
    {
        // Alice's white creature — should be protected.
        var whiteAlly = BuildCreature("Soldier", "{W}", _alice);
        // Alice's non-white creature — should NOT be protected (only white).
        var greenAlly = BuildCreature("Bear", "{1}{G}", _alice);
        // Bob's white creature — opponent's, never affected.
        var enemyWhite = BuildCreature("Knight", "{W}", _bob);

        var granted = BraveTheElementsFactory.Resolve(_alice, BraveTheElementsFactory.QualityRed);

        granted.Should().Contain(whiteAlly);
        granted.Should().NotContain(greenAlly);
        granted.Should().NotContain(enemyWhite);

        // CR 702.16 — only the white ally has protection from red.
        Protection.HasProtectionFromColor(whiteAlly, ManaColor.Red).Should().BeTrue(
            because: "Brave the Elements grants the chosen colour to white creatures you control");
        Protection.HasProtectionFromColor(greenAlly, ManaColor.Red).Should().BeFalse(
            because: "non-white creatures you control are not affected");
        Protection.HasProtectionFromColor(enemyWhite, ManaColor.Red).Should().BeFalse(
            because: "opponents' white creatures are not affected");
    }

    [Fact]
    public void Resolve_GrantsOnlyTheChosenColour_NotTheOtherFour()
    {
        var whiteAlly = BuildCreature("Soldier", "{W}", _alice);

        BraveTheElementsFactory.Resolve(_alice, BraveTheElementsFactory.QualityBlue);

        Protection.HasProtectionFromColor(whiteAlly, ManaColor.Blue).Should().BeTrue();
        Protection.HasProtectionFromColor(whiteAlly, ManaColor.White).Should().BeFalse();
        Protection.HasProtectionFromColor(whiteAlly, ManaColor.Black).Should().BeFalse();
        Protection.HasProtectionFromColor(whiteAlly, ManaColor.Red).Should().BeFalse();
        Protection.HasProtectionFromColor(whiteAlly, ManaColor.Green).Should().BeFalse();
    }

    [Fact]
    public void BuildDefinition_ChosenMode_MapsToColourQuality_OnResolve()
    {
        var whiteAlly = BuildCreature("Soldier", "{W}", _alice);

        var def = BraveTheElementsFactory.BuildDefinition(_alice);

        // Pick the green mode (mode index 4) — CR 601.2b the colour is chosen
        // as the spell is cast.
        var chosen = new ChosenSpellParams(
            ModeIndex: BraveTheElementsFactory.ModeGreen,
            X: null,
            Targets: new IReadOnlyList<object>[0],
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        Protection.HasProtectionFromColor(whiteAlly, ManaColor.Green).Should().BeTrue(
            because: "mode 4 = protection from green");
        Protection.HasProtectionFromColor(whiteAlly, ManaColor.Red).Should().BeFalse();
    }

    [Fact]
    public void Resolve_GrantExpiresAtEndOfTurn()
    {
        var whiteAlly = BuildCreature("Soldier", "{W}", _alice);
        var svc = whiteAlly.ActiveEffects!;

        BraveTheElementsFactory.Resolve(_alice, BraveTheElementsFactory.QualityRed);
        Protection.HasProtectionFromColor(whiteAlly, ManaColor.Red).Should().BeTrue();

        // CR 514.2 — EOT cleanup expires the grant.
        svc.ExpireEndOfTurn();
        // Re-sync the layer pass so the revoked grant is reflected on Abilities.
        svc.Compute(whiteAlly);

        Protection.HasProtectionFromColor(whiteAlly, ManaColor.Red).Should().BeFalse(
            because: "the protection grant expires at end of turn (CR 514.2)");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static Creature BuildCreature(string name, string manaCost, Player owner)
    {
        var c = new Creature(name, manaCost, 2, 2)
        {
            Owner = owner,
            Controller = owner,
            ActiveEffects = new ContinuousEffectsService(),
        };
        c.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(c);
        return c;
    }
}
