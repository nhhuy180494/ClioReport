using ClioReport.Model;
using ClioReport.Rdlc;
using Xunit;

namespace ClioReport.Tests;

public class RdlcImporterTests
{
    private const string Ns = "http://schemas.microsoft.com/sqlserver/reporting/2008/01/reportdefinition";

    private static string Cell(string value, string style = "", string name = "t") => $$"""
        <TablixCell><CellContents><Textbox Name="{{name}}"><CanGrow>true</CanGrow><Paragraphs><Paragraph><TextRuns><TextRun>
          <Value>{{value}}</Value><Style>{{style}}</Style></TextRun></TextRuns><Style><TextAlign>Right</TextAlign></Style></Paragraph></Paragraphs>
          <Style><Border><Style>Solid</Style></Border><PaddingLeft>2pt</PaddingLeft></Style></Textbox></CellContents></TablixCell>
        """;

    private static string Sample() => $$"""
        <Report xmlns="{{Ns}}">
          <ReportSections><ReportSection>
            <Body>
              <ReportItems>
                <Textbox Name="Title"><Top>0cm</Top><Left>0cm</Left><Height>1cm</Height><Width>10cm</Width>
                  <Paragraphs><Paragraph><TextRuns><TextRun><Value>PHIẾU NHẬP KHO</Value>
                    <Style><FontSize>16pt</FontSize><FontWeight>Bold</FontWeight></Style></TextRun></TextRuns><Style><TextAlign>Center</TextAlign></Style></Paragraph></Paragraphs></Textbox>
                <Tablix Name="Tablix1"><Top>1.5cm</Top><Left>0cm</Left><Height>2cm</Height><Width>10cm</Width>
                  <DataSetName>items</DataSetName>
                  <TablixBody>
                    <TablixColumns><TablixColumn><Width>4cm</Width></TablixColumn><TablixColumn><Width>6cm</Width></TablixColumn></TablixColumns>
                    <TablixRows>
                      <TablixRow><Height>0.6cm</Height><TablixCells>{{Cell("Mã", "<FontWeight>Bold</FontWeight>")}}{{Cell("SL")}}</TablixCells></TablixRow>
                      <TablixRow><Height>0.6cm</Height><TablixCells>{{Cell("=Fields!code.Value")}}{{Cell("=Fields!qty.Value", "<Format>N0</Format>")}}</TablixCells></TablixRow>
                      <TablixRow><Height>0.6cm</Height><TablixCells>{{Cell("Tổng kho")}}{{Cell("=Sum(Fields!qty.Value)")}}</TablixCells></TablixRow>
                      <TablixRow><Height>0.6cm</Height><TablixCells>{{Cell("Tổng")}}{{Cell("=Sum(Fields!qty.Value, &quot;DataSet1&quot;) &amp; &quot; pcs&quot;")}}</TablixCells></TablixRow>
                    </TablixRows>
                  </TablixBody>
                  <TablixRowHierarchy><TablixMembers>
                    <TablixMember/>
                    <TablixMember><Group Name="kho"><GroupExpressions><GroupExpression>=Fields!kho.Value</GroupExpression></GroupExpressions></Group>
                      <TablixMembers>
                        <TablixMember><Group Name="Details"/></TablixMember>
                        <TablixMember/>
                      </TablixMembers>
                    </TablixMember>
                    <TablixMember/>
                  </TablixMembers></TablixRowHierarchy>
                </Tablix>
                <Textbox Name="Sign"><Top>5cm</Top><Left>0cm</Left><Height>1cm</Height><Width>5cm</Width>
                  <Paragraphs><Paragraph><TextRuns><TextRun><Value>Người lập</Value></TextRun></TextRuns></Paragraph></Paragraphs></Textbox>
              </ReportItems>
              <Height>6cm</Height>
            </Body><Width>10cm</Width>
          </ReportSection></ReportSections>
          <Page>
            <PageHeader><Height>1cm</Height><ReportItems>
              <Textbox Name="Co"><Top>0cm</Top><Left>0cm</Left><Height>0.5cm</Height><Width>5cm</Width>
                <Paragraphs><Paragraph><TextRuns><TextRun><Value>=Fields!company.Value</Value></TextRun></TextRuns></Paragraph></Paragraphs></Textbox>
            </ReportItems></PageHeader>
            <PageFooter><Height>1cm</Height><ReportItems>
              <Textbox Name="Pg"><Top>0cm</Top><Left>0cm</Left><Height>0.5cm</Height><Width>5cm</Width>
                <Paragraphs><Paragraph><TextRuns><TextRun><Value>="Trang " &amp; Globals!PageNumber &amp; "/" &amp; Globals!TotalPages</Value></TextRun></TextRuns></Paragraph></Paragraphs></Textbox>
            </ReportItems></PageFooter>
            <PageHeight>29.7cm</PageHeight><PageWidth>21cm</PageWidth>
            <LeftMargin>1cm</LeftMargin><RightMargin>1cm</RightMargin><TopMargin>1cm</TopMargin><BottomMargin>1cm</BottomMargin>
          </Page>
        </Report>
        """;

