using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Claws of Gix (The Dark / many reprints, {0}).
///
/// Artifact. Oracle text:
///   "{1}, Sacrifice a permanent: You gain 1 life."
///
/// Required by the Affinity bot deck as a zero-mana artifact enabler
/// (Mox Opal threshold, Arcbound Ravager modular fodder, free artifact
/// count for Cranial Plating / Thoughtcast).
///
/// ## Implemented (v1)
/// - Card identity: Artifact, mana cost {0}, mana value 0. Owner /
///   controller wired at construction.
/// - <b>Activated ability</b> (CR 602) with two costs in declaration order:
///   1. <see cref="ManaCostCost"/> "{1}" — one generic mana.
///   2. <see cref="SacrificeAnyPermanentCost"/> — "sacrifice a permanent"
///      (CR 701.16). Any permanent the controller controls is legal:
///      creature, artifact, enchantment, planeswalker, land, battle.
///      Claws of Gix itself is included (self-sacrifice is legal per
///      CR 117.1 / CR 602 — no "another" restriction in the printed text).
/// - <b>Effect</b>: controller gains 1 life (CR 119.3).
/// - <b>Cannot activate</b> when the controller controls no permanents
///   (CanPay returns false → ability is illegal to activate, CR 602.5a).
///
/// ## Deferred (v1 gaps)
/// - <b>Agent-driven sacrifice target selection</b>:
///   <see cref="SacrificeAnyPermanentCost"/> v1 defers deterministically
///   to the first permanent on the controller's battlefield (same posture
///   as <see cref="SacrificeAnotherCreatureCost"/>). Full prompt-driven
///   selection requires ITarget / TargetResolver infrastructure.
/// - The ability does not tap the artifact as part of its cost (printed
///   text omits {T} — confirmed Scryfall oracle).
/// </summary>
[CardName("Claws of Gix")]
public static class ClawsOfGixFactory
{
    public const string CardName = "Claws of Gix";
    public const string PrintedManaCost = "{0}";

    /// <summary>
    /// Construct Claws of Gix owned and controlled by
    /// <paramref name="owner"/>. Attaches the "{1}, Sacrifice a permanent:
    /// You gain 1 life." activated ability structurally.
    /// </summary>
    public static Artifact Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var claws = new Artifact(CardName, PrintedManaCost);
        claws.SetOwner(owner);
        claws.SetController(owner);

        // ----------------------------------------------------------------
        // {1}, Sacrifice a permanent: You gain 1 life.
        // CR 602 — activated ability; costs are paid in declaration order.
        // CR 701.16 — sacrifice (any permanent the controller controls).
        // CR 119.3 — life gain.
        // ----------------------------------------------------------------
        var sacCost = new SacrificeAnyPermanentCost(claws);

        var gainLifeEffect = new Effect(
            $"{CardName}: controller gains 1 life",
            () =>
            {
                var controller = claws.Controller ?? owner;
                controller.GainLife(1);
            });

        var ability = new ClawsOfGixAbility(
            source: claws,
            controller: owner,
            sacCost: sacCost,
            gainLifeEffect: gainLifeEffect);

        claws.AddAbility(ability);
        return claws;
    }
}

/// <summary>
/// Claws of Gix's only activated ability. Subclasses
/// <see cref="ActivatedAbility"/> so the chosen sacrifice target can be
/// inspected and pre-set by tests and the bot heuristic.
/// </summary>
public sealed class ClawsOfGixAbility : ActivatedAbility
{
    /// <summary>
    /// The sacrifice cost on the ability — exposed so callers can
    /// pre-set <see cref="SacrificeAnyPermanentCost.Target"/> before
    /// activation.
    /// </summary>
    public SacrificeAnyPermanentCost SacrificeChoice { get; }

    internal ClawsOfGixAbility(
        Artifact source,
        Player controller,
        SacrificeAnyPermanentCost sacCost,
        IEffect gainLifeEffect)
        : base(
            source: source,
            controller: controller,
            costs: new ICost[]
            {
                new ManaCostCost("{1}"),
                sacCost,
            },
            effects: new IEffect[] { gainLifeEffect })
    {
        SacrificeChoice = sacCost;
    }
}

/// <summary>
/// "Sacrifice a permanent" — activated-ability cost that requires the
/// controller to sacrifice any permanent they control (no type
/// restriction, no "another" restriction). Claws of Gix itself is a
/// legal sacrifice target (CR 602 does not exclude the source card from
/// the set of legal sacrifices unless the printed text says "another").
///
/// Implements <see cref="ICost"/> so it slots directly into an
/// <see cref="ActivatedAbility"/> cost list.
///
/// ## Deferred (v1 gaps)
/// - <see cref="Target"/> must be set by the agent before
///   <see cref="Pay"/> is called; otherwise the first permanent the
///   controller controls is chosen deterministically (same posture as
///   <see cref="SacrificeAnotherCreatureCost"/>). Full agent-driven
///   target prompting deferred.
/// </summary>
public sealed class SacrificeAnyPermanentCost : ICost
{
    private readonly Permanent _source;

    /// <summary>
    /// Optionally set by the agent to indicate which permanent to
    /// sacrifice. When null the cost falls back to the first permanent
    /// on the controller's battlefield (deterministic v1 behaviour).
    /// </summary>
    public Permanent? Target { get; set; }

    public SacrificeAnyPermanentCost(Permanent source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    /// <inheritdoc/>
    public string Description => "sacrifice a permanent";

    /// <inheritdoc/>
    /// <remarks>
    /// CanPay is true as long as the controller controls at least one
    /// permanent on the battlefield (including <paramref name="_source"/>
    /// itself — self-sacrifice is legal).
    /// </remarks>
    public bool CanPay(Player player)
    {
        if (player == null) return false;
        return player.Zones.Battlefield.GetCards()
            .OfType<Permanent>()
            .Any();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Moves the chosen (or first-deterministic) permanent from the
    /// controller's battlefield to its owner's graveyard
    /// (CR 701.16a — sacrificed permanents go to their owner's
    /// graveyard, not necessarily the activating player's).
    /// </remarks>
    public void Pay(Player player)
    {
        if (player == null) throw new ArgumentNullException(nameof(player));

        var pick = Target ?? player.Zones.Battlefield.GetCards()
            .OfType<Permanent>()
            .FirstOrDefault();

        if (pick == null)
            throw new InvalidOperationException(
                $"Cannot pay {Description}: no permanent to sacrifice.");

        var owner = pick.Owner ?? player;
        player.Zones.Battlefield.RemoveCard(pick);
        owner.Zones.Graveyard.AddCard(pick);
        pick.SetZone(ZoneType.Graveyard);
    }
}
