using System.Windows;
using MinecraftLauncher.Models;

namespace MinecraftLauncher;

public partial class AIConfigDialog : Window
{
    public AIConfig Config { get; private set; }

    public AIConfigDialog(AIConfig config)
    {
        InitializeComponent();
        
        Config = config;
        
        ApiKeyBox.Password = config.ApiKey;
        ApiEndpointBox.Text = config.ApiEndpoint;
        HistorySlider.Value = config.MaxHistoryMessages;
        HistoryValueText.Text = config.MaxHistoryMessages.ToString();
        
        switch (config.Model)
        {
            case "gpt-4o":
                ModelComboBox.SelectedIndex = 1;
                break;
            case "gpt-3.5-turbo":
                ModelComboBox.SelectedIndex = 2;
                break;
            default:
                ModelComboBox.SelectedIndex = 0;
                break;
        }
        
        SendHardwareInfoCheckBox.IsChecked = config.SendHardwareInfo;
        EnableAutoDiagnosisCheckBox.IsChecked = config.EnableAutoDiagnosis;
        EnableProactiveSuggestionsCheckBox.IsChecked = config.EnableProactiveSuggestions;
        
        HistorySlider.ValueChanged += (s, e) =>
        {
            HistoryValueText.Text = ((int)HistorySlider.Value).ToString();
        };
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        Config.ApiKey = ApiKeyBox.Password;
        Config.ApiEndpoint = ApiEndpointBox.Text;
        Config.MaxHistoryMessages = (int)HistorySlider.Value;
        Config.SendHardwareInfo = SendHardwareInfoCheckBox.IsChecked ?? false;
        Config.EnableAutoDiagnosis = EnableAutoDiagnosisCheckBox.IsChecked ?? false;
        Config.EnableProactiveSuggestions = EnableProactiveSuggestionsCheckBox.IsChecked ?? false;
        
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
