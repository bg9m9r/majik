using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Utopia Sprawl (Future Sight / Modern Horizons, {G}).
///
/// Enchantment — Aura. Oracle text:
///   "Enchant Forest
///    As this Aura enters, choose a color.
///    Whenever enchanted Forest is tapped for mana, its controller adds an
///    additional one mana of the chosen color."
///
/// ## Implementation
///
/// Card identity (Enchantment — Aura, {G}, green color indicator) is loaded
/// from <c>Majik.Core/CardData/Cards/utopia-sprawl.json</c> through
/// <see cref="CardDefinitionFactory"/>, matching the JSON-driven land cycle
/// (Blooming Marsh et al.).
///
/// The mana-doubling clause is a triggered <b>mana</b> ability (CR 605.1b —
/// it triggers on mana being produced and itself produces mana). It is
/// modelled as a <see cref="TriggeredAbility"/> subscribing to
/// <see cref="ManaAbilityActivatedEvent"/> (published by
/// <see cref="Majik.Core.Services.ManaAbilityActivator"/> after the
/// activator's pool is topped up — same surface Manabarbs consumes). The
/// condition matches when the tapped source is exactly the enchanted Forest
/// (this Aura's <see cref="Permanent.AttachedTo"/> slot). The effect adds one
/// mana of the chosen color to the enchanted Forest's controller's pool via
/// <see cref="Player.AddManaToPool"/> — CR 106.6 / 605.1b: the controller of
/// the permanent (the player who tapped it) receives the bonus mana, which
/// can differ from the Aura's controller after a control-change.
///
/// ## Choose-a-color (CR 614 replacement-style "as ~ enters")
///
/// "As this Aura enters, choose a color." (CR 614.12) is resolved up front:
/// the chosen <see cref="ManaColor"/> is supplied to <see cref="Create(Player, ManaColor, Majik.Core.Abilities.TriggerManager?)"/>.
/// A live agent prompt for the choice is deferred engine-wide (same posture
/// as Spreading Seas' cast-time target prompt); callers/tests pass the
/// already-chosen color.
///
/// ## Enchant Forest
///
/// <see cref="BuildSpellDefinition"/> derives the cast-time target predicate
/// directly — "Enchant Forest" is a land-subtype restriction
/// (<see cref="AuraEnchantClauseParser"/> only handles bare card-type nouns),
/// so the predicate is the explicit "Land with the Forest subtype" filter
/// (CR 702.5b / 303.4c). On resolution the Aura attaches to the chosen Forest
/// (CR 303.4f), same flow as <see cref="SpreadingSeasFactory"/>.
/// </summary>
[CardName("Utopia Sprawl")]
public static class UtopiaSprawlFactory
{
    public const string CardName = "Utopia Sprawl";
    public const string Cost = "{G}";

    /// <summary>Printed oracle text — kept for documentation parity.</summary>
    public const string OracleText =
        "Enchant Forest\n" +
        "As this Aura enters, choose a color.\n" +
        "Whenever enchanted Forest is tapped for mana, its controller adds " +
        "an additional one mana of the chosen color.";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("utopia-sprawl");

    /// <summary>
    /// Construct Utopia Sprawl with correct card identity only (no live
    /// mana-trigger wiring). Suitable for factory-shape / dispatcher tests.
    /// </summary>
    public static Enchantment Create(Player owner) =>
        (Enchantment)CardDefinitionFactory.Build(Definition, owner);

    /// <summary>
    /// Construct a fully-wired Utopia Sprawl. The triggered mana ability is
    /// attached to the card's <see cref="Card.Abilities"/> collection; when
    /// <paramref name="triggers"/> is supplied it is also registered with the
    /// <see cref="TriggerManager"/> so it surfaces as pending end-to-end.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="chosenColor">The color chosen "as this Aura enters"
    /// (CR 614.12). Must be one of W/U/B/R/G.</param>
    /// <param name="triggers">Optional live trigger manager for end-to-end
    /// firing.</param>
    public static Enchantment Create(Player owner, ManaColor chosenColor, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = Create(owner);

        // "Whenever enchanted Forest is tapped for mana, its controller adds
        // an additional one mana of the chosen color." (CR 605.1b / 603.2.)
        // Closure-captured payload: the player who tapped the enchanted
        // Forest (CR 603.7c — bound at trigger time).
        Player? pendingController = null;

        var condition = new EventTriggerCondition<ManaAbilityActivatedEvent>((e, _) =>
        {
            // The bonus only fires for THIS Aura's enchanted Forest. AttachedTo
            // is the enchanted permanent (null until the Aura is attached).
            var enchanted = card.AttachedTo;
            if (enchanted is null) return false;
            if (!ReferenceEquals(e.Source, enchanted)) return false;
            pendingController = e.Player;
            return true;
        });

        var bonusMana = ManaCostForColor(chosenColor);

        var addManaEffect = new Effect(
            $"Utopia Sprawl — add {bonusMana} to the controller of the enchanted Forest",
            () =>
            {
                var controller = pendingController;
                pendingController = null;
                controller?.AddManaToPool(bonusMana);
            });

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { addManaEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);

        triggers?.RegisterTriggeredAbility(trigger);

        return card;
    }

    /// <summary>
    /// Build the cast-time <see cref="SpellDefinition"/> for Utopia Sprawl —
    /// "Enchant Forest" → single Land-with-Forest-subtype target. The Aura
    /// attaches to the chosen Forest on resolution (CR 303.4f), so when
    /// <see cref="Majik.Core.Services.StackResolver"/> moves the Aura to the
    /// battlefield the trigger's <see cref="Permanent.AttachedTo"/> gate is
    /// already populated.
    /// </summary>
    /// <param name="aura">The Utopia Sprawl permanent being cast.</param>
    /// <param name="battlefield">Current battlefield permanents — the
    /// candidate pool is filtered to Forests.</param>
    public static SpellDefinition BuildSpellDefinition(
        Enchantment aura,
        IEnumerable<Permanent> battlefield)
    {
        ArgumentNullException.ThrowIfNull(aura);
        ArgumentNullException.ThrowIfNull(battlefield);

        // CR 702.5b / 303.4c — "Enchant Forest" restricts the legal target to
        // a Land that has the Forest subtype. AuraEnchantClauseParser only
        // recognises bare card-type nouns, so the predicate is hand-wired.
        return AuraSpellDefinitionBuilder.ForAura(
            aura,
            targetDescription: "target Forest",
            battlefield: battlefield,
            predicate: p => p.HasType(CardType.Land) && p.HasSubtype(CardSubtype.Forest),
            intent: BotIntent.None);
    }

    /// <summary>Single-pip <see cref="ManaCost"/> for a chosen color.</summary>
    private static ManaCost ManaCostForColor(ManaColor color) => color switch
    {
        ManaColor.White => ManaCost.Parse("W"),
        ManaColor.Blue => ManaCost.Parse("U"),
        ManaColor.Black => ManaCost.Parse("B"),
        ManaColor.Red => ManaCost.Parse("R"),
        ManaColor.Green => ManaCost.Parse("G"),
        _ => throw new ArgumentOutOfRangeException(
            nameof(color), color,
            "Utopia Sprawl's chosen color must be one of W/U/B/R/G (CR 105.1)."),
    };
}
