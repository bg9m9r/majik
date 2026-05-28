using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Spells;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Metallic Rebuke (Aether Revolt / Jumpstart 2022,
/// {2}{U}).
///
/// Instant. Oracle text:
///   "Improvise (Your artifacts can help cast this spell. Each artifact you
///    tap after you're done activating mana abilities pays for {1}.)
///    Counter target spell unless its controller pays {3}."
///
/// ## Implemented (v1)
///
/// - Instant card shape ({2}{U}, Blue), mana value 3.
///
/// - <b>Improvise (CR 702.126 / 702.127)</b>: wired as a
///   <see cref="KeywordAbility"/> marker so the bot's
///   <see cref="Majik.Core.Players.Agents.ImproviseAltCostProbe"/> surfaces
///   this card. The working cost-reduction primitive is exposed via
///   <see cref="BuildAdditionalCost"/>: the caller constructs an
///   <see cref="ImproviseAdditionalCost"/> with their pre-selected untapped
///   artifacts and threads it through
///   <see cref="Majik.Core.Game.SpellCastFlow.CastAsync"/>'s
///   <c>additionalCosts</c> parameter. With 2 artifacts tapped the printed
///   {2}{U} is reduced to {U}; coloured pips are preserved per CR 702.127.
///   Pattern mirrors <see cref="KappaCannoneerFactory.BuildAdditionalCost"/>.
///
/// - <b>Counter target spell unless its controller pays {3}</b>: wired in
///   <see cref="BuildSpellDefinition"/>. Declares a single 1..1 "target spell"
///   TargetRequest; on resolution attempts to spend {3} generic from the
///   target's controller's mana pool (CR 118.4 — if they have {3} it is
///   auto-paid and the counter no-ops). If the payment fails the target spell
///   is removed from the stack and its card moves to the graveyard (CR 701.5).
///   Pattern mirrors <see cref="DazeFactory.BuildDefinition"/> with the
///   unless-pay amount raised from 1 to 3.
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — card shape only. Suitable for shape /
///   dispatcher tests.
/// - <see cref="BuildAdditionalCost"/> — build the Improvise cost primitive.
/// - <see cref="BuildSpellDefinition"/> — build the counter-unless-pay-{3}
///   SpellDefinition for use at cast time.
/// </summary>
[CardName("Metallic Rebuke")]
public static class MetallicRebukeFactory
{
    public const string CardName = "Metallic Rebuke";
    public const string PrintedManaCost = "{2}{U}";

    /// <summary>Create a Metallic Rebuke card owned by <paramref name="owner"/>.
    /// Card shape only — call <see cref="BuildSpellDefinition"/> separately to
    /// produce the resolve-time counter effect.</summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Instant(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.126 / 702.127 — Improvise keyword marker. The marker is the
        // same one KappaCannoneerFactory and ChordOfCallingFactory use for
        // their respective keywords: purely descriptive, but picked up by the
        // bot's ImproviseAltCostProbe to surface the effective cast cost.
        card.AddAbility(new KeywordAbility("Improvise", card, owner));

        return card;
    }

    /// <summary>
    /// CR 702.127 — build the Improvise additional cost for this Metallic
    /// Rebuke spell with the caller-selected untapped artifacts. The caller
    /// threads the returned cost through
    /// <see cref="Majik.Core.Game.SpellCastFlow.CastAsync"/>'s
    /// <c>additionalCosts</c> parameter; the cast flow taps the chosen
    /// artifacts and folds {1} of generic reduction per tap into the mana
    /// payment. Tests + bots pre-select the artifact list, mirroring the
    /// deferred agent prompt pattern used by
    /// <see cref="KappaCannoneerFactory.BuildAdditionalCost"/>.
    /// </summary>
    public static ImproviseAdditionalCost BuildAdditionalCost(
        ICard card, IReadOnlyList<Permanent> tappedArtifacts) =>
        new(card, tappedArtifacts);

    /// <summary>
    /// Build the "counter target spell unless its controller pays {3}"
    /// SpellDefinition. Mirrors <see cref="DazeFactory.BuildDefinition"/> with
    /// the unless-pay amount raised to 3 generic mana. At resolution time the
    /// engine auto-consults the target's controller's mana pool; if {3} is
    /// available it is spent and the counter no-ops (CR 118.4). Otherwise the
    /// target spell is removed from the stack and sent to the graveyard
    /// (CR 701.5, CR 608.2b).
    /// </summary>
    /// <param name="targetResolver">Target resolver from the caller's
    /// <see cref="GameContext"/> (chosen handle → live stack object).</param>
    /// <param name="stack">Live stack required to remove the countered spell.
    /// May be null in shape-only tests — the effect becomes a no-op.</param>
    public static SpellDefinition BuildSpellDefinition(
        Func<object, object> targetResolver,
        Majik.Core.Stack.Stack? stack)
    {
        ArgumentNullException.ThrowIfNull(targetResolver);

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
                        "Metallic Rebuke — counter target spell unless its controller pays {3}",
                        () =>
                        {
                            if (stack == null || resolved is not ISpell spell) return;

                            // CR 118.4 — if the target's controller can pay {3}
                            // they may do so to save their spell. v1 auto-pays
                            // when able (mirrors DazeFactory's unless-pay-1
                            // pattern with N=3).
                            if (spell.Controller is not null
                                && spell.Controller.PayMana(
                                    ManaCost.Zero.AddGenericCost(3)))
                            {
                                return; // paid — counter no-ops, spell survives
                            }

                            // Controller couldn't / wouldn't pay — counter
                            // the spell (CR 701.5).
                            OracleSpellBinder.RemoveFromStack(stack, spell);
                            spell.Card.SetZone(ZoneType.Graveyard);
                        }),
                };
            });
    }
}
