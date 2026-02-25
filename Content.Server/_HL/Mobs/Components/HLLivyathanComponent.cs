namespace Content.Server.Mobs.Components;

using Content.Shared.DoAfter;
using Robust.Shared.Audio;

[RegisterComponent]
public sealed partial class HLLivyathanComponent : Component
{
    [DataField]
    public string DragonMorphAction = "ActionLivyathanDragonMorph";

    [DataField]
    public bool AddDragonMorphAction = true;

    [DataField]
    public string DragonPolymorphId = "LivyathanDragonMorph";

    [DataField]
    public string DragonMorphPortalPrototype = "PortalAbyssal";

    [DataField]
    public float DragonMorphDoAfter = 2f;

    [DataField]
    public SoundSpecifier DragonPortalDepartureSound = new SoundPathSpecifier("/Audio/Effects/teleport_departure.ogg");

    [DataField]
    public SoundSpecifier DragonPortalArrivalSound = new SoundPathSpecifier("/Audio/Effects/teleport_arrival.ogg");

    public EntityUid? DragonMorphActionEntity;

    public EntityUid? ActivePortal;

    public DoAfterId? ActiveMorphDoAfter;

    public bool SuppressRevertIntercept;
}
