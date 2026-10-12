using System.Xml.Linq;

namespace Company.Wire.Core;

/// <summary>
/// csproj 编辑器：以 XML API 幂等添加 PackageReference（绝不整文本替换，保留既有内容与格式）。
/// 修改前把原文件备份为 &lt;原文件&gt;.bak（回滚与安全底线，见 WireRunner.Rollback）。
/// </summary>
public static class CsprojEditor
{
    /// <summary>添加包引用。返回 true=本次新增；false=同名引用已存在（文件未动，幂等）。</summary>
    public static bool TryAddPackageReference(string csprojPath, string packageId, string version)
    {
        var doc = XDocument.Load(csprojPath);

        var exists = doc.Descendants("PackageReference")
            .Any(e => string.Equals((string?)e.Attribute("Include"), packageId, StringComparison.OrdinalIgnoreCase));
        if (exists)
        {
            return false;
        }

        // 无条件写备份：只在确有修改时才写盘，所以这里一定是改前状态
        File.Copy(csprojPath, csprojPath + ".bak", overwrite: true);

        var itemGroup = doc.Descendants("ItemGroup")
            .FirstOrDefault(g => g.Elements("PackageReference").Any());
        if (itemGroup is null)
        {
            itemGroup = new XElement("ItemGroup");
            doc.Root!.Add(itemGroup);
        }

        itemGroup.Add(new XElement("PackageReference",
            new XAttribute("Include", packageId),
            new XAttribute("Version", version)));

        doc.Save(csprojPath);
        return true;
    }
}
