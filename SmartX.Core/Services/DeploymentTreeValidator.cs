using SmartX.Core.Models;

namespace SmartX.Core.Services;

public static class DeploymentTreeValidator
{
    public static ValidationResult Validate(DeploymentNode root, int maxDepth = 6)
        => ValidateRecursive(root, path: root.Name, depth: 1, maxDepth, seenDeviceIds: new HashSet<string>());

    private static ValidationResult ValidateRecursive(
        DeploymentNode node, string path, int depth, int maxDepth, HashSet<string> seenDeviceIds)
    {
        if (string.IsNullOrWhiteSpace(node.Name))
            return ValidationResult.Fail($"Unnamed node found at '{path}'.");

        if (depth > maxDepth)
            return ValidationResult.Fail($"'{path}' exceeds the maximum nesting depth of {maxDepth}.");

        if (node.DeviceId is not null && !seenDeviceIds.Add(node.DeviceId))
            return ValidationResult.Fail($"Duplicate device id '{node.DeviceId}' found at '{path}'.");

        if (node.Children.Count == 0)
            return ValidationResult.Ok(path);

        foreach (var child in node.Children)
        {
            var childPath = $"{path} -> {child.Name}";
            var childResult = ValidateRecursive(child, childPath, depth + 1, maxDepth, seenDeviceIds);
            if (!childResult.Success)
                return childResult;
        }

        return ValidationResult.Ok(path);
    }
}

public static class DeploymentTreeParser
{
    public static DeploymentNode Parse(string text)
    {
        var lines = text.Replace("\r", string.Empty)
                         .Split('\n', StringSplitOptions.RemoveEmptyEntries);

        if (lines.Length == 0)
            return new DeploymentNode { Name = "Facility" };

        var root = new DeploymentNode { Name = lines[0].Trim() };
        var stack = new Stack<(int Indent, DeploymentNode Node)>();
        stack.Push((0, root));

        for (int i = 1; i < lines.Length; i++)
        {
            var raw = lines[i];
            int indent = raw.TakeWhile(c => c == ' ').Count();
            var name = raw.Trim();
            if (name.Length == 0) continue;

            var node = new DeploymentNode
            {
                Name = name,
                DeviceId = name.StartsWith("NODE", StringComparison.OrdinalIgnoreCase) ? name : null
            };

            while (stack.Count > 1 && stack.Peek().Indent >= indent)
                stack.Pop();

            stack.Peek().Node.Children.Add(node);
            stack.Push((indent, node));
        }

        return root;
    }
}
