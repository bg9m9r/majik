using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Whirler Virtuoso (Kaladesh, {2}{U}{R}).
///
/// Creature — Human Artificer 2/3. Oracle text:
///   "When Whirler Virtuoso enters, you get {E}{E}{E} (three energy
///    counters). Pay {E}{E}: Create a 1/1 colorless Thopter artifact
///    creature token with flying."
///
/// Modern Boros/Izzet Energy payoff — 4-mana 2/3 that immediately banks
/// three energy AND prints flyers for two energy a pop. Pairs with
/// Aetherworks Marvel's energy ramp (ETB-on-die feed) and the Harnessed
/// Lightning / Voltage Surge cycle.
///
/// ## Implemented (v1)
///
/// - 2/3 <see cref="Creature"/> — Human Artificer, mana cost {2}{U}{R}.
///   Owner / controller wired.
/// - <b>ETB triggered ability</b> (CR 603.6a + CR 106.13): "When Whirler
///   Virtuoso enters, you get {E}{E}{E}." Wired via
///   <see cref="Triggers.OnEnterBattlefieldSelf"/>; on resolution the
///   controller gains three energy through <see cref="Player.GainEnergy"/>
///   (same player-scoped ledger Guide of Souls / Aether Hub feed). The
///   <c>{E}{E}{E}</c> oracle wording is summed into a single
///   <c>GainEnergy(3)</c> call — energy is an integer resource (CR
///   106.13b), not a per-symbol stamp, so the printed three-pip phrasing
///   collapses cleanly.
/// - <b>{E}{E} activated ability</b> (CR 602.1): "Pay {E}{E}: Create a
///   1/1 colorless Thopter artifact creature token with flying."
///   Wired as an <see cref="ActivatedAbility"/> with a single
///   <see cref="PayEnergyCost"/> for two energy (shared with Guide of
///   Souls). On resolution <see cref="TokenFactory.CreateOnBattlefield"/>
///   builds a 1/1 colourless <see cref="CardSubtype.Thopter"/> creature
///   token with the Flying keyword (CR 702.9), then additively stamps
///   <see cref="CardType.Artifact"/> so the resulting token reports
///   Artifact + Creature — Thopter (same multi-type pattern as
///   <see cref="AnimationModuleFactory"/>'s Servo and Ornithopter's
///   artifact-creature shell).
/// - <b>Single-arg dispatcher path</b> — no <see cref="TriggerManager"/>
///   registration; the ETB trigger is attached structurally so shape
///   tests see it. The activated ability's token-creation path uses
///   the no-<see cref="ZoneService"/> branch of
///   <see cref="TokenFactory.CreateOnBattlefield"/>, mirroring
///   <see cref="AnimationModuleFactory.Create(Player)"/>'s posture.
///
/// ## Deferred (v1 gaps)
///
/// - <b>Live TriggerManager / ZoneService wiring</b>: no
///   <c>Create(owner, eventBus, triggers, zones)</c> overload yet. When
///   the dispatcher path is the production single-card construction
///   site this falls into the same posture as
///   <see cref="GuideOfSoulsFactory.Create(Player)"/> — bus-driven ETB
///   firing follows the existing pattern when the call sites land.
/// - <b>Thopter ETB CardMovedEvent</b>: without a <see cref="ZoneService"/>
///   threaded in, the Thopter token enters via the raw zone branch and
///   does NOT publish <see cref="CardMovedEvent"/>. Soul-Warden-style
///   downstream triggers won't fire. Same gap as Animation Module's
///   Servo without zones.
/// </summary>
[CardName("Whirler Virtuoso")]
public static class WhirlerVirtuosoFactory
{
    public const string CardName = "Whirler Virtuoso";
    public const string PrintedManaCost = "{2}{U}{R}";
    public const int Power = 2;
    public const int Toughness = 3;
    public const int EtbEnergyGain = 3;
    public const int ThopterEnergyCost = 2;
    public const int ThopterPower = 1;
    public const int ThopterToughness = 1;
    public const string ThopterTokenName = "Thopter";

    /// <summary>
    /// Construct Whirler Virtuoso — a 2/3 Human Artificer with an ETB
    /// energy trigger and a <c>Pay {E}{E}: Create a 1/1 flying Thopter
    /// token</c> activated ability. Single-arg dispatcher path; the ETB
    /// trigger is attached structurally for shape inspection (no
    /// <see cref="TriggerManager"/> wiring).
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Artificer });

        // Whirler Virtuoso is a plain "Creature — Human Artificer" (NOT an
        // Artifact Creature) — only the Thopter tokens it mints are artifacts.
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a + CR 106.13.
        //   "When Whirler Virtuoso enters, you get {E}{E}{E}."
        //
        // Three energy pips → single GainEnergy(3) call (CR 106.13b —
        // energy is an integer resource; printed pip-phrasing collapses).
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: controller gains {{E}}{{E}}{{E}}",
            () =>
            {
                var controller = card.Controller ?? owner;
                controller.GainEnergy(EtbEnergyGain);
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);

        // ----------------------------------------------------------------
        // Activated ability — CR 602.1.
        //   "Pay {E}{E}: Create a 1/1 colorless Thopter artifact
        //    creature token with flying."
        //
        // Cost: PayEnergyCost(2). Resolve body mints a colourless
        // Thopter token with Flying through TokenFactory.CreateOnBattlefield,
        // then additively stamps CardType.Artifact (Token shell is
        // Creature-only; same multi-type stamp used by Animation Module's
        // Servo).
        // ----------------------------------------------------------------
        var thopterEffect = new Effect(
            $"{CardName}: create 1/1 colourless Thopter token (flying)",
            () =>
            {
                var controller = card.Controller ?? owner;
                if (card.Zone != ZoneType.Battlefield) return; // CR 603.6c

                var spec = new TokenFactory.TokenSpec(
                    Name: ThopterTokenName,
                    Power: ThopterPower,
                    Toughness: ThopterToughness,
                    Subtypes: new[] { CardSubtype.Thopter },
                    Keywords: new[] { "Flying" },
                    Colors: Array.Empty<ManaColor>());

                var token = TokenFactory.CreateOnBattlefield(spec, controller);

                // CR 111.1 — Thopter tokens are artifact creatures. The
                // TokenFactory shell only stamps Creature; layer Artifact
                // on additively (mirrors Animation Module's Servo).
                token.AddCardType(CardType.Artifact);
            });

        var thopterAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new PayEnergyCost(ThopterEnergyCost) },
            effects: new IEffect[] { thopterEffect });

        card.AddAbility(thopterAbility);

        return card;
    }
}
