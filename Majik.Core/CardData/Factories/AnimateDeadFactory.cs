using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Animate Dead (Limited Edition Alpha, {1}{B}).
///
/// Enchantment — Aura. Oracle text (current Comp Rules wording):
///   "Enchant creature card in a graveyard.
///    When Animate Dead enters, if it's on the battlefield, it loses
///     'enchant creature card in a graveyard' and gains 'enchant creature
///     put onto the battlefield with Animate Dead'. Return enchanted
///     creature card to the battlefield under your control and attach
///     Animate Dead to it.
///    When Animate Dead leaves the battlefield, that creature's
///     controller sacrifices it.
///    Enchanted creature gets -1/-0."
///
/// ## Implementation (v1 — simplified per task brief)
///
/// The printed ETB mode-shift ("loses 'enchant creature card in a
/// graveyard' and gains 'enchant creature put onto the battlefield with
/// Animate Dead'") is a CR 303.4f-flavored shape-shift on the aura's
/// Enchant ability. v1 collapses this into a single resolve effect:
/// "target creature card in a graveyard → battlefield under caster's
/// control + auto-attach Animate Dead to it." The runtime never observes
/// the aura with the original "enchant graveyard card" predicate active
/// on the battlefield, so the mode-shift becomes implicit.
///
/// - <b>Card shape</b>: <see cref="Enchantment"/> with <see cref="CardSubtype.Aura"/>,
///   mana cost {1}{B}.
/// - <b>Cast-time targeting</b> (<see cref="BuildSpellDefinition"/>):
///   single target — a creature card in any of the supplied graveyards.
///   Effect on resolve:
///     1. Move the chosen creature card from its graveyard to the
///        caster's battlefield. Routes through <see cref="ZoneService.MoveCard"/>
///        when supplied so ETB triggers fire (CR 603.6a).
///     2. <see cref="Permanent.AttachTo"/> the aura to the just-reanimated
///        creature. The attach is set BEFORE the aura itself enters the
///        battlefield (CR 303.4f) so any aura-attached layer effects scope
///        correctly when <see cref="Services.StackResolver"/> publishes
///        the aura's <see cref="CardMovedEvent"/>.
/// - <b>LTB trigger</b>: when Animate Dead leaves the battlefield, the
///   attached creature's controller sacrifices it (CR 701.16). v1
///   sacrifice = move to owner's graveyard.
/// - <b>Static "-1/-0"</b>: <see cref="AttachedBoostEffect"/> at Layer 7c
///   (CR 613 Layer 7c). Reads <see cref="Permanent.AttachedTo"/>
///   dynamically — same shape as <see cref="ColossusHammerFactory"/>'s
///   "+10/+0".
///
/// ## Deferred (v1 gaps)
///
/// - <b>Real "Enchant creature card in a graveyard" cast-target API</b>:
///   the engine's Aura target plumbing is <see cref="Permanent"/>-typed
///   (CR 303.4a), so the graveyard target is surfaced via a bespoke
///   <see cref="TargetRequest"/> populated with <see cref="Creature"/>
///   <i>cards</i>. The chosen card is reanimated INSIDE the EffectFactory
///   before the auto-attach so the aura's
///   <see cref="Permanent.AttachedTo"/> slot points at a battlefield
///   permanent at the time the aura itself enters.
/// - <b>Mode-shift on ETB</b> (gain "enchant creature put onto the
///   battlefield with Animate Dead"): see class header — collapsed into
///   the single resolve effect. The engine never observes the aura
///   attempting to legally enchant a graveyard card while on the
///   battlefield, so the legality CR 704.5n SBA on aura attachment is a
///   no-op against the reanimated bearer.
/// - <b>Sorcery-speed cast restriction</b>: not enforced — same gap as
///   every other Aura factory in this repo.
/// </summary>
public static class AnimateDeadFactory
{
    public const string CardName = "Animate Dead";
    public const string PrintedManaCost = "{1}{B}";

    /// <summary>Printed oracle text — informational. NOT consumed by
    /// <see cref="AuraEnchantClauseParser"/> because "Enchant creature
    /// card in a graveyard" is a bespoke clause (the parser only handles
    /// the simple single-noun forms; see its xmldoc).</summary>
    public const string OracleText =
        "Enchant creature card in a graveyard.\n" +
        "When Animate Dead enters, if it's on the battlefield, it loses " +
        "\"enchant creature card in a graveyard\" and gains \"enchant " +
        "creature put onto the battlefield with Animate Dead\". Return " +
        "enchanted creature card to the battlefield under your control " +
        "and attach Animate Dead to it.\n" +
        "When Animate Dead leaves the battlefield, that creature's " +
        "controller sacrifices it.\n" +
        "Enchanted creature gets -1/-0.";

