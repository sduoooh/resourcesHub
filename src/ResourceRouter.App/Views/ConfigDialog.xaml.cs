using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ResourceRouter.Core.Models;

namespace ResourceRouter.App.Views;

public partial class ConfigDialog : Window
{
    private IReadOnlyList<PermissionPreset> _presets = Array.Empty<PermissionPreset>();

    public ConfigDialog(Resource resource)
    {
        InitializeComponent();
        EditedResource = resource;

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

        TitleBox.Text = resource.UserTitle;
        NotesBox.Text = resource.UserNotes;
        TagsBox.Text = string.Join(",", resource.UserTags);
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
        EditedResource.UserTitle = string.IsNullOrWhiteSpace(TitleBox.Text) ? null : TitleBox.Text.Trim();
        EditedResource.UserNotes = string.IsNullOrWhiteSpace(NotesBox.Text) ? null : NotesBox.Text.Trim();
        EditedResource.UserTags =
            TagsBox.Text
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .ToArray();

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