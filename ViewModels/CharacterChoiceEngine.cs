using Dujahit.Models.Application;
using Dujahit.Models.Database;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Dujahit.ViewModels
{
    public static class CharacterChoiceEngine
    {
        public static List<ChoiceGroupViewModel> BuildGroups(IEnumerable<ResolvedClassChoice> choices)
        {
            var groups = new List<ChoiceGroupViewModel>();
            foreach (var c in choices)
                groups.Add(new ChoiceGroupViewModel(c));
            return groups;
        }

        public static bool AllSatisfied(IEnumerable<ChoiceGroupViewModel> groups) =>
            groups.All(g => g.IsSatisfied);

        public static IEnumerable<ChoiceGroupViewModel> OfStore(IEnumerable<ChoiceGroupViewModel> groups, string storeAs) =>
            groups.Where(g => g.StoreAs == storeAs);
    }

    public class ChoiceGroupViewModel : ReactiveObject
    {
        public string Id { get; }
        public string Kind { get; }
        public string StoreAs { get; }
        public int Level { get; }
        public int ChooseCount { get; }
        public string Label { get; }
        public string Description { get; }

        public ObservableCollection<ChoiceOptionViewModel> Options { get; } = new();

        public ChoiceGroupViewModel(ResolvedClassChoice c)
        {
            Id = c.Id;
            Kind = c.Kind;
            StoreAs = c.StoreAs;
            Level = c.Level;
            ChooseCount = c.ChooseCount;
            Label = c.Label;
            Description = c.Description;

            foreach (var o in c.Options)
                Options.Add(new ChoiceOptionViewModel(o.Id, o.Name, OnOptionToggled, o.Description));
        }

        public int SelectedCount => Options.Count(o => o.IsSelected);
        public int EffectiveChooseCount => Math.Min(ChooseCount, Options.Count);
        public bool IsSatisfied => SelectedCount == EffectiveChooseCount;
        public string CounterLabel => $"{SelectedCount} / {EffectiveChooseCount} selected";

        public IEnumerable<ChoiceOptionViewModel> Selected => Options.Where(o => o.IsSelected);

        public void ResetOptions(IEnumerable<ChoiceOption> options)
        {
            var keep = new HashSet<string>(Selected.Select(o => o.Name), StringComparer.OrdinalIgnoreCase);
            Options.Clear();
            foreach (var o in options)
                Options.Add(new ChoiceOptionViewModel(o.Id, o.Name, OnOptionToggled, o.Description));
            foreach (var o in Options)
                if (keep.Contains(o.Name)) o.SetSelectedQuiet(true);
            this.RaisePropertyChanged(nameof(SelectedCount));
            this.RaisePropertyChanged(nameof(EffectiveChooseCount));
            this.RaisePropertyChanged(nameof(IsSatisfied));
            this.RaisePropertyChanged(nameof(CounterLabel));
        }

        private void OnOptionToggled(ChoiceOptionViewModel toggled)
        {
            if (ChooseCount == 1 && toggled.IsSelected)
            {
                foreach (var o in Options)
                    if (!ReferenceEquals(o, toggled) && o.IsSelected)
                        o.SetSelectedQuiet(false);
            }
            else if (toggled.IsSelected && SelectedCount > ChooseCount)
            {
                toggled.SetSelectedQuiet(false);
            }

            this.RaisePropertyChanged(nameof(SelectedCount));
            this.RaisePropertyChanged(nameof(IsSatisfied));
            this.RaisePropertyChanged(nameof(CounterLabel));
        }
    }

    public class ChoiceOptionViewModel : ReactiveObject
    {
        private readonly Action<ChoiceOptionViewModel> _onToggled;

        public string Id { get; }
        public string Name { get; }
        public string Description { get; }
        public string? TooltipText => string.IsNullOrWhiteSpace(Description) ? null : Description;

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                this.RaiseAndSetIfChanged(ref _isSelected, value);
                _onToggled?.Invoke(this);
            }
        }

        public ChoiceOptionViewModel(string id, string name, Action<ChoiceOptionViewModel> onToggled, string description = "")
        {
            Id = id;
            Name = name;
            Description = description;
            _onToggled = onToggled;
        }

        public void SetSelectedQuiet(bool value)
        {
            this.RaiseAndSetIfChanged(ref _isSelected, value, nameof(IsSelected));
        }
    }
}
