using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Detention Sphere (Return to Ravnica, {1}{W}{U}).
///
/// Enchantment. Oracle text (verified against Scryfall 2026-06-02):
///   "When this enchantment enters, you may exile target nonland permanent
///    not named Detention Sphere and all other permanents with the same name
///    as that permanent.
///    When this enchantment leaves the battlefield, return the exiled cards
///    to the battlefield under their owner's control."
///
/// Detention Sphere is the same-name-sweep variant of the
/// <see cref="BanishingLightFactory"/> "Oblivion Ring" backbone (exile on ETB
/// until this leaves; return on LTB). The two differences from Banishing
/// Light that force a dedicated factory rather than a call into
/// <see cref="BanishingLightFactory.WireExileEnchantmentTriggers"/>:
/// <list type="number">
///   <item>The ETB exiles the chosen target <i>and</i> every other permanent
///     with the same name (CR 201.2) — a list, not a single card — mirroring
///     the same-name sweep in <see cref="EchoingTruthFactory"/> /
///     <see cref="MaelstromPulseFactory"/>. The LTB therefore returns the
///     whole captured list, each card under <i>its own</i> owner's control.</item>
///   <item>The target is "nonland permanent not named Detention Sphere"
///     (CR 109.5 — any controller, not restricted to an opponent's), with a
///     self-name exclusion so the sphere never exiles a copy of itself.</item>
/// </list>
/// The "you may" optionality is modeled by allowing the ETB to resolve to a
/// no-op when no legal target was chosen — declining to exile leaves the
/// captured list empty, so the LTB later returns nothing (matching the
/// printed optional clause; CR 603.5 — a "may" trigger whose controller
/// declines does nothing).
///
/// ## Implemented (v1)
/// - <b>Enchantment {1}{W}{U}</b>. Base shape (name / Enchantment / cost)
///   materialised from the embedded JSON definition (<c>detention-sphere.json</c>)
///   via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/> — same posture as
///   <see cref="RenegadeMapFactory"/> / <see cref="EchoingTruthFactory"/>
///   (the JSON schema doesn't express the exile-and-return triggers, so they
///   are layered on here).
/// - <b>ETB triggered ability</b> (CR 603.6a / CR 701.21): single 1..1
///   "target nonland permanent not named Detention Sphere"
///   <see cref="TargetRequest"/>. On resolve, after a CR 608.2b legality
///   re-check (still on the battlefield, still nonland, name != "Detention
///   Sphere"), it snapshots every battlefield and exiles the target plus every
///   permanent (target included) whose name matches — controller-agnostic. The
///   exiled cards + their owners are captured in a per-sphere closure shared
///   with the LTB ability.
/// - <b>LTB triggered ability</b> (CR 603.6c / CR 603.10c): fires whenever the
///   sphere moves OUT of the battlefield (any destination — covers dies +
///   bounce + flicker, matching "leaves the battlefield" wording). On resolve,
///   each still-exiled captured card returns to the battlefield under its
///   owner's control (CR 110.2 — Controller := Owner on the way back).
///
/// ## Deferred (v1 gaps)
/// - <b>Same-name sweep is not separately targeted</b>: only the single chosen
///   target must be a legal target; the collateral same-name permanents ignore
///   shroud / hexproof / protection (matching real MTG — the sphere has one
///   target).
/// - <b>Flicker race</b>: if the sphere is flickered, the LTB returns the
///   exiled cards before the flickered sphere re-enters; the re-entered sphere
///   is a new object (CR 400.7) with an empty closure — matching real MTG.
/// </summary>
[CardName("Detention Sphere")]
public static class DetentionSphereFactory
{
    public const string CardName = "Detention Sphere";
    public const string Slug = "detention-sphere";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Detention Sphere with no runtime services. Both triggered
    /// abilities are attached to the card shape; neither is registered with a
    /// <see cref="TriggerManager"/>. Suitable for shape / dispatcher tests.
    /// </summary>
    public static Enchantment Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Detention Sphere with optional runtime services. When
    /// <paramref name="triggers"/> is supplied, both ETB and LTB abilities are
    /// registered so the bus drives them via <see cref="CardMovedEvent"/>.
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

        // The declarative exile_until_leaves verb (detention-sphere.json,
        // sameNameGroup + optional) already attached BOTH linked triggered
        // abilities (ETB same-name sweep + LTB return-all) to the card shape at
        // build time. When a live TriggerManager is supplied, register every
        // triggered ability so the bus drives them — same posture as
        // OblivionRingFactory.
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
