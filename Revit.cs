using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;

namespace DrofusWatcher;

public enum RevitSpaceState
{
    Valid,
    MissingParameter,
    EmptyValue
}

public class RevitSpace
{
    public required ElementId Id { get; init; }
    public string? RevitRoomKeyValue { get; init; }
    public RevitSpaceState State { get; init; }
    public long IdValue => Id.Value;

    public static RevitSpace FromSpace(Space space, string roomKeyRevit)
    {
        var parameter = space.LookupParameter(roomKeyRevit);
        var value = parameter?.AsString();

        return new RevitSpace
        {
            Id = space.Id,
            RevitRoomKeyValue = value,
            State = parameter is null
                ? RevitSpaceState.MissingParameter
                : string.IsNullOrWhiteSpace(value)
                    ? RevitSpaceState.EmptyValue
                    : RevitSpaceState.Valid
        };
    }
}

public static class RevitCollectors
{
    public static IEnumerable<RevitSpace> GetSelectedSpaces(IRevitExtensionContext context, string roomKeyRevit)
    {
        var activeDocument = context.UIApplication.ActiveUIDocument;
        var document = activeDocument?.Document;
        if (document is null || activeDocument is null)
            return [];

        var selectedIds = activeDocument.Selection.GetElementIds();

        return new FilteredElementCollector(document, selectedIds)
            .OfCategory(BuiltInCategory.OST_MEPSpaces)
            .WhereElementIsNotElementType()
            .Cast<Space>()
            .Select(space => RevitSpace.FromSpace(space, roomKeyRevit));
    }
}

