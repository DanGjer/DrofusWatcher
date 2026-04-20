using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace DrofusWatcher;

public class SpaceOutletCounts
{
    public int NormalkraftElectricalUttak { get; set; }
    public int NodkraftElectricalUttak { get; set; }
    public int UpsElectricalUttak { get; set; }
    public int UnknownElectricalUttak { get; set; }

    public int TotalElectricalUttak { get; set; }
    public int TotalDataUttak { get; set; }
    public int TotalUttak => TotalElectricalUttak + TotalDataUttak;
}

public class SpaceOutletRequirements
{
    public int? RequiredNormalkraftUttak { get; init; }
    public int? RequiredNodkraftUttak { get; init; }
    public int? RequiredUpsUttak { get; init; }
    public int? RequiredDataUttak { get; init; }

    public int? RequiredTotalElectricalUttak => SumNullable(
        RequiredNormalkraftUttak,
        RequiredNodkraftUttak,
        RequiredUpsUttak);

    public int? RequiredTotalUttak => SumNullable(
        RequiredTotalElectricalUttak,
        RequiredDataUttak);

    public static SpaceOutletRequirements FromRfp(RfpData? rfp)
    {
        return new SpaceOutletRequirements
        {
            RequiredNormalkraftUttak = Compliance.ParseNullableInt(rfp?.NormalkraftUttak),
            RequiredNodkraftUttak = Compliance.ParseNullableInt(rfp?.NodkraftUttak),
            RequiredUpsUttak = Compliance.ParseNullableInt(rfp?.UpsUttak),
            RequiredDataUttak = Compliance.ParseNullableInt(rfp?.IktUttak)
        };
    }

    private static int? SumNullable(params int?[] values)
    {
        if (values is null || values.Length == 0)
            return null;

        var hasAny = false;
        var sum = 0;

        for (var i = 0; i < values.Length; i++)
        {
            if (values[i].HasValue)
            {
                hasAny = true;
                sum += values[i]!.Value;
            }
        }

        return hasAny ? sum : null;
    }
}

public class SpaceComplianceResult
{
    public required RevitSpace Space { get; init; }
    public required SpaceOutletCounts Counts { get; init; }
    public required SpaceOutletRequirements Requirements { get; init; }

    public bool IsNormalkraftCompliant =>
        Compliance.IsCompliant(Counts.NormalkraftElectricalUttak, Requirements.RequiredNormalkraftUttak);

    public bool IsNodkraftCompliant =>
        Compliance.IsCompliant(Counts.NodkraftElectricalUttak, Requirements.RequiredNodkraftUttak);

    public bool IsUpsCompliant =>
        Compliance.IsCompliant(Counts.UpsElectricalUttak, Requirements.RequiredUpsUttak);

    public bool IsDataCompliant =>
        Compliance.IsCompliant(Counts.TotalDataUttak, Requirements.RequiredDataUttak);

    public bool IsTotalElectricalCompliant =>
        Compliance.IsCompliant(Counts.TotalElectricalUttak, Requirements.RequiredTotalElectricalUttak);

    public bool IsTotalUttakCompliant =>
        Compliance.IsCompliant(Counts.TotalUttak, Requirements.RequiredTotalUttak);

    public bool IsCompliant =>
        IsNormalkraftCompliant
        && IsNodkraftCompliant
        && IsUpsCompliant
        && IsDataCompliant
        && IsTotalElectricalCompliant
        && IsTotalUttakCompliant;
}

public static class Compliance
{
    public const string SUS_AntallDatauttak = "SUS_Antall Datauttak";
    public const string SUS_AntallStikkontaktuttak = "SUS_Antall Stikkontaktuttak";
    public const string Krafttype = "Krafttype";

    private const string KrafttypeNormalkraft = "NORMALKRAFT";
    private const string KrafttypeNodkraft = "NODKRAFT";
    private const string KrafttypeUps = "UPS";

