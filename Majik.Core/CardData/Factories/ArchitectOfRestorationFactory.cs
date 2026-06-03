using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Architect of Restoration — the back face of the
/// transforming Saga The Restoration of Eiganjo // Architect of Restoration
/// (Kamigawa: Neon Dynasty).
///
/// Enchantment Creature — Fox Monk 3/4. Oracle text:
///   "Vigilance
///    Whenever this creature attacks or blocks, create a 1/1 colorless Spirit
///    creature token."
///
/// ## Implemented
/// - 3/4 <see cref="Creature"/> — Fox Monk, white (the back face has no printed
///   mana cost, so the colour is stamped explicitly — CR 202.2c colour
///   indicator — same posture as <see cref="AvatarRokuFactory"/>).
/// - <see cref="MdfcState"/> attached (front = "The Restoration of Eiganjo",
///   back = "Architect of Restoration") pre-flipped to the back face — this
///   face only ever exists as the transformed (back) face on the battlefield
///   (CR 712.4). <see cref="TheRestorationOfEiganjoFactory"/>'s chapter III
///   builds this permanent when the Saga transforms.
/// - <b>Vigilance</b> — a <see cref="KeywordAbility"/> (CR 702.21).
/// - <b>Attacks-or-blocks trigger</b> (CR 508.1f / 509.1c): "Whenever this
///   creature attacks or blocks, create a 1/1 colorless Spirit creature token."
///   Modelled as two triggered abilities — one keyed on
///   <see cref="Triggers.OnAttackSelf"/>, one on <see cref="Triggers.OnBlockSelf"/>
///   — both running the same Spirit-token mint via
///   <see cref="TokenFactory"/>. Both are registered with the supplied
///   <see cref="TriggerManager"/> when present (no-op wiring otherwise — the
///   abilities are attached for shape).
/// </summary>
[CardName("Architect of Restoration")]
public static class ArchitectOfRestorationFactory
{
    public const string FrontName = "The Restoration of Eiganjo";
    public const string CardName = "Architect of Restoration";
    public const int Power = 3;
    public const int Toughness = 4;

    /// <summary>Construct Architect of Restoration with no live runtime wiring.
    /// The Vigilance keyword and the attacks-or-blocks trigger are attached
    /// structurally for shape tests; without a trigger manager the trigger is
    /// not registered.</summary>
    public static Creature Create(Player owner)
        => Create(owner, zoneService: null, triggers: null);

    /// <summary>Construct Architect of Restoration with optional runtime
    /// services.</summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="zoneService">Optional zone service — routes the Spirit
    /// token ETB through <see cref="ZoneService"/> so <see cref="CardMovedEvent"/>
    /// publishes.</param>
    /// <param name="triggers">Optional trigger manager — registers the
    /// attacks-or-blocks Spirit-token triggers.</param>
    public static Creature Create(
        Player owner,
        ZoneService? zoneService,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // CR 712.4 — Architect of Restoration is a 3/4 Enchantment Creature —
        // Fox Monk. Built as a Creature (carries P/T) with the Enchantment card
        // type added (CR 205.2a).
        var card = new Creature(
            name: CardName,
            manaCost: "",
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Fox, CardSubtype.Monk });

        card.SetOwner(owner);
        card.SetController(owner);
        card.AddCardType(CardType.Enchantment);

        // CR 202.2c — the back face is white; the printed mana cost is empty so
        // stamp the colour explicitly (same posture as Avatar Roku's red stamp).
        card.SetTokenColors(new[] { ManaColor.White });

        // CR 712 — this face only exists as the transformed back face.
        card.MdfcState = new MdfcState(FrontName, CardName);
        if (!card.MdfcState.IsBackFace) card.MdfcState.Transform();

        // CR 702.21 — Vigilance.
        card.AddAbility(new KeywordAbility("Vigilance", card, owner));

        // CR 508.1f / 509.1c — "Whenever this creature attacks or blocks,
        // create a 1/1 colorless Spirit creature token."
        AttachSpiritTrigger(card, owner, Triggers.OnAttackSelf(card), zoneService, triggers);
        AttachSpiritTrigger(card, owner, Triggers.OnBlockSelf(card), zoneService, triggers);

        return card;
    }

    /// <summary>Attach a triggered ability keyed on <paramref name="condition"/>
    /// that mints a 1/1 colorless Spirit creature token (CR 111).</summary>
    private static void AttachSpiritTrigger(
        Creature card,
        Player owner,
        ITriggerCondition condition,
        ZoneService? zoneService,
        TriggerManager? triggers)
    {
        var makeSpirit = new Effect(
            $"{CardName}: create a 1/1 colorless Spirit token",
            () =>
            {
                var controller = card.Controller ?? owner;
                TokenFactory.CreateOnBattlefield(
                    new TokenFactory.TokenSpec(
                        Name: "Spirit",
                        Power: 1,
                        Toughness: 1,
                        Subtypes: new[] { CardSubtype.Spirit },
                        Keywords: null,
                        Colors: Array.Empty<ManaColor>()),
                    controller,
                    zoneService);
            });

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { makeSpirit },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);
    }
}
