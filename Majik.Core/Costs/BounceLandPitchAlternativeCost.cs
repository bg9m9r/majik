using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.Costs;

/// <summary>
/// CR 118.9 — "bounce-a-land" alternative cost. The Daze pattern:
///
///   "You may return an Island you control to its owner's hand rather than
///    pay this spell's mana cost."
///
/// Differences vs. <see cref="PitchAlternativeCost"/>:
///   * The paid resource is a permanent on the battlefield, not a card in
///     hand — payment is a battlefield → owner's-hand zone move (no exile).
///   * The required predicate is a Land-subtype (Island for Daze), not a
///     mana color. Generalized to a <see cref="CardSubtype"/> so future
///     prints with the same shape (e.g. Foil's "return a Plains/Island")
///     can re-use it.
///   * Carries its own timing predicate via
///     <see cref="IsLegalInContext(Player)"/>; <see cref="SpellCastFlow"/>
///     calls this hook generically for both pitch shapes (mirrors
///     <see cref="PitchAlternativeCost.IsLegalInContext(Player)"/>). Daze
///     prints no timing restriction — this method returns <c>true</c>
///     unconditionally.
///   * No mana is paid — <see cref="AlternativeManaCost"/> is
///     <see cref="ManaCost.Zero"/>; the bounce is the entire cost.
/// </summary>
public sealed class BounceLandPitchAlternativeCost : IAlternativeCost
{
    /// <summary>The required land subtype the bounced permanent must carry
    /// (e.g. <see cref="CardSubtype.Island"/> for Daze).</summary>
    public CardSubtype RequiredSubtype { get; }

    /// <summary>The permanent the caster chose to bounce.</summary>
    public ICard BouncedPermanent { get; }

    public string Description =>
        $"Pitch — Return a {RequiredSubtype} you control to its owner's hand";

    /// <summary>No mana is paid. CR 118.9.</summary>
    public ManaCost AlternativeManaCost => ManaCost.Zero;

    public BounceLandPitchAlternativeCost(CardSubtype requiredSubtype, ICard bouncedPermanent)
    {
        RequiredSubtype = requiredSubtype;
        BouncedPermanent = bouncedPermanent ?? throw new ArgumentNullException(nameof(bouncedPermanent));
    }

    /// <summary>
    /// CR 118.9 legality. The bounced permanent must be on the battlefield,
    /// controlled by the caster, must carry the required subtype, and must
    /// not be the spell being cast (Daze isn't a permanent so this is
    /// belt-and-suspenders against future shapes).
    /// </summary>
    public bool CanCastFor(ICard card, Player caster)
    {
        if (BouncedPermanent.Zone != ZoneType.Battlefield) return false;
        if (!ReferenceEquals(BouncedPermanent.Controller, caster)) return false;
        if (ReferenceEquals(BouncedPermanent, card)) return false;
        if (!BouncedPermanent.HasSubtype(RequiredSubtype)) return false;
        return true;
    }

    /// <summary>
    /// Daze prints no timing restriction on its pitch cost (unlike the
    /// Force-of-Will cycle's "if it's not your turn" gate). Returns
    /// <c>true</c> unconditionally; <see cref="SpellCastFlow"/> calls this
    /// hook for any bounce-pitch cost.
    /// </summary>
    public bool IsLegalInContext(Player activePlayer) => true;

    /// <summary>
    /// Apply the bounce payment after the spell resolves: move the chosen
    /// permanent from the battlefield to its owner's hand (CR 701.10).
    /// Idempotent — safe if the permanent has already moved elsewhere.
    /// </summary>
    public void OnResolved(ICard card, Player caster)
    {
        if (BouncedPermanent.Zone != ZoneType.Battlefield) return;
        var owner = BouncedPermanent.Owner ?? caster;
        owner.Zones.Battlefield.RemoveCard(BouncedPermanent);
        owner.Zones.Hand.AddCard(BouncedPermanent);
        BouncedPermanent.SetZone(ZoneType.Hand);
    }
}
