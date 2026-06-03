using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Master of Waves (Theros, {3}{U}).
///
/// Creature — Merfolk Wizard 2/1. Oracle text (verified against Scryfall
/// 2026-06-02):
///   "Protection from red
///    Elemental creatures you control get +1/+1.
///    When this creature enters, create a number of 1/0 blue Elemental
///    creature tokens equal to your devotion to blue. (Each {U} in the mana
///    costs of permanents you control counts toward your devotion to blue.)"
///
/// The card's base shape (name, Creature, Merfolk + Wizard subtypes, {3}{U},
/// 2/1) is materialised from the embedded JSON definition
/// (<c>master-of-waves.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The three text abilities are
/// layered on here because the JSON <c>AbilityDefinition</c> schema doesn't
/// express a typed-anthem static or a devotion-scaled token-minting ETB.
///
/// ## Implemented (v1)
/// - 2/1 Creature — Merfolk Wizard at printed cost {3}{U}, owner / controller
///   wired.
/// - <b>Protection from red (CR 702.16)</b> — a single
///   <see cref="ProtectionAbility"/> with quality "red". The DEBT/target/
///   damage/attach gates consult
///   <see cref="Majik.Core.Rules.Protection.HasProtectionFromColor"/> (same
///   marker shape as <see cref="PhyrexianCrusaderFactory"/>).
/// - <b>"Elemental creatures you control get +1/+1" (CR 613.7c)</b> — a
///   <see cref="LordStaticEffect"/> gated on
///   <see cref="CardSubtype.Elemental"/> with <c>power: 1, toughness: 1</c>.
///   Unlike Lord of Atlantis (symmetric "Other Merfolk"), this anthem is
///   scoped to the controller ("you control"), so <c>allPlayers: false,
///   opponentsOnly: false</c>; and it has NO "Other" clause, so
///   <c>includeSelf: true</c> (Master of Waves is a Merfolk, not an
///   Elemental, so self-inclusion is moot for it — but the token Elementals
///   it makes DO count, and any future Elemental copy of Master itself would
///   too). Requires a live <see cref="ContinuousEffectsService"/>; without
///   one the anthem is structural only.
/// - <b>ETB devotion-scaled token minting (CR 603.1 / 700.5 / 111)</b> — a
///   <see cref="TriggeredAbility"/> keyed on
///   <see cref="Triggers.OnEnterBattlefieldSelf"/>. On resolution it creates
///   N = the controller's devotion to blue (CR 700.5 — the number of {U} mana
///   symbols among the mana costs of permanents they control, computed via
///   the shared <see cref="NykthosShrineToNyxFactory.ComputeDevotionToColor"/>
///   helper) 1/0 blue Elemental creature tokens. Master of Waves is on the
///   battlefield when its own trigger resolves, so its own {U} counts toward
///   N (CR 603.3 / 700.5). The minted Elementals are immediately picked up by
///   the +1/+1 anthem above (Layer 7c), so each 1/0 token becomes an effective
///   2/1 and survives the 0-toughness SBA — provided the same
///   <see cref="ContinuousEffectsService"/> is wired (see below).
///
/// ## Single-arg dispatcher path
/// The <see cref="Create(Player)"/> overload attaches the ETB trigger and the
/// protection marker structurally (correct card shape for factory-shape /
/// dispatch tests). The trigger is NOT registered with a
/// <see cref="TriggerManager"/>, no <see cref="ContinuousEffectsService"/> is
/// supplied (so the anthem is dormant), and no <see cref="ZoneService"/> is
/// threaded into token ETB. Production callers use the full overload.
///
/// ## Anthem / token interaction (token survival)
/// The printed tokens are 1/0. A 1/0 creature with 0 toughness dies to SBA
/// 704.5f immediately — UNLESS the +1/+1 Elemental anthem is live. For the
/// tokens to survive, the minted Elementals must read the SAME
/// <see cref="ContinuousEffectsService"/> the anthem is registered against:
/// the full overload sets <c>token.ActiveEffects = continuousEffects</c> on
/// each minted token so the +1/+1 applies (effective 2/1). This mirrors how
/// the engine layers anthems over freshly-created tokens. Without a live
/// service (the shape-only overload) the tokens are still minted, but their
/// survival is the caller's responsibility — same posture as every other
/// continuous-effect-dependent factory.
///
/// ## Deferred (v1 gaps)
/// - <b>Hybrid / Phyrexian blue pips</b>: devotion reads the pure-{U} pip
///   field only (no hybrid / Phyrexian buckets yet) — the shared devotion gap
///   documented on
///   <see cref="NykthosShrineToNyxFactory.ComputeDevotionToColor"/>.
/// </summary>
[CardName("Master of Waves")]
public static class MasterOfWavesFactory
{
    public const string CardName = "Master of Waves";
    public const string Slug = "master-of-waves";
    public const int Power = 2;
    public const int Toughness = 1;

