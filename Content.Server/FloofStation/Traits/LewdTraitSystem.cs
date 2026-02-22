using Content.Server.Chemistry.Containers.EntitySystems;
using Content.Shared.Chemistry.EntitySystems;
using Content.Server.Popups;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.DoAfter;
using Content.Shared.IdentityManagement;
using Content.Shared.Mobs.Systems;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Content.Shared.FloofStation.Traits.Events;
using Robust.Shared.Timing;
using JetBrains.Annotations;

namespace Content.Server.FloofStation.Traits;

[UsedImplicitly]
public sealed class LewdTraitSystem : EntitySystem
{
    [Dependency] private readonly HungerSystem _hunger = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfterSystem = default!;
    [Dependency] private readonly PopupSystem _popupSystem = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutionContainer = default!;

    public override void Initialize()
    {
        base.Initialize();

        //Initializers
        SubscribeLocalEvent<CumProducerComponent, ComponentStartup>(OnComponentInitCum);
        SubscribeLocalEvent<MilkProducerComponent, ComponentStartup>(OnComponentInitMilk);
        //SubscribeLocalEvent<SquirtProducerComponent, ComponentStartup>(OnComponentInitSquirt); //Unused-Trait is WIP
        SubscribeLocalEvent<PissProducerComponent, ComponentStartup>(OnComponentInitPiss);

        //Verbs
        SubscribeLocalEvent<CumProducerComponent, GetVerbsEvent<InnateVerb>>(AddCumVerb);
        SubscribeLocalEvent<RefillableSolutionComponent, GetVerbsEvent<AlternativeVerb>>(AddRefillableInsideVerbs);
        SubscribeLocalEvent<InjectableSolutionComponent, GetVerbsEvent<AlternativeVerb>>(AddInjectableInsideVerbs);
        SubscribeLocalEvent<MilkProducerComponent, GetVerbsEvent<InnateVerb>>(AddMilkVerb);
        //SubscribeLocalEvent<SquirtProducerComponent, GetVerbsEvent<InnateVerb>>(AddSquirtVerb); //Unused-Trait is WIP

        //Events
        SubscribeLocalEvent<CumProducerComponent, CummingDoAfterEvent>(OnDoAfterCum);
        SubscribeLocalEvent<MilkProducerComponent, MilkingDoAfterEvent>(OnDoAfterMilk);
        //SubscribeLocalEvent<SquirtProducerComponent, SquirtingDoAfterEvent>(OnDoAfterSquirt); //Unused-Trait is WIP
        SubscribeLocalEvent<PissProducerComponent, PissingDoAfterEvent>(OnDoAfterPiss);
    }

    #region event handling
    private void OnComponentInitCum(Entity<CumProducerComponent> entity, ref ComponentStartup args)
    {
        if (!_solutionContainer.EnsureSolution(entity.Owner,
                entity.Comp.SolutionName,
                out var solutionCum))
            return;

        solutionCum.MaxVolume = entity.Comp.MaxVolume;

        solutionCum.AddReagent(entity.Comp.ReagentId, entity.Comp.MaxVolume - solutionCum.Volume);
    }

    private void OnComponentInitMilk(Entity<MilkProducerComponent> entity, ref ComponentStartup args)
    {
        if (!_solutionContainer.EnsureSolution(entity.Owner,
                entity.Comp.SolutionName,
                out var solutionMilk))
            return;

        solutionMilk.MaxVolume = entity.Comp.MaxVolume;

        solutionMilk.AddReagent(entity.Comp.ReagentId, entity.Comp.MaxVolume - solutionMilk.Volume);
    }

    //private void OnComponentInitSquirt(Entity<SquirtProducerComponent> entity, ref ComponentStartup args) //Unused-Trait is WIP
    //{
    //    var solutionSquirt = _solutionContainer.EnsureSolution(entity.Owner, entity.Comp.SolutionName);
    //    solutionSquirt.MaxVolume = entity.Comp.MaxVolume;

    //    solutionSquirt.AddReagent(entity.Comp.ReagentId, entity.Comp.MaxVolume - solutionSquirt.Volume);
    //}

    private void OnComponentInitPiss(Entity<PissProducerComponent> entity, ref ComponentStartup args)
    {
        if (!_solutionContainer.EnsureSolution(entity.Owner,
                entity.Comp.SolutionName,
                out var solutionPiss))
            return;

        solutionPiss.MaxVolume = entity.Comp.MaxVolume;

        solutionPiss.AddReagent(entity.Comp.ReagentId, entity.Comp.MaxVolume - solutionPiss.Volume);
    }