    public static IReadOnlyList<SpaceComplianceResult> EvaluateSelectedSpaces(
        Document document,
        IEnumerable<RevitSpace> selectedSpaces,
        double spaceCatchOffsetMm = 0)
    {
        if (document is null || selectedSpaces is null)
            return Array.Empty<SpaceComplianceResult>();

        var selected = selectedSpaces
            .Where(s => s is not null)
            .ToList();

        if (selected.Count == 0)
            return Array.Empty<SpaceComplianceResult>();

        var bySpaceId = new Dictionary<long, SpaceComplianceResult>();

        for (var i = 0; i < selected.Count; i++)
        {
            var space = selected[i];

            if (!bySpaceId.ContainsKey(space.IdValue))
            {
                bySpaceId.Add(space.IdValue, new SpaceComplianceResult
                {
                    Space = space,
                    Counts = new SpaceOutletCounts(),
                    Requirements = SpaceOutletRequirements.FromRfp(space.Rfp)
                });
            }
        }

        var selectedSpaceElements = selected
            .Select(s => document.GetElement(s.Id))
            .OfType<Space>()
            .ToList();

        var geometryCatchOffsetInternal = spaceCatchOffsetMm > 0
            ? UnitUtils.ConvertToInternalUnits(spaceCatchOffsetMm, UnitTypeId.Millimeters)
            : 0;

        var phasesLatestFirst = new FilteredElementCollector(document)
            .OfClass(typeof(Phase))
            .Cast<Phase>()
            .OrderByDescending(p => p.Id.Value)
            .ToList();

        var categories = new List<ElementId>
        {
            new ElementId((long)BuiltInCategory.OST_ElectricalFixtures),
            new ElementId((long)BuiltInCategory.OST_DataDevices)
        };

        var categoryFilter = new ElementMulticategoryFilter(categories);

        var instances = new FilteredElementCollector(document)
            .WherePasses(categoryFilter)
            .WhereElementIsNotElementType()
            .OfClass(typeof(FamilyInstance))
            .Cast<FamilyInstance>();

        foreach (var instance in instances)
        {
            var ownerSpace = GetOwningSpaceLatestFirst(instance, phasesLatestFirst);
            if ((ownerSpace is null || !bySpaceId.ContainsKey(ownerSpace.Id.Value))
                && geometryCatchOffsetInternal > 0)
            {
                ownerSpace = GetOwningSpaceByGeometry(instance, selectedSpaceElements, geometryCatchOffsetInternal);
            }

            if (ownerSpace is null)
                continue;

            if (!bySpaceId.TryGetValue(ownerSpace.Id.Value, out var result))
                continue;

            var dataCount = GetTypeCount(instance, document, SUS_AntallDatauttak);
            if (dataCount > 0)
                result.Counts.TotalDataUttak += dataCount;

            var stikkCount = GetTypeCount(instance, document, SUS_AntallStikkontaktuttak);
            if (stikkCount <= 0)
                continue;

            result.Counts.TotalElectricalUttak += stikkCount;

            var krafttype = NormalizeKrafttype(GetStringValue(instance.LookupParameter(Krafttype)));
            switch (krafttype)
            {
                case KrafttypeNormalkraft:
                    result.Counts.NormalkraftElectricalUttak += stikkCount;
                    break;

                case KrafttypeNodkraft:
                    result.Counts.NodkraftElectricalUttak += stikkCount;
                    break;

                case KrafttypeUps:
                    result.Counts.UpsElectricalUttak += stikkCount;
                    break;

                default:
                    result.Counts.UnknownElectricalUttak += stikkCount;
                    break;
            }
        }

        var orderedResults = new List<SpaceComplianceResult>(selected.Count);
        for (var i = 0; i < selected.Count; i++)
        {
            var id = selected[i].IdValue;
            if (bySpaceId.TryGetValue(id, out var result))
                orderedResults.Add(result);
        }

        return orderedResults;
    }

    internal static bool IsCompliant(int actual, int? required)
    {
        if (!required.HasValue)
            return true;

        return actual >= required.Value;
    }

    internal static int? ParseNullableInt(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();

        if (TryParseIntOrDouble(trimmed, out var parsed))
            return parsed;

        var match = Regex.Match(trimmed, @"-?\d+([.,]\d+)?");
        if (match.Success && TryParseIntOrDouble(match.Value, out parsed))
            return parsed;

        return null;
    }

    private static Space? GetOwningSpaceLatestFirst(FamilyInstance instance, IReadOnlyList<Phase> phasesLatestFirst)
    {
        if (instance is null || phasesLatestFirst is null || phasesLatestFirst.Count == 0)
            return null;

        for (var i = 0; i < phasesLatestFirst.Count; i++)
        {
            try
            {
                var space = instance.get_Space(phasesLatestFirst[i]);
                if (space is not null)
                    return space;
            }
            catch
            {
                // get_Space can throw for some families/phases; ignore and continue.
            }
        }

        return null;
    }

    private static int GetTypeCount(FamilyInstance instance, Document document, string parameterName)
    {
        if (instance is null || document is null || string.IsNullOrWhiteSpace(parameterName))
            return 0;

        var typeId = instance.GetTypeId();
        if (typeId == ElementId.InvalidElementId)
            return 0;

        var typeElement = document.GetElement(typeId);
        if (typeElement is null)
            return 0;

        return ParseParameterAsInt(typeElement.LookupParameter(parameterName));
    }

