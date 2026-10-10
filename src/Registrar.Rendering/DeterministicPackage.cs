using System.IO.Compression;
using System.Text;
using System.Xml;
using DocumentFormat.OpenXml.Packaging;
using Registrar.Core.Model;

namespace Registrar.Rendering;

/// <summary>
/// Makes the rendered package a pure function of (AGR + config).
///
/// Two sources of per-render variance live below the Open XML object model and cannot be
/// removed by naming relationships:
///
/// 1. PackageProperties. Setting it emits OPC package metadata as
///    package/services/metadata/core-properties/{fresh-guid}.psmdcp. The GUID changes every
///    render, and Word and SharePoint do not read that part as document properties anyway,
///    so the metadata was both unstable and invisible. WriteCoreProperties replaces it with
///    a real docProps/core.xml, which is the part consumers actually read.
///
/// 2. Zip entry timestamps. System.IO.Packaging stamps every entry's LastWriteTime with the
///    wall clock on save, and exposes no way to set it. Repack rewrites the archive with a
///    fixed timestamp, preserving entry order and content byte for byte.
///
/// Generation time is deliberately absent from the document. It is run metadata and is
/// already recorded in validation-report.json timestampUtc; baking it into the artifact
/// would make the same request produce a different document every time, which is exactly
/// what artifactSha256 exists to disprove.
/// </summary>
internal static class DeterministicPackage
{
    /// <summary>Zip epoch. DOS timestamps cannot encode anything earlier.</summary>
    private static readonly DateTimeOffset ZipEpoch =
        new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static void WriteCoreProperties(WordprocessingDocument doc, AgrDocument agr)
    {
        var part = doc.AddNewPart<CoreFilePropertiesPart>(RelationshipIds.CoreProperties);

        using var stream = part.GetStream(FileMode.Create, FileAccess.Write);
        using var writer = XmlWriter.Create(stream, new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            Indent = false,
            CloseOutput = false,
        });

        const string cp = "http://schemas.openxmlformats.org/package/2006/metadata/core-properties";
        const string dc = "http://purl.org/dc/elements/1.1/";

        writer.WriteStartDocument();
        writer.WriteStartElement("cp", "coreProperties", cp);
        writer.WriteAttributeString("xmlns", "dc", null, dc);

        writer.WriteElementString("dc", "title", dc, agr.Identity.DocumentId);
        writer.WriteElementString("dc", "creator", dc, "registrar");
        writer.WriteElementString("contentStatus", cp, agr.Identity.ApprovalStatus);

        // No dcterms:created / dcterms:modified: see the class remarks.
        writer.WriteEndElement();
        writer.WriteEndDocument();
    }

    /// <summary>
    /// Rewrites the archive with fixed entry timestamps. Entry order and bytes are preserved,
    /// so the result is the same package with the wall clock taken out of it.
    /// </summary>
    public static byte[] Repack(byte[] docx)
    {
        using var source = new MemoryStream(docx);
        using var sourceZip = new ZipArchive(source, ZipArchiveMode.Read);

        using var target = new MemoryStream();
        using (var targetZip = new ZipArchive(target, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in sourceZip.Entries)
            {
                var copy = targetZip.CreateEntry(entry.FullName, CompressionLevel.Optimal);
                copy.LastWriteTime = ZipEpoch;

                using var from = entry.Open();
                using var to = copy.Open();
                from.CopyTo(to);
            }
        }

        return target.ToArray();
    }
}
