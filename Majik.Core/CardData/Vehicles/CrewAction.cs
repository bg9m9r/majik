using Majik.Core.Effects;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.CardData.Vehicles;

/// <summary>
/// CR 702.122 — perform a Crew N activation. Caller supplies a set of
/// creatures the controller chose to tap; total power must reach the
/// crew cost. On success: each crewmate taps, a one-turn
/// <see cref="VehicleCrewEffect"/> is registered with the supplied
/// <see cref="ContinuousEffectsService"/>.
/// </summary>
public static class CrewAction
{
    public sealed record CrewResult(bool Success, string? Reason);

    public static CrewResult Crew(
        Creature vehicle,
        int crewCost,
        int vehiclePower,
        int vehicleToughness,
        IReadOnlyList<Creature> crewmates,
        ContinuousEffectsService effects)
    {
        if (vehicle == null) throw new ArgumentNullException(nameof(vehicle));
        if (crewmates == null) throw new ArgumentNullException(nameof(crewmates));
        if (effects == null) throw new ArgumentNullException(nameof(effects));

        var totalPower = crewmates.Sum(c => c.Power);
        if (totalPower < crewCost)
        {
            return new CrewResult(false,
                $"crew {crewCost} requires {crewCost} total power; got {totalPower}");
        }
        if (crewmates.Any(c => c.IsTapped))
        {
            return new CrewResult(false, "all crewmates must be untapped");
        }
        if (crewmates.Any(c => c.HasSummoningSickness))
        {
            // CR 702.122 — note: summoning-sick creatures CAN crew (CR 506.2 ban
            // is on attacking + activated abilities with {T}); Crew uses {T}
            // however. So summoning-sick creatures cannot crew. Enforce.
            return new CrewResult(false, "summoning-sick creatures cannot tap to crew");
        }

        foreach (var c in crewmates) c.Tap();
        effects.Register(new VehicleCrewEffect(vehicle, vehiclePower, vehicleToughness));
        return new CrewResult(true, null);
    }
}