    /// <summary>
    /// Creates an Animate Dead with correct card identity only (no live
    /// continuous-effects / trigger wiring). Suitable for factory-shape
    /// / naming tests and the dispatcher path.
    /// </summary>
    public static Enchantment Create(Player owner)
        => Create(owner, continuousEffects: null, zoneService: null, eventBus: null, triggers: null);

    /// <summary>
    /// Creates a fully-wired Animate Dead. When
    /// <paramref name="continuousEffects"/> is supplied, the static
    /// -1/-0 boost (Layer 7c) is registered. When
    /// <paramref name="eventBus"/> and <paramref name="triggers"/> are
    /// supplied, the LTB sacrifice trigger is registered so it fires on
    /// <see cref="CardMovedEvent"/> with <see cref="ZoneType.Battlefield"/>
    /// in the <c>FromZone</c> slot.
    /// </summary>
    public static Enchantment Create(
        Player owner,
        ContinuousEffectsService? continuousEffects,
        ZoneService? zoneService,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Enchantment(
            CardName,
            PrintedManaCost,
            supertypes: null,
            subtypes: new[] { CardSubtype.Aura });
        card.SetOwner(owner);
        card.SetController(owner);

        // -----------------------------------------------------------------
        // Static "-1/-0" — Layer 7c P/T modification (CR 613 Layer 7c).
        // The effect is gated on Animate Dead being on the battlefield
        // AND having a non-null AttachedTo (AttachedBoostEffect.IsActive).
        // -----------------------------------------------------------------
        if (continuousEffects != null)
        {
            continuousEffects.Register(
                new AttachedBoostEffect(card, power: -1, toughness: 0));
        }

        // -----------------------------------------------------------------
        // LTB triggered ability — CR 603.6c / CR 603.10c.
        //   "When Animate Dead leaves the battlefield, that creature's
        //    controller sacrifices it."
        // Match any FromZone == Battlefield movement, regardless of
        // destination (CR 603.10c). v1 sacrifice = move attached creature
        // to its owner's graveyard via ZoneService when supplied.
        // -----------------------------------------------------------------
        var ltbCondition = new EventTriggerCondition<CardMovedEvent>(
            (e, _) => ReferenceEquals(e.Card, card)
                      && e.FromZone == ZoneType.Battlefield);

        var ltbEffect = new Effect(
            $"{CardName} — sacrifice attached creature on LTB (CR 701.16)",
            () => SacrificeAttached(card, zoneService));

        var ltbAbility = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: ltbCondition,
            effects: new IEffect[] { ltbEffect },
            // CR 603.6d — LTB triggers see the permanent as it last
            // existed on the battlefield. Same shape as Spell Queller's
            // LTB trigger (Spell Queller factory).
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(ltbAbility);
        triggers?.RegisterTriggeredAbility(ltbAbility);

        return card;
    }

