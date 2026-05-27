using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Tome Scour (Magic 2010, {U}).
///
/// Sorcery. Oracle text:
///   "Target player mills five cards."
///
/// ## Implemented (v1)
/// - Sorcery {U} (Blue) card shape with owner / controller wired.
/// - <b>Mill 5 target player</b> — <see cref="BuildDefinition"/> declares
///   a single 1..1 "target player" <see cref="TargetRequest"/> and on resolve
///   mills <see cref="MillCount"/> cards from the chosen target via
///   <see cref="MillAction.Apply"/> (CR 701.13). When the chosen target token
///   does not resolve to a <see cref="Player"/> the effect no-ops per
///   CR 608.2b (illegal target at resolution).
///
/// ## Deferred (v1 gaps)
/// - <b>Action validator filtering</b>: target list is unconstrained
///   "target player"; the agent's pick is honoured verbatim.
/// </summary>
[CardName("Tome Scour")]
public static class TomeScourFactory
{
    public const string CardName = "Tome Scour";
    public const string PrintedManaCost = "{U}";
    public const int MillCount = 5;

    /// <summary>
    /// Construct Tome Scour as a Sorcery card. Suitable for shape / dispatcher
    /// tests; the resolve-time spell body lives in <see cref="BuildDefinition"/>.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Sorcery(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        return card;
    }

    /// <summary>
    /// Build the "target player mills five cards" SpellDefinition. The
    /// chosen target token is resolved via <paramref name="targetResolver"/>
    /// (typically the agent's chosen <see cref="Player"/> passed through
    /// verbatim).
    /// </summary>
    /// <param name="targetResolver">Resolves the raw target token chosen
    /// by the caster (expected to yield a <see cref="Player"/>). When the
    /// resolver returns anything that isn't a <see cref="Player"/> the
    /// effect no-ops per CR 608.2b (illegal target at resolution).</param>
    public static SpellDefinition BuildDefinition(
        Func<object, object> targetResolver)
    {
        ArgumentNullException.ThrowIfNull(targetResolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    Description: "target player",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                var resolved = targetResolver(raw);
                return new IEffect[]
                {
                    new Effect(
                        $"{CardName} — target player mills {MillCount} cards",
                        () =>
                        {
                            if (resolved is not Player target) return;
                            MillAction.Apply(target, MillCount);
                        }),
                };
            });
    }
}
