using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ResourceRouter.Core.Abstractions;
using ResourceRouter.Core.Models;

namespace ResourceRouter.App.Views;

public partial class ConfigDialog : Window
{
    private IReadOnlyList<PermissionPreset> _presets = Array.Empty<PermissionPreset>();
    private readonly IResourceMetadataFacetPolicy _metadataFacetPolicy;

    public ConfigDialog(Resource resource, IResourceMetadataFacetPolicy metadataFacetPolicy)
    {
        InitializeComponent();
        EditedResource = resource;
        _metadataFacetPolicy = metadataFacetPolicy ?? throw new ArgumentNullException(nameof(metadataFacetPolicy));

        _presets = PermissionPreset.BuiltIn.Values.ToArray();
        PresetCombo.ItemsSource = _presets;
        PresetCombo.DisplayMemberPath = nameof(PermissionPreset.DisplayName);
        PresetCombo.SelectedValuePath = nameof(PermissionPreset.Id);

        PrivacyCombo.ItemsSource = Enum.GetValues<PrivacyLevel>();
        SyncCombo.ItemsSource = Enum.GetValues<SyncPolicy>();
        ModelCombo.ItemsSource = Enum.GetValues<ModelType>();

        var presetId = string.IsNullOrWhiteSpace(resource.PermissionPresetId)
            ? PermissionPreset.PrivatePresetId
            : resource.PermissionPresetId;
        PresetCombo.SelectedValue = presetId;

        PrivacyCombo.SelectedItem = resource.Privacy;
        SyncCombo.SelectedItem = resource.SyncPolicy;
        ModelCombo.SelectedItem = resource.ProcessingModel;

        var metadataFacet = _metadataFacetPolicy.Read(resource);
        TitleBox.Text = metadataFacet.TitleOverride;
        NotesBox.Text = metadataFacet.Annotations;
        TagsBox.Text = string.Join(",", metadataFacet.PropertyTags);
    }

    public Resource EditedResource { get; }

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        if (PresetCombo.SelectedItem is PermissionPreset selectedPreset)
        {
            EditedResource.PermissionPresetId = selectedPreset.Id;
        }

        EditedResource.Privacy = (PrivacyLevel)PrivacyCombo.SelectedItem;
        EditedResource.SyncPolicy = (SyncPolicy)SyncCombo.SelectedItem;
        EditedResource.ProcessingModel = (ModelType)ModelCombo.SelectedItem;
        var currentFacet = _metadataFacetPolicy.Read(EditedResource);
        var propertyTags =
            TagsBox.Text
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(ResourceTagRules.Normalize)
                .Where(static tag => !string.IsNullOrWhiteSpace(tag))
                .Select(static tag => tag!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

        _metadataFacetPolicy.Apply(EditedResource, new ResourceMetadataFacet
        {
            TitleOverride = TitleBox.Text,
            Annotations = NotesBox.Text,
            Summary = currentFacet.Summary,
            ConditionTags = currentFacet.ConditionTags,
            PropertyTags = propertyTags,
            OriginalFileName = currentFacet.OriginalFileName,
            MimeType = currentFacet.MimeType,
            FileSize = currentFacet.FileSize,
            Source = currentFacet.Source,
            CreatedAt = currentFacet.CreatedAt,
            ExtensionMetadata = currentFacet.ExtensionMetadata
        });

        DialogResult = true;
        Close();
    }

    private void OnPresetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PresetCombo.SelectedItem is not PermissionPreset preset)
        {
            return;
        }

        PrivacyCombo.SelectedItem = preset.Privacy;
        SyncCombo.SelectedItem = preset.SyncPolicy;
        ModelCombo.SelectedItem = preset.ProcessingModel;
    }
}