    public void AddCumVerb(Entity<CumProducerComponent> entity, ref GetVerbsEvent<InnateVerb> args)
    {
        if (args.Using == null ||
             !args.CanInteract ||
             args.User != args.Target ||
             !EntityManager.HasComponent<RefillableSolutionComponent>(args.Using.Value)) //see if removing this part lets you milk on the ground.
            return;

        _solutionContainer.EnsureSolution(entity.Owner, entity.Comp.SolutionName, out _);

        var user = args.User;
        var used = args.Using.Value;

        InnateVerb verbCum = new()
        {
            Act = () => AttemptCum(entity, user, used),
            Text = Loc.GetString($"cum-verb-get-text"),
            Priority = 1
        };
        args.Verbs.Add(verbCum);
    }

    // Combined handler for RefillableSolutionComponent verbs (cum and piss)
    public void AddRefillableInsideVerbs(EntityUid uid, RefillableSolutionComponent component, GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanInteract)
            return;

        var user = args.User;
        var target = uid;

        // Add cum verb if user has CumProducerComponent
        if (TryComp<CumProducerComponent>(args.User, out var cumProducer))
        {
            _solutionContainer.EnsureSolution(args.User, cumProducer.SolutionName, out _);

            AlternativeVerb verbCumInside = new()
            {
                Act = () => AttemptCum((args.User, cumProducer), user, target),
                Text = Loc.GetString("cum-verb-inside-text"),
                Priority = -50 // HardLight: 2<-50; Should never happen as an alt+click verb unless absolutely no other alt-click verbs are available.
            };
            args.Verbs.Add(verbCumInside);
        }

