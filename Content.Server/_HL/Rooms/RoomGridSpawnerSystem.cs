using Content.Server.Popups;
using Content.Server.Shuttles.Save;
using Content.Shared._HL.Rooms;
using Content.Shared.Interaction;
using Content.Server.Mind;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Shuttles.Save;
using Content.Shared.Timing;
using Robust.Server.Player;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.Shared.Player;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Robust.Server.GameObjects;

namespace Content.Server._HL.Rooms;

public sealed class RoomGridSpawnerSystem : EntitySystem
{
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly UseDelaySystem _useDelay = default!;
    [Dependency] private readonly ShipSerializationSystem _shipSerialization = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly MapSystem _map = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly MindSystem _mind = default!;
    [Dependency] private readonly ITileDefinitionManager _tileDefManager = default!;
    [Dependency] private readonly AppearanceSystem _appearance = default!;

    private readonly Dictionary<NetUserId, PendingRoomLoad> _pendingLoads = new();
    private readonly Dictionary<NetUserId, ActiveRoomSession> _activeSessions = new();

    private EntityQuery<MapGridComponent> _gridQuery;
    private EntityQuery<TransformComponent> _xformQuery;

    public override void Initialize()
    {
        base.Initialize();
        _gridQuery = GetEntityQuery<MapGridComponent>();
        _xformQuery = GetEntityQuery<TransformComponent>();

        SubscribeLocalEvent<RoomGridSpawnerConsoleComponent, InteractHandEvent>(OnConsoleInteract);
        SubscribeLocalEvent<MindContainerComponent, MindRemovedMessage>(OnMindRemoved);
        SubscribeNetworkEvent<SendRoomGridDataMessage>(OnRoomGridData);
    }

    private void OnConsoleInteract(EntityUid uid, RoomGridSpawnerConsoleComponent component, InteractHandEvent args)
    {
        if (args.Handled || args.User == EntityUid.Invalid)
            return;

        if (component.InUse)
        {
            _popup.PopupEntity("This console is already in use.", args.User, args.User);
            args.Handled = true;
            return;
        }

        if (!_xformQuery.TryComp(uid, out var consoleXform) || consoleXform.GridUid == null)
            return;

        if (TryComp<UseDelayComponent>(uid, out var useDelay) && !_useDelay.TryResetDelay((uid, useDelay), true))
            return;

        if (!_mind.TryGetMind(args.User, out _, out var mindComp) || mindComp.UserId == null)
            return;

        var session = _player.GetSessionById(mindComp.UserId.Value);
        if (session == null)
            return;

        if (!TryGetAreaMarker(uid, component, consoleXform, out var markerUid, out var markerComp, out var markerXform))
        {
            _popup.PopupEntity("No linked room marker found.", args.User, args.User);
            return;
        }

        var bounds = GetAreaBounds(markerComp, markerXform);
        var characterKey = BuildCharacterKey(mindComp.UserId.Value, mindComp.CharacterName ?? session.Name);

        var pending = new PendingRoomLoad(uid, markerUid, consoleXform.GridUid.Value, bounds, characterKey);
        _pendingLoads[mindComp.UserId.Value] = pending;

        RaiseNetworkEvent(new RequestRoomGridLoadMessage(GetNetEntity(uid), characterKey), session);
        _popup.PopupEntity("Looking for your saved room...", args.User, args.User);
        args.Handled = true;
    }

    private void OnRoomGridData(SendRoomGridDataMessage msg, EntitySessionEventArgs args)
    {
        var userId = args.SenderSession.UserId;
        if (!_pendingLoads.TryGetValue(userId, out var pending))
            return;

        _pendingLoads.Remove(userId);

        if (msg.CharacterKey != pending.CharacterKey)
            return;

        if (!EntityManager.TryGetEntity(msg.ConsoleNetEntity, out var consoleUid) || Deleted(consoleUid))
            return;

        var consoleEntity = consoleUid.Value;

        var roomData = msg.RoomData;
        if (!msg.Found || string.IsNullOrWhiteSpace(roomData))
        {
            var blank = CreateBlankRoomData(pending.Bounds, BlankRoomSize, BlankRoomTileId);
            roomData = _shipSerialization.SerializeShipGridDataToYaml(blank);
            RaiseNetworkEvent(new SendRoomGridSaveDataClientMessage(pending.CharacterKey, roomData), args.SenderSession);
            _popup.PopupEntity("No saved room found. Created a blank room.", consoleEntity, args.SenderSession);
        }

        if (!_gridQuery.TryComp(pending.GridUid, out var gridComp))
            return;

        consoleEntity = ClearArea(pending.GridUid, gridComp, pending.Bounds, consoleEntity, pending.MarkerUid, DeleteContainedEntitiesOnReset);

        var shipData = _shipSerialization.DeserializeShipGridDataFromYaml(roomData, userId);
        shipData = FilterShipGridDataToBounds(shipData, pending.Bounds);
        _shipSerialization.ReconstructShipOnExistingGrid(shipData, pending.GridUid, Vector2.Zero);

        if (TryComp<RoomGridSpawnerConsoleComponent>(consoleEntity, out var consoleComp))
        {
            consoleComp.InUse = true;
            _appearance.SetData(consoleEntity, RoomGridSpawnerVisuals.InUse, true);
        }

        _activeSessions[userId] = new ActiveRoomSession(consoleEntity, pending.MarkerUid, pending.GridUid, pending.Bounds, pending.CharacterKey);

        _popup.PopupEntity("Room loaded.", consoleEntity, args.SenderSession);
    }

