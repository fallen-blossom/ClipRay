using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace ClipRay.App.Settings;

/// <summary>Explicit display text. Does not change configuration values or thread culture.</summary>
public static class SettingsText
{
    public static readonly DependencyProperty LanguageProperty = DependencyProperty.RegisterAttached(
        "Language", typeof(string), typeof(SettingsText),
        new FrameworkPropertyMetadata("zh-CN", FrameworkPropertyMetadataOptions.Inherits, Update));
    public static readonly DependencyProperty ChineseProperty = DependencyProperty.RegisterAttached(
        "Chinese", typeof(string), typeof(SettingsText), new PropertyMetadata(null, Update));
    public static readonly DependencyProperty EnglishProperty = DependencyProperty.RegisterAttached(
        "English", typeof(string), typeof(SettingsText), new PropertyMetadata(null, Update));
    public static string GetLanguage(DependencyObject target) => (string)target.GetValue(LanguageProperty);
    public static void SetLanguage(DependencyObject target, string value) => target.SetValue(LanguageProperty, value);
    public static string? GetChinese(DependencyObject target) => (string?)target.GetValue(ChineseProperty);
    public static void SetChinese(DependencyObject target, string value) => target.SetValue(ChineseProperty, value);
    public static string? GetEnglish(DependencyObject target) => (string?)target.GetValue(EnglishProperty);
    public static void SetEnglish(DependencyObject target, string value) => target.SetValue(EnglishProperty, value);

    private static void Update(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (GetChinese(target) is not { } chinese || GetEnglish(target) is not { } english) return;
        var text = AppInterfaceLanguage.Text(GetLanguage(target), chinese, english);
        switch (target)
        {
            case TextBlock block: block.Text = text; break;
            case Run run: run.Text = text; break;
            case ContentControl control: control.Content = text; break;
        }
    }
}