    [Fact]
    public void Page_IsConverted_ToMillimetres()
    {
        var def = RdlcImporter.ImportXml(Sample()).Definition;

        Assert.Equal(210, def.Page.Width, 3);
        Assert.Equal(297, def.Page.Height, 3);
        Assert.Equal(10, def.Page.Margin, 3);
        Assert.Equal("items", def.DataSource);
    }

    [Fact]
    public void Bands_FollowTheTablixStructure()
    {
        var def = RdlcImporter.ImportXml(Sample()).Definition;

        Assert.Equal(
            new[] { "PageHeader", "ReportHeader", "ColumnHeader", "Detail", "GroupFooter", "ReportFooter", "PageFooter" },
            def.Bands.Select(b => b.Type.ToString()));
        var group = Assert.Single(def.Groups);
        Assert.Equal("kho", group.Name);
        Assert.Equal("kho", group.Expression);
        Assert.Equal("kho", def.Bands.Single(b => b.Type == Model.BandType.GroupFooter).Group);
    }

    [Fact]
    public void Textbox_StyleAndGeometry_AreConverted()
    {
        var def = RdlcImporter.ImportXml(Sample()).Definition;

        var title = def.Bands.Single(b => b.Type == Model.BandType.ReportHeader).Elements.Single(e => e.Text == "PHIẾU NHẬP KHO");
        Assert.True(title.Bold);
        Assert.Equal(16, title.FontSize, 3);
        Assert.Equal(Model.TextAlign.Center, title.Align);
        Assert.Equal(100, title.Width, 3);
        Assert.Equal(10, title.Height, 3);

        var header = def.Bands.Single(b => b.Type == Model.BandType.ColumnHeader);
        Assert.Equal(6, header.Height, 3);
        Assert.Equal(new[] { 0.0, 40.0 }, header.Elements.Select(e => e.X));
        Assert.All(header.Elements, e => Assert.True(e.Border));
        Assert.True(header.Elements[0].Bold);
    }

    [Fact]
    public void Expressions_AreTranslated_AndAggregatesCreated()
    {
        var result = RdlcImporter.ImportXml(Sample());
        var def = result.Definition;

        Assert.Empty(result.Warnings);
        var detail = def.Bands.Single(b => b.Type == Model.BandType.Detail);
        Assert.Equal("code", detail.Elements[0].Expression);
        Assert.Equal("Format(qty, 'N0')", detail.Elements[1].Expression);

        Assert.Equal(2, def.Aggregates.Count);
        Assert.Contains(def.Aggregates, a => a.Expression == "qty" && a.Group == "kho");
        Assert.Contains(def.Aggregates, a => a.Expression == "qty" && a.Group is null);

        var pageFooter = def.Bands.Single(b => b.Type == Model.BandType.PageFooter).Elements.Single();
        Assert.Equal("'' + ('Trang ') + (PageNumber) + ('/') + (PageCount)", pageFooter.Expression);
    }

    [Fact]
    public void FreeItemsBelowTheTablix_GoToTheReportFooter()
    {
        var def = RdlcImporter.ImportXml(Sample()).Definition;

        var footer = def.Bands.Single(b => b.Type == Model.BandType.ReportFooter);
        var sign = footer.Elements.Single(e => e.Text == "Người lập");
        Assert.True(sign.Y >= 6 - 0.001);   // after the 'Tổng' row (6 mm)
    }

    [Fact]
    public void Imported_Report_Renders_WithTheEngine()
    {
        var result = RdlcImporter.ImportXml(Sample());
        var data = new Dictionary<string, object?>
        {
            ["company"] = "Sample",
            ["items"] = new List<Dictionary<string, object?>>
            {
                new() { ["kho"] = "A", ["code"] = "X1", ["qty"] = 1200m },
                new() { ["kho"] = "A", ["code"] = "X2", ["qty"] = 300m },
                new() { ["kho"] = "B", ["code"] = "Y1", ["qty"] = 5m }
            }
        };

        var rendered = ReportEngine.Render(result.Definition, new ObjectDataContext(data));
        var texts = rendered.Pages.SelectMany(p => p.Elements).Select(e => e.Text).ToList();

        Assert.Contains("1,200", texts);
        Assert.Contains("1500", texts);     // kho A total
        Assert.Contains("1505 pcs", texts); // report total
        Assert.Contains("Trang 1/1", texts);
        Assert.Contains("Sample", texts);
    }

