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
            ["company"] = "Kugil",
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
        Assert.Contains("Kugil", texts);
    }

    [Fact]
    public void Unsupported_Things_AreReported_NotGuessed()
    {
        var xml = Sample().Replace("=Fields!code.Value", "=IIf(Fields!qty.Value &gt; 0, &quot;a&quot;, &quot;b&quot;)");
        var result = RdlcImporter.ImportXml(xml);

        var warning = Assert.Single(result.Warnings);
        Assert.Contains("IIf", warning);
        var detail = result.Definition.Bands.Single(b => b.Type == Model.BandType.Detail);
        Assert.Null(detail.Elements[0].Expression);
    }

    [Fact]
    public void NotAReport_Throws()
    {
        Assert.Throws<ReportException>(() => RdlcImporter.ImportXml("<html/>"));
    }
}
