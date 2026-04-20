namespace DrofusWatcher;

public enum RunMode
{
    [Description("Run framework command")]
    Execute
}

public class AssistantArgs
{
    [Description("Room key (Revit)"), ControlData(ToolTip = "Select operation mode")]
    public string RoomKeyRevit { get; set; } = string.Empty;

    [Description("Room key (dRofus)"), ControlData(ToolTip = "Select operation mode")]
    public string RoomKeyDrofus { get; set; } = string.Empty;

    [Description("Space catch offset (mm)"), ControlData(ToolTip = "Offset used for geometry fallback when element is just outside a space")]
    public double SpaceCatchOffsetMm { get; set; } = 150;
}