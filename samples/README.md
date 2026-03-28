# Samples

## ResourceRouter.SampleTextPlugin

Minimal plugin implementing `IFormatConverter` for `text/plain`.

Build output DLL and copy it to:
- `%LOCALAPPDATA%/ResourceRouter/plugins/`

Then restart app to load plugin via `PluginHost.LoadPlugins(...)`.
