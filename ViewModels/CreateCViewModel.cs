using Avalonia.Controls;
using Dujahit.Models.Application;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using Dujahit.Models;

namespace Dujahit.ViewModels
{
    public class CreateCViewModel : ViewModelBase
    {
        private MainWindowViewModel _mainWVM;
        private  IFileDialogService? _fileDialogService;

        private string _campaignName;

        public string CampaignName 
        {
            get => _campaignName;
            set => this.RaiseAndSetIfChanged(ref _campaignName, value);
        }

        private string _campaignPort;

        public string CampaignPort
        {
            get => _campaignPort;
            set => this.RaiseAndSetIfChanged(ref _campaignPort, value);
        }

        private string? _selectedTemplatePath;
        public string? SelectedTemplatePath
        {
            get => _selectedTemplatePath;
            set => this.RaiseAndSetIfChanged(ref _selectedTemplatePath, value);
        }

        public ICommand CreateCampaign {  get; set; }
        public ICommand ChooseTemplate { get; set; }

        public ICommand Back { get; set; }

        public CreateCViewModel(MainWindowViewModel mwvm, string campaignName) 
        {
            _mainWVM = mwvm;
            CampaignName = campaignName;

            CreateCampaign = ReactiveCommand.CreateFromTask(CreateCampaignAsync);
            ChooseTemplate = ReactiveCommand.CreateFromTask(SearchForFile);
            Back = ReactiveCommand.Create(BackProcess);
            UseBundledTemplateIfPresent();
        }

        public void BackProcess()
        {
            _mainWVM.NavigateTo(new StartDashViewModel(_mainWVM));
        }

        public async Task CreateCampaignAsync()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(SelectedTemplatePath))
                {
                    Debug.WriteLine("No template selected.");
                    return;
                }

                if (string.IsNullOrWhiteSpace(CampaignName))
                {
                    Debug.WriteLine("Campaign name is required.");
                    return;
                }

                var localTemplatePath = SelectedTemplatePath;
                var localName = CampaignName;
                var localPort = string.IsNullOrWhiteSpace(CampaignPort) ? "5555" : CampaignPort;

                await _mainWVM.ShowLoadingAndEnterAsync($"Creating {localName}", async loading =>
                {
                    loading.Report("Reading the rulebook", 0.10);
                    await Task.Run(() => App.PM.LoadTemplateAsync(localTemplatePath));

                    var templateId = App.PM.CurrentTemplateId;
                    if (string.IsNullOrEmpty(templateId))
                    {
                        loading.Fail("That template failed to load");
                        return (UserRole?)null;
                    }

                    if (!int.TryParse(localPort, out var portNum) || portNum < 1 || portNum > 65535)
                    {
                        loading.Fail("Pick a port between 1 and 65535");
                        return (UserRole?)null;
                    }

                    await Task.Run(() => App.PM.CreateNewCampaign(localName, templateId, localPort, loading.Report));

                    // Creator hosts, so DM for now. A spectator creating on behalf of others is a future case.
                    return (UserRole?)UserRole.Dm;
                });
            }
            catch (Exception ex)
            {
                ErrorLog.Log($"Failed to create campaign", ex);
            }
        }

        public void SetFileDialogService(IFileDialogService service)
        {
            _fileDialogService = service;
        }

        // Nobody should have to go and find the rulebook on a fresh install, so the one next to the exe is already chosen.
        public void UseBundledTemplateIfPresent()
        {
            if (!string.IsNullOrWhiteSpace(SelectedTemplatePath)) return;
            var bundled = ProgramManager.BundledTemplatePath();
            if (bundled != null) SelectedTemplatePath = bundled;
        }

        public async Task SearchForFile() 
        {
            try
            {
                if (_fileDialogService is null)
                {
                    Debug.WriteLine("File dialog service not set.");
                    return;
                }

                var path = await _fileDialogService.PickFileAsync("Choose Template");
                if (path is null) return;

                SelectedTemplatePath = path;
                Debug.WriteLine(SelectedTemplatePath);
            }
            catch (Exception ex)
            {
                ErrorLog.Log($"Failed to pick file", ex);
            }
        }
    }
}
