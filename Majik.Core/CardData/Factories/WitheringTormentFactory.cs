using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Withering Torment (Murders at Karlov Manor, {2}{B}).
///
/// Instant. Oracle text (verified against Scryfall 2026-06-14):
///   "Destroy target creature or enchantment. You lose 2 life."
///
/// Withering Torment is the mono-black "destroy target creature or
/// enchantment" instant with a flat self-life-loss rider — structurally the
/// <see cref="MortifyFactory"/> destroy (target creature or enchantment, any
/// controller) combined with the resolution life-loss rider shape of
/// <see cref="FeedTheSwarmFactory"/>. It differs from Feed the Swarm in two
/// ways: (1) the target is NOT restricted to permanents an opponent controls
/// (any creature or enchantment is a legal target), and (2) the life loss is a
/// fixed 2 rather than the target's mana value.
///
/// ## Implemented (v1)
/// - <b>Instant shape</b> at printed cost {2}{B}. The base shape (name /
///   Instant type / {2}{B} cost) is materialised from the embedded JSON
///   definition (<c>withering-torment.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/> — same data-only posture as
///   <see cref="MortifyFactory"/> (the JSON <c>SpellDefinition</c> schema does
///   not express the creature-or-enchantment target request or the life-loss
///   rider, so the resolve behaviour is layered on here via
///   <see cref="BuildDefinition"/>).
/// - <b>Destroy target creature or enchantment</b> —
///   <see cref="BuildDefinition"/> returns a <see cref="SpellDefinition"/> with
///   a single 1..1 "target creature or enchantment"
///   <see cref="TargetRequest"/>. The live <c>CandidateGatherer</c> walks every
///   player's battlefield, yielding cards with <see cref="CardType.Creature"/>
///   or <see cref="CardType.Enchantment"/> (CR 700.4 — a permanent may have
///   multiple card types) regardless of controller. The bot's
///   <see cref="BotIntent.Removal"/> ranker pushes opponent permanents up.
/// - On resolution: re-checks the target is still a Creature or Enchantment on
///   the Battlefield (CR 608.2b illegal-target gate — if the target is illegal
///   the entire spell does nothing, including the life-loss rider). When valid:
///   1. Destroys the target via
///      <see cref="OracleSpellBinder.MoveToGraveyard(ICard, ZoneMoveReason)"/>
///      with <see cref="Majik.Core.Zones.ZoneMoveReason.Destroy"/> (CR 701.7)
///      so indestructible (CR 702.12) / regeneration shields (CR 701.15) are
///      honoured at the destroy site — Withering Torment does NOT print "can't
///      be regenerated".
///   2. Applies a fixed 2 life loss to the caster (CR 119.3).
/// </summary>
[CardName("Withering Torment")]
public static class WitheringTormentFactory
{
    public const string CardName = "Withering Torment";

    /// <summary>JSON slug for the embedded base-shape definition.</summary>
    public const string Slug = "withering-torment";

    /// <summary>Fixed life the caster loses on resolution (CR 119.3).</summary>
    private const int LifeLoss = 2;

    /// <summary>
    /// Materialise the Instant card shape (name / Instant / {2}{B}) from the
    /// embedded JSON definition. Resolve behaviour (destroy target creature or
    /// enchantment; you lose 2 life) is built on demand via
    /// <see cref="BuildDefinition"/>, mirroring <see cref="MortifyFactory"/>.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var built = CardDefinitionFactory.Build(definition, owner);
        if (built is not Instant card)
        {
            throw new InvalidOperationException(
                $"Expected '{CardName}' to materialise as an Instant but got "
                + $"'{built.GetType().Name}'.");
        }

        return card;
    }

    /// <summary>
    /// Build the "destroy target creature or enchantment; you lose 2 life"
    /// <see cref="SpellDefinition"/>.
    ///
    /// On resolve: validates the target is still a Creature or Enchantment on
    /// the Battlefield (CR 608.2b — illegal target → no-op, including the
    /// life-loss rider). When valid, destroys the target via
    /// <see cref="OracleSpellBinder.MoveToGraveyard"/> with
    /// <see cref="Majik.Core.Zones.ZoneMoveReason.Destroy"/> (CR 701.7), then
    /// applies the fixed 2 life loss to the caster (CR 119.3).
    /// </summary>
    /// <param name="caster">Cast-time controller — "you" in "You lose 2 life"
    /// (CR 119.3).</param>
    /// <param name="targetResolver">Maps the agent-supplied raw target token to
    /// the live engine object. Pass <c>o =&gt; o</c> for tests that hand
    /// permanents directly.</param>
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
                    Description: "target creature or enchantment",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    // Agent-prompt MVP: live gather every creature / enchantment
                    // on any battlefield (Withering Torment is not restricted to
                    // permanents an opponent controls). Removal intent in the
                    // bot's ranker pushes opponent permanents up.
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .Where(c => c.HasType(CardType.Creature)
                            || c.HasType(CardType.Enchantment))
                        .Cast<object>()
                        .ToList()),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                var resolved = targetResolver(raw);
                return new IEffect[]
                {
                    new Effect(
                        $"{CardName}: destroy target creature or enchantment; you lose {LifeLoss} life",
                        () =>
                        {
                            // CR 608.2b — resolution-time legality re-check. If
                            // the target is illegal the entire spell does
                            // nothing — including the life-loss rider.
                            if (resolved is not Permanent target) return;
                            if (target.Zone != ZoneType.Battlefield) return;
                            if (!target.HasType(CardType.Creature)
                                && !target.HasType(CardType.Enchantment)) return;

                            // CR 701.7 — Destroy. Indestructible (CR 702.12) and
                            // regeneration (CR 701.15) honoured via the
                            // Destroy-reason gate in MoveToGraveyard (Withering
                            // Torment does NOT print "can't be regenerated").
                            OracleSpellBinder.MoveToGraveyard(
                                target,
                                Majik.Core.Zones.ZoneMoveReason.Destroy);

                            // CR 119.3 — "You lose 2 life." Fixed amount applied
                            // to the caster.
                            caster.LoseLife(LifeLoss);
                        }),
                };
            });
    }
}
