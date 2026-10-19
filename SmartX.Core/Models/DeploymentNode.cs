namespace SmartX.Core.Models;

public class DeploymentNode
{
    public string Name { get; set; } = string.Empty;
    public string? DeviceId { get; set; }
    public List<DeploymentNode> Children { get; init; } = new();
}

public class ValidationResult
{
    public bool Success { get; init; }
    public string? Path { get; init; }
    public string? Error { get; init; }

    public static ValidationResult Ok(string path) => new() { Success = true, Path = path };
    public static ValidationResult Fail(string error) => new() { Success = false, Error = error };
}
