using Dujahit.Models;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;

namespace Dujahit.ViewModels
{
    public class GlobalSearchViewModel : ViewModelBase
    {
        private CancellationTokenSource? _searchCts;

        public bool IsDm { get; set; } = true;
        public string UserId { get; set; } = "";

        public ObservableCollection<SearchResultRow> Results { get; } = new();

        private string _query = "";
        public string Query
        {
            get => _query;
            set { this.RaiseAndSetIfChanged(ref _query, value); _ = RunSearchAsync(); }
        }

        public bool HasResults => Results.Count > 0;
        public bool ShowPrompt => string.IsNullOrWhiteSpace(Query);
        public bool ShowEmptyHint => !string.IsNullOrWhiteSpace(Query) && Results.Count == 0;
        public SearchResultRow? FirstResult => Results.Count > 0 ? Results[0] : null;

        public event Action<SearchResultRow>? ResultChosen;
        public ReactiveCommand<SearchResultRow, Unit> ChooseCommand { get; }

        public GlobalSearchViewModel()
        {
            ChooseCommand = ReactiveCommand.Create<SearchResultRow>(r => { if (r != null) ResultChosen?.Invoke(r); });
        }

        public void Reset()
        {
            _query = "";
            this.RaisePropertyChanged(nameof(Query));
            Results.Clear();
            RaiseStateChanged();
        }

        public void ChooseFirst()
        {
            var r = FirstResult;
            if (r != null) ResultChosen?.Invoke(r);
        }

        private async Task RunSearchAsync()
        {
            // Eleven queries a keystroke, so a stale one gets killed rather than finished and binned.
            _searchCts?.Cancel();
            _searchCts?.Dispose();
            _searchCts = new CancellationTokenSource();
            var ct = _searchCts.Token;

            var q = Query;
            if (string.IsNullOrWhiteSpace(q))
            {
                Results.Clear();
                RaiseStateChanged();
                return;
            }

            List<SearchHit> hits;
            try { hits = await App.PM.SearchCampaignAsync(q, IsDm, UserId, ct); }
            catch (OperationCanceledException) { return; }
            if (ct.IsCancellationRequested) return;

            Results.Clear();
            foreach (var h in hits) Results.Add(new SearchResultRow(h));
            RaiseStateChanged();
        }

        private void RaiseStateChanged()
        {
            this.RaisePropertyChanged(nameof(HasResults));
            this.RaisePropertyChanged(nameof(ShowPrompt));
            this.RaisePropertyChanged(nameof(ShowEmptyHint));
            this.RaisePropertyChanged(nameof(FirstResult));
        }
    }

    public class SearchResultRow
    {
        public string Type { get; }
        public string Id { get; }
        public string Title { get; }
        public string Subtitle { get; }

        public SearchResultRow(SearchHit hit)
        {
            Type = hit.Type;
            Id = hit.Id;
            Title = hit.Title;
            Subtitle = hit.Subtitle;
        }
    }
}
