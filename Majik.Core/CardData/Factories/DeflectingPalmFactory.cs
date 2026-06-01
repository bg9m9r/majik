using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Deflecting Palm (Khans of Tarkir, {R}{W}).
///
/// Instant. Oracle text (verified against Scryfall 2026-06-01):
///   "The next time a source of your choice would deal damage to you this
///    turn, prevent that damage. If damage is prevented this way, Deflecting
///    Palm deals that much damage to that source's controller."
///
/// ## Why it gets its own factory
/// The oracle text already binds via
/// <see cref="SpellTemplates.Templates.Bespoke.DeflectingPalmFamilyTemplate"/>,
/// so the runtime behaviour ships through the template path. But
/// <c>IsImplemented</c> is derived from the <c>[CardName]</c> factory
/// registry (<see cref="ImplementedCardNames"/>), NOT from template binding —
/// a template-only card still reports unimplemented. This thin factory adds
/// the missing <c>[CardName]</c> dispatch (and a directly-testable
/// <see cref="BuildSpellDefinition"/>) so the flag flips on and the card has
/// a first-class entry point, reusing the same
/// <see cref="PreventNextDamageFromChosenSourceShield"/> +
/// <see cref="Fx.DealDamage"/> primitives the family template already uses.
/// No new engine mechanic is required.
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {R}{W} (Boros). Card shape comes from the
///   embedded JSON (<c>deflecting-palm.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory.Build"/>.
/// - <b>Resolve (CR 615)</b>: register a
///   <see cref="PreventNextDamageFromChosenSourceShield"/> on the caster's
///   replacement bus, beneficiary = the caster. The first
///   <see cref="DamageIntent"/> aimed at the caster this turn is cancelled
///   (CR 615.1), the shield is one-shot, and it auto-drops at end of turn
///   (<see cref="IEndOfTurnExpirable"/>).
/// - <b>Redirect rider (CR 119 / 615)</b>: when damage is prevented this
///   way, the shield's <c>OnPrevent</c> callback deals that much damage to
///   the prevented source's controller via <see cref="Fx.DealDamage"/>
///   (Player → <see cref="Player.LoseLife"/>). This is a genuine
///   damage-dealing event from Deflecting Palm, so it is routed through
///   <see cref="Fx.DealDamage"/> rather than a raw life-loss shortcut.
///
/// ## Rules citations
/// - CR 615.1 — prevention cancels the damage entirely.
/// - CR 119 — "deals that much damage" (the redirect to the source's
///   controller).
/// - CR 614 — replacement-effect routing through the
///   <see cref="ReplacementBus"/>.
///
/// ## Target choice / deferred (v1 gaps)
/// - <b>"Source of your choice" prompt</b>: lossy at v1 — the shield fires
///   on the FIRST qualifying damage intent aimed at the caster rather than
///   gating on a player-selected source. Same posture as the family
///   template. Real "choose a source" plumbing would need an extra source
///   request + per-source shield.
/// - <b>Per-source-controller routing</b>: the rider resolves the source's
///   controller at prevention time from the intent's source
///   (<see cref="DamageIntent.Source"/>); a player source bounces back to
///   itself, a card source to its controller. Matches the template.
/// </summary>
[CardName("Deflecting Palm")]
public static class DeflectingPalmFactory
{
    public const string CardName = "Deflecting Palm";
    public const string Slug = "deflecting-palm";
    public const string PrintedManaCost = "{R}{W}";

    /// <summary>Build the card shape from the embedded JSON definition.</summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Instant)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> for Deflecting Palm. No modes,
    /// no X, no target requests — the resolve body registers the one-shot
    /// prevention shield (beneficiary = caster) with the redirect rider on
    /// the supplied replacement bus.
    /// </summary>
    /// <param name="caster">The player who cast Deflecting Palm; the
    /// prevention beneficiary ("you").</param>
    /// <param name="replacements">The replacement bus the shield registers
    /// on. Required — without a live bus the shield has nothing to attach to
    /// (same gating posture as the family template's
    /// <c>CanBind</c>).</param>
    public static SpellDefinition BuildSpellDefinition(
        Player caster,
        ReplacementBus replacements)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(replacements);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => BuildResolveEffect(caster, replacements));
    }

    /// <summary>
    /// Build the resolve effect: register the one-shot
    /// <see cref="PreventNextDamageFromChosenSourceShield"/> for the caster
    /// (CR 615), with a redirect rider that deals the prevented amount to the
    /// source's controller (CR 119).
    /// </summary>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        Player caster,
        ReplacementBus replacements)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(replacements);

        return new IEffect[]
        {
            new Effect(
                $"{CardName}: prevent next damage to you this turn, deal it back to the source's controller (CR 615/119).",
                () =>
                {
                    replacements.Register(
                        new PreventNextDamageFromChosenSourceShield(
                            caster,
                            onPrevent: (amount, intent) =>
                            {
                                // CR 119 — "deals that much damage to that
                                // source's controller". A genuine damage
                                // event from Deflecting Palm, routed through
                                // Fx.DealDamage (Player → LoseLife).
                                var controller = ResolveSourceController(intent.Source);
                                if (controller is not null)
                                {
                                    Fx.DealDamage(controller, amount);
                                }
                            }));
                }),
        };
    }

    /// <summary>
    /// Resolve the controller of a damage source. A player source bounces
    /// back to itself; a card source to its current controller. Mirrors the
    /// family template's <c>ResolveSourceController</c>.
    /// </summary>
    private static Player? ResolveSourceController(object source) => source switch
    {
        Player p => p,
        ICard card => card.Controller,
        _ => null,
    };
}
