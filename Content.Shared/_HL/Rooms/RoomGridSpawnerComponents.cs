using Robust.Shared.GameStates;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared._HL.Rooms;

[RegisterComponent, NetworkedComponent]
public sealed partial class RoomGridSpawnerConsoleComponent : Component
{
    [DataField("area_group")]
    public string AreaGroup = string.Empty;

    [DataField("in_use")]
    public bool InUse;
}

[RegisterComponent]
public sealed partial class RoomGridSpawnAreaComponent : Component
{
    [DataField("area_group")]
    public string AreaGroup = string.Empty;

    [DataField("width")]
    public float Width = 7f;

    [DataField("height")]
    public float Height = 7f;
}
