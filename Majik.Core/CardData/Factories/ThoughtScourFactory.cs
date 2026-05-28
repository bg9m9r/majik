using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Thought Scour (Innistrad / Dark Ascension, {U}).
///
/// Instant. Oracle text:
///   "Target player mills two cards. Draw a card."
///
/// ## Implemented (v1)
/// - Instant {U} (Blue) card shape with owner / controller wired.
/// - <b>Mill 2 target player, then draw 1</b> — <see cref="BuildDefinition"/>
///   declares a single 1..1 "target player" <see cref="TargetRequest"/> whose
///   <c>CandidateGatherer</c> yields ALL players (caster + opponents), since
///   Thought Scour allows targeting yourself for self-mill. On resolve:
///   <ol>
///     <li>Mills <see cref="MillCount"/> cards from the chosen target via
///         <see cref="MillAction.Apply"/> (CR 701.13).</li>
///     <li>Draws 1 card for the caster (CR 121.1). Empty library flags
///         the caster for the draw-from-empty penalty (CR 704.5b).</li>
///   </ol>
///   When the chosen target token does not resolve to a <see cref="Player"/>
///   neither effect fires (CR 608.2b — illegal target at resolution).
///
/// ## Deferred (v1 gaps)
/// - <b>Action validator filtering</b>: target list is unconstrained
///   "target player"; the agent's pick is honoured verbatim.
/// </summary>
[CardName("Thought Scour")]
public static class ThoughtScourFactory
{
    public const string CardName = "Thought Scour";
    public const string PrintedManaCost = "{U}";
    public const int MillCount = 2;

    /// <summary>
    /// Construct Thought Scour as an Instant card. Suitable for shape /
    /// dispatcher tests; the resolve-time spell body lives in
    /// <see cref="BuildDefinition"/>.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Instant(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        return card;
    }

    /// <summary>
    /// Build the "target player mills two cards, then draw a card"
    /// SpellDefinition.
    ///
    /// <para>The <paramref name="caster"/> is needed to:
    /// <list type="bullet">
    ///   <item>populate the <c>CandidateGatherer</c> (caster is a legal
    ///     self-mill target), and</item>
    ///   <item>resolve the "draw a card" half of the effect.</item>
    /// </list>
    /// </para>
    ///
    /// <para>The chosen target token is resolved via
    /// <paramref name="targetResolver"/> (typically the agent's chosen
    /// <see cref="Player"/> passed through verbatim). When the resolver
    /// returns anything that isn't a <see cref="Player"/> both the mill
    /// and the draw no-op per CR 608.2b.</para>
    /// </summary>
    /// <param name="caster">The player casting this spell — determines the
    /// draw recipient and seeds the CandidateGatherer.</param>
    /// <param name="targetResolver">Resolves the raw target token chosen
    /// by the caster.</param>
    public static SpellDefinition BuildDefinition(
        Player caster,
        Func<object, object> targetResolver)
    {
        ArgumentNullException.ThrowIfNull(caster);
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
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Mill,
                    // CR 115.4 — Thought Scour says "target player", not
                    // "target opponent"; the caster is a legal target
                    // (self-mill is a core Grixis graveyard strategy).
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .Cast<object>()
                        .ToList()
                        .AsReadOnly()),
            },
            EffectFactory: p =>
            {
                var raw      = p.Targets[0][0];
                var resolved = targetResolver(raw);
                return new IEffect[]
                {
                    new Effect(
                        $"{CardName} — target player mills {MillCount}, then caster draws 1",
                        () =>
                        {
                            // CR 608.2b — if the target token did not resolve
                            // to a Player the entire spell is a no-op.
                            if (resolved is not Player target) return;

                            // 1. "Target player mills two cards." (CR 701.13)
                            MillAction.Apply(target, MillCount);

                            // 2. "Draw a card." — for the caster (CR 121.1).
                            // Empty library: flag player for the draw-from-
                            // empty-library SBA loss (CR 704.5b).
                            var top = caster.Zones.Library.GetCards().FirstOrDefault();
                            if (top == null)
                            {
                                caster.MarkTriedToDrawFromEmptyLibrary();
                                return;
                            }
                            caster.Zones.Library.RemoveCard(top);
                            caster.Zones.Hand.AddCard(top);
                            top.SetZone(ZoneType.Hand);
                        }),
                };
            });
    }
}
