using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Hell Mongrel (Shadows over Innistrad, {3}{B}).
///
/// Creature — Nightmare Dog 4/3. Oracle text (verified against Scryfall
/// 2026-06-16):
///   "Discard a card: This creature gets +1/+1 until end of turn.
///    Madness {2}{B} (If you discard this card, discard it into exile. When
///    you do, cast it for its madness cost or put it into your graveyard.)"
///
/// ## Pure wire-up — both primitives already exist
///
/// This factory composes two existing engine primitives; it introduces no new
/// mechanic:
/// - <b>"Discard a card" activation cost</b> via <see cref="DiscardACardCost"/>
///   (CR 117.1 / CR 701.16a) — the same cost Psychic Frog uses. It routes the
///   discard through <see cref="Majik.Core.Primitives.Fx.DiscardCard"/>, so a
///   <see cref="Majik.Core.Events.DiscardedEvent"/> (wasCost: true) fires and
///   any madness card discarded to pay the cost is handled by the central
///   funnel.
/// - <b>+1/+1-until-end-of-turn self-pump</b> via
///   <see cref="PumpUntilEndOfTurnEffect"/>(+1, +1) on the card's
///   <see cref="Creature.ActiveEffects"/> (CR 602 / CR 613.1f Layer 7c) — the
///   same self-pump shape as <see cref="BlazingRootwallaFactory"/> /
///   <see cref="WallOfFireFactory"/>, just +1/+1 instead of firebreathing's
///   +N/+0. The effect expires at the cleanup step (CR 514.2) via
///   <see cref="ContinuousEffectsService.ExpireEndOfTurn"/>.
///
/// ## Madness is intrinsic — NOT wired here (CR 702.35)
///
/// Madness {2}{B} rides the card while it is in hand, so it cannot hang off the
/// per-permanent ability wiring. It is handled centrally by
/// <see cref="Majik.Core.Keywords.MadnessCatalog"/> (already registers
/// "Hell Mongrel" → {2}{B}) consulted by the discard funnel
/// <see cref="Majik.Core.Primitives.Fx.DiscardCard"/>. This factory therefore
/// implements ONLY the card's non-madness body — same posture as
/// <see cref="BlazingRootwallaFactory"/> and <see cref="InsolentNeonateFactory"/>.
///
/// ## Implemented (v1)
///
/// - 4/3 Creature — Nightmare Dog at printed cost {3}{B}, owner/controller
///   wired. Base shape materialised from the embedded JSON definition
///   (<c>hell-mongrel.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/>.
/// - <b>"Discard a card: This creature gets +1/+1 until end of turn."</b> — a
///   self-pump <see cref="ActivatedAbility"/> whose sole cost is a
///   <see cref="DiscardACardCost"/> (no mana). Repeatable so long as the
///   controller has a card to discard (CR 602.5e — no once-per-turn rider
///   printed). On resolution it registers a
///   <see cref="PumpUntilEndOfTurnEffect"/>(+1, +1) against the card's
///   <see cref="Creature.ActiveEffects"/>; null <c>ActiveEffects</c>
///   (shape-only path) is a silent no-op (same posture as
///   <see cref="BlazingRootwallaFactory"/>).
///
/// ## Deferred (v1 gaps)
///
/// - <b>Discard prompt</b> on the activation cost (CR 701.16a — the
///   discarding player chooses) — <see cref="DiscardACardCost"/>
///   deterministically picks the first card in hand. Agent-driven discard
///   prompts are deferred behind the same queue as Psychic Frog / Liliana of
///   the Veil / Faithless Looting.
/// </summary>
[CardName("Hell Mongrel")]
public static class HellMongrelFactory
{
    public const string CardName = "Hell Mongrel";
    public const string Slug = "hell-mongrel";
    public const string Cost = "{3}{B}";
    public const int PowerBoost = 1;
    public const int ToughnessBoost = 1;

    /// <summary>
    /// Construct Hell Mongrel — the <see cref="NamedCardFactory"/> dispatch
    /// target. The "discard a card: +1/+1 until end of turn" pump is attached;
    /// it is repeatable (no per-turn lock printed). Madness {2}{B} is intrinsic
    /// (handled by <see cref="Majik.Core.Keywords.MadnessCatalog"/> + the
    /// discard funnel), so it is NOT wired here.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Nightmare/Dog subtypes, {3}{B}, 4/3). The JSON carries no abilities —
        // the discard-pump is layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 602 / CR 613.1f Layer 7c — "Discard a card: This creature gets
        // +1/+1 until end of turn." Plain self-pump activated ability whose
        // sole cost is a "discard a card" cost (no mana; uses the stack). On
        // resolution a PumpUntilEndOfTurnEffect(+1, +1) is registered against
        // card.ActiveEffects. null ActiveEffects (shape-only path) = silent
        // no-op (same posture as BlazingRootwallaFactory / WallOfFireFactory).
        var pumpEffect = new Effect(
            $"{CardName}: +{PowerBoost}/+{ToughnessBoost} until end of turn (discard a card)",
            () =>
            {
                card.ActiveEffects?.Register(
                    new PumpUntilEndOfTurnEffect(card, PowerBoost, ToughnessBoost));
            });

        card.AddAbility(new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new DiscardACardCost() },
            effects: new IEffect[] { pumpEffect }));

        return card;
    }
}
