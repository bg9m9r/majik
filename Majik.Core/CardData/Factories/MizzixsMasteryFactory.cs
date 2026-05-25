using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Mizzix's Mastery (Commander 2015, {3}{R}{R}).
///
/// Sorcery. Oracle text:
///   "Choose target instant or sorcery card in your graveyard. Copy that
///    card. You may cast the copy. Exile that card.
///    Overload {X}{X}{R}{R}{R} (You may cast this spell for its overload
///    cost. If you do, change its text by replacing all instances of
///    'target' with 'each.')"
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {3}{R}{R}, owner / controller wired.
/// - <b>1..1 TargetRequest for an instant or sorcery in your graveyard</b>
///   declared in <see cref="BuildDefinition"/>. The candidate pool is the
///   controller's graveyard filtered to instant/sorcery types at
///   resolution time.
/// - <b>Copy-and-cast resolution (CR 707.10 / CR 707.10a)</b>: on resolve,
///   the chosen card's <see cref="SpellDefinition"/> is looked up via the
///   caller-supplied <paramref name="spellDefinitionLookup"/> (production
///   wiring routes through
///   <see cref="Majik.Core.CardData.ScryfallCardFactory.LookupSpellDefinition"/>).
///   The looked-up effect list is executed in place — same lossy-v1 shape
///   as <see cref="Majik.Core.Services.SpellCopier"/>: the copy doesn't
///   push a real <see cref="Majik.Core.Stack.IStackObject"/> on the
///   stack, so observers counting stack items won't see it, and "you may
///   choose new targets for the copy" reuses whatever default
///   <see cref="ChosenSpellParams"/> the lookup produces.
/// - <b>"You may cast the copy"</b> — v1 auto-accepts the "may". The copy
///   is always cast when a SpellDefinition for the chosen card resolves.
///   Same posture as Through the Breach / Sneak Attack / Yawgmoth's Will
///   for "you may" defaults.
/// - <b>Exile original (CR 701.10)</b>: after the copy executes, the
///   chosen card moves Graveyard → Exile via raw zone manipulation. Routes
///   through <see cref="Majik.Core.Services.ZoneService.MoveCard"/> when
///   one is supplied so a <see cref="Majik.Core.Events.CardMovedEvent"/>
///   publishes for any downstream "leaves graveyard" triggers
///   (CR 603.6a / CR 701.20).
///
/// ## Deferred (v1 gaps)
/// - <b>Overload {X}{X}{R}{R}{R}</b>: CR 702.96 — Overload is a templated
///   alt-cost that swaps "target" for "each". The engine has an
///   <see cref="Majik.Core.Players.Agents.OverloadAltCostProbe"/> + the
///   alt-cost discovery surface but the per-card "swap target with each"
///   resolve-side switch isn't wired here. Mizzix's Mastery for its
///   printed cost works; the overloaded form is deferred until a generic
///   overload-resolve plumbing lands.
/// - <b>Real spell-copy stack object</b>: inherited from
///   <see cref="Majik.Core.Services.SpellCopier"/>'s v1 stub — the copy
///   isn't a distinct <see cref="Majik.Core.Stack.IStackObject"/>;
///   anything subscribing to <c>StackObjectAddedEvent</c> won't see it.
/// - <b>"Choose new targets"</b>: the copy reuses whatever targets the
///   SpellDefinition's EffectFactory default-picks (typically empty —
///   the engine's resolve-time fallback machinery picks first-legal
///   targets). A real "choose new targets" prompt is deferred.
/// - <b>Empty / no-instant-or-sorcery graveyard</b>: clean no-op
///   (CR 608.2b — illegal target → fizzle). The spell still resolves and
///   no effect happens.
/// - <b>No SpellDefinition lookup supplied</b>: the resolve body exiles
///   the chosen card but skips the copy (shape-only path; tests can
///   assert exile-without-copy behaviour). Production callers always
///   wire the lookup.
/// </summary>
[CardName("Mizzix's Mastery")]
public static class MizzixsMasteryFactory
{
    public const string CardName = "Mizzix's Mastery";
    public const string PrintedManaCost = "{3}{R}{R}";