    [Fact]
    public void Unsupported_Things_AreReported_NotGuessed()
    {
        var xml = Sample().Replace("=Fields!code.Value", "=Switch(Fields!qty.Value &gt; 0, &quot;a&quot;)");
        var result = RdlcImporter.ImportXml(xml);

        var warning = Assert.Single(result.Warnings);
        Assert.Contains("Switch", warning);
        var detail = result.Definition.Bands.Single(b => b.Type == Model.BandType.Detail);
        Assert.Null(detail.Elements[0].Expression);
    }

    private static string WithImage(string image)
    {
        var xml = Sample();
        var at = xml.IndexOf("<ReportItems>", StringComparison.Ordinal) + "<ReportItems>".Length;
        return xml.Insert(at, image);
    }

    [Fact]
    public void Embedded_image_keeps_its_place_and_is_bound_to_a_field()
    {
        var xml = WithImage("<Image Name=\"Logo\"><Source>Embedded</Source><Value>company-logo</Value><Top>0cm</Top><Left>1cm</Left><Height>1.5cm</Height><Width>3cm</Width></Image>");
        var result = RdlcImporter.ImportXml(xml);

        var image = result.Definition.Bands.SelectMany(b => b.Elements).Single(e => e.Type == "image");
        Assert.Equal("company_logo", image.Expression);
        Assert.Equal(10, image.X, 1);
        Assert.Equal(30, image.Width, 1);
        Assert.Equal(15, image.Height, 1);
        Assert.Contains(result.Warnings, w => w.Contains("company_logo") && w.Contains("byte[]"));
    }

    [Fact]
    public void Database_image_uses_its_field()
    {
        var xml = WithImage("<Image Name=\"Pic\"><Source>Database</Source><Value>=Fields!photo.Value</Value><Top>0cm</Top><Left>0cm</Left><Height>1cm</Height><Width>1cm</Width></Image>");
        var result = RdlcImporter.ImportXml(xml);

        var image = result.Definition.Bands.SelectMany(b => b.Elements).Single(e => e.Type == "image");
        Assert.Equal("photo", image.Expression);
        Assert.DoesNotContain(result.Warnings, w => w.Contains("Pic"));
    }

    [Fact]
    public void NotAReport_Throws()
    {
        Assert.Throws<ReportException>(() => RdlcImporter.ImportXml("<html/>"));
    }

    [Fact]
    public void Page_inside_ReportSection_is_read()
    {
        var xml = $"""
            <Report xmlns="{Ns}"><ReportSections><ReportSection>
              <Body><ReportItems/><Height>2cm</Height></Body><Width>19cm</Width>
              <Page><PageHeight>29.7cm</PageHeight><PageWidth>21cm</PageWidth><LeftMargin>1cm</LeftMargin><RightMargin>1cm</RightMargin>
                <TopMargin>1cm</TopMargin><BottomMargin>1cm</BottomMargin></Page>
            </ReportSection></ReportSections></Report>
            """;
        var def = RdlcImporter.ImportXml(xml).Definition;
        Assert.Equal(210, def.Page.Width, 1);
        Assert.Equal(297, def.Page.Height, 1);
        Assert.Equal(10, def.Page.Margin, 1);
    }

    private static string Report(string items) => $"""
        <Report xmlns="{Ns}"><ReportSections><ReportSection>
          <Body><ReportItems>{items}</ReportItems><Height>10cm</Height></Body><Width>19cm</Width>
        </ReportSection></ReportSections></Report>
        """;

    private static string Box(string name, string paragraphs, string geometry = "<Top>0cm</Top><Left>0cm</Left><Height>2cm</Height><Width>10cm</Width>") =>
        $"<Textbox Name=\"{name}\">{geometry}<Paragraphs>{paragraphs}</Paragraphs></Textbox>";

    private static string Para(string text, string runStyle = "") =>
        $"<Paragraph><TextRuns><TextRun><Value>{text}</Value><Style>{runStyle}</Style></TextRun></TextRuns></Paragraph>";

    [Fact]
    public void First_OfAField_HasNoExtraParentheses()
    {
        var xml = Report(Box("T", Para("=First(Fields!address.Value, &quot;DS&quot;)")));
        var e = RdlcImporter.ImportXml(xml).Definition.Bands[0].Elements[0];
        Assert.Equal("address", e.Expression);
    }