        // Add piss verb if user has PissProducerComponent
        if (TryComp<PissProducerComponent>(args.User, out var pissProducer))
        {
            _solutionContainer.EnsureSolution(args.User, pissProducer.SolutionName, out _);

            AlternativeVerb verbPissInside = new()
            {
                Act = () => AttemptPiss((args.User, pissProducer), user, target),
                Text = Loc.GetString("piss-verb-inside-text"),
                Priority = -50 // HardLight: 2<-50; Should never happen as an alt+click verb unless absolutely no other alt-click verbs are available.
            };
            args.Verbs.Add(verbPissInside);
        }
    }

    // Combined handler for InjectableSolutionComponent verbs (cum and piss)
    public void AddInjectableInsideVerbs(EntityUid uid, InjectableSolutionComponent component, GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanInteract)
            return;

        var user = args.User;
        var target = uid;

        // Add cum verb if user has CumProducerComponent
        if (TryComp<CumProducerComponent>(args.User, out var cumProducer))
        {
            _solutionContainer.EnsureSolution(args.User, cumProducer.SolutionName, out _);

            AlternativeVerb verbCumInside = new()
            {
                Act = () => AttemptCum((args.User, cumProducer), user, target),
                Text = Loc.GetString("cum-verb-inside-text"),
                Priority = -50 // HardLight: 2<-50; Should never happen as an alt+click verb unless absolutely no other alt-click verbs are available.
            };
            args.Verbs.Add(verbCumInside);
        }

        // Add piss verb if user has PissProducerComponent
        if (TryComp<PissProducerComponent>(args.User, out var pissProducer))
        {
            _solutionContainer.EnsureSolution(args.User, pissProducer.SolutionName, out _);

            AlternativeVerb verbPissInside = new()
            {
                Act = () => AttemptPiss((args.User, pissProducer), user, target),
                Text = Loc.GetString("piss-verb-inside-text"),
                Priority = -50 // HardLight: 2<-50; Should never happen as an alt+click verb unless absolutely no other alt-click verbs are available.
            };
            args.Verbs.Add(verbPissInside);
        }
    }

    public void AddMilkVerb(Entity<MilkProducerComponent> entity, ref GetVerbsEvent<InnateVerb> args)
    {
        if (args.Using == null ||
             !args.CanInteract ||
             args.User != args.Target ||
             !EntityManager.HasComponent<RefillableSolutionComponent>(args.Using.Value)) //see if removing this part lets you milk on the ground.
            return;

        _solutionContainer.EnsureSolution(entity.Owner, entity.Comp.SolutionName, out _);

        var user = args.User;
        var used = args.Using.Value;

        InnateVerb verbMilk = new()
        {
            Act = () => AttemptMilk(entity, user, used),
            Text = Loc.GetString($"milk-verb-get-text"),
            Priority = 1
        };
        args.Verbs.Add(verbMilk);
    }

    //public void AddSquirtVerb(Entity<SquirtProducerComponent> entity, ref GetVerbsEvent<InnateVerb> args) //Unused-Trait is WIP
    //{
    //    if (args.Using == null ||
    //         !args.CanInteract ||
    //         !EntityManager.HasComponent<RefillableSolutionComponent>(args.Using.Value)) //see if removing this part lets you milk on the ground.
    //        return;

    //    _solutionContainer.EnsureSolution(entity.Owner, entity.Comp.SolutionName);

    //    var user = args.User;
    //    var used = args.Using.Value;

    //    InnateVerb verbSquirt = new()
    //    {
    //        Act = () => AttemptSquirt(entity, user, used),
    //        Text = Loc.GetString($"squirt-verb-get-text"),
    //        Priority = 1
    //    };
    //    args.Verbs.Add(verbSquirt);
    //}

    // Note: AddPissInsideVerb and AddPissInsideInjectableVerb have been combined into
    // AddRefillableInsideVerbs and AddInjectableInsideVerbs above
    private void OnDoAfterCum(Entity<CumProducerComponent> entity, ref CummingDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Args.Used == null)
            return;

        if (!_solutionContainer.ResolveSolution(entity.Owner, entity.Comp.SolutionName, ref entity.Comp.Solution, out var solution))
            return;

        // Try refillable solution first (containers like beakers)
        if (_solutionContainer.TryGetRefillableSolution(args.Args.Used.Value, out var targetSoln, out var targetSolution))
        {
            args.Handled = true;
            var quantity = solution.Volume;
            if (quantity == 0)
            {
                _popupSystem.PopupEntity(Loc.GetString("cum-verb-dry"), entity.Owner, args.Args.User);
                return;
            }

            if (quantity > targetSolution.AvailableVolume)
                quantity = targetSolution.AvailableVolume;

            var split = _solutionContainer.SplitSolution(entity.Comp.Solution.Value, quantity);
            _solutionContainer.TryAddSolution(targetSoln.Value, split);
            _popupSystem.PopupEntity(Loc.GetString("cum-verb-success", ("amount", quantity), ("target", Identity.Entity(args.Args.Used.Value, EntityManager))), entity.Owner, args.Args.User, PopupType.Medium);
            return;
        }

        // Try injectable solution (entities like players with stomachs)
        if (_solutionContainer.TryGetInjectableSolution(args.Args.Used.Value, out var injectSoln, out var injectSolution))
        {
            args.Handled = true;
            var quantity = solution.Volume;
            if (quantity == 0)
            {
                _popupSystem.PopupEntity(Loc.GetString("cum-verb-dry"), entity.Owner, args.Args.User);
                return;
            }

            if (quantity > injectSolution.AvailableVolume)
                quantity = injectSolution.AvailableVolume;

            var split = _solutionContainer.SplitSolution(entity.Comp.Solution.Value, quantity);
            _solutionContainer.TryAddSolution(injectSoln.Value, split);
            _popupSystem.PopupEntity(Loc.GetString("cum-verb-success", ("amount", quantity), ("target", Identity.Entity(args.Args.Used.Value, EntityManager))), entity.Owner, args.Args.User, PopupType.Medium);
            return;
        }
    }

    private void OnDoAfterMilk(Entity<MilkProducerComponent> entity, ref MilkingDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Args.Used == null)
            return;

        if (!_solutionContainer.ResolveSolution(entity.Owner, entity.Comp.SolutionName, ref entity.Comp.Solution, out var solution))
            return;

        if (!_solutionContainer.TryGetRefillableSolution(args.Args.Used.Value, out var targetSoln, out var targetSolution))
            return;

        args.Handled = true;
        var quantity = solution.Volume;
        if (quantity == 0)
        {
            _popupSystem.PopupEntity(Loc.GetString("milk-verb-dry"), entity.Owner, args.Args.User);
            return;
        }

        if (quantity > targetSolution.AvailableVolume)
            quantity = targetSolution.AvailableVolume;

        var split = _solutionContainer.SplitSolution(entity.Comp.Solution.Value, quantity);
        _solutionContainer.TryAddSolution(targetSoln.Value, split);
        _popupSystem.PopupEntity(Loc.GetString("milk-verb-success", ("amount", quantity), ("target", Identity.Entity(args.Args.Used.Value, EntityManager))), entity.Owner, args.Args.User, PopupType.Medium);
    }

    //private void OnDoAfterSquirt(Entity<SquirtProducerComponent> entity, ref SquirtingDoAfterEvent args) //Unused-Trait is WIP
    //{
    //    if (args.Cancelled || args.Handled || args.Args.Used == null)
    //        return;

    //    if (!_solutionContainer.ResolveSolution(entity.Owner, entity.Comp.SolutionName, ref entity.Comp.Solution, out var solution))
    //        return;

    //    if (!_solutionContainer.TryGetRefillableSolution(args.Args.Used.Value, out var targetSoln, out var targetSolution))
    //        return;

    //    args.Handled = true;
    //    var quantity = solution.Volume;
    //    if (quantity == 0)
    //    {
    //        _popupSystem.PopupEntity(Loc.GetString("squirt-verb-dry"), entity.Owner, args.Args.User);
    //        return;
    //    }

    //    if (quantity > targetSolution.AvailableVolume)
    //        quantity = targetSolution.AvailableVolume;

    //    var split = _solutionContainer.SplitSolution(entity.Comp.Solution.Value, quantity);
    //    _solutionContainer.TryAddSolution(targetSoln.Value, split);
    //    _popupSystem.PopupEntity(Loc.GetString("squirt-verb-success", ("amount", quantity), ("target", Identity.Entity(args.Args.Used.Value, EntityManager))), entity.Owner, args.Args.User, PopupType.Medium);
    //}

    private void OnDoAfterPiss(Entity<PissProducerComponent> entity, ref PissingDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Args.Used == null)
            return;

        if (!_solutionContainer.ResolveSolution(entity.Owner, entity.Comp.SolutionName, ref entity.Comp.Solution, out var solution))
            return;

        // Try refillable solution first (containers like beakers)
        if (_solutionContainer.TryGetRefillableSolution(args.Args.Used.Value, out var targetSoln, out var targetSolution))
        {
            args.Handled = true;
            var quantity = solution.Volume;
            if (quantity == 0)
            {
                _popupSystem.PopupEntity(Loc.GetString("piss-verb-dry"), entity.Owner, args.Args.User);
                return;
            }

            if (quantity > targetSolution.AvailableVolume)
                quantity = targetSolution.AvailableVolume;

            var split = _solutionContainer.SplitSolution(entity.Comp.Solution.Value, quantity);
            _solutionContainer.TryAddSolution(targetSoln.Value, split);
            _popupSystem.PopupEntity(Loc.GetString("piss-verb-success", ("amount", quantity), ("target", Identity.Entity(args.Args.Used.Value, EntityManager))), entity.Owner, args.Args.User, PopupType.Medium);
            return;
        }

        // Try injectable solution (entities like players with stomachs)
        if (_solutionContainer.TryGetInjectableSolution(args.Args.Used.Value, out var injectSoln, out var injectSolution))
        {
            args.Handled = true;
            var quantity = solution.Volume;
            if (quantity == 0)
            {
                _popupSystem.PopupEntity(Loc.GetString("piss-verb-dry"), entity.Owner, args.Args.User);
                return;
            }

            if (quantity > injectSolution.AvailableVolume)
                quantity = injectSolution.AvailableVolume;

            var split = _solutionContainer.SplitSolution(entity.Comp.Solution.Value, quantity);
            _solutionContainer.TryAddSolution(injectSoln.Value, split);
            _popupSystem.PopupEntity(Loc.GetString("piss-verb-success", ("amount", quantity), ("target", Identity.Entity(args.Args.Used.Value, EntityManager))), entity.Owner, args.Args.User, PopupType.Medium);
            return;
        }
    }
    #endregion

    #region utilities
    private void AttemptCum(Entity<CumProducerComponent> lewd, EntityUid userUid, EntityUid containerUid)
    {
        if (!HasComp<CumProducerComponent>(userUid))
            return;

        var doargs = new DoAfterArgs(EntityManager, userUid, 5, new CummingDoAfterEvent(), lewd, lewd, used: containerUid)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            MovementThreshold = 1.0f,
        };

        _doAfterSystem.TryStartDoAfter(doargs);
    }

    private void AttemptMilk(Entity<MilkProducerComponent> lewd, EntityUid userUid, EntityUid containerUid)
    {
        if (!HasComp<MilkProducerComponent>(userUid))
            return;

        var doargs = new DoAfterArgs(EntityManager, userUid, 5, new MilkingDoAfterEvent(), lewd, lewd, used: containerUid)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            MovementThreshold = 1.0f,
        };

        _doAfterSystem.TryStartDoAfter(doargs);
    }

    //private void AttemptSquirt(Entity<SquirtProducerComponent> lewd, EntityUid userUid, EntityUid containerUid) //Unused-Trait is WIP
    //{
    //    if (!HasComp<SquirtProducerComponent>(userUid))
    //        return;

    //    var doargs = new DoAfterArgs(EntityManager, userUid, 5, new SquirtingDoAfterEvent(), lewd, lewd, used: containerUid)
    //    {
    //        BreakOnUserMove = true,
    //        BreakOnDamage = true,
    //        BreakOnTargetMove = true,
    //        MovementThreshold = 1.0f,
    //    };

    //    _doAfterSystem.TryStartDoAfter(doargs);
    //}

    private void AttemptPiss(Entity<PissProducerComponent> lewd, EntityUid userUid, EntityUid containerUid)
    {
        if (!HasComp<PissProducerComponent>(userUid))
            return;

        var doargs = new DoAfterArgs(EntityManager, userUid, 5, new PissingDoAfterEvent(), lewd, lewd, used: containerUid)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            MovementThreshold = 1.0f,
        };

        _doAfterSystem.TryStartDoAfter(doargs);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var queryCum = EntityQueryEnumerator<CumProducerComponent>();
        var queryPiss = EntityQueryEnumerator<PissProducerComponent>();
        var queryMilk = EntityQueryEnumerator<MilkProducerComponent>();
        var now = _timing.CurTime;

        while (queryCum.MoveNext(out var uid, out var containerCum))
        {
            if (now < containerCum.NextGrowth)
                continue;

            containerCum.NextGrowth = now + containerCum.GrowthDelay;

            if (_mobState.IsDead(uid))
                continue;

            if (EntityManager.TryGetComponent(uid, out HungerComponent? hunger))
            {
                if (_hunger.GetHungerThreshold(hunger) < HungerThreshold.Okay)
                    continue;

                _hunger.ModifyHunger(uid, -containerCum.HungerUsage, hunger);
            }

            if (!_solutionContainer.ResolveSolution(uid, containerCum.SolutionName, ref containerCum.Solution))
                continue;

            _solutionContainer.TryAddReagent(containerCum.Solution.Value, containerCum.ReagentId, containerCum.QuantityPerUpdate, out _);
        }

        while (queryMilk.MoveNext(out var uid, out var containerMilk))
        {
            if (now < containerMilk.NextGrowth)
                continue;

            containerMilk.NextGrowth = now + containerMilk.GrowthDelay;

            if (_mobState.IsDead(uid))
                continue;

            if (EntityManager.TryGetComponent(uid, out HungerComponent? hunger))
            {
                if (_hunger.GetHungerThreshold(hunger) < HungerThreshold.Okay)
                    continue;

                _hunger.ModifyHunger(uid, -containerMilk.HungerUsage, hunger);
            }

            if (!_solutionContainer.ResolveSolution(uid, containerMilk.SolutionName, ref containerMilk.Solution))
                continue;

            _solutionContainer.TryAddReagent(containerMilk.Solution.Value, containerMilk.ReagentId, containerMilk.QuantityPerUpdate, out _);
        }

        while (queryPiss.MoveNext(out var uid, out var containerPiss))
        {
            if (now < containerPiss.NextGrowth)
                continue;

            containerPiss.NextGrowth = now + containerPiss.GrowthDelay;

            if (_mobState.IsDead(uid))
                continue;

            if (EntityManager.TryGetComponent(uid, out HungerComponent? hunger))
            {
                if (_hunger.GetHungerThreshold(hunger) < HungerThreshold.Okay)
                    continue;

                _hunger.ModifyHunger(uid, -containerPiss.HungerUsage, hunger);
            }

            if (!_solutionContainer.ResolveSolution(uid, containerPiss.SolutionName, ref containerPiss.Solution))
                continue;

            _solutionContainer.TryAddReagent(containerPiss.Solution.Value, containerPiss.ReagentId, containerPiss.QuantityPerUpdate, out _);
        }

        //if (!(now < containerSquirt.NextGrowth)) //Unused-Trait is WIP
        //{
        //    containerSquirt.NextGrowth = now + containerSquirt.GrowthDelay;

        //
        //    if (EntityManager.TryGetComponent(uid, out HungerComponent? hunger))
        //    {
        //
        //        if (!(_hunger.GetHungerThreshold(hunger) < HungerThreshold.Okay))
        //            _hunger.ModifyHunger(uid, -containerSquirt.HungerUsage, hunger);
        //    }

        //    if (_solutionContainer.ResolveSolution(uid, containerSquirt.SolutionName, ref containerSquirt.Solution))
        //        _solutionContainer.TryAddReagent(containerSquirt.Solution.Value, containerSquirt.ReagentId, containerSquirt.QuantityPerUpdate, out _);
        //}
    }
    #endregion
}
