using Content.Shared.Actions;
using Robust.Shared.Serialization;

namespace Content.Shared.Sprite;

/// <summary>
/// Dedicated action event used exclusively by SpriteStateToggleComponent to avoid cross-talk with other Toggle systems.
/// </summary>
public sealed partial class SpriteStateToggleActionEvent : InstantActionEvent
{
}
