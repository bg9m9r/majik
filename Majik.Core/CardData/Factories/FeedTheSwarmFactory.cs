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
/// Named-card factory for Feed the Swarm (Zendikar Rising, {B}).
///
/// Sorcery. Oracle text (verified against Scryfall 2026-06-02):
///   "Destroy target creature or enchantment an opponent controls. You lose
///    life equal to that permanent's mana value."
///
/// Feed the Swarm is the mono-black "destroy target creature or enchantment"
/// removal — structurally the opponent-scoped, self-life-loss cousin of
/// <see cref="MortifyFactory"/> (destroy target creature or enchantment) with
/// the two riders the engine already has primitives for: (1) the target must
/// be a permanent <b>an opponent controls</b> (CR 109.5 — controller compared
/// against the resolving caster), and (2) a resolution rider <b>"you lose life
/// equal to that permanent's mana value"</b> (CR 119.3 + CR 202.3), the same
/// shape as <see cref="VendettaFactory"/>'s "you lose life equal to that
/// creature's toughness" — only the captured property (mana value vs
/// toughness) differs.
///
/// ## Implemented (v1)
/// - <b>Sorcery shape</b> at printed cost {B}. The base shape (name / Sorcery
///   type / {B} cost) is materialised from the embedded JSON definition
///   (<c>feed-the-swarm.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/> — same data-only posture as
///   <see cref="MortifyFactory"/> (the JSON <c>SpellDefinition</c> schema does
///   not express the opponent-scoped creature-or-enchantment target request or
///   the mana-value life-loss rider, so the resolve behaviour is layered on
///   here via <see cref="BuildDefinition"/>).
/// - <b>Destroy target creature or enchantment an opponent controls</b> —
///   <see cref="BuildDefinition"/> returns a <see cref="SpellDefinition"/> with
///   a single 1..1 "target creature or enchantment an opponent controls"
///   <see cref="TargetRequest"/>. The live <c>CandidateGatherer</c> walks every
///   player's battlefield, yielding cards with <see cref="CardType.Creature"/>
///   or <see cref="CardType.Enchantment"/> (CR 700.4 — a permanent may have
///   multiple card types) whose <see cref="Card.Controller"/> is NOT the caster
///   (CR 109.5 — "an opponent controls"). The bot's
///   <see cref="BotIntent.Removal"/> ranker pushes the opponent's biggest
///   threat up.
/// - On resolution: re-checks the target is still a Creature or Enchantment on
///   the Battlefield AND still controlled by an opponent of the caster
///   (CR 608.2b illegal-target gate — if the target is illegal the entire spell
///   does nothing, including the life-loss rider). When valid:
///   1. Captures the target's mana value (CR 202.3 —
///      <see cref="Card.ManaCostValue"/>'s <c>TotalValue</c>) <em>before</em>
///      destruction so the value is read while the permanent is still on the
///      battlefield.
///   2. Destroys the target via
///      <see cref="OracleSpellBinder.MoveToGraveyard(ICard, ZoneMoveReason)"/>
///      with <see cref="Majik.Core.Zones.ZoneMoveReason.Destroy"/> (CR 701.7) so
///      indestructible (CR 702.12) / regeneration shields (CR 701.15) are
///      honoured at the destroy site — Feed the Swarm does NOT print "can't be
///      regenerated".
///   3. Applies life loss to the caster equal to the captured mana value
///      (CR 119.3) — parity with <see cref="VendettaFactory"/>.
/// </summary>
[CardName("Feed the Swarm")]
public static class FeedTheSwarmFactory
{
    public const string CardName = "Feed the Swarm";

    /// <summary>JSON slug for the embedded base-shape definition.</summary>
    public const string Slug = "feed-the-swarm";

    /// <summary>
    /// Materialise the Sorcery card shape (name / Sorcery / {B}) from the
    /// embedded JSON definition. Resolve behaviour (destroy target creature or
    /// enchantment an opponent controls; lose life equal to its mana value) is
    /// built on demand via <see cref="BuildDefinition"/>, mirroring
    /// <see cref="MortifyFactory"/>.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var built = CardDefinitionFactory.Build(definition, owner);
        if (built is not Sorcery card)
        {
            throw new InvalidOperationException(
                $"Expected '{CardName}' to materialise as a Sorcery but got "
                + $"'{built.GetType().Name}'.");
        }

        return card;
    }

    /// <summary>
    /// Build the "destroy target creature or enchantment an opponent controls;
    /// you lose life equal to that permanent's mana value"
    /// <see cref="SpellDefinition"/>.
    ///
    /// On resolve: validates the target is still a Creature or Enchantment on
    /// the Battlefield AND controlled by an opponent of <paramref name="caster"/>
    /// (CR 608.2b — illegal target → no-op, including the life-loss rider). When
    /// valid, captures the target's mana value, destroys it via
    /// <see cref="OracleSpellBinder.MoveToGraveyard"/> with
    /// <see cref="Majik.Core.Zones.ZoneMoveReason.Destroy"/> (CR 701.7), then
    /// applies the life loss to the caster (CR 119.3).
    /// </summary>
    /// <param name="caster">Cast-time controller — defines "an opponent
    /// controls" (CR 109.5) and suffers the life loss equal to the target's
    /// mana value (CR 119.3).</param>
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
                    Description: "target creature or enchantment an opponent controls",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    // Agent-prompt MVP: live gather every creature / enchantment
                    // an opponent of the caster controls (CR 109.5). Removal
                    // intent in the bot's ranker pushes the opponent's biggest
                    // threat up.
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .Where(c => (c.HasType(CardType.Creature)
                                || c.HasType(CardType.Enchantment))
                            && !ReferenceEquals(c.Controller, caster))
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
                        $"{CardName}: destroy target creature or enchantment an opponent controls; you lose life equal to its mana value",
                        () =>
                        {
                            // CR 608.2b — resolution-time legality re-check. If
                            // the target is illegal the entire spell does
                            // nothing — including the life-loss rider.
                            if (resolved is not Permanent target) return;
                            if (target.Zone != ZoneType.Battlefield) return;
                            if (!target.HasType(CardType.Creature)
                                && !target.HasType(CardType.Enchantment)) return;
                            // CR 109.5 — "an opponent controls": the target must
                            // be controlled by a player other than the caster.
                            if (ReferenceEquals(target.Controller, caster)) return;

                            // CR 202.3 — capture mana value BEFORE destruction
                            // (the permanent leaves the battlefield after the
                            // move; read its mana cost while it is still live).
                            var manaValue = target.ManaCostValue.TotalValue;

                            // CR 701.7 — Destroy. Indestructible (CR 702.12) and
                            // regeneration (CR 701.15) honoured via the
                            // Destroy-reason gate in MoveToGraveyard (Feed the
                            // Swarm does NOT print "can't be regenerated").
                            OracleSpellBinder.MoveToGraveyard(
                                target,
                                Majik.Core.Zones.ZoneMoveReason.Destroy);

                            // CR 119.3 — "You lose life equal to that permanent's
                            // mana value." Applied to the caster.
                            caster.LoseLife(manaValue);
                        }),
                };
            });
    }
}
