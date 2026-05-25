using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Imperious Perfect (Lorwyn / Modern Masters
/// reprints, Creature — Elf Warrior {2}{G}).
///
/// Oracle text:
///   "Other Elf creatures you control get +1/+1.
///    {G}, {T}: Create a 1/1 green Elf Warrior creature token."
///
/// ## Implemented (v1)
/// - 2/2 Creature — Elf Warrior, mana cost {2}{G}, owner/controller wired.
/// - <b>"Other Elf creatures you control get +1/+1"</b> via
///   <see cref="LordStaticEffect"/> — <c>matchingSubtype: Elf</c>,
///   <c>power: 1, toughness: 1</c>, no granted keywords, <c>includeSelf:
///   false</c>. CR 613.1g Layer 7c. Same lord shape as
///   <see cref="ElvishArchdruidFactory"/> — the two stack +1/+1 on the
///   same Elves the controller controls. Newly-created tokens from
///   Imperious Perfect's own activated ability are also Elves, so they
///   benefit from any other Elf lord on the battlefield (and from a
///   second Imperious Perfect).
/// - <b>Activated ability — {G}, {T}: Create a 1/1 green Elf Warrior
///   creature token</b> (CR 602.1 / CR 111.6). Wired as an
///   <see cref="ActivatedAbility"/> with the two-cost vector:
///     - <see cref="ManaCostCost"/> for the {G} mana payment.
///     - <see cref="AdditionalCost.Tap"/> for {T}.
///   The effect creates a 1/1 green Elf Warrior token under Imperious
///   Perfect's current controller via
///   <see cref="TokenFactory.CreateOnBattlefield"/>. When a
///   <see cref="ZoneService"/> is supplied, the token enters via the
///   service so ETB triggers / replacements fire (Soul Warden, Bridge
///   from Below as a graveyard-resident, etc.).
///
/// ## Deferred (v1 gaps)
/// - <b>LTB unregister for the lord static</b>: same shape as Goblin
///   Chieftain / Elvish Archdruid — <see cref="ContinuousEffect.IsActive"/>
///   short-circuits when Imperious Perfect isn't on the battlefield so
///   the bonus lifts correctly. A future Prune pass could drop the entry
///   on LTB.
/// </summary>
[CardName("Imperious Perfect")]
public static class ImperiousPerfectFactory
{
    public const string CardName = "Imperious Perfect";
    public const string PrintedManaCost = "{2}{G}";
    public const int Power = 2;
    public const int Toughness = 2;
    public const int TokenPower = 1;
    public const int TokenToughness = 1;

    /// <summary>
    /// Construct Imperious Perfect with no live runtime services.
    /// Suitable for card-shape / dispatcher tests — the lord static effect
    /// is NOT registered (no layers service). The activated ability is
    /// still attached to the card shape; without a ZoneService, token
    /// creation falls back to raw zone manipulation.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, continuousEffects: null, zoneService: null);

    /// <summary>
    /// Construct Imperious Perfect with optional runtime services. When
    /// <paramref name="continuousEffects"/> is supplied, the +1/+1 lord
    /// static is registered. When <paramref name="zoneService"/> is
    /// supplied, the activated ability creates the token via the service
    /// so ETB events fire.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="continuousEffects">Layers service to register the
    /// "Other Elf creatures you control get +1/+1" lord static against.
    /// May be null — no live bonus.</param>
    /// <param name="zoneService">Optional zone service so token-ETB
    /// CardMovedEvent fires (Soul Warden etc.). Pass null for raw zone
    /// moves.</param>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? continuousEffects,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Elf, CardSubtype.Warrior });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 613.1g Layer 7c — "Other Elf creatures you control get
        // +1/+1." includeSelf: false; no granted keywords. Same shape as
        // Elvish Archdruid's lord static. Two Elf lords on the same
        // battlefield stack additively.
        if (continuousEffects != null)
        {
            continuousEffects.Register(new LordStaticEffect(
                source: card,
                matchingSubtype: CardSubtype.Elf,
                power: 1,
                toughness: 1,
                grantedKeywords: null,
                includeSelf: false,
                opponentsOnly: false));
        }

        // CR 602.1 — "{G}, {T}: Create a 1/1 green Elf Warrior creature
        // token." Two-cost vector: mana payment + tap. CR 117 — choices
        // (none here — token spec is fixed) are made at resolution time.
        var tokenEffect = new Effect(
            $"{CardName}: create a 1/1 green Elf Warrior creature token",
            () => CreateElfWarriorToken(card.Controller ?? owner, zoneService));

        var activatedAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{G}"),
                AdditionalCost.Tap(card),
            },
            effects: new IEffect[] { tokenEffect });

        card.AddAbility(activatedAbility);

        return card;
    }

    /// <summary>
    /// CR 111 / 111.6 — create one 1/1 green Elf Warrior creature token
    /// under <paramref name="controller"/>'s control. Mirrors
    /// <see cref="GoblinRabblemasterFactory.CreateGoblinToken"/>'s shape;
    /// only the spec differs (subtypes Elf+Warrior, colour Green).
    /// </summary>
    public static Creature CreateElfWarriorToken(
        Player controller,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var spec = new TokenFactory.TokenSpec(
            Name: "Elf Warrior",
            Power: TokenPower,
            Toughness: TokenToughness,
            Subtypes: new[] { CardSubtype.Elf, CardSubtype.Warrior },
            Keywords: null,
            // CR 105 / CR 111.4 — printed "1/1 green Elf Warrior
            // creature token".
            Colors: new[] { ManaColor.Green });

        return TokenFactory.CreateOnBattlefield(spec, controller, zoneService);
    }
}
