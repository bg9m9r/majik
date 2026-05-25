using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.Costs;

/// <summary>
/// CR 702.115 — Surge. "You may cast this spell for its surge cost if you
/// or a teammate has cast another spell this turn."
///
/// Two-headed-giant teammates aren't modelled in v1, so the legality gate
/// reduces to "the controller has cast another spell this turn" — the same
/// per-player tally <see cref="TurnState.SpellsCastByPlayer"/> exposes (CR
/// 700.6 per-turn tally).
///
/// Casting via this alternative cost:
///   1. Replaces the spell's printed mana cost with
///      <see cref="AlternativeManaCost"/> at cost-determination time.
///   2. Stamps <see cref="Card.WasCastForSurge"/> on the spell's card during
///      <see cref="OnResolved"/>. <see cref="Majik.Core.Game.SpellCastFlow"/>
///      also mirrors the stamp at announce time so resolve-body branches
///      that fire mid-resolution see the flag without depending on the
///      OnResolved cleanup pass having already run.
///   3. Eligibility is gated on <see cref="IsLegalInContext(Player)"/>
///      reading the live <see cref="TurnState"/> for "did the caster cast
///      another spell this turn?" The cast-flow announce path calls this
///      gate after the standard <see cref="CanCastFor"/> check, matching
///      the <see cref="PitchAlternativeCost"/> shape.
///
/// Mirror shape of <see cref="EvokeAlternativeCost"/> — a stateless alt
/// cost with a runtime predicate readable off shared engine state.
/// </summary>
public sealed class SurgeAlternativeCost : IAlternativeCost
{
    private readonly TurnState _turnState;

    /// <summary>Mana portion of the surge cost (e.g. <c>{R}</c> for
    /// Reckless Bushwhacker — printed mana cost {2}{R}, surge cost {R}).</summary>
    public ManaCost AlternativeManaCost { get; }

    public string Description => $"Surge {AlternativeManaCost}";

    /// <param name="surgeManaCost">The alternative cost printed after the
    /// "Surge" keyword (e.g. <c>ManaCost.Parse("R")</c> for Reckless
    /// Bushwhacker).</param>
    /// <param name="turnState">Live per-turn tally consulted by
    /// <see cref="IsLegalInContext(Player)"/> to read
    /// <see cref="TurnState.SpellsCastByPlayer"/>. Required — surge is a
    /// per-turn-gated alt cost (CR 702.115a).</param>
    public SurgeAlternativeCost(ManaCost surgeManaCost, TurnState turnState)
    {
        AlternativeManaCost = surgeManaCost ?? throw new ArgumentNullException(nameof(surgeManaCost));
        _turnState = turnState ?? throw new ArgumentNullException(nameof(turnState));
    }

    /// <summary>
    /// Surge is announced at the same step as the normal cast (CR 601.2b),
    /// so the spell's card must still be in the caster's hand. The
    /// per-turn "another spell already cast" predicate lives on
    /// <see cref="IsLegalInContext(Player)"/> — the cast-flow announce
    /// path checks both <see cref="CanCastFor"/> AND
    /// <see cref="IsLegalInContext(Player)"/>, mirroring
    /// <see cref="PitchAlternativeCost"/>.
    /// </summary>
    public bool CanCastFor(ICard card, Player caster)
    {
        if (card == null || caster == null) return false;
        // CR 601.2 — surge does not relax the default casting zone (hand).
        if (card.Zone != ZoneType.Hand) return false;
        if (!ReferenceEquals(card.Owner, caster)) return false;
        return true;
    }

    /// <summary>
    /// CR 702.115a — "if you or a teammate has cast another spell this
    /// turn". v1 has no team modelling, so the predicate reduces to "the
    /// caster has cast at least one spell this turn" — i.e.
    /// <see cref="TurnState.SpellsCastByPlayer"/> &gt; 0. Note that the
    /// per-turn spell tally is incremented by the engine on each
    /// <see cref="Majik.Core.Domain.DomainEvents.SpellCastEvent"/>; the
    /// Surge-cast spell itself is announced and gated BEFORE its own
    /// SpellCastEvent fires, so the gate correctly reads the count of
    /// strictly-prior spells.
    /// </summary>
    public bool IsLegalInContext(Player caster)
    {
        if (caster == null) return false;
        return _turnState.SpellsCastByPlayer(caster) > 0;
    }

    /// <summary>
    /// Side-effect on resolution: flip <see cref="Card.WasCastForSurge"/>
    /// so resolve bodies (Reckless Bushwhacker's haste + +1/+0 swarm
    /// rider) can read the surge posture off the card. Idempotent — safe
    /// if the cast-flow announce path already stamped the same flag.
    /// </summary>
    public void OnResolved(ICard card, Player caster)
    {
        if (card is Card concrete)
        {
            concrete.SetWasCastForSurge(true);
        }
    }
}
