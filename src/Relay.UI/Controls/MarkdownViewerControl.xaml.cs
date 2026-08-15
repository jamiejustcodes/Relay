using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Relay.UI.Controls;

public partial class MarkdownViewerControl : UserControl
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    public static readonly DependencyProperty MarkdownProperty = DependencyProperty.Register(
        nameof(Markdown),
        typeof(string),
        typeof(MarkdownViewerControl),
        new PropertyMetadata(string.Empty, OnMarkdownChanged));

    public string Markdown
    {
        get => (string)GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    public MarkdownViewerControl()
    {
        InitializeComponent();
    }

    private static void OnMarkdownChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MarkdownViewerControl control)
        {
            control.RenderMarkdown(e.NewValue as string);
        }
    }

    private void RenderMarkdown(string? markdown)
    {
        ContentContainer.Children.Clear();

        if (string.IsNullOrWhiteSpace(markdown))
        {
            return;
        }

        try
        {
            var doc = Markdig.Markdown.Parse(markdown, Pipeline);

            foreach (var block in doc)
            {
                var element = RenderBlock(block);
                if (element != null)
                {
                    ContentContainer.Children.Add(element);
                }
            }
        }
        catch
        {
            // Fallback plain text
            var fallbackBlock = new TextBlock
            {
                Text = markdown,
                FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI, sans-serif"),
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(241, 245, 249)),
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 20
            };
            ContentContainer.Children.Add(fallbackBlock);
        }
    }

    private UIElement? RenderBlock(Markdig.Syntax.Block block)
    {
        return block switch
        {
            FencedCodeBlock fenced => CreateCodeBlockElement(fenced.Lines.ToString(), fenced.Info),
            CodeBlock code => CreateCodeBlockElement(code.Lines.ToString(), null),
            HeadingBlock heading => CreateHeadingElement(heading),
            ParagraphBlock paragraph => CreateParagraphElement(paragraph),
            ListBlock list => CreateListElement(list),
            QuoteBlock quote => CreateQuoteElement(quote),
            ThematicBreakBlock => CreateThematicBreakElement(),
            _ => null
        };
    }

    private static UIElement CreateCodeBlockElement(string code, string? language)
    {
        string langDisplay = string.IsNullOrWhiteSpace(language) ? "code" : language.Trim();
        string cleanCode = code.TrimEnd('\r', '\n');

        var container = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(9, 12, 19)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(36, 45, 66)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(0, 8, 0, 12),
            ClipToBounds = true
        };

        var rootGrid = new Grid();
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // Header bar
        var headerBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(20, 26, 40)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(36, 45, 66)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(12, 6, 8, 6)
        };

        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Language label
        var langText = new TextBlock
        {
            Text = langDisplay.ToLowerInvariant(),
            FontFamily = new FontFamily("Cascadia Code, Consolas, Courier New, monospace"),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(langText, 0);
        headerGrid.Children.Add(langText);

        // Copy button with ChatGPT-like hover state and feedback
        var copyBtn = new Button
        {
            Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            Padding = new Thickness(8, 3, 8, 3),
            VerticalAlignment = VerticalAlignment.Center
        };

        var copyContent = new StackPanel { Orientation = Orientation.Horizontal };
        var copyIcon = new TextBlock
        {
            Text = "📋",
            FontSize = 11,
            Margin = new Thickness(0, 0, 5, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        var copyLabel = new TextBlock
        {
            Text = "Copy code",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
            VerticalAlignment = VerticalAlignment.Center
        };
        copyContent.Children.Add(copyIcon);
        copyContent.Children.Add(copyLabel);
        copyBtn.Content = copyContent;

        // Hover effect
        copyBtn.MouseEnter += (s, e) =>
        {
            copyBtn.Background = new SolidColorBrush(Color.FromArgb(50, 99, 102, 241));
            copyLabel.Foreground = Brushes.White;
        };
        copyBtn.MouseLeave += (s, e) =>
        {
            copyBtn.Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255));
            copyLabel.Foreground = new SolidColorBrush(Color.FromRgb(226, 232, 240));
        };

        // Click to copy
        copyBtn.Click += async (s, e) =>
        {
            try
            {
                Clipboard.SetText(cleanCode);
                copyIcon.Text = "✓";
                copyLabel.Text = "Copied!";
                copyLabel.Foreground = new SolidColorBrush(Color.FromRgb(16, 185, 129));
                await Task.Delay(2000);
                copyIcon.Text = "📋";
                copyLabel.Text = "Copy code";
                copyLabel.Foreground = new SolidColorBrush(Color.FromRgb(226, 232, 240));
            }
            catch { }
        };

        Grid.SetColumn(copyBtn, 1);
        headerGrid.Children.Add(copyBtn);
        headerBorder.Child = headerGrid;
        Grid.SetRow(headerBorder, 0);
        rootGrid.Children.Add(headerBorder);

        // Code body
        var scrollViewer = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(12, 10, 12, 10),
            Background = Brushes.Transparent
        };

        var codeBox = new TextBox
        {
            Text = cleanCode,
            FontFamily = new FontFamily("Cascadia Code, Consolas, Courier New, monospace"),
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            IsReadOnly = true,
            TextWrapping = TextWrapping.NoWrap,
            IsUndoEnabled = false,
            Cursor = Cursors.IBeam
        };

        scrollViewer.Content = codeBox;
        Grid.SetRow(scrollViewer, 1);
        rootGrid.Children.Add(scrollViewer);

        container.Child = rootGrid;
        return container;
    }

    private UIElement CreateHeadingElement(HeadingBlock heading)
    {
        var textBlock = new TextBlock
        {
            FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI, sans-serif"),
            FontWeight = FontWeights.Bold,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(248, 250, 252))
        };

        switch (heading.Level)
        {
            case 1:
                textBlock.FontSize = 17;
                textBlock.Margin = new Thickness(0, 12, 0, 6);
                break;
            case 2:
                textBlock.FontSize = 15;
                textBlock.Margin = new Thickness(0, 10, 0, 4);
                break;
            case 3:
            default:
                textBlock.FontSize = 13;
                textBlock.FontWeight = FontWeights.SemiBold;
                textBlock.Margin = new Thickness(0, 8, 0, 4);
                break;
        }

        if (heading.Inline != null)
        {
            PopulateInlines(textBlock.Inlines, heading.Inline);
        }

        return textBlock;
    }

    private UIElement CreateParagraphElement(ParagraphBlock paragraph)
    {
        var textBlock = new TextBlock
        {
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI, sans-serif"),
            FontSize = 13,
            LineHeight = 20,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(241, 245, 249)),
            Margin = new Thickness(0, 0, 0, 8)
        };

        if (paragraph.Inline != null)
        {
            PopulateInlines(textBlock.Inlines, paragraph.Inline);
        }

        return textBlock;
    }

    private UIElement CreateListElement(ListBlock list)
    {
        var stack = new StackPanel { Margin = new Thickness(4, 0, 0, 8) };
        int itemIndex = 1;

        foreach (var item in list)
        {
            if (item is ListItemBlock listItem)
            {
                var rowGrid = new Grid { Margin = new Thickness(0, 2, 0, 3) };
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var bullet = new TextBlock
                {
                    Text = list.IsOrdered ? $"{itemIndex}." : "•",
                    FontSize = list.IsOrdered ? 12 : 14,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(99, 102, 241)),
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 0, 4, 0)
                };
                Grid.SetColumn(bullet, 0);
                rowGrid.Children.Add(bullet);

                var contentStack = new StackPanel();
                foreach (var subBlock in listItem)
                {
                    var elem = RenderBlock(subBlock);
                    if (elem != null) contentStack.Children.Add(elem);
                }
                Grid.SetColumn(contentStack, 1);
                rowGrid.Children.Add(contentStack);

                stack.Children.Add(rowGrid);
                itemIndex++;
            }
        }
        return stack;
    }

    private UIElement CreateQuoteElement(QuoteBlock quote)
    {
        var border = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(99, 102, 241)),
            BorderThickness = new Thickness(3, 0, 0, 0),
            Background = new SolidColorBrush(Color.FromArgb(35, 99, 102, 241)),
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(0, 4, 0, 8),
            CornerRadius = new CornerRadius(0, 4, 4, 0)
        };

        var stack = new StackPanel();
        foreach (var block in quote)
        {
            var elem = RenderBlock(block);
            if (elem != null) stack.Children.Add(elem);
        }
        border.Child = stack;
        return border;
    }

    private static UIElement CreateThematicBreakElement()
    {
        return new Border
        {
            Height = 1,
            Background = new SolidColorBrush(Color.FromRgb(31, 38, 54)),
            Margin = new Thickness(0, 8, 0, 8)
        };
    }

    private void PopulateInlines(InlineCollection target, ContainerInline container)
    {
        foreach (var inline in container)
        {
            AppendInline(target, inline);
        }
    }

    private void AppendInline(InlineCollection target, Markdig.Syntax.Inlines.Inline inline)
    {
        if (inline is LiteralInline literal)
        {
            target.Add(new Run(literal.Content.ToString()));
        }
        else if (inline is CodeInline codeInline)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(30, 37, 54)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(46, 55, 78)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(4, 1, 4, 1),
                Margin = new Thickness(2, 0, 2, 0)
            };
            var codeText = new TextBlock
            {
                Text = codeInline.Content,
                FontFamily = new FontFamily("Cascadia Code, Consolas, Courier New, monospace"),
                FontSize = 11.5,
                Foreground = new SolidColorBrush(Color.FromRgb(56, 189, 248))
            };
            border.Child = codeText;
            target.Add(new InlineUIContainer(border) { BaselineAlignment = BaselineAlignment.Center });
        }
        else if (inline is EmphasisInline emphasis)
        {
            var span = new Span();
            if (emphasis.DelimiterCount >= 2)
            {
                span.FontWeight = FontWeights.Bold;
                span.Foreground = new SolidColorBrush(Color.FromRgb(248, 250, 252));
            }
            else
            {
                span.FontStyle = FontStyles.Italic;
                span.Foreground = new SolidColorBrush(Color.FromRgb(226, 232, 240));
            }

            foreach (var child in emphasis)
            {
                AppendInline(span.Inlines, child);
            }
            target.Add(span);
        }
        else if (inline is LinkInline link)
        {
            var hyperlink = new Hyperlink
            {
                NavigateUri = Uri.TryCreate(link.Url, UriKind.Absolute, out var uri) ? uri : null,
                Foreground = new SolidColorBrush(Color.FromRgb(129, 140, 248)),
                TextDecorations = null
            };
            hyperlink.RequestNavigate += (s, e) =>
            {
                try
                {
                    if (e.Uri != null)
                    {
                        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
                    }
                }
                catch { }
            };

            foreach (var child in link)
            {
                AppendInline(hyperlink.Inlines, child);
            }
            target.Add(hyperlink);
        }
        else if (inline is LineBreakInline)
        {
            target.Add(new LineBreak());
        }
        else if (inline is ContainerInline containerInline)
        {
            PopulateInlines(target, containerInline);
        }
    }
}
