using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for March of Otherworldly Light (Kamigawa: Neon
/// Dynasty, {X}{W}).
///
/// Instant. Oracle text:
///   "As an additional cost to cast this spell, you may exile any number
///    of white cards from your hand. This spell costs {2} less to cast
///    for each card exiled this way.
///    Exile target artifact, creature, or enchantment with mana value X
///    or less."
///
/// ## Implemented (v1)
///
/// - <b>Instant</b> at <c>{X}{W}</c>, owner/controller wired.
/// - <b>March additional cost (CR 601.2f + CR 117.7c)</b> — reuses the
///   existing <see cref="MarchAdditionalCost"/> primitive with
///   <see cref="ManaColor.White"/>. The cost is OPTIONAL (the caster may
///   exile zero white cards). For each white hand card exiled, the cast's
///   generic cost is reduced by {2}, floored at zero. The reduction is
///   applied after X is folded into Generic per
///   <see cref="SpellCastFlow.ComputeAndApplyTotalCost"/>, so an {X=4}{W}
///   cast with 2 white cards exiled reduces 4 → 0 generic (the {W} pip
///   is preserved).
/// - <b>Exile target artifact/creature/enchantment with MV ≤ X</b> —
///   built via <see cref="BuildSpellDefinition"/>. Lands are NOT legal
///   targets (CR 301, 302, 303 — only artifact, creature, and enchantment
///   types are named). Planeswalkers are NOT legal targets (only artifact,
///   creature, and enchantment types named). Target legality is validated
///   at both target selection (CandidateGatherer) and resolution (CR
///   608.2b re-check). The MV check uses
///   <see cref="ManaCost.Parse(string).TotalValue"/> on the card's printed
///   mana cost string (CR 202.3 — mana value of a permanent on the stack
///   or battlefield is its printed cost's total value; X counts as 0 per
///   CR 202.3f, which is handled by Parse leaving HasX true but TotalValue
///   summing only the numeric pips).
/// - <b>Exile</b> — routes through the raw zone move pattern matching
///   PathToExileFactory and WorldBreakerFactory: remove from battlefield,
///   add to exile zone, SetZone.
///
/// ## Design references
///
/// - March additional cost + colour swap: <see cref="MarchOfWretchedSorrowFactory"/>
///   (black sibling — swap MarchExileColor + resolve body).
/// - Exile-target permanent from battlefield: <see cref="PathToExileFactory"/>
///   + <see cref="WorldBreakerFactory.ExileFromBattlefield"/>.
/// - X-spell shape: <see cref="BonfireOfTheDamnedFactory"/> /
///   <see cref="ChordOfCallingFactory"/> for HasVariableX idiom.
/// - Artifact/creature/enchantment type filter: <see cref="AuraEnchantClauseParser"/>
///   and HeuristicBotAgent permanent-type checks.
///
/// ## Sibling cards (cycle)
///
/// All five "March of …" cards from Kamigawa: Neon Dynasty reuse
/// <see cref="MarchAdditionalCost"/> with a different colour:
///   * <i>March of Wretched Sorrow</i> — {X}{B} — black exile; damage + life.
///   * <i>March of Burgeoning Life</i> — {X}{G} — green exile; creature tutor.
///   * <i>March of Reckless Joy</i> — {X}{R} — red exile; top-X impulse.
///   * <i>March of Swirling Mist</i> — {X}{U} — blue exile; phase out.
/// </summary>
[CardName("March of Otherworldly Light")]
public static class MarchOfOtherworldlyLightFactory
{
    public const string CardName = "March of Otherworldly Light";
    public const string PrintedManaCost = "{X}{W}";

    /// <summary>The colour of the cards eligible for the March exile —
    /// white for this card. Surfaced for the bot's
    /// <see cref="MarchAdditionalCost.AvailableHandCards"/> probe.</summary>
    public const ManaColor MarchExileColor = ManaColor.White;

    /// <summary>Construct the runtime card shape.</summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Instant(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the reusable <see cref="MarchAdditionalCost"/> for this
    /// spell with the caller-selected hand cards. Pass an empty list when
    /// the caster declines the optional cost (the spell still casts at
    /// full {X}{W}). Mirrors
    /// <see cref="MarchOfWretchedSorrowFactory.BuildAdditionalCost"/>.
    /// </summary>
    public static MarchAdditionalCost BuildAdditionalCost(
        ICard source, IReadOnlyList<ICard> exiledHandCards)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(exiledHandCards);
        return new MarchAdditionalCost(source, MarchExileColor, exiledHandCards);
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when March of
    /// Otherworldly Light is cast. <see cref="SpellDefinition.HasVariableX"/>
    /// is true so the cast flow prompts for X at cast time. Resolution
    /// exiles the chosen artifact, creature, or enchantment if its mana
    /// value is ≤ X (CR 608.2b re-checks legality at resolution).
    ///
    /// Target legality (CandidateGatherer):
    ///   - Must be on the battlefield.
    ///   - Must be artifact, creature, OR enchantment.
    ///   - Lands and planeswalkers are NOT legal targets.
    ///   - MV check is NOT enforced in the gatherer (X is only known
    ///     at cast time), but IS enforced at resolution.
    /// </summary>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target token → live game
    /// object).</param>
    public static SpellDefinition BuildSpellDefinition(
        Func<object, object> resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: true,
            TargetRequests: new[]
            {
                new TargetRequest(
                    Description: "target artifact, creature, or enchantment",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    // CandidateGatherer — all artifacts, creatures, and
                    // enchantments on the battlefield. Lands and
                    // planeswalkers are NOT in this list (CR oracle text
                    // names only the three types). The MV ≤ X constraint
                    // is validated at resolution (X is locked in at cast).
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .Where(c => c.HasType(CardType.Artifact)
                            || c.HasType(CardType.Creature)
                            || c.HasType(CardType.Enchantment))
                        .Cast<object>()
                        .ToList()),
            },
            EffectFactory: chosen =>
            {
                var x = chosen.X ?? 0;
                var rawTarget = chosen.Targets.Count > 0 && chosen.Targets[0].Count > 0
                    ? resolver(chosen.Targets[0][0])
                    : null;

                return new IEffect[]
                {
                    Fx.Inline(
                        $"{CardName}: exile target artifact/creature/enchantment with MV ≤ {x}.",
                        () =>
                        {
                            if (rawTarget is not ICard target) return;

                            // CR 608.2b — re-check at resolution:
                            // target must still be on the battlefield,
                            // still be an artifact/creature/enchantment,
                            // and must still have MV ≤ X.
                            if (target.Zone != ZoneType.Battlefield) return;

                            var isLegalType = target.HasType(CardType.Artifact)
                                || target.HasType(CardType.Creature)
                                || target.HasType(CardType.Enchantment);
                            if (!isLegalType) return;

                            // CR 202.3 — mana value uses the printed cost.
                            // For permanents on the battlefield the MV is
                            // based on the printed mana cost (not modified
                            // by alternative costs). X in a printed cost
                            // counts as 0 per CR 202.3f (TotalValue does
                            // not sum HasX).
                            var manaCostStr = target.ManaCost ?? string.Empty;
                            var mv = ManaCost.Parse(manaCostStr).TotalValue;
                            if (mv > x) return;

                            // CR 701.21 — exile from battlefield.
                            var holder = target.Controller ?? target.Owner;
                            holder?.Zones.Battlefield.RemoveCard(target);
                            var exileOwner = target.Owner ?? holder;
                            exileOwner?.Zones.Exile.AddCard(target);
                            target.SetZone(ZoneType.Exile);
                        }),
                };
            });
    }
}
