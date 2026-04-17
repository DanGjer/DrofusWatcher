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

            var validSpaces = selectedSpaces
                .Where(space => space.State == RevitSpaceState.Valid)
                .Where(space => !string.IsNullOrWhiteSpace(space.RevitRoomKeyValue))
                .ToList();

            var lookupKeys = validSpaces
                .Select(space => space.RevitRoomKeyValue!.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (lookupKeys.Count == 0)
            {
                var message = $"No valid values found for Revit parameter '{args.RoomKeyRevit}' on selected spaces.";
                TaskDialog.Show("Selected Spaces", message);
                return Result.Text.Succeeded(message);
            }

            // Create dRofus client using active Revit document
            var client = new dRofusClientFactory().Create(document);

            IReadOnlyList<Room> rooms;

            // Try a single batch request first for performance.
            try
            {
                var roomsQuery = Query.List()
                    .Select("id", args.RoomKeyDrofus, "name", "architect_no",
                        "room_data_20101610",
                        "room_data_20102210",
                        "room_data_20102310",
                        "room_data_21101010")
                    .Filter(Filter.In(args.RoomKeyDrofus, lookupKeys));

                rooms = client.GetRooms(roomsQuery).ToList();
            }
            catch
            {
                // If one key causes a bad request, retry per key and skip only failing values.
                var fallbackRooms = new List<Room>();
                var failedKeyCount = 0;

                for (var i = 0; i < lookupKeys.Count; i++)
                {
                    var key = lookupKeys[i];

                    try
                    {
                        var singleKeyQuery = Query.List()
                            .Select("id", args.RoomKeyDrofus, "name", "architect_no",
                                "room_data_20101610",
                                "room_data_20102210",
                                "room_data_20102310",
                                "room_data_21101010")
                            .Filter(Filter.Eq(args.RoomKeyDrofus, key));

                        fallbackRooms.AddRange(client.GetRooms(singleKeyQuery));
                    }
                    catch
                    {
                        failedKeyCount++;
                    }
                }

                if (failedKeyCount == lookupKeys.Count)
                    throw;

                rooms = fallbackRooms;
            }

            // Build key → room lookup using RoomFunctionNumber (strongly-typed) or args.RoomKeyDrofus via AdditionalProperties
            var roomByKey = rooms
                .Where(r => r.RoomFunctionNumber != null)
                .GroupBy(r => r.RoomFunctionNumber!.Trim(), StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

            foreach (var space in selectedSpaces)
            {
                if (space.RevitRoomKeyValue is not null &&
                    roomByKey.TryGetValue(space.RevitRoomKeyValue.Trim(), out var room))
                {
                    space.DrofusRoomName = room.Name;
                    space.Rfp = new RfpData
                    {
                        ArchitectNo      = room.RoomNumber?.ToString(),
                        NormalkraftUttak = room.AdditionalProperties.TryGetValue("room_data_20101610", out var v1) ? v1?.ToString() : null,
                        NodkraftUttak    = room.AdditionalProperties.TryGetValue("room_data_20102210", out var v2) ? v2?.ToString() : null,
                        UpsUttak         = room.AdditionalProperties.TryGetValue("room_data_20102310", out var v3) ? v3?.ToString() : null,
                        IktUttak         = room.AdditionalProperties.TryGetValue("room_data_21101010", out var v4) ? v4?.ToString() : null
                    };
                }
            }

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
                },
                spacesToEvaluate =>
                {
                    var complianceResults = Compliance.EvaluateSelectedSpaces(document, spacesToEvaluate);
                    foreach (var result in complianceResults)
                    {
                        result.Space.ActualElectricalUttak = result.Counts.TotalElectricalUttak;
                        result.Space.ActualDataUttak = result.Counts.TotalDataUttak;
                        result.Space.ActualTotalUttak = result.Counts.TotalUttak;
                        result.Space.IsCompliant = result.IsCompliant;
                    }
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