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
/// Named-card factory for Fertile Ground (Eighth Edition / Ravnica, {1}{G}).
///
/// Enchantment — Aura. Oracle text (Scryfall, verified):
///   "Enchant land
///    Whenever enchanted land is tapped for mana, its controller adds an
///    additional one mana of any color."
///
/// ## Implementation
///
/// Structurally identical to <see cref="UtopiaSprawlFactory"/> — the
/// mana-bonus clause is a triggered <b>mana</b> ability (CR 605.1b: it
/// triggers on mana being produced and itself produces mana), modelled as a
/// <see cref="TriggeredAbility"/> subscribing to
/// <see cref="ManaAbilityActivatedEvent"/> (published by
/// <see cref="Majik.Core.Services.ManaAbilityActivator"/> after the
/// activator's pool is topped up). The condition matches when the tapped
/// source is exactly the enchanted land (this Aura's
/// <see cref="Permanent.AttachedTo"/> slot). The effect adds one mana to the
/// enchanted land's controller's pool via <see cref="Player.AddManaToPool"/>
/// — CR 106.6 / 605.1b: the controller of the permanent (the player who
/// tapped it) receives the bonus, which can differ from the Aura's controller
/// after a control-change ("its controller").
///
/// Two differences from Utopia Sprawl:
///
/// 1. <b>"Enchant land"</b> (CR 702.5b) accepts ANY land, not just a Forest.
///    The cast-time predicate is therefore <c>p.HasType(CardType.Land)</c>.
///
/// 2. <b>"one mana of any color"</b> (CR 106.1b) is chosen on resolution, not
///    fixed "as this Aura enters". The colour is supplied by the optional
///    <paramref name="colorPicker"/> callback, defaulting to
///    <see cref="LotusCobraFactory.DefaultColor"/> (Green) when absent — the
///    same v1 deferral as Lotus Cobra / Crumbling Vestige (no
///    <c>ChooseManaColorAsync</c> agent hook yet).
///
/// Card identity (Enchantment — Aura, {1}{G}, green colour indicator) is
/// loaded from <c>Majik.Core/CardData/Cards/fertile-ground.json</c> through
/// <see cref="CardDefinitionFactory"/>.
/// </summary>
[CardName("Fertile Ground")]
public static class FertileGroundFactory
{
    public const string CardName = "Fertile Ground";
    public const string Cost = "{1}{G}";

    /// <summary>Printed oracle text — kept for documentation parity.</summary>
    public const string OracleText =
        "Enchant land\n" +
        "Whenever enchanted land is tapped for mana, its controller adds " +
        "an additional one mana of any color.";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("fertile-ground");

    /// <summary>
    /// Construct Fertile Ground with its triggered mana ability attached to
    /// <see cref="Card.Abilities"/>. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to in production — the live
    /// <see cref="TriggerManager"/> auto-registers the trigger when the Aura
    /// crosses onto the battlefield (CR 603.3), so passing a TriggerManager is
    /// only needed for eager (pre-ETB) registration in unit tests. Without this
    /// the prod dispatch built a do-nothing Aura: the bonus mana never fired
    /// because the trigger lived only on the three-arg overload (which prod
    /// never called) — the audit's MissingTrigger flag was a real bug, not a
    /// false positive. The "any color" bonus defaults to Green (Lotus Cobra
    /// deferral — no agent colour surface in the binder layer yet).
    /// </summary>
    public static Enchantment Create(Player owner) =>
        Create(owner, triggers: null, colorPicker: null);

    /// <summary>
    /// Construct a fully-wired Fertile Ground. The triggered mana ability is
    /// attached to the card's <see cref="Card.Abilities"/> collection; when
    /// <paramref name="triggers"/> is supplied it is also registered with the
    /// <see cref="TriggerManager"/> so it surfaces as pending end-to-end.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">Optional live trigger manager for end-to-end
    /// firing.</param>
    /// <param name="colorPicker">Optional callback returning the colour for
    /// the "any color" bonus (CR 106.1b). Consulted on each fire; when null
    /// (or a non-coloured pip) Green is used — same posture as Lotus Cobra.</param>
    public static Enchantment Create(Player owner, TriggerManager? triggers, Func<ManaColor>? colorPicker)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Build identity directly (NOT via Create(owner), which now delegates
        // here — that would recurse).
        var card = (Enchantment)CardDefinitionFactory.Build(Definition, owner);

        // "Whenever enchanted land is tapped for mana, its controller adds
        // an additional one mana of any color." (CR 605.1b / 603.2.)
        // Closure-captured payload: the player who tapped the enchanted land
        // (CR 603.7c — bound at trigger time).
        Player? pendingController = null;

        var condition = new EventTriggerCondition<ManaAbilityActivatedEvent>((e, _) =>
        {
            // The bonus only fires for THIS Aura's enchanted land. AttachedTo
            // is the enchanted permanent (null until the Aura is attached).
            var enchanted = card.AttachedTo;
            if (enchanted is null) return false;
            if (!ReferenceEquals(e.Source, enchanted)) return false;
            pendingController = e.Player;
            return true;
        });

        var addManaEffect = new Effect(
            $"{CardName} — add one mana of any color to the controller of the enchanted land",
            () =>
            {
                var controller = pendingController;
                pendingController = null;
                // CR 106.1b — "any color" means a WUBRG colour. Colour chosen
                // on resolution; defaults to Green (Lotus Cobra deferral).
                var chosen = colorPicker?.Invoke() ?? LotusCobraFactory.DefaultColor;
                controller?.AddManaToPool(LotusCobraFactory.BuildOneManaOfColor(chosen));
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
    /// Build the cast-time <see cref="SpellDefinition"/> for Fertile Ground —
    /// "Enchant land" → single land target (any land). The Aura attaches to
    /// the chosen land on resolution (CR 303.4f), so when
    /// <see cref="Majik.Core.Services.StackResolver"/> moves the Aura to the
    /// battlefield the trigger's <see cref="Permanent.AttachedTo"/> gate is
    /// already populated.
    /// </summary>
    /// <param name="aura">The Fertile Ground permanent being cast.</param>
    /// <param name="battlefield">Current battlefield permanents — the
    /// candidate pool is filtered to lands.</param>
    public static SpellDefinition BuildSpellDefinition(
        Enchantment aura,
        IEnumerable<Permanent> battlefield)
    {
        ArgumentNullException.ThrowIfNull(aura);
        ArgumentNullException.ThrowIfNull(battlefield);

        // CR 702.5b — "Enchant land" restricts the legal target to any Land.
        return AuraSpellDefinitionBuilder.ForAura(
            aura,
            targetDescription: "target land",
            battlefield: battlefield,
            predicate: p => p.HasType(CardType.Land),
            intent: BotIntent.None);
    }
}
