using Dock.Serializer.SystemTextJson;

[assembly: DockJsonSourceGeneration]
// Registers LogViewDocument for Dock layout serialization so restored layout.json
// deserializes as LogViewDocument (not base Document), and document content templates match.
[assembly: DockJsonSerializable(typeof(NovaLog.Avalonia.Docking.LogViewDocument))]
