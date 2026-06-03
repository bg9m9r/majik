using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Baleful Mastery (Commander Legends: Battle for
/// Baldur's Gate, {3}{B}).
///
/// Instant. Oracle text:
///   "You may pay {1}{B} rather than pay this spell's mana cost.
///    If the {1}{B} cost was paid, an opponent draws a card.
///    Exile target creature or planeswalker."
///
/// ## Scryfall identity
/// <list type="bullet">
///   <item>Mana cost: {3}{B}; mana value 4</item>
///   <item>Type line: Instant; colors: B</item>
///   <item>Alternative cost: {1}{B}</item>
/// </list>
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {3}{B} (black). The card shape is loaded from
///   the embedded JSON definition (<c>baleful-mastery.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built
///   through <see cref="CardDefinitionFactory"/> — same posture as the other
///   data-backed factories (e.g.
///   <see cref="AnOfferYouCantRefuseFactory"/>).
/// - Resolve-time <see cref="SpellDefinition"/> (via
///   <see cref="BuildSpellDefinition"/>) declares one 1..1
///   "target creature or planeswalker" request. The live
///   <c>CandidateGatherer</c> walks every player's battlefield yielding
///   permanents whose card-type set includes Creature or Planeswalker
///   (mirrors <see cref="BedevilFactory"/>'s gather, narrowed to the two
///   exilable types). On resolution the target is exiled (CR 701.21) via
///   owner-routed zone moves (the established Anguished Unmaking / Path to
///   Exile shape), after a CR 608.2b resolution-time legality re-check.
/// - <b>Alternative cost rider</b> — "If the {1}{B} cost was paid, an
///   opponent draws a card." The <c>alternativeCostPaid</c> flag passed to
///   <see cref="BuildSpellDefinition"/> records which cost the caster paid;
///   when set, the resolve body makes an opponent draw a card (CR 121.1) via
///   <see cref="Fx.DrawCards"/>. The opponent is selected as the first
///   non-caster player from <see cref="ChosenSpellParams.AllPlayers"/>
///   (CR 102.1 — in a two-player game the single other player). The
///   draw-clause and the exile-clause are independent printed sentences, so
///   the opponent still draws even if the exile half fizzles on an illegal
///   target (CR 608.2b).
///
/// ## Alternative-cost wiring
/// The {1}{B} optional alternative cost itself (CR 118.9 — "you may pay …
/// rather than pay this spell's mana cost") is the host's cast-flow
/// responsibility: the caller charges <see cref="AlternativeManaCost"/> via
/// the standard <c>SpellCastFlow.CastAsync(alternativeCost: …)</c> branch and
/// passes the resulting "was the alt cost paid?" boolean into
/// <see cref="BuildSpellDefinition"/>. This mirrors the Surge convention
/// (<see cref="RecklessBushwhackerFactory"/>) where the alt-cost mechanism
/// and the "if its [alt] cost was paid" conditional rider are decoupled — no
/// per-card alt-cost sentinel is required because the rider only depends on a
/// single boolean known at cast time.
///
/// ## Rules citations
/// - CR 118.9 — alternative cost ({1}{B} rather than the printed {3}{B}).
/// - CR 121.1 — draw a card.
/// - CR 701.21 — Exile.
/// - CR 608.2b — illegal target at resolution → that part of the effect does
///   nothing (here: the exile clause no-ops, the independent draw clause
///   still fires).
/// </summary>
[CardName("Baleful Mastery")]
public static class BalefulMasteryFactory
{
    public const string CardName = "Baleful Mastery";
    public const string Slug = "baleful-mastery";

    /// <summary>CR 118.9 — the printed alternative mana cost {1}{B}.</summary>
    public const string AlternativeManaCost = "{1}{B}";

    /// <summary>
    /// Build the card shape from the embedded JSON definition. Behaviour
    /// (exile + conditional opponent-draw) is supplied at resolution via
    /// <see cref="BuildSpellDefinition"/>.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Instant)CardDefinitionFactory.Build(definition, owner);
    }

    /// <summary>
    /// Build the "exile target creature or planeswalker; if the {1}{B} cost
    /// was paid, an opponent draws a card" SpellDefinition.
    /// </summary>
    /// <param name="caster">The controller of Baleful Mastery — used to
    /// identify "an opponent" for the conditional draw rider.</param>
    /// <param name="targetResolver">Maps the agent-supplied raw target token
    /// to the live engine object. Pass <c>o =&gt; o</c> for tests that hand
    /// permanents directly.</param>
    /// <param name="alternativeCostPaid"><c>true</c> when the caster paid the
    /// {1}{B} alternative cost rather than the printed {3}{B} — gates the
    /// "an opponent draws a card" rider (CR 121.1).</param>
    public static SpellDefinition BuildSpellDefinition(
        Player caster,
        Func<object, object> targetResolver,
        bool alternativeCostPaid)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(targetResolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature or planeswalker",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    // Agent-prompt MVP: live gather every creature / planeswalker
                    // on any battlefield. Removal intent in the bot's ranker
                    // pushes opponent permanents up.
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .Where(c => c.HasType(CardType.Creature)
                            || c.HasType(CardType.Planeswalker))
                        .Cast<object>()
                        .ToList()),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                var resolved = targetResolver(raw);

                // CR 102.1 — "an opponent". In a two-player game the single
                // other player; pick the first non-caster from the players
                // the cast flow stamped on ChosenSpellParams.AllPlayers.
                var opponent = (p.AllPlayers ?? Array.Empty<Player>())
                    .FirstOrDefault(pl => !ReferenceEquals(pl, caster));

                return new IEffect[]
                {
                    new Effect(
                        $"{CardName}: exile target creature or planeswalker" +
                        (alternativeCostPaid ? "; an opponent draws a card" : string.Empty),
                        () =>
                        {
                            // CR 121.1 — the {1}{B} alternative-cost rider:
                            // an opponent draws a card. Independent of the
                            // exile clause (separate printed sentences), so
                            // it fires even when the exile half fizzles on an
                            // illegal target (CR 608.2b).
                            if (alternativeCostPaid && opponent != null)
                            {
                                Fx.DrawCards(opponent, 1);
                            }

                            // CR 608.2b — resolution-time legality re-check:
                            // exile only fires when the target is still a
                            // creature or planeswalker on the battlefield.
                            if (resolved is not Permanent target) return;
                            if (target.Zone != ZoneType.Battlefield) return;
                            if (!target.HasType(CardType.Creature)
                                && !target.HasType(CardType.Planeswalker)) return;

                            // CR 701.21 — Exile. Routed through the owning
                            // player's zones so owner-of-zone bookkeeping stays
                            // consistent across multi-player games (mirrors
                            // Anguished Unmaking / Path to Exile). Indestructible
                            // (CR 702.12) does not prevent exile.
                            var fromOwner = target.Owner;
                            if (fromOwner != null)
                            {
                                fromOwner.Zones.Battlefield.RemoveCard(target);
                                fromOwner.Zones.Exile.AddCard(target);
                            }
                            target.SetZone(ZoneType.Exile);
                        }),
                };
            });
    }
}
