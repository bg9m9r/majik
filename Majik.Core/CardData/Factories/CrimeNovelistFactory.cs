using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Crime Novelist (Outlaws of Thunder Junction, {2}{R}).
/// Creature — Goblin Bard 1/3. Oracle text (verified against Scryfall):
///   "Whenever you sacrifice an artifact, put a +1/+1 counter on this creature
///    and add {R}."
///
/// The base shape (name, Creature, Goblin + Bard subtypes, {2}{R}, 1/3) is
/// materialised from the embedded JSON definition (<c>crime-novelist.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The single printed behaviour — the
/// "whenever you sacrifice an artifact" trigger — is layered on HERE rather than
/// declaratively because its payoff pairs a +1/+1 counter with an
/// <b>add {R}</b> mana production, and the declarative triggered-effect surface
/// (<see cref="EffectDefinition"/>) has no "add mana" effect kind — only the
/// spell-resolution <c>AddMana</c> body step exists. So the factory mirrors the
/// trigger-adds-{R} pattern from
/// <see cref="BirgiGodOfStorytellingFactory"/> (an
/// <see cref="Effect"/> calling <see cref="Player.AddManaToPool"/>), scoped to
/// the controller-side sacrifice predicate.
///
/// ## Implemented (v1)
/// - <b>"Whenever you sacrifice an artifact" (CR 603.1 / CR 701.16 / CR 109.5)</b>:
///   an <see cref="EventTriggerCondition{PermanentSacrificedEvent}"/> over the
///   dedicated <see cref="PermanentSacrificedEvent"/> — the same sacrifice-
///   detection surface Vengeful Tracker (opponent-scoped) and Mortician Beetle
///   (any-player) subscribe to. The "you" scope (CR 109.5) is the predicate
///   <c>SacrificingPlayer == controller</c>; the "an artifact" gate (CR 205.2)
///   is <c>SacrificedCard.HasType(Artifact)</c>. No nontoken restriction — a
///   sacrificed Treasure/Clue token artifact fires it too.
/// - <b>"Put a +1/+1 counter on this creature" (CR 122.1 / CR 614)</b>: one
///   <see cref="CounterType.PlusOnePlusOne"/> via <see cref="CountersService.Add"/>
///   so a replacement bus (Hardened Scales / Doubling Season) can rewrite the
///   count when one is supplied.
/// - <b>"and add {R}" (CR 605.1a is NOT a mana ability — this is a triggered
///   ability that produces mana on resolution, CR 606.3)</b>: the same
///   <see cref="Effect"/> adds {R} to the controller's mana pool via
///   <see cref="Player.AddManaToPool"/>. Both payoffs run in the one effect so
///   they resolve together (CR 603.3).
///
/// Adding this <c>[CardName]</c> factory flips <c>IsImplemented</c> on
/// automatically via <see cref="ImplementedCardNames"/> — no seed regen.
/// </summary>
[CardName("Crime Novelist")]
public static class CrimeNovelistFactory
{
    public const string CardName = "Crime Novelist";

    /// <summary>JSON slug for the embedded card definition.</summary>
    public const string Slug = "crime-novelist";

    /// <summary>
    /// Construct Crime Novelist with no live <see cref="TriggerManager"/>
    /// wiring. The sacrifice trigger is attached to the card shape for
    /// structural / dispatch tests but is not registered. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null, replacements: null);

    /// <summary>
    /// Construct Crime Novelist with an optional <see cref="TriggerManager"/>
    /// and <see cref="ReplacementBus"/>. When <paramref name="triggers"/> is
    /// supplied the declarative-equivalent sacrifice trigger is registered so a
    /// qualifying <see cref="PermanentSacrificedEvent"/> auto-queues the ability.
    /// When <paramref name="replacements"/> is supplied the +1/+1 counter
    /// placement is routed through <see cref="CountersService.Add"/> so
    /// Hardened Scales / Doubling Season replacements (CR 614) can rewrite the
    /// count.
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers, ReplacementBus? replacements = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Goblin + Bard subtypes, {2}{R}, 1/3). The JSON carries no abilities —
        // the sacrifice trigger is layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        if (CardDefinitionFactory.Build(definition, owner, replacements) is not Creature card)
        {
            throw new InvalidOperationException(
                $"Expected '{CardName}' to materialise as a Creature.");
        }

        var trigger = BuildSacrificeArtifactTrigger(card, owner, replacements);
        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);

        return card;
    }

    /// <summary>
    /// Build the "Whenever you sacrifice an artifact, put a +1/+1 counter on
    /// this creature and add {R}." trigger (CR 603.1 / CR 701.16 / CR 109.5 /
    /// CR 122.1 / CR 606.3).
    /// </summary>
    private static TriggeredAbility BuildSacrificeArtifactTrigger(
        Creature card, Player owner, ReplacementBus? replacements)
    {
        var payoff = new Effect(
            $"{CardName}: whenever you sacrifice an artifact, put a +1/+1 counter on it and add {{R}}",
            () =>
            {
                var controller = card.Controller ?? owner;
                // CR 122.1 / CR 614 — one +1/+1 counter; the replacement bus
                // (if supplied) can rewrite the placed count.
                CountersService.Add(card, CounterType.PlusOnePlusOne, 1, replacements);
                // CR 606.3 — the triggered ability produces {R} on resolution.
                controller.AddManaToPool(Majik.Core.ValueObjects.ManaCost.Parse("{R}"));
            });

        return new TriggeredAbility(
            source: card,
            controller: owner,
            // CR 109.5 — "you sacrifice": the sacrificing player is the
            // controller. CR 205.2 — "an artifact": the sacrificed permanent has
            // the Artifact card type (a token artifact qualifies too — no
            // nontoken restriction).
            condition: new EventTriggerCondition<PermanentSacrificedEvent>(
                (e, _) => ReferenceEquals(e.SacrificingPlayer, card.Controller ?? owner)
                          && e.SacrificedCard.HasType(CardType.Artifact)),
            effects: new IEffect[] { payoff },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield });
    }
}
