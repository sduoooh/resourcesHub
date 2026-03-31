using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using ResourceRouter.Core.Models;
using ResourceRouter.Infrastructure.Storage;

namespace ResourceRouter.App.Views;

public partial class ConfigResourceHubWindow : Window
{
    private const string BasicDocumentId = "framework.basic";
    private const string NativeDocumentId = "framework.native";
    private const string PluginDocumentId = "plugins.settings";

    private readonly AppConfig _baselineConfig;
    private readonly List<ConfigResourceDocument> _allDocuments;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private ConfigResourceDocument? _current;

    public ConfigResourceHubWindow(AppConfig config)
    {
        InitializeComponent();

        _baselineConfig = config;
        _allDocuments = BuildDocuments(config);

        ResourceList.ItemsSource = _allDocuments;
        if (_allDocuments.Count > 0)
        {
            ResourceList.SelectedIndex = 0;
        }

        SavedConfig = config;
    }

    public AppConfig SavedConfig { get; private set; }

    private void OnResourceSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!TryPersistCurrentEditor())
        {
            ResourceList.SelectionChanged -= OnResourceSelectionChanged;
            ResourceList.SelectedItem = _current;
            ResourceList.SelectionChanged += OnResourceSelectionChanged;
            return;
        }

        _current = ResourceList.SelectedItem as ConfigResourceDocument;
        if (_current is null)
        {
            TitleText.Text = "选择左侧配置资源";
            TagsText.Text = string.Empty;
            JsonEditor.Text = string.Empty;
            return;
        }

        TitleText.Text = _current.DisplayName;
        TagsText.Text = "Tags: " + _current.TagDisplay;
        JsonEditor.Text = _current.JsonContent;
    }

    private void OnTagFilterChanged(object sender, TextChangedEventArgs e)
    {
        var filter = TagFilterTextBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(filter))
        {
            ResourceList.ItemsSource = _allDocuments;
            return;
        }

        var keywords = filter
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(k => k.ToLowerInvariant())
            .ToArray();

        var filtered = _allDocuments
            .Where(doc => keywords.All(k => doc.Tags.Any(tag => tag.Contains(k, StringComparison.OrdinalIgnoreCase))))
            .ToList();

        ResourceList.ItemsSource = filtered;
        if (_current is not null && !filtered.Contains(_current))
        {
            ResourceList.SelectedIndex = filtered.Count > 0 ? 0 : -1;
        }
    }

    private void OnSaveCurrentClick(object sender, RoutedEventArgs e)
    {
        if (!TryPersistCurrentEditor())
        {
            return;
        }

        MessageBox.Show("当前配置资源已保存到编辑缓冲区。", "配置资源中心");
    }

    private void OnDragJsonClick(object sender, RoutedEventArgs e)
    {
        if (_current is null)
        {
            MessageBox.Show("请先选择一个配置资源。", "配置资源中心");
            return;
        }

        if (!TryPersistCurrentEditor())
        {
            return;
        }

        var outputPath = WriteCurrentJsonToTempFile(_current);
        StartFileDrag(outputPath);
    }

    private void OnApplyClick(object sender, RoutedEventArgs e)
    {
        if (!TryPersistCurrentEditor())
        {
            return;
        }

        try
        {
            SavedConfig = BuildConfigFromDocuments();
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"配置资源解析失败: {ex.Message}", "配置资源中心");
        }
    }

    private bool TryPersistCurrentEditor()
    {
        if (_current is null)
        {
            return true;
        }

        var text = JsonEditor.Text;
        try
        {
            using var json = JsonDocument.Parse(text);
            _current.JsonContent = JsonSerializer.Serialize(json.RootElement, _jsonOptions);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"JSON 格式错误，无法保存当前资源: {ex.Message}", "配置资源中心");
            return false;
        }
    }

    private AppConfig BuildConfigFromDocuments()
    {
        var basicDocument = GetDocument(BasicDocumentId);
        var nativeDocument = GetDocument(NativeDocumentId);
        var pluginDocument = GetDocument(PluginDocumentId);

        var basic = JsonSerializer.Deserialize<BasicConfigResource>(basicDocument.JsonContent, _jsonOptions)
                    ?? throw new InvalidOperationException("无法读取 framework.basic");
        var native = JsonSerializer.Deserialize<NativeConfigResource>(nativeDocument.JsonContent, _jsonOptions)
                     ?? throw new InvalidOperationException("无法读取 framework.native");
        var plugin = JsonSerializer.Deserialize<PluginConfigResource>(pluginDocument.JsonContent, _jsonOptions)
                     ?? throw new InvalidOperationException("无法读取 plugins.settings");

        var normalizedPluginSettings = plugin.PluginSettings.ToDictionary(
            kv => kv.Key,
            kv => new Dictionary<string, string>(kv.Value, StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);

        var internalEnabled = native.EnableInternalMechanisms;

        return new AppConfig
        {
            DefaultPrivacy = basic.DefaultPrivacy,
            DefaultSyncPolicy = basic.DefaultSyncPolicy,
          DefaultProcessingModel = internalEnabled ? basic.DefaultProcessingModel : ModelType.None,
            EnableAI = basic.EnableAI,
          EnableInternalMechanisms = internalEnabled,
            DefaultPermissionPresetId = string.IsNullOrWhiteSpace(basic.DefaultPermissionPresetId)
                ? _baselineConfig.DefaultPermissionPresetId
                : basic.DefaultPermissionPresetId,
            OllamaEndpoint = string.IsNullOrWhiteSpace(basic.OllamaEndpoint)
                ? "http://localhost:11434/api/generate"
                : basic.OllamaEndpoint,
            PluginDirectory = string.IsNullOrWhiteSpace(basic.PluginDirectory) ? null : basic.PluginDirectory,
            EnableOcr = internalEnabled && native.EnableOcr,
            OcrProvider = string.IsNullOrWhiteSpace(native.OcrProvider)
              ? (internalEnabled ? NativeCapabilityProviders.Ocr.Auto : NativeCapabilityProviders.Ocr.None)
                : native.OcrProvider,
            OcrCliPath = string.IsNullOrWhiteSpace(native.OcrCliPath) ? null : native.OcrCliPath,
            OcrEndpoint = string.IsNullOrWhiteSpace(native.OcrEndpoint) ? null : native.OcrEndpoint,
            OcrModel = string.IsNullOrWhiteSpace(native.OcrModel) ? "eng+chi_sim" : native.OcrModel,
            OcrApiKey = string.IsNullOrWhiteSpace(native.OcrApiKey) ? null : native.OcrApiKey,
            EnableAudioTranscription = internalEnabled && native.EnableAudioTranscription,
            AudioTranscriptionProvider = string.IsNullOrWhiteSpace(native.AudioTranscriptionProvider)
              ? (internalEnabled ? NativeCapabilityProviders.AudioTranscription.Auto : NativeCapabilityProviders.AudioTranscription.None)
                : native.AudioTranscriptionProvider,
            AudioTranscriptionCliPath = string.IsNullOrWhiteSpace(native.AudioTranscriptionCliPath)
                ? null
                : native.AudioTranscriptionCliPath,
            AudioTranscriptionEndpoint = string.IsNullOrWhiteSpace(native.AudioTranscriptionEndpoint)
                ? null
                : native.AudioTranscriptionEndpoint,
            AudioTranscriptionModel = string.IsNullOrWhiteSpace(native.AudioTranscriptionModel)
                ? "whisper-1"
                : native.AudioTranscriptionModel,
            AudioTranscriptionApiKey = string.IsNullOrWhiteSpace(native.AudioTranscriptionApiKey)
                ? null
                : native.AudioTranscriptionApiKey,
            CloudAiProvider = string.IsNullOrWhiteSpace(native.CloudAiProvider)
              ? (internalEnabled ? NativeCapabilityProviders.CloudAI.Auto : NativeCapabilityProviders.CloudAI.None)
              : native.CloudAiProvider,
            CloudAiEndpoint = string.IsNullOrWhiteSpace(native.CloudAiEndpoint)
              ? null
              : native.CloudAiEndpoint,
            CloudAiModel = string.IsNullOrWhiteSpace(native.CloudAiModel)
              ? "gpt-4o-mini"
              : native.CloudAiModel,
            CloudSyncProvider = string.IsNullOrWhiteSpace(native.CloudSyncProvider)
                ? (internalEnabled ? NativeCapabilityProviders.CloudSync.Auto : NativeCapabilityProviders.CloudSync.None)
                : native.CloudSyncProvider,
            CloudEndpoint = string.IsNullOrWhiteSpace(native.CloudEndpoint) ? null : native.CloudEndpoint,
            RemoteProvider = string.IsNullOrWhiteSpace(native.RemoteProvider)
              ? NativeCapabilityProviders.Remote.Auto
              : native.RemoteProvider,
            RemoteEndpoint = string.IsNullOrWhiteSpace(native.RemoteEndpoint)
              ? null
              : native.RemoteEndpoint,
            EnableRemoteMechanism = native.EnableRemoteMechanism,
            ThumbnailProviderMode = string.IsNullOrWhiteSpace(native.ThumbnailProviderMode)
              ? (internalEnabled ? NativeCapabilityProviders.Thumbnail.Auto : NativeCapabilityProviders.Thumbnail.None)
                : native.ThumbnailProviderMode,
            HealthMonitoringByType = new Dictionary<string, bool>(native.HealthMonitoringByType, StringComparer.OrdinalIgnoreCase),
            FeatureizationByType = new Dictionary<string, bool>(native.FeatureizationByType, StringComparer.OrdinalIgnoreCase),
            DedupByType = new Dictionary<string, bool>(native.DedupByType, StringComparer.OrdinalIgnoreCase),
            HealthMonitoringBySource = new Dictionary<string, bool>(native.HealthMonitoringBySource, StringComparer.OrdinalIgnoreCase),
            FeatureizationBySource = new Dictionary<string, bool>(native.FeatureizationBySource, StringComparer.OrdinalIgnoreCase),
            DedupBySource = new Dictionary<string, bool>(native.DedupBySource, StringComparer.OrdinalIgnoreCase),
            CloudAiApiKey = string.IsNullOrWhiteSpace(native.CloudAiApiKey)
              ? _baselineConfig.CloudAiApiKey ?? _baselineConfig.CloudApiKey
              : native.CloudAiApiKey,
            CloudApiKey = string.IsNullOrWhiteSpace(native.CloudAiApiKey)
              ? _baselineConfig.CloudAiApiKey ?? _baselineConfig.CloudApiKey
              : native.CloudAiApiKey,
            PluginSettings = normalizedPluginSettings
        };
    }

    private ConfigResourceDocument GetDocument(string id)
    {
        return _allDocuments.First(doc => string.Equals(doc.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    private List<ConfigResourceDocument> BuildDocuments(AppConfig config)
    {
        var basic = new BasicConfigResource
        {
            DefaultPrivacy = config.DefaultPrivacy,
            DefaultSyncPolicy = config.DefaultSyncPolicy,
            DefaultProcessingModel = config.DefaultProcessingModel,
            EnableAI = config.EnableAI,
            OllamaEndpoint = config.OllamaEndpoint,
            PluginDirectory = config.PluginDirectory,
            DefaultPermissionPresetId = config.DefaultPermissionPresetId
        };

        var native = new NativeConfigResource
        {
          EnableInternalMechanisms = config.EnableInternalMechanisms,
            EnableOcr = config.EnableOcr,
            OcrProvider = config.OcrProvider,
            OcrCliPath = config.OcrCliPath,
            OcrEndpoint = config.OcrEndpoint,
            OcrModel = config.OcrModel,
            OcrApiKey = config.OcrApiKey,
            EnableAudioTranscription = config.EnableAudioTranscription,
            AudioTranscriptionProvider = config.AudioTranscriptionProvider,
            AudioTranscriptionCliPath = config.AudioTranscriptionCliPath,
            AudioTranscriptionEndpoint = config.AudioTranscriptionEndpoint,
            AudioTranscriptionModel = config.AudioTranscriptionModel,
            AudioTranscriptionApiKey = config.AudioTranscriptionApiKey,
            CloudAiProvider = config.CloudAiProvider,
            CloudAiEndpoint = config.CloudAiEndpoint,
            CloudAiModel = config.CloudAiModel,
            CloudAiApiKey = config.CloudAiApiKey ?? config.CloudApiKey,
            CloudSyncProvider = config.CloudSyncProvider,
            CloudEndpoint = config.CloudEndpoint,
            RemoteProvider = config.RemoteProvider,
            RemoteEndpoint = config.RemoteEndpoint,
            EnableRemoteMechanism = config.EnableRemoteMechanism,
            HealthMonitoringByType = new Dictionary<string, bool>(config.HealthMonitoringByType, StringComparer.OrdinalIgnoreCase),
            FeatureizationByType = new Dictionary<string, bool>(config.FeatureizationByType, StringComparer.OrdinalIgnoreCase),
            DedupByType = new Dictionary<string, bool>(config.DedupByType, StringComparer.OrdinalIgnoreCase),
            HealthMonitoringBySource = new Dictionary<string, bool>(config.HealthMonitoringBySource, StringComparer.OrdinalIgnoreCase),
            FeatureizationBySource = new Dictionary<string, bool>(config.FeatureizationBySource, StringComparer.OrdinalIgnoreCase),
            DedupBySource = new Dictionary<string, bool>(config.DedupBySource, StringComparer.OrdinalIgnoreCase),
            ThumbnailProviderMode = config.ThumbnailProviderMode
        };

        var plugin = new PluginConfigResource
        {
            PluginSettings = config.PluginSettings.ToDictionary(
                kv => kv.Key,
                kv => new Dictionary<string, string>(kv.Value, StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase)
        };

        return new List<ConfigResourceDocument>
        {
            new()
            {
                Id = BasicDocumentId,
                DisplayName = "框架基础配置",
                Tags = new[] { "config", "framework", "basic" },
                JsonContent = JsonSerializer.Serialize(basic, _jsonOptions)
            },
            new()
            {
                Id = NativeDocumentId,
                DisplayName = "框架原生能力配置",
                Tags = new[] { "config", "framework", "native" },
                JsonContent = JsonSerializer.Serialize(native, _jsonOptions)
            },
            new()
            {
                Id = PluginDocumentId,
                DisplayName = "插件配置集合",
                Tags = new[] { "config", "plugin", "settings" },
                JsonContent = JsonSerializer.Serialize(plugin, _jsonOptions)
            }
        };
    }

    private string WriteCurrentJsonToTempFile(ConfigResourceDocument document)
    {
        var dir = LocalPathProvider.ConfigHubTempDirectory;

        var path = Path.Combine(dir, document.SuggestedFileName);
        File.WriteAllText(path, document.JsonContent, Encoding.UTF8);
        return path;
    }

    private static void StartFileDrag(string filePath)
    {
        var data = new DataObject();
        var files = new StringCollection { filePath };
        data.SetFileDropList(files);
        DragDrop.DoDragDrop(Application.Current.MainWindow!, data, DragDropEffects.Copy);
    }

    private sealed class BasicConfigResource
    {
        public PrivacyLevel DefaultPrivacy { get; init; }

        public SyncPolicy DefaultSyncPolicy { get; init; }

        public ModelType DefaultProcessingModel { get; init; }

        public bool EnableAI { get; init; }

        public string OllamaEndpoint { get; init; } = "http://localhost:11434/api/generate";

        public string? PluginDirectory { get; init; }

        public string? DefaultPermissionPresetId { get; init; }
    }

    private sealed class NativeConfigResource
    {
      public bool EnableInternalMechanisms { get; init; }

        public bool EnableOcr { get; init; }

        public string OcrProvider { get; init; } = NativeCapabilityProviders.Ocr.Auto;

        public string? OcrCliPath { get; init; }

        public string? OcrEndpoint { get; init; }

        public string OcrModel { get; init; } = "eng+chi_sim";

        public string? OcrApiKey { get; init; }

        public bool EnableAudioTranscription { get; init; }

        public string AudioTranscriptionProvider { get; init; } = NativeCapabilityProviders.AudioTranscription.Auto;

        public string? AudioTranscriptionCliPath { get; init; }

        public string? AudioTranscriptionEndpoint { get; init; }

        public string AudioTranscriptionModel { get; init; } = "whisper-1";

        public string? AudioTranscriptionApiKey { get; init; }

        public string CloudAiProvider { get; init; } = NativeCapabilityProviders.CloudAI.Auto;

        public string? CloudAiEndpoint { get; init; }

        public string CloudAiModel { get; init; } = "gpt-4o-mini";

        public string? CloudAiApiKey { get; init; }

        public string CloudSyncProvider { get; init; } = NativeCapabilityProviders.CloudSync.Auto;

        public string? CloudEndpoint { get; init; }

        public string RemoteProvider { get; init; } = NativeCapabilityProviders.Remote.Auto;

        public string? RemoteEndpoint { get; init; }

        public bool EnableRemoteMechanism { get; init; }

        public Dictionary<string, bool> HealthMonitoringByType { get; init; } =
          new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, bool> FeatureizationByType { get; init; } =
          new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, bool> DedupByType { get; init; } =
          new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, bool> HealthMonitoringBySource { get; init; } =
          new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, bool> FeatureizationBySource { get; init; } =
          new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, bool> DedupBySource { get; init; } =
          new(StringComparer.OrdinalIgnoreCase);

        public string ThumbnailProviderMode { get; init; } = NativeCapabilityProviders.Thumbnail.Auto;
    }

    private sealed class PluginConfigResource
    {
        public Dictionary<string, Dictionary<string, string>> PluginSettings { get; init; } =
            new(StringComparer.OrdinalIgnoreCase);
    }
}
