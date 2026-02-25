using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;

namespace Content.Shared._HL.Mobs;

public sealed partial class LivyathanDragonMorphActionEvent : InstantActionEvent;

public sealed partial class LivyathanDragonRevertActionEvent : InstantActionEvent;

[Serializable, NetSerializable]
public sealed partial class LivyathanDragonMorphDoAfterEvent : SimpleDoAfterEvent
{
    public bool Revert;
}