    private static Space? GetOwningSpaceByGeometry(
        FamilyInstance instance,
        IReadOnlyList<Space> candidateSpaces,
        double offset)
    {
        if (instance is null || candidateSpaces is null || candidateSpaces.Count == 0)
            return null;

        var basePoint = GetReferencePoint(instance);
        if (basePoint is null)
            return null;

        var pointsToTest = BuildSamplePoints(basePoint, offset);

        for (var i = 0; i < candidateSpaces.Count; i++)
        {
            var space = candidateSpaces[i];
            for (var j = 0; j < pointsToTest.Count; j++)
            {
                try
                {
                    if (space.IsPointInSpace(pointsToTest[j]))
                        return space;
                }
                catch
                {
                    // Some spaces can throw for geometric checks; ignore and continue.
                }
            }
        }

        return null;
    }

    private static XYZ? GetReferencePoint(FamilyInstance instance)
    {
        if (instance.Location is LocationPoint locationPoint)
            return locationPoint.Point;

        if (instance.Location is LocationCurve locationCurve)
        {
            var curve = locationCurve.Curve;
            if (curve is not null)
                return curve.Evaluate(0.5, true);
        }

        var boundingBox = instance.get_BoundingBox(null);
        if (boundingBox is null)
            return null;

        return (boundingBox.Min + boundingBox.Max) * 0.5;
    }

    private static List<XYZ> BuildSamplePoints(XYZ point, double offset)
    {
        var points = new List<XYZ> { point };

        if (offset <= 0)
            return points;

        var offsets = new[]
        {
            new XYZ(offset, 0, 0),
            new XYZ(-offset, 0, 0),
            new XYZ(0, offset, 0),
            new XYZ(0, -offset, 0),
            new XYZ(offset, offset, 0),
            new XYZ(offset, -offset, 0),
            new XYZ(-offset, offset, 0),
            new XYZ(-offset, -offset, 0)
        };

        for (var i = 0; i < offsets.Length; i++)
            points.Add(point + offsets[i]);

        return points;
    }

    private static int ParseParameterAsInt(Parameter? parameter)
    {
        if (parameter is null)
            return 0;

        switch (parameter.StorageType)
        {
            case StorageType.Integer:
                return parameter.AsInteger();

            case StorageType.Double:
                return ConvertToInt(parameter.AsDouble());

            case StorageType.String:
                return ParseNullableInt(parameter.AsString()) ?? 0;

            case StorageType.ElementId:
                return ParseNullableInt(parameter.AsValueString()) ?? 0;

            default:
                return ParseNullableInt(parameter.AsValueString()) ?? 0;
        }
    }

    private static string? GetStringValue(Parameter? parameter)
    {
        if (parameter is null)
            return null;

        if (parameter.StorageType == StorageType.String)
            return parameter.AsString();

        return parameter.AsValueString();
    }

    private static bool TryParseIntOrDouble(string input, out int value)
    {
        if (int.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            return true;

        if (int.TryParse(input, NumberStyles.Integer, CultureInfo.GetCultureInfo("nb-NO"), out value))
            return true;

        if (int.TryParse(input, NumberStyles.Integer, CultureInfo.CurrentCulture, out value))
            return true;

        if (double.TryParse(input, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var d1))
        {
            value = ConvertToInt(d1);
            return true;
        }

        if (double.TryParse(input, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.GetCultureInfo("nb-NO"), out var d2))
        {
            value = ConvertToInt(d2);
            return true;
        }

        if (double.TryParse(input, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out var d3))
        {
            value = ConvertToInt(d3);
            return true;
        }

        value = 0;
        return false;
    }

    private static int ConvertToInt(double value)
    {
        return (int)Math.Round(value, MidpointRounding.AwayFromZero);
    }

    private static string NormalizeKrafttype(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = RemoveDiacritics(value).Trim().ToUpperInvariant();

        if (normalized.Contains("UPS"))
            return KrafttypeUps;

        if (normalized.Contains("NODKRAFT"))
            return KrafttypeNodkraft;

        if (normalized.Contains("NORMALKRAFT"))
            return KrafttypeNormalkraft;

        return normalized;
    }

    private static string RemoveDiacritics(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);

        for (var i = 0; i < normalized.Length; i++)
        {
            var ch = normalized[i];
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                builder.Append(ch);
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}