    private void OnMindRemoved(EntityUid uid, MindContainerComponent component, MindRemovedMessage args)
    {
        NetUserId? userId = args.Mind.Comp.UserId;
        if (userId == null && _player.TryGetSessionByEntity(uid, out var playerSession))
            userId = playerSession.UserId;

        if (userId == null)
            return;

        if (!_activeSessions.TryGetValue(userId.Value, out var activeSession))
            return;

        SaveAndResetRoom(userId.Value, activeSession);
    }

    private void SaveAndResetRoom(NetUserId userId, ActiveRoomSession session)
    {
        if (!_gridQuery.TryComp(session.GridUid, out var gridComp))
            return;

        var excluded = new HashSet<EntityUid> { session.ConsoleUid, session.MarkerUid };
        var shipData = _shipSerialization.SerializeShipArea(session.GridUid, userId, $"Room_{session.CharacterKey}", session.Bounds, excluded);
        var yaml = _shipSerialization.SerializeShipGridDataToYaml(shipData);

        if (_player.TryGetSessionById(userId, out var playerSession))
        {
            RaiseNetworkEvent(new SendRoomGridSaveDataClientMessage(session.CharacterKey, yaml), playerSession);
        }

        ClearArea(session.GridUid, gridComp, session.Bounds, session.ConsoleUid, session.MarkerUid, DeleteContainedEntitiesOnReset);
        _activeSessions.Remove(userId);
    }

