using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Tokens;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Bovine Intervention (Modern Horizons 3, {1}{W}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "Destroy target artifact or creature. Its controller creates a 2/2
///    white Ox creature token."
///
/// ## Why it gets its own factory
/// Bovine Intervention combines two already-shipping templates: the
/// "destroy target artifact or creature" targeting of
/// <see cref="PutrefyFactory"/> (artifact-or-creature candidate gather +
/// resolution-time legality re-check) with the "its controller creates a
/// vanilla token" tail of <see cref="GenerousGiftFactory"/> /
/// <see cref="BeastWithinFactory"/> — only the token is a 2/2 white Ox
/// instead of a 3/3 green Elephant/Beast. No new engine mechanic is
/// required; <see cref="CardSubtype.Ox"/> already exists in the subtype
/// enum. Unlike Putrefy it is a plain Destroy (no "can't be regenerated"
/// rider), so regeneration (CR 701.15) is honoured.
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {1}{W}, white. Card shape comes from the
///   embedded JSON (<c>bovine-intervention.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory"/>.
/// - <b>Destroy target artifact or creature</b> — a single 1..1
///   "target artifact or creature" <see cref="TargetRequest"/>; the
///   candidate gatherer walks every battlefield yielding permanents with
///   type Artifact (CR 301) or Creature (CR 302). The caster may target
///   their own permanents — the printed text has no controller restriction
///   (CR 115.1 / CR 608.2b).
/// - <b>Destroyed permanent's controller creates a 2/2 white Ox token</b> —
///   the controller at the moment of resolution (CR 608.2b
///   last-known-information) receives the token via
///   <see cref="TokenFactory.CreateOnBattlefield"/>. The token is a 2/2
///   white Ox creature token (CR 111.4 — white stamped via
///   <see cref="TokenFactory.TokenSpec.Colors"/>; enters with summoning
///   sickness via <see cref="TokenFactory"/>).
/// - If the target is illegal at resolution (CR 608.2b), neither the
///   destroy nor the token occur.
///
/// Indestructible (CR 702.12) and regeneration (CR 701.15) are honoured at
/// the destroy site via
/// <see cref="OracleSpellBinder.MoveToGraveyard(ICard, ZoneMoveReason)"/>
/// with <see cref="ZoneMoveReason.Destroy"/>. The token half of the spell
/// is unconditional per the printed wording and fires even when
/// indestructible cancels the destroy.
/// </summary>
[CardName("Bovine Intervention")]
public static class BovineInterventionFactory
{
    public const string CardName = "Bovine Intervention";
    public const string Slug = "bovine-intervention";
    public const string PrintedManaCost = "{1}{W}";

    private static readonly TokenFactory.TokenSpec OxTokenSpec =
        new(Name: "Ox", Power: 2, Toughness: 2,
            Subtypes: new[] { CardSubtype.Ox },
            // CR 105 / CR 111.4 — printed "2/2 white Ox creature token".
            Colors: new[] { Majik.Core.ValueObjects.ManaColor.White });

    /// <summary>Build the card shape from the embedded JSON definition.</summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Instant)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Bovine Intervention
    /// is cast. Single 1..1 "target artifact or creature" request; on
    /// resolution:
    /// <list type="number">
    ///   <item>Confirms the target is still a <see cref="Permanent"/> on the
    ///     Battlefield with type Artifact or Creature (CR 608.2b — if the
    ///     target is illegal at resolution, the whole spell does nothing).</item>
    ///   <item>Snapshots the target's controller (CR 608.2b
    ///     last-known-information — "its controller" at resolution).</item>
    ///   <item>Destroys the target via
    ///     <see cref="OracleSpellBinder.MoveToGraveyard"/> with
    ///     <see cref="ZoneMoveReason.Destroy"/> (CR 701.7) — plain Destroy,
    ///     so indestructible (CR 702.12) and regeneration (CR 701.15)
    ///     are honoured.</item>
    ///   <item>The destroyed permanent's controller creates a 2/2 white Ox
    ///     creature token (CR 111.6 / <see cref="TokenFactory"/>).</item>
    /// </list>
    /// </summary>
    /// <param name="targetResolver">Maps the agent-supplied raw target token
    /// to the live engine object. Pass <c>o =&gt; o</c> for tests that hand
    /// permanents directly.</param>
    public static SpellDefinition BuildSpellDefinition(
        Func<object, object> targetResolver)
    {
        ArgumentNullException.ThrowIfNull(targetResolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    Description: "target artifact or creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    // Agent-prompt: walk every battlefield, yield permanents
                    // that are artifacts (CR 301) or creatures (CR 302).
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .Where(c => c.HasType(CardType.Artifact)
                                 || c.HasType(CardType.Creature))
                        .Cast<object>()
                        .ToList()),
            },
            EffectFactory: chosen =>
            {
                var raw = targetResolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    new Effect(
                        $"{CardName}: destroy target artifact or creature + create 2/2 Ox token",
                        () =>
                        {
                            if (raw is not Permanent target) return;

                            // CR 608.2b — resolution-time legality re-check.
                            if (target.Zone != ZoneType.Battlefield) return;
                            if (!target.HasType(CardType.Artifact)
                                && !target.HasType(CardType.Creature)) return;

                            // Snapshot controller BEFORE moving the permanent —
                            // "its controller" refers to the controller at the
                            // moment of resolution (CR 608.2b last-known-info).
                            var targetController = target.Controller ?? target.Owner;

                            // CR 701.7 — Destroy. Plain Destroy, so
                            // indestructible (CR 702.12) and regeneration
                            // (CR 701.15) are honoured by MoveToGraveyard via
                            // the Destroy-reason gate. The token half is
                            // unconditional per the printed oracle text — it
                            // fires even when indestructible cancels the destroy.
                            OracleSpellBinder.MoveToGraveyard(target, ZoneMoveReason.Destroy);

                            // "Its controller creates a 2/2 white Ox creature
                            // token." (CR 111.4 / CR 111.6). White colour
                            // identity stamped on the spec (CR 105 / CR 903.4).
                            if (targetController == null) return;
                            TokenFactory.CreateOnBattlefield(OxTokenSpec, targetController);
                        }),
                };
            });
    }
}
