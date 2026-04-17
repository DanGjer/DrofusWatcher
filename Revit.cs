using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;

namespace DrofusWatcher;

public enum RevitSpaceState
{
    Valid,
    MissingParameter,
    EmptyValue
}

public class RfpData
{
    public string? ArchitectNo { get; init; }       // architect_no
    public string? NormalkraftUttak { get; init; }  // room_data_20101610
    public string? NodkraftUttak { get; init; }     // room_data_20102210
    public string? UpsUttak { get; init; }           // room_data_20102310
    public string? IktUttak { get; init; }           // room_data_21101010
}

public class RevitSpace
{
    public required ElementId Id { get; init; }
    public string? RevitRoomKeyValue { get; init; }
    public string? DrofusRoomName { get; set; }
    public RevitSpaceState State { get; init; }
    public long IdValue => Id.Value;
    public RfpData? Rfp { get; set; }
    public int? ActualElectricalUttak { get; set; }
    public int? ActualDataUttak { get; set; }
    public int? ActualTotalUttak { get; set; }
    public bool? IsCompliant { get; set; }
    public string ComplianceStatus => IsCompliant is null ? "Not checked" : IsCompliant.Value ? "OK" : "Fail";

    public string ActualElectricalUttakDisplay => ActualElectricalUttak?.ToString() ?? "-";
    public string ActualDataUttakDisplay => ActualDataUttak?.ToString() ?? "-";

    public string DrofusRoomNameDisplay
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(DrofusRoomName))
                return DrofusRoomName;

            if (State == RevitSpaceState.Valid && !string.IsNullOrWhiteSpace(RevitRoomKeyValue) && Rfp is null)
                return "(No dRofus match)";

            return string.Empty;
        }
    }

    public string StateDisplay
    {
        get
        {
            if (State != RevitSpaceState.Valid)
                return State.ToString();

            if (!string.IsNullOrWhiteSpace(RevitRoomKeyValue) && Rfp is null)
                return "MissingInDrofus";

            return State.ToString();
        }
    }

    public int? RfpElectricalTotal
    {
        get
        {
            if (Rfp is null)
                return null;

            var normalkraft = ParseNullableInt(Rfp.NormalkraftUttak);
            var nodkraft = ParseNullableInt(Rfp.NodkraftUttak);
            var ups = ParseNullableInt(Rfp.UpsUttak);

            var hasAny = normalkraft.HasValue || nodkraft.HasValue || ups.HasValue;
            if (!hasAny)
                return null;

            var sum = (normalkraft ?? 0) + (nodkraft ?? 0) + (ups ?? 0);
            return sum;
        }
    }

    public string RfpStatus
    {
        get
        {
            if (RfpElectricalTotal is null)
                return "No RFP data";

            if (ActualElectricalUttak < RfpElectricalTotal.Value)
                return "Under RFP";
            if (ActualElectricalUttak == RfpElectricalTotal.Value)
                return "On RFP";
            return "Over RFP";
        }
    }

    public int? RfpDataTotal
    {
        get
        {
            if (Rfp is null)
                return null;

            return ParseNullableInt(Rfp.IktUttak);
        }
    }

    public string ElectricalOutletColorStatus
    {
        get
        {
            if (RfpElectricalTotal is null || ActualElectricalUttak is null)
                return "Gray";

            if (ActualElectricalUttak < RfpElectricalTotal.Value)
                return "Red";
            if (ActualElectricalUttak == RfpElectricalTotal.Value)
                return "Green";
            return "Blue";
        }
    }

    public string DataOutletColorStatus
    {
        get
        {
            if (RfpDataTotal is null || ActualDataUttak is null)
                return "Gray";

            if (ActualDataUttak < RfpDataTotal.Value)
                return "Red";
            if (ActualDataUttak == RfpDataTotal.Value)
                return "Green";
            return "Blue";
        }
    }

    private static int? ParseNullableInt(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (int.TryParse(value, out var intValue))
            return intValue;

        if (double.TryParse(value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.GetCultureInfo("nb-NO"), out var doubleValue))
            return (int)doubleValue;

        var match = System.Text.RegularExpressions.Regex.Match(value, @"(\d+(?:[.,]\d+)?)");
        if (match.Success && double.TryParse(match.Groups[1].Value.Replace(",", "."), System.Globalization.CultureInfo.InvariantCulture, out var extracted))
            return (int)extracted;

        return null;
    }

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