    /// <summary>
    /// Construct Mizzix's Mastery card shape only. Use
    /// <see cref="BuildDefinition"/> for the resolve-time copy/exile
    /// pipeline.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Sorcery(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the runnable <see cref="SpellDefinition"/> for Mizzix's
    /// Mastery.
    /// <para>
    /// The TargetRequest declares a 1..1 target on instant/sorcery cards
    /// in <paramref name="caster"/>'s graveyard. The
    /// <paramref name="targetResolver"/> resolves the raw target token
    /// chosen by the caster (expected to yield a <see cref="Card"/>) —
    /// same pattern as Brain Freeze / Aether Gust's player resolver.
    /// </para>
    /// <para>
    /// <paramref name="spellDefinitionLookup"/> binds the chosen graveyard
    /// card's oracle to a <see cref="SpellDefinition"/> at resolution
    /// time; production callers wire
    /// <see cref="Majik.Core.CardData.ScryfallCardFactory.LookupSpellDefinition"/>.
    /// When null, the copy half is skipped (the exile half still happens)
    /// — suitable for shape tests.
    /// </para>
    /// <para>
    /// <paramref name="zoneService"/> routes the exile move so a
    /// <see cref="Majik.Core.Events.CardMovedEvent"/> publishes for any
    /// downstream "leaves graveyard" triggers (CR 603.6a / CR 701.20).
    /// When null, raw zone manipulation is used.
    /// </para>
    /// </summary>
    public static SpellDefinition BuildDefinition(
        Player caster,
        Func<object, object> targetResolver,
        Func<ICard, SpellDefinition?>? spellDefinitionLookup = null,
        Majik.Core.Services.ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(targetResolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    Description: "target instant or sorcery card in your graveyard",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: caster.Zones.Graveyard.GetCards()
                        .Where(c => c.HasType(CardType.Instant) || c.HasType(CardType.Sorcery))
                        .Cast<object>().ToList()),
            },
            EffectFactory: p =>
            {
                // Pre-resolve target token at EffectFactory build time
                // (parallels Brain Freeze). The chosen token is the agent's
                // pick — expected to be a Card from the controller's grave.
                object? rawTarget = null;
                if (p.Targets.Count > 0 && p.Targets[0].Count > 0)
                {
                    rawTarget = targetResolver(p.Targets[0][0]);
                }

                return new IEffect[]
                {
                    new Effect(
                        $"{CardName}: copy target instant/sorcery in graveyard, exile original",
                        () => ResolveBody(caster, rawTarget, spellDefinitionLookup, zoneService, targetResolver)),
                };
            });
    }

    /// <summary>
    /// Resolve body. Picks the chosen instant/sorcery card from
    /// <paramref name="caster"/>'s graveyard (deterministic first-card
    /// fallback when no target was supplied — same posture as Eternal
    /// Witness / Wishclaw Talisman), executes the looked-up
    /// <see cref="SpellDefinition"/>'s effects in place (CR 707.10 lossy
    /// v1 copy), then exiles the original.
    /// </summary>
    private static void ResolveBody(
        Player caster,
        object? rawTarget,
        Func<ICard, SpellDefinition?>? spellDefinitionLookup,
        Majik.Core.Services.ZoneService? zoneService,
        Func<object, object> targetResolver)
    {
        // 1) Resolve the target.
        ICard? picked = rawTarget as ICard;

        // 2) Deterministic fallback — first legal instant/sorcery card in
        // controller's graveyard. Single-arg dispatcher path / no-agent
        // posture (mirrors Eternal Witness / Tasigur).
        if (picked == null)
        {
            picked = caster.Zones.Graveyard.GetCards()
                .FirstOrDefault(c =>
                    c.HasType(CardType.Instant) || c.HasType(CardType.Sorcery));
        }

        // No legal target → CR 608.2b illegal-on-resolution: clean no-op.
        if (picked == null) return;

        // 3) Illegal-on-resolution recheck — the card must still be in
        // the caster's graveyard AND be an instant or sorcery.
        if (picked.Zone != ZoneType.Graveyard) return;
        if (!ReferenceEquals(picked.Owner, caster)) return;
        if (!picked.HasType(CardType.Instant) && !picked.HasType(CardType.Sorcery)) return;

        // 4) "Copy that card. You may cast the copy." — execute the
        // bound SpellDefinition's effects in place. v1 lossy semantics
        // (see class xmldoc + SpellCopier). v1 auto-accepts the "may".
        if (spellDefinitionLookup != null)
        {
            var def = spellDefinitionLookup(picked);
            if (def != null)
            {
                // Default ChosenSpellParams: no mode, no X, no targets.
                // The EffectFactory's resolve-time fallback machinery
                // picks first-legal targets (same posture as the
                // dispatcher path's no-agent fallback). Mana payment is
                // explicitly empty — copies don't pay mana (CR 707.10).
                var p = new ChosenSpellParams(
                    ModeIndex: null,
                    X: null,
                    Targets: Array.Empty<IReadOnlyList<object>>(),
                    Mana: ManaPayment.Empty);
                var effects = def.EffectFactory(p);
                foreach (var effect in effects)
                {
                    effect.Execute();
                }
            }
        }

        // 5) "Exile that card." — Graveyard → Exile.
        if (zoneService != null)
        {
            zoneService.MoveCard(picked, ZoneType.Graveyard, ZoneType.Exile, caster);
        }
        else
        {
            caster.Zones.Graveyard.RemoveCard(picked);
            caster.Zones.Exile.AddCard(picked);
            picked.SetZone(ZoneType.Exile);
        }
    }
}
