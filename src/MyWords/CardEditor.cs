using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace MyWords;

public sealed class CardEditor : Window
{
    public WordCard? Result { get; private set; }
    public CardEditor(WordEntry word)
    {
        Style = (Style)FindResource(typeof(Window));
        Title = "编辑词卡 · " + word.Text;
        Width = 520; Height = 640; MinWidth = 400; MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        var layout = new DockPanel { Margin = new Thickness(22) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        var cancel = new Button { Content = "取消", IsCancel = true, Margin = new Thickness(0, 0, 10, 0) };
        var save = new Button { Content = "保存词卡", Style = (Style)FindResource("PrimaryButton") };
        buttons.Children.Add(cancel); buttons.Children.Add(save); layout.Children.Add(buttons);
        var form = new StackPanel();
        form.Children.Add(new TextBlock { Text = word.Text, FontSize = 26, FontFamily = new System.Windows.Media.FontFamily("Georgia"), Margin = new Thickness(0, 0, 0, 12) });
        var inputs = new List<TextBox>();
        foreach (var (label, value, multi) in new[] {
            ("音标", word.Card.Phonetic, false), ("词性", word.Card.PartOfSpeech, false),
            ("释义", word.Card.Meaning, true), ("英文例句", word.Card.Example, true),
            ("例句翻译", word.Card.ExampleTranslation, true), ("常用搭配", word.Card.Collocations, true), ("记忆提示", word.Card.MemoryTip, true) })
        {
            form.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 10, 0, 5) });
            var input = new TextBox { Text = value, MaxLength = 4000, TextWrapping = TextWrapping.Wrap, AcceptsReturn = multi, MinHeight = multi ? 65 : 35, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            System.Windows.Automation.AutomationProperties.SetName(input, label);
            inputs.Add(input); form.Children.Add(input);
        }
        save.Click += (_, _) =>
        {
            Result = new WordCard { Phonetic = inputs[0].Text.Trim(), PartOfSpeech = inputs[1].Text.Trim(), Meaning = inputs[2].Text.Trim(), Example = inputs[3].Text.Trim(), ExampleTranslation = inputs[4].Text.Trim(), Collocations = inputs[5].Text.Trim(), MemoryTip = inputs[6].Text.Trim(), AiGenerated = word.Card.AiGenerated };
            DialogResult = true;
        };
        layout.Children.Add(new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = layout;
    }
}
