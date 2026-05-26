using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.Costs;

/// <summary>
/// "Sacrifice another creature or Blood token" — printed on Falkenrath Pit
/// Fighter (Innistrad: Crimson Vow, {R}) and the broader Crimson Vow "sac
/// a creature or Blood" payoff family. Cost surface lets the controller
/// trade either:
/// <list type="bullet">
///   <item>any creature other than the ability's source (the same
///     <see cref="SacrificeAnotherCreatureCost"/> contract), or</item>
///   <item>any Blood token (CR 111.10 / 205.3i —
///     <see cref="CardSubtype.Blood"/> on an artifact token).</item>
/// </list>
///
/// Slots into <see cref="Majik.Core.Abilities.ActivatedAbility"/> cost lists
/// next to a <see cref="ManaCostCost"/> in the standard
/// "{cost}, sacrifice X: do Y" pattern.
///
/// ## Selection policy
///
/// <see cref="Target"/> may be set by the agent before <see cref="Pay"/>.
/// When null, the deterministic v1 picker chooses the FIRST Blood token on
/// the controller's battlefield (cheaper to trade — Blood is a renewable,
/// less-valuable resource); failing that, the first non-source creature.
/// Same posture as <see cref="SacrificeAnotherCreatureCost"/>'s
/// deterministic picker.
///
/// ## Deferred (v1 gaps)
///
/// - Full agent-driven choice prompt waits on the shared
///   choose-a-permanent surface (same gap as the broader sacrifice-prompt
///   family — Goblin Bombardment / Skirk Prospector / Walking Ballista's
///   ping targeting).
/// </summary>
public sealed class SacrificeAnotherCreatureOrBloodTokenCost : ICost
{
    private readonly Permanent _self;

    /// <summary>
    /// Optionally set by the agent to indicate which permanent to sacrifice.
    /// Must be either a non-source <see cref="Creature"/> or a Blood-subtype
    /// permanent (<see cref="CardSubtype.Blood"/>) controlled by the paying
    /// player.
    /// </summary>
    public Permanent? Target { get; set; }

    public SacrificeAnotherCreatureOrBloodTokenCost(Permanent self)
    {
        _self = self ?? throw new ArgumentNullException(nameof(self));
    }

    public string Description =>
        $"sacrifice a creature other than {_self.Name} or a Blood token";

    /// <inheritdoc/>
    public bool CanPay(Player player)
    {
        if (player == null) return false;
        return player.Zones.Battlefield.GetCards()
            .OfType<Permanent>()
            .Any(p => IsEligible(p));
    }

    /// <inheritdoc/>
    public void Pay(Player player)
    {
        if (player == null) throw new ArgumentNullException(nameof(player));

        var pick = Target;
        if (pick != null && !IsEligible(pick))
        {
            throw new InvalidOperationException(
                $"Target {pick.Name} is not eligible: must be a non-source creature or a Blood token.");
        }

        // Deterministic v1 picker: prefer the cheaper resource (Blood token)
        // before sacrificing a creature.
        pick ??= player.Zones.Battlefield.GetCards()
            .OfType<Permanent>()
            .FirstOrDefault(p => p.HasSubtype(CardSubtype.Blood) && IsEligible(p));
        pick ??= player.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .FirstOrDefault(c => !ReferenceEquals(c, _self));

        if (pick == null)
            throw new InvalidOperationException(
                $"Cannot pay {Description}: no eligible permanent to sacrifice.");

        player.Zones.Battlefield.RemoveCard(pick);
        player.Zones.Graveyard.AddCard(pick);
        pick.SetZone(ZoneType.Graveyard);
    }

    private bool IsEligible(Permanent p)
    {
        if (p == null) return false;
        if (ReferenceEquals(p, _self)) return false;
        // Creature alternative — any other creature.
        if (p is Creature) return true;
        // Blood-token alternative — Blood subtype (CR 205.3i).
        if (p.HasSubtype(CardSubtype.Blood)) return true;
        return false;
    }
}