    /// <summary>Printed stats of the minted Elemental tokens: 1/0 blue.</summary>
    public const int TokenPower = 1;
    public const int TokenToughness = 0;

    /// <summary>
    /// Construct Master of Waves with no live wiring. The protection marker and
    /// the ETB token-minting trigger attach structurally; the trigger is NOT
    /// registered with a <see cref="TriggerManager"/>, and no
    /// <see cref="ContinuousEffectsService"/> is supplied, so the Elemental
    /// anthem is dormant and the ETB token mint no-ops at resolution (no live
    /// services). This is the overload <see cref="NamedCardFactory"/> dispatches
    /// to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, continuousEffects: null, triggers: null, zoneService: null);

    /// <summary>
    /// Construct a fully-wired Master of Waves.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="continuousEffects">Layers service the Elemental +1/+1
    /// anthem is registered against, and which the minted tokens read so the
    /// anthem applies to them (1/0 → 2/1, surviving the 0-toughness SBA). May be
    /// null — no live anthem and the tokens stay a printed 1/0.</param>
    /// <param name="triggers">Trigger manager for ETB registration. May be null
    /// — the trigger attaches structurally but isn't enrolled.</param>
    /// <param name="zoneService">Threaded into token ETB so a
    /// <see cref="Majik.Core.Events.CardMovedEvent"/> fires when each token
    /// enters (ETB observers see the tokens). May be null.</param>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? continuousEffects,
        TriggerManager? triggers,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature, Merfolk
        // + Wizard subtypes, {3}{U}, 2/1). The JSON carries no abilities — the
        // three text abilities are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 702.16 — Protection from red. Marker; Rules.Protection reads the
        // "red" quality for combat / damage / target / attach gates.
        card.AddAbility(new ProtectionAbility("red"));

        // CR 613.7c — "Elemental creatures you control get +1/+1." Scoped to the
        // controller ("you control" ⇒ allPlayers: false, opponentsOnly: false).
        // No "Other" clause ⇒ includeSelf: true (Master of Waves is a Merfolk,
        // not an Elemental, so it never self-buffs in practice; the inclusion
        // matters only if Master itself were ever an Elemental — and crucially
        // its own minted Elemental tokens DO qualify).
        if (continuousEffects != null)
        {
            continuousEffects.Register(new LordStaticEffect(
                source: card,
                matchingSubtype: CardSubtype.Elemental,
                power: 1,
                toughness: 1,
                grantedKeywords: null,
                includeSelf: true,
                opponentsOnly: false,
                allPlayers: false));
        }

        // ----------------------------------------------------------------
        // ETB devotion-scaled token mint — CR 603.1 (ETB trigger) /
        // CR 700.5 (devotion to blue) / CR 111 (token creation) /
        // CR 105 / 111.4 (token colour).
        //   "When this creature enters, create a number of 1/0 blue Elemental
        //    creature tokens equal to your devotion to blue."
        // N is read on resolution off the controller's live devotion to blue.
        // Master of Waves is on the battlefield by then, so its own {U} counts
        // (CR 700.5). Each minted token reads the same continuous-effects
        // service so the +1/+1 Elemental anthem applies (1/0 → 2/1) and the
        // token survives the 0-toughness SBA (CR 704.5f).
        // ----------------------------------------------------------------
        var tokenSpec = new TokenFactory.TokenSpec(
            Name: "Elemental",
            Power: TokenPower,
            Toughness: TokenToughness,
            Subtypes: new[] { CardSubtype.Elemental },
            Keywords: null,
            // CR 105 / 111.4 — printed "1/0 blue Elemental creature token".
            Colors: new[] { ManaColor.Blue });

        var etbEffect = new Effect(
            $"{CardName}: create devotion-to-blue many 1/0 blue Elemental tokens",
            () =>
            {
                var controller = card.Controller ?? owner;
                var n = NykthosShrineToNyxFactory.ComputeDevotionToColor(
                    controller, ManaColor.Blue);
                if (n <= 0) return; // CR 122.1c — "create zero tokens" makes none.

                for (var i = 0; i < n; i++)
                {
                    var token = TokenFactory.CreateOnBattlefield(
                        tokenSpec, controller, zoneService);
                    // Wire the anthem service into the token so the +1/+1
                    // Elemental anthem (Layer 7c) applies — 1/0 → 2/1, so the
                    // token survives SBA 704.5f.
                    if (continuousEffects != null) token.ActiveEffects = continuousEffects;
                }
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }
}
