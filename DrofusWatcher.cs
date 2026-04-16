using dRofusClient.Rooms;

namespace DrofusWatcher;

public class DrofusWatcherCommand : IRevitExtension<AssistantArgs>
{
    public IExtensionResult Run(IRevitExtensionContext context, AssistantArgs args, CancellationToken cancellationToken)
    {
        var uiDocument = context.UIApplication.ActiveUIDocument;
        var document = uiDocument?.Document;

        if (document is null)
            return Result.Text.Failed("Revit has no active model open");

        if (string.IsNullOrWhiteSpace(args.RoomKeyRevit))
            return Result.Text.Failed("RoomKeyRevit must be provided.");

        try
        {
            var selectedSpaces = RevitCollectors.GetSelectedSpaces(context, args.RoomKeyRevit).ToList();

            if (selectedSpaces.Count == 0)
            {
                TaskDialog.Show("Selected Spaces", "No MEP Spaces are selected.");
                return Result.Text.Succeeded("No MEP Spaces are selected.");
            }

            // Create dRofus client using active Revit document
            var client = new dRofusClientFactory().Create(document);

            // Build query to get rooms
            var queryRoomLogs = Query.List()
                .Select(args.RoomKeyDrofus, "action", "field", "old_value", "new_value")
                .Filter(Filter.Eq(args.RoomKeyDrofus, "02.01.0547"))
                .Filter(Filter.Eq("action", "Change"))
                .Filter(Filter.StartsWith("field", "Elkraft"));

            // Execute query and get results
            var allRooms = client.GetRoomLogs(queryRoomLogs);

            var window = new SelectedSpacesWindow(
                selectedSpaces,
                args.RoomKeyRevit,
                space =>
                {
                    uiDocument!.Selection.SetElementIds([space.Id]);
                },
                space =>
                {
                    uiDocument!.ShowElements(space.Id);
                });
            window.ShowModal(context.UIApplication.MainWindowHandle);




            return Result.Text.Succeeded($"Displayed {selectedSpaces.Count} selected MEP spaces.");
        }
        catch (Exception ex)
        {
            return Result.Text.Failed($"Error retrieving rooms from dRofus: {ex.Message}");
        }
    }
}