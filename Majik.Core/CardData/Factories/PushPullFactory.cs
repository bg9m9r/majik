using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Services;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the COMBINED split card Push // Pull (Strixhaven:
/// School of Mages, {1}{W/B} // {4}{B/R}{B/R}). Both faces are Sorceries.
///
/// ## Card text (Scryfall verified 2026-06-02)
///   Push {1}{W/B} — Sorcery: "Destroy target tapped creature."
///   Pull {4}{B/R}{B/R} — Sorcery: "Put up to two target creature cards from a
///     single graveyard onto the battlefield under your control. They gain
///     haste until end of turn. Sacrifice them at the beginning of the next
///     end step."
///
/// ## Split-card posture (CR 712.2 / 712.4)
///
/// A split card has two faces printed on one card. The caster chooses one face
/// to cast and only that face's mana cost / effect applies (CR 712.4a). Neither
/// face is a permanent — both halves are Sorceries here, so each resolves as a
/// one-shot effect that then heads to the graveyard.
///
/// The combined card name "Push // Pull" is the <c>[CardName]</c> dispatch key
/// (matching the embedded seed row), mirroring the two-face posture of
/// <see cref="WearTearFactory"/> / <see cref="BoomBustFactory"/>. The card
/// SHAPE is materialised from the embedded JSON definition (<c>push-pull.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>; each face's resolve-time
/// behaviour is delegated to the already-implemented single-half factories
/// (<see cref="PushFactory"/> / <see cref="PullFactory"/>), which carry the
/// destroy-tapped-creature / multi-target-reanimate behaviour.
///
/// ## Implemented (v1)
/// - <b>Card shape</b>: Sorcery, combined card name. The combined object
///   carries the front (Push) face's {1}{W/B} cost — the engine's split-cast
///   plumbing selects the per-face cost when each face is cast; the printed
///   front cost is the natural default for the single combined object (same
///   posture as <see cref="WearTearFactory"/> carrying the Wear cost). Colours
///   W/B/R are unioned from a color indicator on the combined JSON so the
///   combined object reports the full split-card colour identity (CR 709.4).
/// - <b>Push face</b> — destroy target tapped creature, delegated to
///   <see cref="PushFactory.BuildDefinition"/> (CR 701.7 Destroy; CR 608.2b
///   illegal-target re-check honoured in that half's resolve body).
/// - <b>Pull face</b> — reanimate up to two creature cards from a single
///   graveyard under the caster's control with haste, sacrificed at the next
///   end step, delegated to <see cref="PullFactory.BuildSpellDefinition"/>
///   (CR 110.2 / CR 702.10 / CR 603.7 / CR 701.16).
///
/// ## Deferred (v1 gaps — shared with Wear // Tear / Boom // Bust)
/// - <b>Split-cast surface</b>: the engine has no general split-cast surface
///   yet, so the combined object exposes the front (Push) cost and each half is
///   castable independently via its own <c>[CardName]</c> factory
///   (<see cref="PushFactory"/> / <see cref="PullFactory"/>). Push // Pull is
///   not a fuse card, so there is no fuse gap here.
/// </summary>
[CardName("Push // Pull")]
public static class PushPullFactory
{
    public const string CardName = "Push // Pull";
    public const string Slug = "push-pull";

    /// <summary>CR 712 — Push (front face) printed cost.</summary>
    public const string PushManaCost = "{1}{W/B}";

    /// <summary>CR 712 — Pull (back face) printed cost.</summary>
    public const string PullManaCost = "{4}{B/R}{B/R}";

    /// <summary>
    /// Build the combined card shape from the embedded JSON definition
    /// (Sorcery, combined name "Push // Pull", colours W/B/R). This is the
    /// overload <see cref="NamedCardFactory"/> dispatches to; per-face resolve
    /// behaviour is built on demand via <see cref="BuildPushDefinition"/> /
    /// <see cref="BuildPullDefinition"/>.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Sorcery)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the resolve-time definition for the Push face: "Destroy target
    /// tapped creature." Delegated to <see cref="PushFactory.BuildDefinition"/>
    /// so the destroy-tapped-creature behaviour (CR 701.7; CR 608.2b legality
    /// re-check) stays single-sourced.
    /// </summary>
    /// <param name="resolver">Resolves a chosen target token to the live game
    /// object.</param>
    public static SpellDefinition BuildPushDefinition(Func<object, object> resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        return PushFactory.BuildDefinition(resolver);
    }

    /// <summary>
    /// Build the resolve-time definition for the Pull face: "Put up to two
    /// target creature cards from a single graveyard onto the battlefield under
    /// your control..." Delegated to
    /// <see cref="PullFactory.BuildSpellDefinition"/> so the multi-target
    /// reanimate / haste / delayed-sacrifice behaviour (CR 110.2 / CR 702.10 /
    /// CR 603.7 / CR 701.16) stays single-sourced.
    /// </summary>
    /// <param name="caster">Spell controller — battlefield destination + delayed
    /// trigger controller.</param>
    /// <param name="resolver">Resolves each chosen target token to the live
    /// game object.</param>
    /// <param name="zoneService">Optional ZoneService for ETB/LTB routing.</param>
    /// <param name="triggers">Optional TriggerManager for the end-step
    /// sacrifice.</param>
    public static SpellDefinition BuildPullDefinition(
        Player caster,
        Func<object, object> resolver,
        ZoneService? zoneService = null,
        TriggerManager? triggers = null)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(resolver);
        return PullFactory.BuildSpellDefinition(caster, resolver, zoneService, triggers);
    }
}