    private bool TryGetAreaMarker(EntityUid consoleUid, RoomGridSpawnerConsoleComponent component, TransformComponent consoleXform,
        out EntityUid markerUid, out RoomGridSpawnAreaComponent markerComp, out TransformComponent markerXform)
    {
        markerUid = default;
        markerComp = default!;
        markerXform = default!;

        if (consoleXform.GridUid == null || string.IsNullOrWhiteSpace(component.AreaGroup))
            return false;

        var bestDist = float.MaxValue;
        var query = EntityQueryEnumerator<RoomGridSpawnAreaComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var areaComp, out var xform))
        {
            if (!string.Equals(areaComp.AreaGroup, component.AreaGroup, StringComparison.OrdinalIgnoreCase))
                continue;
            if (xform.GridUid != consoleXform.GridUid)
                continue;

            var dist = (xform.LocalPosition - consoleXform.LocalPosition).LengthSquared();
            if (dist >= bestDist)
                continue;

            bestDist = dist;
            markerUid = uid;
            markerComp = areaComp;
            markerXform = xform;
        }

        return markerUid != default;
    }

    private static Box2 GetAreaBounds(RoomGridSpawnAreaComponent markerComp, TransformComponent markerXform)
    {
        var widthTiles = Math.Max(1, (int)MathF.Round(markerComp.Width));
        var heightTiles = Math.Max(1, (int)MathF.Round(markerComp.Height));

        var center = markerXform.LocalPosition;
        var centerX = (int)MathF.Round(center.X);
        var centerY = (int)MathF.Round(center.Y);

        var left = centerX - widthTiles / 2;
        var bottom = centerY - heightTiles / 2;

        return new Box2(
            new Vector2(left, bottom),
            new Vector2(left + widthTiles, bottom + heightTiles));
    }

    private EntityUid ClearArea(EntityUid gridUid, MapGridComponent grid, Box2 bounds, EntityUid consoleUid, EntityUid markerUid, bool includeContained)
    {
        var respawnConsole = consoleUid != EntityUid.Invalid && EntityManager.EntityExists(consoleUid);
        EntityCoordinates? consoleCoords = null;
        Angle consoleRotation = Angle.Zero;

        if (respawnConsole && _xformQuery.TryComp(consoleUid, out var consoleXform))
        {
            consoleCoords = consoleXform.Coordinates;
            consoleRotation = consoleXform.LocalRotation;
        }

        var entities = new HashSet<EntityUid>();
        var innerBounds = bounds.Enlarged(-0.001f);
        var flags = includeContained ? LookupFlags.All : LookupFlags.All & ~LookupFlags.Contained;
        _lookup.GetLocalEntitiesIntersecting(gridUid, bounds, entities, flags);
        foreach (var entity in entities)
        {
            if (entity == gridUid || entity == markerUid)
                continue;

            if (HasComp<MapGridComponent>(entity))
                continue;

            if (HasComp<MindContainerComponent>(entity))
                continue;

            if (_xformQuery.TryComp(entity, out var entityXform) && !innerBounds.Contains(entityXform.LocalPosition))
                continue;

            Del(entity);
        }

        if (respawnConsole)
            Del(consoleUid);

        var minX = (int)MathF.Floor(bounds.Left);
        var maxX = (int)MathF.Ceiling(bounds.Right) - 1;
        var minY = (int)MathF.Floor(bounds.Bottom);
        var maxY = (int)MathF.Ceiling(bounds.Top) - 1;

        var tile = new Tile(_tileDefManager[BlankRoomTileId].TileId);
        for (var x = minX; x <= maxX; x++)
        {
            for (var y = minY; y <= maxY; y++)
            {
                _map.SetTile(gridUid, grid, new Vector2i(x, y), tile);
            }
        }

        if (respawnConsole && consoleCoords != null)
        {
            var newConsole = Spawn(RoomConsolePrototype, consoleCoords.Value);
            _transform.SetLocalRotation(newConsole, consoleRotation);
            if (TryComp<RoomGridSpawnerConsoleComponent>(newConsole, out var consoleComp))
                consoleComp.InUse = false;
            _appearance.SetData(newConsole, RoomGridSpawnerVisuals.InUse, false);
            return newConsole;
        }

        return consoleUid;
    }

    private static ShipGridData FilterShipGridDataToBounds(ShipGridData source, Box2 bounds)
    {
        var filtered = new ShipGridData
        {
            Metadata = source.Metadata,
            Grids = new List<GridData>()
        };

        if (source.Grids.Count == 0)
            return filtered;

        var grid = source.Grids[0];
        var gridOut = new GridData
        {
            GridId = grid.GridId,
            AtmosphereData = null,
            DecalData = null
        };

        foreach (var tile in grid.Tiles)
        {
            var pos = new Vector2(tile.X + 0.5f, tile.Y + 0.5f);
            if (bounds.Contains(pos))
                gridOut.Tiles.Add(tile);
        }

        var included = new HashSet<string>();
        foreach (var entity in grid.Entities)
        {
            if (entity.IsContained)
                continue;
            if (bounds.Contains(entity.Position))
                included.Add(entity.EntityId);
        }

        bool added;
        do
        {
            added = false;
            foreach (var entity in grid.Entities)
            {
                if (included.Contains(entity.EntityId))
                    continue;
                if (string.IsNullOrEmpty(entity.ParentContainerEntity))
                    continue;
                if (!included.Contains(entity.ParentContainerEntity))
                    continue;

                included.Add(entity.EntityId);
                added = true;
            }
        }
        while (added);

        foreach (var entity in grid.Entities)
        {
            if (included.Contains(entity.EntityId))
                gridOut.Entities.Add(entity);
        }

        filtered.Grids.Add(gridOut);
        return filtered;
    }

    private static string BuildCharacterKey(NetUserId userId, string characterName)
    {
        var safeName = characterName.Trim();
        if (string.IsNullOrWhiteSpace(safeName))
            safeName = "unknown";

        return $"{userId.UserId:N}_{safeName}";
    }

    private static ShipGridData CreateBlankRoomData(Box2 bounds, int size, string tileId)
    {
        var half = (size - 1) / 2;
        var center = bounds.Center;
        var originX = (int)MathF.Round(center.X) - half;
        var originY = (int)MathF.Round(center.Y) - half;

        var grid = new GridData
        {
            AtmosphereData = null,
            DecalData = null
        };

        for (var x = 0; x < size; x++)
        {
            for (var y = 0; y < size; y++)
            {
                grid.Tiles.Add(new TileData
                {
                    X = originX + x,
                    Y = originY + y,
                    TileType = tileId
                });
            }
        }

        return new ShipGridData
        {
            Metadata = new ShipMetadata
            {
                ShipName = "BlankRoom",
                Timestamp = DateTime.UtcNow
            },
            Grids = new List<GridData> { grid }
        };
    }

    private const int BlankRoomSize = 7;
    private const string BlankRoomTileId = "FloorSteel";
    private const string RoomConsolePrototype = "ComputerRoomGridSpawner";
    private const bool DeleteContainedEntitiesOnReset = false;

    private readonly record struct PendingRoomLoad(EntityUid ConsoleUid, EntityUid MarkerUid, EntityUid GridUid, Box2 Bounds, string CharacterKey);

    private readonly record struct ActiveRoomSession(EntityUid ConsoleUid, EntityUid MarkerUid, EntityUid GridUid, Box2 Bounds, string CharacterKey);
}
