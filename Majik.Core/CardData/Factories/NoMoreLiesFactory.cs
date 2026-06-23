using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Abilities;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Spells;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for No More Lies (Murders at Karlov Manor, {W}{U}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "Counter target spell unless its controller pays {3}. If that spell is
///    countered this way, exile it instead of putting it into its owner's
///    graveyard."
///
/// ## Scryfall identity
/// <list type="bullet">
///   <item>Mana cost: {W}{U}; mana value 2</item>
///   <item>Type line: Instant; colors: W, U (Azorius — multicolour)</item>
/// </list>
///
/// Same "counter target spell unless its controller pays {N}" body as
/// <see cref="GeistlightSnareFactory"/> / <see cref="MysticalDisputeFactory"/>
/// (N=3 here), with the only difference being the exile-on-counter rider: a
/// countered spell is placed into its owner's exile zone instead of the
/// graveyard (CR 614 — the replacement on the counter's normal "to graveyard"
/// outcome, modelled inline by routing the countered card to
/// <see cref="ZoneType.Exile"/>). Exile placement mirrors
/// <see cref="SpellQuellerFactory"/>'s exile path.
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {W}{U} (multicolour). The base shape (name,
///   Instant, {W}{U}, W/U) is loaded from the embedded JSON definition
///   (<c>no-more-lies.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
///   <see cref="CardDefinitionFactory"/>.
/// - <b>Counter unless pay {3}, exile if countered</b>:
///   <see cref="BuildSpellDefinition"/> declares one 1..1 "target spell"
///   <see cref="TargetRequest"/>. At resolution the target's controller is
///   auto-prompted via <see cref="Player.PayMana"/> for {3}; if they can pay,
///   the counter no-ops (CR 118.4). Otherwise the spell is countered: removed
///   from the stack via <see cref="OracleSpellBinder.RemoveFromStack"/> and the
///   underlying card moved to its owner's <see cref="ZoneType.Exile"/> zone
///   instead of the graveyard (the card's own replacement of the counter's
///   normal destination).
///
/// ## Deferred (v1 gaps)
/// - <b>Agent prompt for "pay {3}?"</b> — same auto-pay posture as Geistlight
///   Snare / Mystical Dispute / Mana Leak / Daze. The real "would you like to
///   pay?" choice is queued behind a future agent-prompt surface.
/// </summary>
[CardName("No More Lies")]
public static class NoMoreLiesFactory
{
    public const string CardName = "No More Lies";
    public const string Slug = "no-more-lies";

    /// <summary>Pay-or-counter rider (CR 118.4 — "unless its controller pays
    /// {3}").</summary>
    public const int UnlessPayGeneric = 3;

    /// <summary>
    /// Construct No More Lies as an Instant card with owner / controller wired.
    /// The resolve-time SpellDefinition (counter-unless-pay-{3}, exile if
    /// countered) is built on demand via <see cref="BuildSpellDefinition"/> —
    /// mirrors <see cref="GeistlightSnareFactory"/>. The base shape (name,
    /// Instant, {W}{U}, W/U) is materialised from the embedded JSON definition.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Instant)CardDefinitionFactory.Build(definition, owner);
    }

    /// <summary>
    /// Build the "counter target spell unless its controller pays {3}; if
    /// countered, exile it instead of putting it into its owner's graveyard"
    /// SpellDefinition. Mirrors
    /// <see cref="GeistlightSnareFactory.BuildSpellDefinition"/> with N=3, but
    /// the countered card is moved to exile (CR 614 — replacing the counter's
    /// normal "to graveyard" destination) rather than the graveyard.
    /// CR 608.2b — illegal target at resolution is handled by the pre-resolve
    /// target-legality check; this body assumes the resolved target is still a
    /// live <see cref="ISpell"/>.
    /// </summary>
    /// <param name="targetResolver">Resolves the raw target token chosen by the
    /// caster to a live engine object (pass-through in tests; production callers
    /// route via a TargetResolver service).</param>
    /// <param name="stack">Active stack; required to remove the countered spell.
    /// Null in pure-shape tests; the effect becomes a no-op.</param>
    public static SpellDefinition BuildSpellDefinition(
        Func<object, object> targetResolver,
        Majik.Core.Stack.Stack? stack)
    {
        ArgumentNullException.ThrowIfNull(targetResolver);

        var unlessCost = ManaCost.Zero.AddGenericCost(UnlessPayGeneric);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest("target spell", 1, 1, Array.Empty<object>()),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                var resolved = targetResolver(raw);
                return new IEffect[]
                {
                    new Effect(
                        "No More Lies — counter target spell unless its controller pays {3}; exile it if countered",
                        () =>
                        {
                            if (stack == null || resolved is not ISpell spell) return;

                            // CR 118.4 — target's controller may pay {3} to
                            // prevent the counter. v1 auto-pays when able (same
                            // posture as Geistlight Snare / Mystical Dispute).
                            if (spell.Controller is not null
                                && spell.Controller.PayMana(unlessCost))
                            {
                                return;
                            }

                            // CR 701.5 — counter the spell: remove from stack.
                            OracleSpellBinder.RemoveFromStack(stack, spell);

                            // Exile-on-counter rider — CR 614: the card replaces
                            // the counter's normal "to its owner's graveyard"
                            // destination with exile. Place the underlying card
                            // into its owner's exile zone (mirrors Spell
                            // Queller's exile path) instead of the graveyard.
                            if (spell.Card is not Card card) return;
                            var owner = card.Owner;
                            if (owner != null && card.Zone != ZoneType.Exile)
                            {
                                owner.Zones.Exile.AddCard(card);
                            }
                            card.SetZone(ZoneType.Exile);
                        }),
                };
            });
    }
}
