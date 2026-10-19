namespace SmartX.Core.Models;

public enum SensorCategory
{
    Environmental,
    PowerConsumption,
    Actuator
}

public class SensorRegistration
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string MacAddress { get; set; } = string.Empty;

    public string Location { get; set; } = string.Empty;

    public SensorCategory Category { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public string DeviceKind { get; set; } = string.Empty;

    public DateTimeOffset RegisteredAt { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset LastSeen { get; set; } = DateTimeOffset.UtcNow;

    public List<SensorAttachment> Attachments { get; init; } = new();
}

public class SensorAttachment
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public long SizeBytes { get; set; }
    public string StoredPath { get; set; } = string.Empty;
    public DateTimeOffset UploadedAt { get; set; } = DateTimeOffset.UtcNow;
}
