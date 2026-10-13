using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace MyWords;

public partial class MainWindow : Window
{
    private readonly AppController controller;
    private bool ready;
    private string page = "library";
    private Guid? studyId;
    private string activeStudyDay = "";
    private readonly DispatcherTimer dayTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    public bool AllowClose { get; set; }
    public event Action? HiddenToTray;
    private WordEntry? Selected => WordsList.SelectedItem as WordEntry;

    public MainWindow(AppController controller, string bridgeStatus)
    {
        this.controller = controller;
        InitializeComponent();
        BridgeStatusLabel.Text = bridgeStatus;
        DataPathLabel.Text = "词库位置：" + controller.Store.FilePath;
        controller.Store.Changed += () => Dispatcher.BeginInvoke(() => { RefreshLibrary(); if (page == "study") RefreshStudy(); });
        controller.BusyChanged += () => Dispatcher.BeginInvoke(RefreshDetail);
        controller.Notice += (message, error) => Dispatcher.BeginInvoke(() => SetStatus(message, error));
        ready = true;
        RefreshLibrary();
        Navigate("library");
        dayTimer.Tick += (_, _) =>
        {
            if (IsVisible && page == "study" && activeStudyDay != Today().ToString("yyyy-MM-dd")) RefreshStudy();
        };
        IsVisibleChanged += (_, _) => { if (IsVisible) dayTimer.Start(); else dayTimer.Stop(); };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { HideToTray(); e.Handled = true; }
            else if (e.Key == Key.L && Keyboard.Modifiers == ModifierKeys.Control) { Navigate("library"); AddInput.Focus(); e.Handled = true; }
        };
    }

    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.Now);
    public void ShowLibrary() { Show(); WindowState = WindowState.Normal; Activate(); Navigate("library"); AddInput.Focus(); }
    public void ShowStudy() { Show(); WindowState = WindowState.Normal; Activate(); Navigate("study"); }
    public void HideToTray() { Hide(); HiddenToTray?.Invoke(); }
    private void Window_Closing(object? sender, CancelEventArgs e) { if (!AllowClose) { e.Cancel = true; HideToTray(); } else dayTimer.Stop(); }
    private void Library_Click(object sender, RoutedEventArgs e) => Navigate("library");
    private void Study_Click(object sender, RoutedEventArgs e) => Navigate("study");
    private void Settings_Click(object sender, RoutedEventArgs e) => Navigate("settings");
    internal void Navigate(string target)
    {
        page = target;
        LibraryPanel.Visibility = target == "library" ? Visibility.Visible : Visibility.Collapsed;
        StudyPanel.Visibility = target == "study" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPanel.Visibility = target == "settings" ? Visibility.Visible : Visibility.Collapsed;
        AddBar.Visibility = target == "library" ? Visibility.Visible : Visibility.Collapsed;
        foreach (var (button, name) in new[] { (LibraryNav, "library"), (StudyNav, "study"), (SettingsNav, "settings") })
        {
            button.Background = name == target ? (Brush)FindResource("Accent") : Brushes.Transparent;
            button.Foreground = name == target ? Brushes.White : (Brush)FindResource("Ink");
            button.BorderBrush = name == target ? (Brush)FindResource("Accent") : Brushes.Transparent;
        }
        if (target == "study") Run(RefreshStudy);
        if (target == "settings")
        {
            var settings = controller.Settings.Value;
            Run(() => ApiKeyInput.Password = controller.Settings.GetApiKey());
            ModelInput.Text = settings.Model;
            PromptInput.Text = settings.Prompt;
            AutoEnrichInput.IsChecked = settings.AutoEnrich;
        }
    }

    public void SetStatus(string message, bool error = false)
    {
        StatusText.Text = message;
        StatusText.ToolTip = message;
        StatusText.Foreground = error ? new SolidColorBrush(Color.FromRgb(156, 65, 52)) : (Brush)FindResource("Accent");
    }
    private void Run(Action action) { try { action(); } catch (Exception ex) when (ex is not OutOfMemoryException) { SetStatus(ex.Message, true); } }
    private void Add_Click(object sender, RoutedEventArgs e) => AddWord();
    private void AddInput_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { AddWord(); e.Handled = true; } }
    private void AddWord() => Run(() =>
    {
        var added = controller.Add(AddInput.Text);
        AddInput.Clear();
        SearchInput.Clear();
        RefreshLibrary(added.Word.Id);
        SetStatus(added.Message);
        AddInput.Focus();
    });
    private void Search_Changed(object sender, TextChangedEventArgs e) { if (ready) RefreshLibrary(); }
    private void Sort_Changed(object sender, SelectionChangedEventArgs e) { if (ready) RefreshLibrary(); }
    private void Words_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (ready) RefreshDetail(); }
    private void RefreshLibrary(Guid? preferred = null)
    {
        if (!ready) return;
        var selectedId = preferred ?? Selected?.Id;
        var snapshot = controller.Store.Snapshot();
        var query = SearchInput.Text.Trim();
        var words = snapshot.Words.Where(w => w.Text.Contains(query, StringComparison.OrdinalIgnoreCase) || w.Card.Meaning.Contains(query, StringComparison.OrdinalIgnoreCase));
        words = SortInput.SelectedIndex switch
        {
            0 => words.OrderByDescending(w => w.AddCount).ThenBy(w => w.Text, StringComparer.Ordinal),
            1 => words.OrderBy(w => w.Text, StringComparer.Ordinal),
            2 => words.OrderByDescending(w => w.Text, StringComparer.Ordinal),
            _ => words.Reverse()
        };
        var visible = words.ToList();
        WordsList.ItemsSource = visible;
        WordsList.SelectedItem = visible.FirstOrDefault(w => w.Id == selectedId) ?? visible.FirstOrDefault();
        EmptyLibrary.Visibility = visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyLibrary.Text = snapshot.Words.Count == 0 ? "从上方收藏第一个词。\n也可以在设置中连接 Manggo。" : "没有找到这个词。\n试试其他单词或释义。";
        StatsLabel.Text = $"{snapshot.Words.Count} 个单词  /  重复收藏 {snapshot.Words.Sum(w => (long)w.RepeatCount)} 次";
        ListCountLabel.Text = query.Length == 0 ? $"共 {visible.Count} 个词，点击查看词卡" : $"找到 {visible.Count} 个词";
        RefreshDetail();
    }

    private void RefreshDetail()
    {
        var word = Selected;
        DetailPanel.Visibility = word == null ? Visibility.Collapsed : Visibility.Visible;
        NoSelection.Visibility = word == null ? Visibility.Visible : Visibility.Collapsed;
        if (word == null) return;
        DetailWord.Text = word.Text;
        DetailPhonetic.Text = word.Card.Phonetic;
        DetailPhonetic.Visibility = string.IsNullOrWhiteSpace(word.Card.Phonetic) ? Visibility.Collapsed : Visibility.Visible;
        DetailCount.Text = $"收藏 {word.AddCount} 次    重复 {word.RepeatCount} 次    {word.RatingLabel}";
        DetailMeaning.Text = string.IsNullOrWhiteSpace(word.Card.Meaning) ? "还没有释义，点击下方 AI 整理或手动编辑。" : (word.Card.PartOfSpeech + "  " + word.Card.Meaning).Trim();
        DetailExample.Text = string.IsNullOrWhiteSpace(word.Card.Example) ? "等待一个好记的例句。" : word.Card.Example;
        DetailTranslation.Text = word.Card.ExampleTranslation;
        DetailCollocations.Text = string.IsNullOrWhiteSpace(word.Card.Collocations) ? "—" : word.Card.Collocations;
        DetailMemory.Text = string.IsNullOrWhiteSpace(word.Card.MemoryTip) ? "—" : word.Card.MemoryTip;
        var busy = controller.IsBusy(word.Id);
        GenerateButton.IsEnabled = !busy;
        GenerateButton.Content = busy ? "正在整理…" : word.Card.AiGenerated ? "重新整理" : "AI 整理";
        DetailSource.Text = busy ? "正在后台整理，你可以继续收藏其他单词。" : word.Card.AiGenerated ? "AI 辅助整理，可编辑修正。" : "词卡保存在本地，可随时编辑。";
    }
    private async void Generate_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } word) await controller.EnrichAsync(word.Id);
    }
    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } word) return;
        var editor = new CardEditor(word) { Owner = this };
        if (editor.ShowDialog() == true) Run(() => { controller.Store.UpdateCard(word.Id, editor.Result!); SetStatus("词卡已保存。"); });
    }
    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } word) return;
        if (MessageBox.Show(this, $"删除 {word.Text} 及其收藏计数？", "删除单词", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes)
            Run(() => { controller.Store.Delete(word.Id); SetStatus("单词已删除。"); });
    }

    private void RefreshStudy()
    {
        var snapshot = controller.Store.EnsureDaily(Today());
        activeStudyDay = Today().ToString("yyyy-MM-dd");
        var remaining = snapshot.DailyIds.Where(id => !snapshot.CompletedIds.Contains(id)).ToList();
        var word = remaining.Count == 0 ? null : snapshot.Words.FirstOrDefault(w => w.Id == remaining[0]);
        var keepAnswer = word != null && studyId == word.Id && StudyAnswer.Visibility == Visibility.Visible;
        studyId = word?.Id;
        StudyProgress.Text = $"今日学习    {snapshot.CompletedIds.Count} / {snapshot.DailyIds.Count}";
        StudyProgressBar.Maximum = Math.Max(1, snapshot.DailyIds.Count);
        StudyProgressBar.Value = snapshot.CompletedIds.Count;
        RevealButton.Visibility = word == null || keepAnswer ? Visibility.Collapsed : Visibility.Visible;
        StudyAnswer.Visibility = keepAnswer ? Visibility.Visible : Visibility.Collapsed;
        RatingButtons.Visibility = keepAnswer ? Visibility.Visible : Visibility.Collapsed;
        if (word == null)
        {
            StudyWord.FontFamily = new FontFamily("Microsoft YaHei UI");
            StudyWord.FontSize = 30;
            StudyWord.Text = snapshot.Words.Count == 0 ? "先收录几个单词" : "今天的单词，学完了";
            StudyHint.Text = snapshot.Words.Count == 0 ? "回到单词库，收藏阅读中遇到的生词。" : "明天会从你的词库重新挑选，最多 20 个。";
            return;
        }
        StudyWord.FontFamily = new FontFamily("Georgia");
        StudyWord.FontSize = 44;
        StudyWord.Text = word.Text;
        StudyHint.Text = $"收藏 {word.AddCount} 次，重复 {word.RepeatCount} 次";
        StudyPhonetic.Text = word.Card.Phonetic;
        StudyMeaning.Text = string.IsNullOrWhiteSpace(word.Card.Meaning) ? "这个词还没有释义，可先到单词库补充词卡。" : (word.Card.PartOfSpeech + "  " + word.Card.Meaning).Trim();
        StudyExample.Text = word.Card.Example;
        StudyTranslation.Text = word.Card.ExampleTranslation;
        StudyMemory.Text = word.Card.MemoryTip;
    }
    private void Reveal_Click(object sender, RoutedEventArgs e)
    {
        StudyAnswer.Visibility = RatingButtons.Visibility = Visibility.Visible;
        RevealButton.Visibility = Visibility.Collapsed;
    }
    private void Rate_Click(object sender, RoutedEventArgs e) => Run(() =>
    {
        if (studyId is not Guid id) return;
        controller.Store.Rate(id, (string)((Button)sender).Tag, Today());
        RefreshStudy();
    });
    private void SaveSettings_Click(object sender, RoutedEventArgs e) => Run(() =>
    {
        controller.Settings.Save(new AppSettings
        {
            EncryptedApiKey = SettingsStore.Protect(ApiKeyInput.Password), Model = ModelInput.Text.Trim(), Prompt = PromptInput.Text.Trim(),
            AutoEnrich = AutoEnrichInput.IsChecked == true, BridgeToken = controller.Settings.Value.BridgeToken
        });
        SetStatus("设置已保存。已有单词可在词卡中点击 AI 整理。");
    });
    private void CopyToken_Click(object sender, RoutedEventArgs e) => Run(() => { Clipboard.SetText(controller.Settings.Value.BridgeToken); SetStatus("连接口令已复制，粘贴到 Manggo 的拾词生词本配置中。"); });
    private void Export_Click(object sender, RoutedEventArgs e) => Run(() =>
    {
        var dialog = new SaveFileDialog { Title = "导出词库", FileName = "拾词备份.json", Filter = "JSON 词库|*.json" };
        if (dialog.ShowDialog(this) == true) { controller.Store.Export(dialog.FileName); SetStatus("词库已导出，备份中不包含 API Key。"); }
    });
    private void Import_Click(object sender, RoutedEventArgs e) => Run(() =>
    {
        var dialog = new OpenFileDialog { Title = "导入拾词备份", Filter = "JSON 词库|*.json" };
        if (dialog.ShowDialog(this) == true)
        {
            if (new FileInfo(dialog.FileName).Length > 30_000_000) throw new ArgumentException("词库文件过大。");
            var count = controller.Store.Import(dialog.FileName);
            SetStatus($"导入了 {count} 个新词，已有单词保持不变。");
        }
    });
}
