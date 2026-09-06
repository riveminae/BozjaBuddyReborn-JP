using System.Security.Cryptography;
using Lumina;
using Lumina.Data.Files;
using Lumina.Data.Parsing.Uld;

// Offline metadata only: no process access, game input, callbacks, or asset export.
try
{
    if (args.Length != 1 || !Directory.Exists(args[0]))
        throw new ArgumentException("Pass the existing game sqpack directory as the only argument.");

    const string asset = "ui/uld/MYCBattleAreaInfo.uld";
    using var game = new GameData(args[0]);
    var layout = game.GetFile<UldFile>(asset) ?? throw new InvalidOperationException("Recruitment layout was not found.");
    Console.WriteLine($"Asset: {asset}; SHA256: {Convert.ToHexString(SHA256.HashData(layout.Data))}");
    Console.WriteLine($"Lumina: {typeof(GameData).Assembly.GetName().Version}");
    PrintNodes("widget", layout.WidgetData.Nodes);
    foreach (var component in layout.Components)
    {
        Console.WriteLine($"Component: {component.Id}; type: {component.Type}");
        PrintNodes($"component:{component.Id}", component.Nodes);
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"FAIL: {ex.GetType().Name}: {ex.Message}");
    Environment.ExitCode = 1;
}

static void PrintNodes(string scope, UldRoot.NodeData[] nodes)
{
    foreach (var node in nodes)
        Console.WriteLine($"{scope} node={node.NodeId} parent={node.ParentId} child={node.ChildNodeId} " +
            $"next={node.NextSiblingId} previous={node.PrevSiblingId} type={node.NodeType} " +
            $"x={node.X} y={node.Y} width={node.W} height={node.H}");
}
