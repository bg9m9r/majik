using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Banishing Light (Journey into Nyx, {2}{W}).
///
/// Enchantment. Oracle text:
///   "When Banishing Light enters, exile target nonland permanent an
///    opponent controls until Banishing Light leaves the battlefield."
///
/// The original "Oblivion Ring" template — exile a problem permanent
/// while the enchantment sticks; return it if the enchantment dies.
/// Shares the exile-on-ETB / return-on-LTB shape with Brain Maggot
/// (hand variant), Spell Queller (stack variant), and Skyclave
/// Apparition (token-spawning variant) — all built on the same
/// per-source closure that captures the exiled card between the two
/// triggered abilities.
///
/// ## Implemented (v1)
/// - <b>Enchantment {2}{W}</b>. Owner / controller wired.
/// - <b>ETB triggered ability</b> (CR 603.6a / CR 701.21):
///   <list type="bullet">
///     <item>Single 1..1 "target nonland permanent an opponent
///       controls" <see cref="TargetRequest"/>.</item>
///     <item>On resolve: CR 608.2b legality re-check (still on the
///       battlefield, still nonland, controlled by an opponent of the
///       Banishing Light controller). If legal, exile via raw zone
///       move. A reference to the exiled card AND its previous owner /
///       controller is captured in a per-Banishing-Light closure
///       shared with the LTB ability.</item>
///   </list>
/// - <b>LTB triggered ability</b> (CR 603.6c / CR 603.10c): fires
///   whenever Banishing Light moves OUT of the battlefield (any
///   destination — covers dies + bounce + flicker, matching "leaves
///   the battlefield" wording, same posture as Spell Queller /
///   Skyclave Apparition). On resolve: if a card was exiled and is
///   still in exile, it is returned to the battlefield under its
///   owner's control (CR 110.2 — "under its owner's control" maps
///   Controller := Owner on the way back).
///
/// ## Deferred (v1 gaps)
/// - <b>Multiple-permanent "until this leaves" stacking</b>: a single
///   Banishing Light only ever exiles one card per ETB resolution
///   (the printed "target" is singular). The per-instance closure
///   captures one card; subsequent ETBs of new Banishing Light
///   instances use their own closures. No re-trigger of the ETB on
///   the same Banishing Light is possible without leaving and
///   re-entering, which would be a fresh ICard identity.
/// - <b>Flicker race</b>: if Banishing Light is flickered, the LTB
///   returns the exiled card to the battlefield before the flickered
///   Banishing Light re-enters. The re-entered Banishing Light is a
///   new object (CR 400.7) so its closure starts empty — matching
///   real MTG. The factory captures owner + controller separately
///   so even when ownership changes (e.g. Switcheroo), the return is
///   routed to the captured owner.
/// </summary>
[CardName("Banishing Light")]
public static class BanishingLightFactory
{
    public const string CardName = "Banishing Light";
    public const string PrintedManaCost = "{2}{W}";
    public const string Slug = "banishing-light";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Banishing Light with no runtime services. Both
    /// triggered abilities are attached to the card shape; neither is
    /// registered with a <see cref="TriggerManager"/>. Suitable for
    /// shape / dispatcher tests.
    /// </summary>
    public static Enchantment Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Banishing Light with optional runtime services. When
    /// <paramref name="triggers"/> is supplied, both ETB and LTB
    /// abilities are registered so the bus drives them via
    /// <see cref="CardMovedEvent"/>.
    /// </summary>
    public static Enchantment Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var built = CardDefinitionFactory.Build(Definition, owner);
        if (built is not Enchantment card)
        {
            throw new InvalidOperationException(
                $"Expected '{CardName}' to materialise as an Enchantment but got "
                + $"'{built.GetType().Name}'.");
        }
        card.SetOwner(owner);
        card.SetController(owner);

        // The declarative exile_until_leaves verb (banishing-light.json) already
        // attached BOTH linked triggered abilities (ETB exile + LTB return) to
        // the card shape at build time. When a live TriggerManager is supplied,
        // register every triggered ability so the bus drives them — same posture
        // as OblivionRingFactory. This is the declarative replacement for the
        // former bespoke WireExileEnchantmentTriggers backbone; the whole
        // Banishing Light family (Banishing Light / Conclave Tribunal / Cast Out
        // / Borrowed Time / Detention Sphere) now rides the same closed verb.
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
