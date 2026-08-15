using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Relay.Core.Interfaces;
using Relay.Core.Models;

namespace Relay.UI.ViewModels;

public partial class HistoryViewModel : ObservableObject
{
    private readonly IHistoryRepository _historyRepository;

    [ObservableProperty]
    private string _searchFilter = string.Empty;

    [ObservableProperty]
    private HistoryItem? _selectedItem;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private ObservableCollection<HistoryItem> _items = new();

    public bool HasItems => Items.Count > 0;

    public HistoryViewModel(IHistoryRepository historyRepository)
    {
        _historyRepository = historyRepository;
    }

    public async Task LoadHistoryAsync()
    {
        IsLoading = true;
        try
        {
            var list = await _historyRepository.GetHistoryAsync(100, SearchFilter);
            Items.Clear();
            foreach (var item in list)
            {
                Items.Add(item);
            }

            OnPropertyChanged(nameof(HasItems));

            if (Items.Count > 0)
            {
                if (SelectedItem == null || !Items.Any(i => i.Id == SelectedItem.Id))
                {
                    SelectedItem = Items[0];
                }
            }
            else
            {
                SelectedItem = null;
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task SearchAsync()
    {
        await LoadHistoryAsync();
    }

    [RelayCommand]
    public async Task ClearSearchAsync()
    {
        SearchFilter = string.Empty;
        await LoadHistoryAsync();
    }

    [RelayCommand]
    public async Task DeleteItemAsync(HistoryItem item)
    {
        if (item == null) return;

        await _historyRepository.DeleteHistoryItemAsync(item.Id);
        Items.Remove(item);
        if (SelectedItem == item)
        {
            SelectedItem = Items.FirstOrDefault();
        }
    }

    [RelayCommand]
    public async Task ClearAllAsync()
    {
        var result = MessageBox.Show("Are you sure you want to clear all analysis history?", "Clear History", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result == MessageBoxResult.Yes)
        {
            await _historyRepository.ClearAllHistoryAsync();
            Items.Clear();
            SelectedItem = null;
        }
    }

    [RelayCommand]
    public void CopySelectedContent()
    {
        if (SelectedItem != null && !string.IsNullOrEmpty(SelectedItem.MarkdownResponse))
        {
            try
            {
                Clipboard.SetText(SelectedItem.MarkdownResponse);
            }
            catch { }
        }
    }
}
