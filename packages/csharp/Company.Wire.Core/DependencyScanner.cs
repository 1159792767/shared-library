using System.Xml.Linq;

namespace Company.Wire.Core;

/// <summary>csproj 扫描器：提取 Company.* 依赖包名（向导据此自动生成依赖登记）与根命名空间。</summary>
public static class DependencyScanner
{
    public static ScanResult Scan(string csprojPath)
    {
        var doc = XDocument.Load(csprojPath);

        var packages = doc.Descendants("PackageReference")
            .Select(e => (string?)e.Attribute("Include") ?? string.Empty)
            .Where(id => id.StartsWith("Company.", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var rootNamespace = doc.Descendants("RootNamespace").FirstOrDefault()?.Value.Trim();
        if (string.IsNullOrEmpty(rootNamespace))
        {
            rootNamespace = Path.GetFileNameWithoutExtension(csprojPath);
        }

        return new ScanResult(packages, rootNamespace);
    }
}
