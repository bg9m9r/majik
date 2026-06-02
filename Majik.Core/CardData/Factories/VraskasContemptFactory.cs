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
/// Named-card factory for Vraska's Contempt (Ixalan, {2}{B}{B}).
///
/// Instant. Oracle text (verified against Scryfall 2026-06-01):
///   "Exile target creature or planeswalker. You gain 2 life."
///
/// Vraska's Contempt is the <b>exile</b> cousin of
/// <see cref="HerosDownfallFactory"/> (Hero's Downfall — "destroy target
/// creature or planeswalker"): same creature-or-planeswalker target shape,
/// but it <b>exiles</b> (CR 701.21) instead of destroying (so indestructible
/// / regeneration don't save the target), and adds a fixed "You gain 2 life"
/// rider (CR 119.3) that resolves as part of the same resolution.
///
/// ## Implemented (v1)
/// - <b>Instant shape</b> at printed cost {2}{B}{B}. The base shape (name /
///   Instant type / {2}{B}{B} cost) is materialised from the embedded JSON
///   definition (<c>vraskas-contempt.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/> — same posture as
///   <see cref="DreadboreFactory"/> (the JSON <c>SpellDefinition</c> schema
///   does not yet express a creature-or-planeswalker target request plus a
///   life-gain rider, so the resolve behaviour is layered on here via
///   <see cref="BuildDefinition"/>).
/// - <b>Exile target creature or planeswalker</b> —
///   <see cref="BuildDefinition"/> returns a <see cref="SpellDefinition"/>
///   with a single 1..1 "target creature or planeswalker"
///   <see cref="TargetRequest"/>. The live <c>CandidateGatherer</c> walks
///   every player's battlefield, yielding cards with
///   <see cref="CardType.Creature"/> or <see cref="CardType.Planeswalker"/>
///   (CR 700.4 — a permanent may have multiple card types). The bot's
///   <see cref="BotIntent.Removal"/> ranker pushes opponent permanents
///   to the top.
/// - On resolution: re-checks the target is still a Creature or
///   Planeswalker on the Battlefield (CR 608.2b illegal-target gate),
///   then exiles via
///   <see cref="OracleSpellBinder.MoveToExile(ICard)"/> (CR 701.21).
///   Exile is not a destroy, so indestructible (CR 702.12) and regeneration
///   (CR 701.15) do not protect the target — matching the printed text.
/// - <b>You gain 2 life</b> — the caster gains 2 life (CR 119.3). This
///   rider runs regardless of whether the exile target was still legal at
///   resolution: the printed text is a separate sentence with no link to
///   the exile clause, so the caster gains life even if the creature /
///   planeswalker left the battlefield before resolution (CR 608.2c — the
///   spell does as much as it can; the un-targeted life-gain clause is
///   unaffected by the illegal target).
/// </summary>
[CardName("Vraska's Contempt")]
public static class VraskasContemptFactory
{
    public const string CardName = "Vraska's Contempt";

    /// <summary>JSON slug for the embedded base-shape definition.</summary>
    public const string Slug = "vraskas-contempt";

    /// <summary>Fixed life gained on resolution (CR 119.3).</summary>
    public const int LifeGain = 2;

    /// <summary>
    /// Materialise the Instant card shape (name / Instant / {2}{B}{B}) from
    /// the embedded JSON definition. Resolve behaviour (exile target
    /// creature or planeswalker + gain 2 life) is built on demand via
    /// <see cref="BuildDefinition"/>, mirroring <see cref="DreadboreFactory"/>.
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
    /// Build the "exile target creature or planeswalker; you gain 2 life"
    /// <see cref="SpellDefinition"/>. On resolve:
    /// <list type="number">
    ///   <item>Validate the target is still a Creature or Planeswalker on the
    ///   Battlefield (CR 608.2b — illegal target → no exile); when valid,
    ///   exile it via <see cref="OracleSpellBinder.MoveToExile"/>
    ///   (CR 701.21).</item>
    ///   <item><paramref name="caster"/> gains 2 life (CR 119.3). This clause
    ///   is untargeted and resolves even if the exile target became illegal
    ///   (CR 608.2c).</item>
    /// </list>
    /// </summary>
    /// <param name="targetResolver">Maps the agent-supplied raw target token
    /// to the live engine object. Pass <c>o =&gt; o</c> for tests that hand
    /// permanents directly.</param>
    /// <param name="caster">The spell's controller — the player who gains
    /// 2 life on resolution.</param>
    public static SpellDefinition BuildDefinition(
        Func<object, object> targetResolver,
        Player caster)
    {
        ArgumentNullException.ThrowIfNull(targetResolver);
        ArgumentNullException.ThrowIfNull(caster);

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
                    // Agent-prompt MVP: live gather every creature /
                    // planeswalker on any battlefield. Removal intent in
                    // the bot's ranker pushes opponent permanents up.
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
                return new IEffect[]
                {
                    new Effect(
                        $"{CardName}: exile target creature or planeswalker, gain {LifeGain} life",
                        () =>
                        {
                            // CR 608.2b — resolution-time legality re-check of
                            // the exile target. Illegal target → skip the
                            // exile only; the life-gain clause below still
                            // resolves (CR 608.2c — do as much as possible).
                            if (resolved is Permanent target
                                && target.Zone == ZoneType.Battlefield
                                && (target.HasType(CardType.Creature)
                                    || target.HasType(CardType.Planeswalker)))
                            {
                                // CR 701.21 — Exile. Not a destroy, so
                                // indestructible (CR 702.12) / regeneration
                                // (CR 701.15) don't protect the target.
                                OracleSpellBinder.MoveToExile(target);
                            }

                            // CR 119.3 — the caster gains 2 life. Untargeted
                            // clause; resolves independent of the exile.
                            caster.GainLife(LifeGain);
                        }),
                };
            });
    }
}
