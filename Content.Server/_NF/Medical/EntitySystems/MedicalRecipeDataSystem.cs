using System.Linq;
using Content.Client.Chemistry.EntitySystems;
using Content.Server.Medical.Components;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Damage;
using Content.Shared.Kitchen;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server._NF.Medical.EntitySystems;

public sealed class MedicalRecipeDataSystem : SharedMedicalGuideDataSystem
{
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IPrototypeManager _protoMan = default!;

    private Dictionary<string, List<MedicalRecipeData>> _sources = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);
        _player.PlayerStatusChanged += OnPlayerStatusChanged;

        ReloadRecipes();
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (!args.WasModified<EntityPrototype>()
            && !args.WasModified<FoodRecipePrototype>()
        )
            return;

        ReloadRecipes();
    }

    public void ReloadRecipes()
    {
        _sources.Clear();

        // Recipes
        foreach (var recipe in _protoMan.EnumeratePrototypes<FoodRecipePrototype>())
        {
            if (recipe.HideInGuidebook)
                continue;

            MicrowaveRecipeType recipeType = (MicrowaveRecipeType)recipe.RecipeType;
            if (recipeType.HasFlag(MicrowaveRecipeType.MedicalAssembler))
            {
                _sources.GetOrNew(recipe.Result).Add(new MedicalRecipeData(recipe));
            }
        }

        Registry.Clear();

        foreach (var (result, sources) in _sources)
        {
            var proto = _protoMan.Index<EntityPrototype>(result);
            ReagentQuantity[] reagents = [];
            // Hack: assume there is only one solution in the result
            if (proto.TryGetComponent<SolutionContainerManagerComponent>(out var manager, Factory))
                reagents = manager?.Solutions?.FirstOrNull()?.Value?.Contents?.ToArray() ?? [];

            DamageSpecifier? damage = null;
            if (proto.TryGetComponent<HealingComponent>(out var healing, Factory))
                damage = healing.Damage;

            // Limit the number of sources to 10 - shouldn't be an issue for medical recipes, but just in case.
            var distinctSources = sources.DistinctBy(it => it.Identitier).Take(10);

            var entry = new MedicalGuideEntry(result, proto.Name, distinctSources.ToArray(), reagents, damage);
            Registry.Add(entry);
        }

        RaiseNetworkEvent(new MedicalGuideRegistryChangedEvent(Registry));
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        if (args.NewStatus != SessionStatus.Connected)
            return;

        RaiseNetworkEvent(new MedicalGuideRegistryChangedEvent(Registry), args.Session);
    }
}