    [Fact]
    public void First_OfAnExpression_KeepsItsParentheses()
    {
        var xml = Report(Box("T", Para("=First(Fields!a.Value + Fields!b.Value, &quot;DS&quot;) * 2")));
        var e = RdlcImporter.ImportXml(xml).Definition.Bands[0].Elements[0];
        Assert.Equal("(a + b) * 2", e.Expression);
    }

    [Fact]
    public void IIf_and_First_are_translated()
    {
        var xml = Report(Box("T", Para("=IIf(Fields!a.Value = &quot;P&quot;, &quot;Pass&quot;, &quot;Fail&quot;) &amp; First(Fields!b.Value, &quot;DS&quot;)")));
        var result = RdlcImporter.ImportXml(xml);
        var e = result.Definition.Bands[0].Elements[0];
        Assert.Contains("?", e.Expression);
        Assert.Contains("b", e.Expression);
        Assert.True(!result.Warnings.Any(w => w.Contains("skipped")));
        Assert.True(result.Warnings.Any(w => w.Contains("First")));
    }

    [Fact]
    public void Paragraphs_are_separated_by_line_breaks()
    {
        var xml = Report(Box("T", Para("one") + Para("two")));
        var e = RdlcImporter.ImportXml(xml).Definition.Bands[0].Elements[0];
        Assert.Equal("one\ntwo", e.Text);
    }

    [Fact]
    public void Paragraphs_with_different_styles_become_separate_elements()
    {
        var xml = Report(Box("T", Para("Title") + Para("Sub", "<FontSize>9pt</FontSize><Color>Red</Color>")));
        var elements = RdlcImporter.ImportXml(xml).Definition.Bands[0].Elements;
        Assert.Equal(2, elements.Count);
        Assert.Equal("Title", elements[0].Text);
        Assert.Equal("Sub", elements[1].Text);
        Assert.Equal(9, elements[1].FontSize);
        Assert.NotNull(elements[1].Color);
        Assert.True(elements[1].Y > elements[0].Y);
    }

    [Fact]
    public void Column_hierarchy_headers_become_the_column_header_band()
    {
        string Header(string name, string text, double cm, string kids = "") =>
            $"<TablixMember><TablixHeader><Size>{cm}cm</Size><CellContents>{Box(name, Para(text), "")}</CellContents></TablixHeader>{kids}</TablixMember>";
        var cells = Cell("=Fields!a.Value", "", "c1") + Cell("=Fields!b.Value", "", "c2") + Cell("=Fields!c.Value", "", "c3");
        var xml = Report($"""
            <Tablix Name="T"><Top>0cm</Top><Left>0cm</Left><Height>3cm</Height><Width>9cm</Width><DataSetName>ds</DataSetName>
              <TablixBody>
                <TablixColumns><TablixColumn><Width>3cm</Width></TablixColumn><TablixColumn><Width>3cm</Width></TablixColumn><TablixColumn><Width>3cm</Width></TablixColumn></TablixColumns>
                <TablixRows><TablixRow><Height>1cm</Height><TablixCells>{cells}</TablixCells></TablixRow></TablixRows>
              </TablixBody>
              <TablixColumnHierarchy><TablixMembers>
                {Header("h1", "No.", 2)}
                {Header("h2", "Group", 1, "<TablixMembers>" + Header("h3", "X", 1) + Header("h4", "Y", 1) + "</TablixMembers>")}
              </TablixMembers></TablixColumnHierarchy>
              <TablixRowHierarchy><TablixMembers><TablixMember><Group Name="Details"/></TablixMember></TablixMembers></TablixRowHierarchy>
            </Tablix>
            """);
        var band = RdlcImporter.ImportXml(xml).Definition.Bands.Single(b => b.Type == BandType.ColumnHeader);
        Assert.Equal(20, band.Height, 2);
        var no = band.Elements.Single(e => e.Text == "No.");
        Assert.Equal((0, 30, 20), (Math.Round(no.X), Math.Round(no.Width), Math.Round(no.Height)));
        var group = band.Elements.Single(e => e.Text == "Group");
        Assert.Equal((30, 60, 10), (Math.Round(group.X), Math.Round(group.Width), Math.Round(group.Height)));
        var y = band.Elements.Single(e => e.Text == "Y");
        Assert.Equal((60, 10, 30, 10), (Math.Round(y.X), Math.Round(y.Y), Math.Round(y.Width), Math.Round(y.Height)));
    }
}
