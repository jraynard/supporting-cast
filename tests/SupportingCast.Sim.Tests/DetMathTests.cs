using System.Text.RegularExpressions;

namespace SupportingCast.Sim.Tests;

/// <summary>
/// The sim calls Narsil's platform-exact <see cref="DetMath"/> (co-op peers on different machines must
/// agree); Narsil tests its accuracy, this keeps the C runtime's trig out of SupportingCast.Sim.
/// </summary>
public class DetMathTests
{
    /// <summary>Calls the C runtime makes, which may round differently per OS and CPU.</summary>
    private static readonly Regex Platform = new(@"\bMath[F]?\.(Sin|Cos|Tan|Asin|Acos|Atan2?|Sinh|Cosh|Tanh|Pow|Exp|Log\w*|Cbrt)\s*\(");

    [Fact]
    public void TheSimUsesNoPlatformTrig()
    {
        var src = Path.Combine(Path.GetDirectoryName(TestWorld.FindDefsDirectory())!, "..", "src", "SupportingCast.Sim");
        var found = new List<string>();
        foreach (var file in Directory.GetFiles(src, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") || file.EndsWith("DetMath.cs"))
                continue;
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
                if (Platform.IsMatch(lines[i]) && !lines[i].TrimStart().StartsWith("//"))
                    found.Add($"{Path.GetRelativePath(src, file)}:{i + 1}: {lines[i].Trim()}");
        }
        Assert.True(found.Count == 0, "Use DetMath instead (co-op peers on other platforms would desync):\n" + string.Join("\n", found));
    }
}
