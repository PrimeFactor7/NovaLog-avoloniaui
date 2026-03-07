using NovaLog.Avalonia.ViewModels;
using NovaLog.Core.Models;

namespace NovaLog.Tests.ViewModels;

public class SourceManagerPersistenceTests
{
    [Fact]
    public void CreateSnapshot_RestoreSources_PreservesMergeTreeAndAliases()
    {
        var vm = new SourceManagerViewModel();
        vm.AddSource(@"C:\logs\alpha.log", SourceKind.File, "alpha");
        vm.AddSource(@"C:\logs\beta.log", SourceKind.File, "beta");

        var alpha = vm.Sources.Single(s => s.SourceId == "alpha");
        var beta = vm.Sources.Single(s => s.SourceId == "beta");
        alpha.DisplayName = "Alpha Alias";
        alpha.IsSelectedForMerge = true;
        beta.IsSelectedForMerge = true;

        vm.MergeSelectedCommand.Execute(null);

        var merge = vm.Sources.Single(s => s.Kind == SourceKind.Merge);
        merge.DisplayName = "Merged Pair";

        var snapshot = vm.CreateSnapshot();

        var restored = new SourceManagerViewModel();
        restored.RestoreSources(snapshot);

        Assert.Equal(3, restored.Sources.Count);

        var restoredMerge = restored.Sources.Single(s => s.Kind == SourceKind.Merge);
        Assert.Equal("Merged Pair", restoredMerge.DisplayName);
        Assert.Equal(2, restoredMerge.ChildSourceIds.Count);
        Assert.True(restoredMerge.IsExpanded);

        var restoredAlpha = restored.Sources.Single(s => s.SourceId == "alpha");
        Assert.Equal("Alpha Alias", restoredAlpha.DisplayName);
        Assert.True(restoredAlpha.IsChild);

        Assert.Equal(3, restored.DisplaySources.Count);
        Assert.Equal(SourceKind.Merge, restored.DisplaySources[0].Kind);
    }

    [Fact]
    public void RemoveSelected_MergeNode_RestoresChildrenToTopLevel()
    {
        var vm = new SourceManagerViewModel();
        vm.AddSource(@"C:\logs\alpha.log", SourceKind.File, "alpha");
        vm.AddSource(@"C:\logs\beta.log", SourceKind.File, "beta");

        vm.Sources.Single(s => s.SourceId == "alpha").IsSelectedForMerge = true;
        vm.Sources.Single(s => s.SourceId == "beta").IsSelectedForMerge = true;
        vm.MergeSelectedCommand.Execute(null);

        var merge = vm.Sources.Single(s => s.Kind == SourceKind.Merge);
        vm.SelectedSource = merge;

        vm.RemoveSelectedCommand.Execute(null);

        Assert.DoesNotContain(vm.Sources, s => s.Kind == SourceKind.Merge);
        Assert.All(vm.Sources, s => Assert.False(s.IsChild));
        Assert.Equal(2, vm.DisplaySources.Count);
    }
}
