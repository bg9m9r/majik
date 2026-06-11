using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Spells;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Broken Concentration (Torment, {1}{U}{U}).
///
/// Instant. Oracle text (verified against Scryfall 2026-06-10):
///   "Counter target spell.
///    Madness {3}{U} (If you discard this card, discard it into exile. When you
///    do, cast it for its madness cost or put it into your graveyard.)"
///
/// The counter body is the archetypal hard counter — byte-for-byte the
/// <see cref="SawItComingFactory"/> / <see cref="CancelFactory"/> shape (no type
/// filter, any spell is a legal target). At resolution the target spell is
/// removed from the stack via <see cref="OracleSpellBinder.RemoveFromStack"/>
/// and its card moves to its owner's graveyard (CR 701.5, CR 608.2b). Differs
/// from <see cref="NegateFactory"/> in that there is no noncreature restriction
/// — creature spells are countered too.
///
/// ## Madness (CR 702.35) — intrinsic, no factory code
///
/// Madness works engine-wide for every catalogued card: the central discard
/// funnel <see cref="Majik.Core.Primitives.Fx.DiscardCard"/> consults
/// <see cref="Majik.Core.Keywords.MadnessCatalog"/> (Broken Concentration →
/// {3}{U}) and routes a discarded madness card to exile + offers it for its
/// madness cost automatically. The "Madness {3}{U}" oracle line therefore needs
/// NO wiring here; this factory implements only the "Counter target spell."
/// body. (MadnessDiscardFunnelTests + the catalog cover the mechanic.)
///
/// CR rule references: 117.5 (mana cost), 608.2b (illegal-target check),
/// 701.5 (counter), 702.35 (madness — intrinsic via the discard funnel).
/// </summary>
[CardName("Broken Concentration")]
public static class BrokenConcentrationFactory
{
    public const string CardName = "Broken Concentration";
    public const string Slug = "broken-concentration";
    public const string PrintedManaCost = "{1}{U}{U}";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Materialise the Broken Concentration card shape from the embedded JSON
    /// definition (<c>broken-concentration.json</c>) via
    /// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
    /// <see cref="CardDefinitionFactory.Build"/>. The counter
    /// <see cref="SpellDefinition"/> is built separately via
    /// <see cref="BuildSpellDefinition"/> (the JSON schema doesn't express the
    /// counter target-and-resolve shape) — same posture as
    /// <see cref="SawItComingFactory"/>.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var built = CardDefinitionFactory.Build(Definition, owner);
        if (built is not Instant card)
        {
            throw new InvalidOperationException(
                $"Expected '{CardName}' to materialise as an Instant but got "
                + $"'{built.GetType().Name}'.");
        }
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the "counter target spell" SpellDefinition. Declares a single
    /// 1..1 "target spell" request (no type filter); on resolution removes the
    /// target from the stack and sends its card to the graveyard (CR 701.5).
    /// Mirrors <see cref="SawItComingFactory.BuildSpellDefinition"/>.
    /// </summary>
    /// <param name="targetResolver">Target resolver from the caller's
    /// <see cref="GameContext"/> (chosen → live stack object).</param>
    /// <param name="stack">Live stack — required to remove the countered
    /// spell. Null in pure-shape tests; the effect becomes a no-op.</param>
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
                    new Effect("Broken Concentration — counter target spell", () =>
                    {
                        if (stack == null || resolved is not ISpell spell) return;

                        OracleSpellBinder.RemoveFromStack(stack, spell);
                        spell.Card.SetZone(ZoneType.Graveyard);
                    }),
                };
            });
    }
}
