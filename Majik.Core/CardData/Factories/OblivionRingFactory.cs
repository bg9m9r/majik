using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Oblivion Ring (Lorwyn / reprints, {2}{W}).
///
/// Enchantment. Oracle text (verified against Scryfall):
///   "When this enchantment enters, exile another target nonland permanent.
///    When this enchantment leaves the battlefield, return the exiled card to
///    the battlefield under its owner's control."
///
/// The original "O-Ring" template — exile a permanent while the enchantment
/// sticks; return it if the enchantment leaves. Two LINKED abilities (CR 607),
/// the "until" duration (CR 603.6e) + the return (CR 610.3).
///
/// ## Declarative — fully JSON-driven (PLAN 03 convergence)
/// Both the card shape AND the exile-on-ETB / return-on-LTB linked pair are now
/// loaded from <c>Majik.Core/CardData/Cards/oblivion-ring.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The JSON carries an
/// <c>etb_self</c> trigger with the new <c>exile_until_leaves</c> effect verb
/// (<see cref="Majik.Core.CardData.Definitions.ExileUntilLeavesEffectDef"/>),
/// which composes the printed restrictions:
/// <list type="bullet">
///   <item><c>opponentControlsOnly: false</c> — Oblivion Ring has NO "an
///   opponent controls" clause; it may exile the controller's own
///   permanents.</item>
///   <item><c>excludeSelf: true</c> — "ANOTHER target nonland permanent"
///   (CR 109.5 — the source cannot be chosen).</item>
/// </list>
/// At build time the verb attaches the linked LTB triggered ability to the same
/// card (sharing a per-instance closure that captures the exiled object), so
/// <see cref="TriggerManager.BindCard"/> auto-registers both abilities when the
/// enchantment enters the battlefield — no hand-rolled closure here.
///
/// This is the declarative replacement for the bespoke
/// <see cref="BanishingLightFactory.WireExileEnchantmentTriggers"/> shape; the
/// Banishing Light family (Banishing Light / Conclave Tribunal / Cast Out /
/// Borrowed Time / Detention Sphere) still shares that imperative backbone and
/// is a follow-up conversion.
///
/// ## Implemented (v1)
/// - <b>Enchantment {2}{W}</b>. Owner / controller wired.
/// - <b>ETB triggered ability</b> (CR 603.6a / CR 701.21): single 1..1
///   "another target nonland permanent" request; on resolve the verb re-checks
///   legality (CR 608.2b — still on the battlefield, still nonland, not the
///   Oblivion Ring itself), exiles it, and records it for the linked return.
/// - <b>LTB triggered ability</b> (CR 603.6e / CR 610.3): fires whenever
///   Oblivion Ring leaves the battlefield (any destination); returns the SAME
///   exiled card to its owner's battlefield under its owner's control
///   (CR 110.2). No-ops if Oblivion Ring already left (the linked return fires
///   once) or the exiled object has since left exile.
/// </summary>
[CardName("Oblivion Ring")]
public static class OblivionRingFactory
{
    public const string CardName = "Oblivion Ring";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("oblivion-ring");

    /// <summary>
    /// Construct Oblivion Ring with both linked triggered abilities attached to
    /// the card shape but NOT registered with a <see cref="TriggerManager"/>.
    /// Suitable for shape / dispatcher tests.
    /// </summary>
    public static Enchantment Create(Player owner) =>
        Create(owner, triggers: null);

    /// <summary>
    /// Construct Oblivion Ring with optional <see cref="TriggerManager"/>
    /// wiring. When <paramref name="triggers"/> is supplied, both ETB and LTB
    /// abilities are registered so the bus drives them via
    /// <see cref="Majik.Core.Events.CardMovedEvent"/>.
    /// </summary>
    public static Enchantment Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Enchantment)CardDefinitionFactory.Build(Definition, owner);

        // The exile_until_leaves verb already attached BOTH linked abilities to
        // the card at build time. When a live TriggerManager is supplied, bind
        // the card so both are registered (BindCard registers every
        // ITriggeredAbility the card carries) — matching the previous explicit
        // RegisterTriggeredAbility wiring.
        if (triggers != null)
        {
            foreach (var ability in card.Abilities.OfType<ITriggeredAbility>())
            {
                triggers.RegisterTriggeredAbility(ability);
            }
        }

        return card;
    }
}
