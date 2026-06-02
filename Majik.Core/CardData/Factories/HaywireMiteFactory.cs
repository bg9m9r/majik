using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Haywire Mite (The Brothers' War, {1}).
///
/// Artifact Creature — Insect 1/1. Oracle text (verified against Scryfall):
///   "When this creature dies, you gain 2 life.
///    {G}, Sacrifice this creature: Exile target noncreature artifact or
///    noncreature enchantment."
///
/// FULLY declarative now (<c>haywire-mite.json</c>): the card's base shape
/// (name, Artifact + Creature, Insect subtype, {1}, 1/1), the dies trigger,
/// AND the sacrifice-exile activated ability are all carried by the embedded
/// JSON definition. The factory is a thin loader (same posture as
/// <see cref="ShatterFactory"/> / <see cref="GlaringFleshrakerFactory"/>) that
/// also registers the triggered ability with an optional
/// <see cref="TriggerManager"/>.
///
/// ## Implemented (v1)
///
/// - <b>Dies trigger (CR 603.6c / 700.4)</b> — "When this creature dies, you
///   gain 2 life." The JSON <c>dies_self</c> trigger variant builds a
///   <see cref="TriggeredAbility"/> over the Battlefield → Graveyard move with
///   active zones {Battlefield, Graveyard}; the <c>gain_life_self</c> effect
///   gains the controller 2 life on resolution (CR 119.3).
/// - <b>{G}, Sacrifice this creature: Exile target noncreature artifact or
///   noncreature enchantment (CR 602)</b> — now a declarative
///   <c>activated</c> ability: a <c>mana</c> cost ({G}) PLUS a
///   <c>sacrifice_self</c> additional cost (CR 602.5 / 118.8 — the mite is
///   sacrificed as part of activation by the declarative sacrifice
///   <see cref="Majik.Core.Costs.AdditionalCost"/>, paid in the production
///   activation path via <see cref="Majik.Core.Services.AbilityActivator"/> →
///   <see cref="Majik.Core.Costs.CostPayment"/>), and a single targeted
///   <c>exile_target</c> effect over the
///   <c>noncreature_artifact_or_enchantment</c> filter. The agent picks the
///   target at activation (CR 602.2b); resolution exiles it (CR 701.21) after
///   re-checking the filter (CR 608.2b — an off-filter / departed target
///   fizzles the exile, but the mite is still sacrificed because the cost was
///   already paid).
/// - <b>Instant speed</b> — no sorcery-speed restriction (CR 602.5b).
/// </summary>
[CardName("Haywire Mite")]
public static class HaywireMiteFactory
{
    public const string CardName = "Haywire Mite";
    public const string Slug = "haywire-mite";
    public const int LifeGain = 2;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Haywire Mite with the dies trigger + sacrifice-exile activated
    /// ability attached to the card shape, but the dies trigger NOT registered
    /// with a <see cref="TriggerManager"/>. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, triggers: null);

    /// <summary>
    /// Construct Haywire Mite with an optional <see cref="TriggerManager"/>.
    /// When supplied, the dies trigger is registered so a Battlefield →
    /// Graveyard move places it on the stack automatically (CR 603.3).
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);

        if (triggers != null)
        {
            foreach (var trigger in card.Abilities.OfType<TriggeredAbility>())
            {
                triggers.RegisterTriggeredAbility(trigger);
            }
        }

        return card;
    }
}
