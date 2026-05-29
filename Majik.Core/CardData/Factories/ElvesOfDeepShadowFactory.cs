using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Elves of Deep Shadow (Ravnica: City of Guilds,
/// {G}).
///
/// Creature — Elf Druid 1/1. Oracle text (verified against Scryfall):
///   "{T}: Add {B}. This creature deals 1 damage to you."
///
/// The base shape (name, Creature, Elf Druid subtypes, {G}, 1/1) is
/// materialised from the embedded JSON definition
/// (<c>elves-of-deep-shadow.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The single mana ability is
/// layered on top here — the JSON <c>AbilityDefinition</c> schema doesn't
/// express the self-damage rider, so it lives in the factory (same posture
/// as <see cref="TwinSilkSpiderFactory"/>, whose JSON also carries no
/// abilities).
///
/// ## Implemented (v1)
/// - 1/1 <see cref="Creature"/> — Elf Druid at {G}, owner / controller wired.
/// - <b>Single mana ability (CR 605.1)</b>: <c>{T}: Add {B}. This creature
///   deals 1 damage to you.</c> Built via the additional-cost overload of
///   <see cref="ManaAbility"/>: tapping pays {T}; the
///   <c>additionalCostPayer</c> then reduces the controller's life by 1
///   (CR 120.3 — damage to a player causes loss of life equal to that
///   damage). Mirrors the pain-land coloured-mode shape in
///   <see cref="PainLandCycleFactory"/>.
///
/// CR 119.4 does NOT gate this damage — like the pain lands, Elves of Deep
/// Shadow can deal lethal damage to you (you simply lose the game from SBAs
/// after activating); the <c>canActivateCheck</c> therefore only tests
/// <c>!IsTapped</c> and never requires life &gt; 1.
///
/// ## Deferred (v1 gaps)
/// - Full <c>DamageDealtEvent</c> route: the 1 damage goes through
///   <see cref="Player.LoseLife"/>, not a damage event — damage-prevention
///   subscribers don't intercept it. Same simplification the pain-land
///   cycle and Mana Crypt take. The "this creature deals" wording (vs the
///   pain land's "this land deals") is cosmetic: the damage's source isn't
///   modelled, only the life loss it causes.
/// </summary>
[CardName("Elves of Deep Shadow")]
public static class ElvesOfDeepShadowFactory
{
    public const string CardName = "Elves of Deep Shadow";
    public const string Slug = "elves-of-deep-shadow";

    /// <summary>
    /// Construct Elves of Deep Shadow owned and controlled by
    /// <paramref name="owner"/>. The single
    /// <c>{T}: Add {B}. This creature deals 1 damage to you.</c> mana
    /// ability is attached structurally.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature, Elf
        // Druid subtypes, {G}, 1/1). The JSON carries no abilities — the
        // mana ability with its self-damage rider is layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 605.1 — {T}: Add {B}. This creature deals 1 damage to you.
        // Mana ability (never on the stack). The {T} tap is the printed
        // cost; the additionalCostPayer then reduces the controller's life
        // by 1 (CR 120.3 — "deals 1 damage to you" reduces life by the
        // damage amount). Gate on !IsTapped so duplicate activations are
        // prevented; no life-floor gate (CR 119.4 does not apply — this is
        // damage, not a "Pay 1 life" cost) so it may deal lethal damage to
        // you.
        card.AddAbility(new ManaAbility(
            source: card,
            controller: owner,
            manaGenerated: ManaCost.Parse("B"),
            canActivateCheck: () => !card.IsTapped,
            additionalCostPayer: p => p.LoseLife(1)));

        return card;
    }
}
