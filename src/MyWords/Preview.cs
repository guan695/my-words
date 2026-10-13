using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MyWords;

internal static class Preview
{
    public static void Render(string directory)
    {
        Directory.CreateDirectory(directory);
        var dataDirectory = Path.Combine(directory, "demo-" + Guid.NewGuid().ToString("N"));
        var store = new WordStore(Path.Combine(dataDirectory, "words.json"));
        var approach = store.Add("approach", new WordCard { Phonetic = "/əˈproʊtʃ/", PartOfSpeech = "n. / v.", Meaning = "方法；接近；着手处理", Example = "Try a different approach to learning.", ExampleTranslation = "试试用另一种方法学习。", Collocations = "a new approach  一种新方法\napproach a problem  着手解决问题", MemoryTip = "把 approach 和「靠近」联系起来：靠近一个人，也可以是靠近一个问题、找到解决它的方法。", AiGenerated = false });
        for (var i = 0; i < 4; i++) store.Add("approach");
        store.Add("curious", new WordCard { Meaning = "好奇的；求知欲强的", Example = "Stay curious about the world.", ExampleTranslation = "对世界保持好奇。" });
        store.Add("curious"); store.Add("curious");
        store.Add("notice", new WordCard { Meaning = "注意到；通知", Example = "Notice the small things.", ExampleTranslation = "留意那些小事。" }); store.Add("notice");
        store.Add("steady", new WordCard { Meaning = "稳定的；持续的" });
        store.Add("figure out", new WordCard { Meaning = "弄清楚；想出" });
        store.Add("embrace", new WordCard { Meaning = "拥抱；欣然接受" });
        using var controller = new AppController(store, new SettingsStore(dataDirectory));
        var window = new MainWindow(controller, "已就绪。拾词运行时，Manggo 可以一键收藏。");
        window.SetStatus("界面预览 · 示例词库，不会写入你的真实词库。");
        Capture(window, Path.Combine(directory, "wordbook.png"));
        window.Navigate("study");
        Capture(window, Path.Combine(directory, "study.png"));
        window.Navigate("settings");
        Capture(window, Path.Combine(directory, "settings.png"));
        window.AllowClose = true;
    }
    private static void Capture(MainWindow window, string path)
    {
        var root = (FrameworkElement)window.Content;
        root.Measure(new Size(924, 680));
        root.Arrange(new Rect(0, 0, 924, 680));
        root.UpdateLayout();
        var bitmap = new RenderTargetBitmap(924, 680, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(path);
        encoder.Save(output);
    }
}
