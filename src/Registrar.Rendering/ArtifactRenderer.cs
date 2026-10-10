using Registrar.Core;
using Registrar.Core.Config;
using Registrar.Core.Model;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Registrar.Rendering;

/// <summary>
/// Renders the DOCX. The output is a pure function of (AGR + config): no clock, no GUIDs,
/// no ambient state. That is what lets artifactSha256 prove the artifact, and it is why this
/// class takes no IClock. Generation time is run metadata and belongs in the validation
/// report, not in the document.
/// </summary>
public sealed class ArtifactRenderer
{
    private const string MainDocumentContentType =
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml";

    public RenderResult Render(AgrDocument agr, ConfigBundle config, byte[] logoBytes)
    {
        var style = config.Style;
        var ctx = new RenderContext(style, config.ClausesById, agr.DataInputs.PlaceholderMap, config.RadioPattern, config.WarnCallout, config.Clauses, config.CopyReadyMessage, config.CanonicalTokens);

        var clauseChecks = new List<Check>();
        var audits = new List<ClauseInsertionAudit>();
        var scanUnits = new List<ScanUnit>();

        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            // MainDocumentPart has a variable content type (document vs template), so it takes
            // the content-type overload. The id is explicit for the same reason as every other
            // part: an SDK-generated one is random and lands in _rels/.rels.
            var main = doc.AddNewPart<MainDocumentPart>(MainDocumentContentType, RelationshipIds.MainDocument);
            NumberingPart.Build(main);
            var body = new Body();

            var type = config.ArtifactTypesByCode[agr.Identity.ArtifactType];
            var footerId = FooterRenderer.Create(main, agr, type, style);

            var header = HeaderTableRenderer.Render(main, agr, type, style, logoBytes);
            body.Append(header);
            scanUnits.Add(new ScanUnit(header.InnerText, "document chrome"));
            foreach (var element in DchRenderer.Render(agr, style))
            {
                body.Append(element);
                scanUnits.Add(new ScanUnit(element.InnerText, "document chrome"));
            }
            if (style.Spacing.CalloutBeforePt > 0)
                body.Append(OpenXmlHelpers.Spacer(style.Spacing.CalloutBeforePt));
            var callout = ConfigCalloutRenderer.Render(agr, type, style);
            body.Append(callout);
            scanUnits.Add(new ScanUnit(callout.InnerText, "document chrome"));

            for (var s = 0; s < agr.DataInputs.Sections.Count; s++)
            {
                var section = SectionRenderer.Render(agr.DataInputs.Sections[s], s, ctx);
                foreach (var el in section.Elements) body.Append(el);
                clauseChecks.AddRange(section.ClauseChecks);
                audits.AddRange(section.Audits);
                scanUnits.AddRange(section.ScanUnits);
            }

            body.Append(SectionProps(style, footerId));
            main.Document = new Document(body);

            DeterministicPackage.WriteCoreProperties(doc, agr);
        }

        var policy = CharacterPolicy.FromConfig(config.ContentLint);
        var postRenderChecks = new List<Check>(clauseChecks);
        postRenderChecks.AddRange(PostRenderScan.Scan(scanUnits, policy));

        return new RenderResult(DeterministicPackage.Repack(stream.ToArray()), postRenderChecks, audits);
    }

    private static SectionProperties SectionProps(StyleRules style, string footerId)
    {
        var w = (uint)OpenXmlHelpers.Twips(style.Page.WidthIn);
        var h = (uint)OpenXmlHelpers.Twips(style.Page.HeightIn);
        var m = (uint)OpenXmlHelpers.Twips(style.Page.MarginsIn);
        return new SectionProperties(
            new FooterReference { Type = HeaderFooterValues.Default, Id = footerId },
            new PageSize { Width = w, Height = h },
            new PageMargin { Top = (int)m, Bottom = (int)m, Left = m, Right = m, Header = 720U, Footer = 720U, Gutter = 0U });
    }
}