    /// <summary>
    /// Build the cast-time <see cref="SpellDefinition"/> for Animate Dead.
    /// Bespoke target: a creature CARD in any of the supplied graveyards
    /// (the engine's Aura target plumbing only handles battlefield
    /// permanents, so we sidestep <see cref="AuraSpellDefinitionBuilder"/>).
    ///
    /// On resolve:
    ///   1. The chosen creature card is moved from its graveyard to the
    ///      caster's battlefield (CR 603.6a — routes through ZoneService
    ///      when supplied so ETB triggers fire).
    ///   2. The aura is attached to the just-reanimated creature
    ///      (<see cref="Permanent.AttachTo"/>) BEFORE the aura itself
    ///      enters the battlefield (CR 303.4f). This way the
    ///      <see cref="AttachedBoostEffect"/>'s
    ///      <see cref="Permanent.AttachedTo"/> read sees the bearer
    ///      already populated when the aura's
    ///      <see cref="CardMovedEvent"/> fires.
    /// </summary>
    /// <param name="aura">The Animate Dead permanent being cast.</param>
    /// <param name="graveyardSources">Players whose graveyards are
    /// scanned for creature-card candidates. Pass all players for the
    /// "in a graveyard" wording (CR 700.6).</param>
    /// <param name="zoneService">Optional. When supplied the reanimation
    /// move routes through <see cref="ZoneService.MoveCard"/> so ETB
    /// triggers fire on the reanimated creature.</param>
    public static SpellDefinition BuildSpellDefinition(
        Enchantment aura,
        IEnumerable<Player> graveyardSources,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(aura);
        ArgumentNullException.ThrowIfNull(graveyardSources);

        // CR 700.6 — collect creature cards across every player's
        // graveyard so a single TargetRequest sees them all. The agent
        // picks one; if no candidate exists, the spell is illegal at
        // cast (CR 601.2c) — empty LegalCandidates list signals this.
        var candidates = graveyardSources
            .Where(p => p != null)
            .SelectMany(p => p.Zones.Graveyard.GetCards())
            .OfType<Creature>()
            .Cast<object>()
            .ToList();

        var request = new TargetRequest(
            Description: "target creature card in a graveyard",
            MinTargets: 1,
            MaxTargets: 1,
            LegalCandidates: candidates);

        var caster = aura.Controller ?? aura.Owner
            ?? throw new InvalidOperationException(
                $"{CardName}: aura has no controller/owner — cannot resolve");

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[] { request },
            EffectFactory: chosen =>
            {
                if (chosen.Targets.Count == 0 || chosen.Targets[0].Count == 0)
                {
                    // CR 608.2b — no legal target on resolution → spell
                    // does nothing. Return an empty effect list so
                    // resolution doesn't crash; StackResolver will still
                    // move the aura to the battlefield with AttachedTo
                    // null. SBAs (CR 704.5n) then send it to the
                    // graveyard, which is the printed "fizzle"
                    // shape for Animate Dead with no creature target.
                    return Array.Empty<IEffect>();
                }

                if (chosen.Targets[0][0] is not Creature picked)
                {
                    return Array.Empty<IEffect>();
                }

                return new IEffect[]
                {
                    new Effect(
                        $"{CardName} — reanimate {picked.Name} + auto-attach",
                        () => ResolveReanimateAndAttach(aura, picked, caster, zoneService)),
                };
            });
    }

    /// <summary>
    /// Shared resolve helper: move the chosen creature card to the
    /// caster's battlefield, then attach Animate Dead to it. Order is
    /// load-bearing — see <see cref="BuildSpellDefinition"/> xmldoc.
    /// </summary>
    private static void ResolveReanimateAndAttach(
        Enchantment aura,
        Creature picked,
        Player caster,
        ZoneService? zoneService)
    {
        // Determine the graveyard owner so the raw-zone fallback removes
        // from the correct zone instance.
        var graveOwner = picked.Owner ?? caster;

        if (zoneService != null)
        {
            zoneService.MoveCard(picked, ZoneType.Graveyard, ZoneType.Battlefield, caster);
        }
        else
        {
            graveOwner.Zones.Graveyard.RemoveCard(picked);
            caster.Zones.Battlefield.AddCard(picked);
            picked.SetZone(ZoneType.Battlefield);
            picked.SetController(caster);
        }

        // CR 303.4f — Aura enters attached to its target. Attach BEFORE
        // the aura's own zone move (StackResolver) so AttachedBoostEffect
        // and the LTB-sac trigger see a populated AttachedTo slot the
        // moment the aura hits the battlefield.
        aura.AttachTo(picked);
    }

    /// <summary>
    /// LTB sacrifice helper: when Animate Dead leaves the battlefield,
    /// the attached creature's controller sacrifices it (CR 701.16).
    /// v1: send the attached creature to its owner's graveyard via
    /// ZoneService (so dies-triggers / SBAs fire). No-op if no creature
    /// is currently attached or the attached creature already left
    /// (CR 603.10c — LTB sees the aura's last on-battlefield state, but
    /// the attached permanent may have moved before the sac resolves).
    /// </summary>
    private static void SacrificeAttached(Enchantment aura, ZoneService? zoneService)
    {
        var bearer = aura.AttachedTo;
        if (bearer == null) return;
        if (bearer.Zone != ZoneType.Battlefield) return;

        var bearerOwner = bearer.Owner ?? bearer.Controller;
        if (bearerOwner == null) return;

        if (zoneService != null)
        {
            zoneService.MoveCard(bearer, ZoneType.Battlefield, ZoneType.Graveyard, bearerOwner);
        }
        else
        {
            // The bearer's zone-owning Player owns the battlefield slot
            // (CR 110.2) — strip from controller's battlefield, drop into
            // owner's graveyard.
            var controllerZone = (bearer.Controller ?? bearerOwner).Zones.Battlefield;
            controllerZone.RemoveCard(bearer);
            bearerOwner.Zones.Graveyard.AddCard(bearer);
            bearer.SetZone(ZoneType.Graveyard);
        }
    }
}
