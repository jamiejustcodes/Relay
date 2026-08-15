using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Relay.Core.Interfaces;
using Relay.Core.Models;

namespace Relay.UI.ViewModels;

public partial class FloatingResultViewModel : ObservableObject
{
    private readonly IAiProviderFactory _aiProviderFactory;
    private readonly ISettingsService _settingsService;
    private readonly IHistoryRepository _historyRepository;
    private readonly ISearchService _searchService;

    private CaptureRegion? _currentRegion;
    private ScreenContext? _currentContext;
    private CancellationTokenSource? _streamCts;
    private readonly List<ChatMessage> _conversationHistory = new();

    [ObservableProperty]
    private string _title = "Analyzing Selection...";

    [ObservableProperty]
    private string _summary = string.Empty;

    [ObservableProperty]
    private string _markdownContent = string.Empty;

    [ObservableProperty]
    private IntentType _detectedIntent = IntentType.General;

    [ObservableProperty]
    private string _applicationBadge = string.Empty;

    [ObservableProperty]
    private string _dimensionBadge = string.Empty;

    [ObservableProperty]
    private BitmapImage? _thumbnailImage;

    [ObservableProperty]
    private bool _isStreaming;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PinTooltip))]
    [NotifyPropertyChangedFor(nameof(PinIcon))]
    [NotifyPropertyChangedFor(nameof(PinText))]
    [NotifyPropertyChangedFor(nameof(PinBackgroundBrush))]
    [NotifyPropertyChangedFor(nameof(PinBorderBrush))]
    [NotifyPropertyChangedFor(nameof(PinForegroundBrush))]
    private bool _isPinned = true;

    public string PinTooltip => IsPinned ? "Always on Top: Active (Click to allow windows over this)" : "Always on Top: Inactive (Click to keep on top of all windows)";
    public string PinIcon => IsPinned ? "📌" : "📍";
    public string PinText => IsPinned ? "Pinned" : "Float";
    public Brush PinBackgroundBrush => IsPinned ? new SolidColorBrush(Color.FromArgb(40, 99, 102, 241)) : new SolidColorBrush(Color.FromArgb(16, 255, 255, 255));
    public Brush PinBorderBrush => IsPinned ? new SolidColorBrush(Color.FromArgb(90, 99, 102, 241)) : new SolidColorBrush(Color.FromArgb(30, 255, 255, 255));
    public Brush PinForegroundBrush => IsPinned ? new SolidColorBrush(Color.FromRgb(199, 210, 254)) : new SolidColorBrush(Color.FromRgb(148, 163, 184));

    [ObservableProperty]
    private string? _activePrompt;

    public bool HasActivePrompt => !string.IsNullOrWhiteSpace(ActivePrompt);

    [ObservableProperty]
    private string _questionInput = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private ObservableCollection<ActionItem> _actionItems = new();

    [ObservableProperty]
    private ObservableCollection<SearchResultItem> _searchResults = new();

    [ObservableProperty]
    private bool _hasSearchResults;

    public event EventHandler? CloseRequested;

    public FloatingResultViewModel(
        IAiProviderFactory aiProviderFactory,
        ISettingsService settingsService,
        IHistoryRepository historyRepository,
        ISearchService searchService)
    {
        _aiProviderFactory = aiProviderFactory;
        _settingsService = settingsService;
        _historyRepository = historyRepository;
        _searchService = searchService;
    }

    public async Task InitializeWithCaptureAsync(CaptureRegion region, ScreenContext context, string? initialQuestion = null)
    {
        _currentRegion = region;
        _currentContext = context;
        _conversationHistory.Clear();
        SearchResults.Clear();
        HasSearchResults = false;
        ActionItems.Clear();

        // 1. Setup thumbnails & badges
        if (region.ThumbnailBytes != null && region.ThumbnailBytes.Length > 0)
        {
            ThumbnailImage = LoadBitmapImage(region.ThumbnailBytes);
        }
        else if (region.ImageBytes.Length > 0)
        {
            ThumbnailImage = LoadBitmapImage(region.ImageBytes);
        }

        DimensionBadge = $"{region.Width} × {region.Height} px";
        ApplicationBadge = !string.IsNullOrEmpty(context.ApplicationName)
            ? $"{context.ApplicationName}" + (!string.IsNullOrEmpty(context.WindowTitle) ? $" • {context.WindowTitle}" : "")
            : "Screen Selection";

        // Store active prompt for UI display, and CLEAR bottom input box so it is ready for follow-ups
        ActivePrompt = !string.IsNullOrWhiteSpace(initialQuestion) ? initialQuestion.Trim() : null;
        OnPropertyChanged(nameof(HasActivePrompt));
        QuestionInput = string.Empty;

        // If user provided a prompt, record it in conversational history
        if (!string.IsNullOrWhiteSpace(initialQuestion))
        {
            _conversationHistory.Add(new ChatMessage("user", initialQuestion.Trim()));
        }

        // 2. Start streaming analysis with initial question
        await RunAnalysisAsync(initialQuestion);
    }

    [RelayCommand]
    public async Task SubmitQuestionAsync()
    {
        if (string.IsNullOrWhiteSpace(QuestionInput) || _currentRegion == null)
            return;

        string question = QuestionInput.Trim();
        QuestionInput = string.Empty;
        ActivePrompt = question;
        OnPropertyChanged(nameof(HasActivePrompt));

        // Add user question to history
        _conversationHistory.Add(new ChatMessage("user", question));

        await RunAnalysisAsync(question);
    }

    [RelayCommand]
    public async Task TriggerActionAsync(ActionItem item)
    {
        if (item == null) return;

        switch (item.ActionType.ToUpperInvariant())
        {
            case "COPY":
                string textToCopy = !string.IsNullOrEmpty(item.Payload) ? item.Payload : MarkdownContent;
                try
                {
                    Clipboard.SetText(textToCopy);
                }
                catch { }
                break;

            case "SEARCH":
                string query = !string.IsNullOrEmpty(item.Payload) ? item.Payload : Title;
                await ExecuteWebSearchAsync(query);
                break;

            case "EXPLAIN":
            case "TRANSLATE":
                QuestionInput = item.Label;
                await SubmitQuestionAsync();
                break;
        }
    }

    [RelayCommand]
    public async Task ExecuteWebSearchAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return;

        try
        {
            var results = await _searchService.SearchAsync(query);
            SearchResults.Clear();
            foreach (var res in results)
            {
                SearchResults.Add(res);
            }
            HasSearchResults = SearchResults.Count > 0;
        }
        catch
        {
            HasSearchResults = false;
        }
    }

    [RelayCommand]
    public void OpenUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch { }
    }

    [RelayCommand]
    public void CopyAllContent()
    {
        try
        {
            if (!string.IsNullOrEmpty(MarkdownContent))
            {
                Clipboard.SetText(MarkdownContent);
            }
        }
        catch { }
    }

    [RelayCommand]
    public void TogglePin()
    {
        IsPinned = !IsPinned;
    }

    [RelayCommand]
    public void Close()
    {
        _streamCts?.Cancel();
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private async Task RunAnalysisAsync(string? userQuestion)
    {
        if (_currentRegion == null) return;

        _streamCts?.Cancel();
        _streamCts = new CancellationTokenSource();
        var ct = _streamCts.Token;

        IsStreaming = true;
        HasError = false;
        ErrorMessage = string.Empty;
        Title = "Analyzing Selection...";
        Summary = string.Empty;
        MarkdownContent = string.Empty;
        ActionItems.Clear();

        var request = new AiAnalysisRequest
        {
            Region = _currentRegion,
            Context = _currentContext,
            UserQuestion = userQuestion,
            ConversationHistory = _conversationHistory.ToList(),
            Stream = true
        };

        var markdownBuffer = new System.Text.StringBuilder();

        try
        {
            var activeProvider = _aiProviderFactory.GetActiveProvider();
            await foreach (var chunk in activeProvider.AnalyzeStreamAsync(request, ct))
            {
                if (ct.IsCancellationRequested) break;

                if (!string.IsNullOrEmpty(chunk.ErrorMessage))
                {
                    HasError = true;
                    ErrorMessage = chunk.ErrorMessage;
                    Title = "Notice";
                    Summary = chunk.ErrorMessage;
                    break;
                }

                if (chunk.DetectedIntent.HasValue)
                {
                    DetectedIntent = chunk.DetectedIntent.Value;
                }

                if (!string.IsNullOrEmpty(chunk.Title))
                {
                    Title = chunk.Title;
                }

                if (!string.IsNullOrEmpty(chunk.Summary))
                {
                    Summary = chunk.Summary;
                }

                if (chunk.ActionItems != null && chunk.ActionItems.Count > 0)
                {
                    ActionItems.Clear();
                    foreach (var action in chunk.ActionItems)
                    {
                        ActionItems.Add(action);
                    }
                }

                if (!string.IsNullOrEmpty(chunk.TextDelta))
                {
                    markdownBuffer.Append(chunk.TextDelta);
                    MarkdownContent = markdownBuffer.ToString();
                }
            }

            // If intent is SHOP or SEARCH, trigger web search in background
            if (DetectedIntent == IntentType.Shop || DetectedIntent == IntentType.Search)
            {
                _ = ExecuteWebSearchAsync(Title);
            }

            // Save to history if configured
            if (!HasError && _settingsService.CurrentSettings.SaveHistory)
            {
                try
                {
                    string? thumbBase64 = null;
                    if (_currentRegion.ThumbnailBytes != null && _currentRegion.ThumbnailBytes.Length > 0)
                    {
                        thumbBase64 = Convert.ToBase64String(_currentRegion.ThumbnailBytes);
                    }

                    string resolvedTitle = !string.IsNullOrWhiteSpace(Title) && Title != "Analyzing Selection..."
                        ? Title
                        : (!string.IsNullOrWhiteSpace(Summary) ? Summary : (!string.IsNullOrWhiteSpace(userQuestion) ? userQuestion : "Screen Analysis"));

                    var historyItem = new HistoryItem
                    {
                        ApplicationName = _currentContext?.ApplicationName ?? "Desktop",
                        WindowTitle = _currentContext?.WindowTitle ?? "",
                        Intent = DetectedIntent,
                        UserQuestion = userQuestion ?? "",
                        Title = resolvedTitle,
                        Summary = Summary,
                        MarkdownResponse = MarkdownContent,
                        ThumbnailBase64 = thumbBase64,
                        ImageWidth = _currentRegion.Width,
                        ImageHeight = _currentRegion.Height
                    };

                    await _historyRepository.SaveHistoryItemAsync(historyItem, CancellationToken.None);
                }
                catch (Exception saveEx)
                {
                    Debug.WriteLine($"[History] Failed to persist history item: {saveEx.Message}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Clean cancellation
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = $"Error during analysis: {ex.Message}";
        }
        finally
        {
            IsStreaming = false;
        }
    }

    private static BitmapImage? LoadBitmapImage(byte[]? bytes)
    {
        if (bytes == null || bytes.Length == 0) return null;
        try
        {
            var bmp = new BitmapImage();
            using var ms = new MemoryStream(bytes);
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }
}
