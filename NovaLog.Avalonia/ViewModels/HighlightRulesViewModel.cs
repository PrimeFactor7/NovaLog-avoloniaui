using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovaLog.Core.Models;

namespace NovaLog.Avalonia.ViewModels;

public partial class HighlightRulesViewModel : ObservableObject
{
    public ObservableCollection<HighlightRule> Rules { get; }

    [ObservableProperty] private HighlightRule? _selectedRule;
    [ObservableProperty] private string _pattern = "";
    [ObservableProperty] private string _foregroundHex = "#FFFF00";
    [ObservableProperty] private string? _backgroundHex;
    [ObservableProperty] private bool _isBackgroundEnabled;
    [ObservableProperty] private int _ruleTypeIndex; // 0=Match, 1=Line

    public HighlightRulesViewModel(IEnumerable<HighlightRule> initialRules)
    {
        Rules = new ObservableCollection<HighlightRule>(initialRules);
    }

    partial void OnSelectedRuleChanged(HighlightRule? value)
    {
        if (value != null)
        {
            Pattern = value.Pattern;
            ForegroundHex = value.ForegroundHex;
            BackgroundHex = value.BackgroundHex;
            IsBackgroundEnabled = value.BackgroundHex != null;
            RuleTypeIndex = value.RuleType == HighlightRuleType.LineHighlight ? 1 : 0;
        }
    }

    [RelayCommand]
    private void AddOrUpdate()
    {
        if (string.IsNullOrWhiteSpace(Pattern)) return;

        var rule = Rules.FirstOrDefault(r => r.Pattern == Pattern);
        if (rule == null)
        {
            rule = new HighlightRule { Pattern = Pattern };
            Rules.Add(rule);
        }

        rule.ForegroundHex = ForegroundHex;
        rule.BackgroundHex = IsBackgroundEnabled ? BackgroundHex ?? "#30FFFF00" : null;
        rule.RuleType = RuleTypeIndex == 1 ? HighlightRuleType.LineHighlight : HighlightRuleType.MatchHighlight;
        rule.Invalidate();
        
        // Refresh UI list
        var idx = Rules.IndexOf(rule);
        Rules[idx] = rule;
        SelectedRule = rule;
    }

    [RelayCommand]
    private void Remove()
    {
        if (SelectedRule != null)
        {
            Rules.Remove(SelectedRule);
            SelectedRule = null;
        }
    }
}
