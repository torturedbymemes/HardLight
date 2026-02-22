using Content.Shared.Roles;
using Robust.Shared.Prototypes;

namespace Content.Server.StationEvents.Components;

[RegisterComponent, Access(typeof(Events.DynamicJobAllocationRule))]
public sealed partial class DynamicJobAllocationRuleComponent : Component
{
    /// <summary>
    /// How often to check and adjust job slots (in seconds)
    /// </summary>
    [DataField("checkInterval")]
    public float CheckInterval = 600f; // 10 minutes

    /// <summary>
    /// Time since last check
    /// </summary>
    public float TimeSinceLastCheck = 0f;

    /// <summary>
    /// Mercenary job ID
    /// </summary>
    [DataField("mercenaryJob")]
    public ProtoId<JobPrototype> MercenaryJob = "Mercenary";

    /// <summary>
    /// Maximum number of mercenary slots
    /// </summary>
    [DataField("mercenaryCap")]
    public int MercenaryCap = 40;
}
