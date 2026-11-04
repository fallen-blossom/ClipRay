using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Text.Json;
using ClipRay.App.Ocr;
using ClipRay.App.Translation;
using ClipRay.Core.Geometry;

public static class SourceInkStyleStabilityTests
{
    public static void Run(string output)
    {
        Directory.CreateDirectory(output);
        var evidence=new List<object>();
        using(var source=Draw([("No",24f,FontStyle.Regular,Color.FromArgb(45,45,45)),
            ("account",24f,FontStyle.Regular,Color.FromArgb(45,45,45)),
            ("is",24f,FontStyle.Regular,Color.FromArgb(45,45,45)),
            ("required",24f,FontStyle.Regular,Color.FromArgb(45,45,45)),
            ("for",24f,FontStyle.Regular,Color.FromArgb(45,45,45)),
            ("offline",24f,FontStyle.Regular,Color.FromArgb(45,45,45)),
            ("features.",24f,FontStyle.Regular,Color.FromArgb(45,45,45))],out var line))
        {
            var region=VisualTextRegionBuilder.Build([line],source).Single();
            var styles=Words(source,line);
            Check(styles.All(style=>Near(style.ForegroundColor,Color.FromArgb(45,45,45),20)),
                "Antialiasing changed black ink into gray or inverted foreground/background.");
            Check(styles.All(style=>style.FontWeight==SourceFontWeight.Normal),
                "Dense individual glyphs fabricated bold styling in a normal-weight sentence.");
            Check(region.SourceStyleSpans.Count==0,"A uniform line fabricated word-level style spans.");
            Check(styles.Max(style=>style.EstimatedFontSizePixels!.Value)
                /styles.Min(style=>style.EstimatedFontSizePixels!.Value)<1.2,
                "Normal ascender/descender variation fabricated mixed font sizes.");
            source.Save(Path.Combine(output,"uniform-ink-source.png"));
            evidence.Add(new{Case="uniform",Styles=styles});
        }
        var gray=Color.FromArgb(174,174,174);
        using(var source=Draw([("Quiet",24f,FontStyle.Regular,gray)],out var line))
        {
            // A single darker noise pixel must not recolor pale source text.
            source.SetPixel((int)line.Bounds.Left+1,(int)line.Bounds.Top+1,Color.Black);
            var style=Words(source,line).Single();
            Check(Near(style.ForegroundColor,gray,18),"Pale gray ink was replaced by contrast correction or dark noise.");
            evidence.Add(new{Case="pale-gray-with-noise",Style=style});
            source.Save(Path.Combine(output,"pale-gray-source.png"));
        }
        using(var source=Draw([("Keep",20f,FontStyle.Regular,Color.FromArgb(40,40,40)),
            ("RED",36f,FontStyle.Bold,Color.FromArgb(195,45,50)),
            ("readable",20f,FontStyle.Regular,Color.FromArgb(40,40,40))],out var line))
        {
            var region=VisualTextRegionBuilder.Build([line],source).Single();
            var styles=Words(source,line);
            Check(Near(styles[1].ForegroundColor,Color.FromArgb(195,45,50),18),"A true red phrase lost its source color.");
            Check(styles[1].EstimatedFontSizePixels>styles[0].EstimatedFontSizePixels*1.4,
                "A true mixed-size phrase was flattened.");
            Check(styles[1].FontWeight>styles[0].FontWeight,"A true bold phrase lost its measured weight difference.");
            Check(region.SourceStyleSpans.Count>=3,"A true mixed-style sentence lost its source spans.");
            source.Save(Path.Combine(output,"mixed-ink-source.png"));
            evidence.Add(new{Case="mixed",Styles=styles,Spans=region.SourceStyleSpans});
        }
        using(var source=Draw([("Save",28f,FontStyle.Regular,Color.FromArgb(45,45,45)),
            ("tall",28f,FontStyle.Italic,Color.FromArgb(45,45,45)),
            ("a",28f,FontStyle.Regular,Color.FromArgb(45,45,45)),
            ("this",28f,FontStyle.Regular,Color.FromArgb(45,45,45))],out var line))
        {
            var region=VisualTextRegionBuilder.Build([line],source).Single();
            Check(region.SourceStyleSpans.Any(span=>span.Text=="tall" && span.Style.IsItalic),
                "Independent consistent italic glyphs lost their local source span.");
            Check(region.SourceStyleSpans.Where(span=>span.Text!="tall").All(span=>!span.Style.IsItalic),
                "Upright asymmetric glyphs acquired an italic source span.");
            var request=VisualTranslationRequestFactory.Create([region],"zh",DeepSeekModels.Flash).Single();
            var result=new TranslationResult(true,"Keep tall readable.","controlled-alignment") {
                TextAlignments=[new(5,4,5,4)]};
            var resolution=TranslationStyleResolver.Resolve(region,request,result);
            Check(resolution.Runs.Any(run=>run.Start==5 && run.Length==4 && run.Style.IsItalic),
                "A validated local italic span did not reach its aligned target run.");
            var layout=new TranslationTextLayout(true,28,36,SourceFontWeight.Normal,
                SourceFontCategory.SansSerif,false,SourceTextAlignment.Left,0,"Segoe UI") {
                StyleRuns=resolution.Runs};
            var italicShape=TranslationTextLayoutPolicy.Format(result.Text,800,layout,
                System.Windows.Media.Brushes.Black).BuildGeometry(new System.Windows.Point());
            // GeometryGroup.ToString() is only its type name, not its glyph
            // outlines. Compare actual flattened path coordinates instead.
            var italicGeometry=italicShape.GetFlattenedPathGeometry().ToString(
                System.Globalization.CultureInfo.InvariantCulture);
            var upright=layout with {StyleRuns=resolution.Runs.Select(run=>run with {
                Style=run.Style with {IsItalic=false}}).ToArray()};
            var uprightShape=TranslationTextLayoutPolicy.Format(result.Text,800,upright,
                System.Windows.Media.Brushes.Black).BuildGeometry(new System.Windows.Point());
            var uprightGeometry=uprightShape.GetFlattenedPathGeometry().ToString(
                System.Globalization.CultureInfo.InvariantCulture);
            Check(italicGeometry!=uprightGeometry,"A validated italic target run did not change actual render geometry.");
            Console.WriteLine($"ITALIC_GEOMETRY_ORACLE type={italicShape.GetType().Name} rawEqual={italicShape.ToString()==uprightShape.ToString()} pathEqual={italicGeometry==uprightGeometry}");
            source.Save(Path.Combine(output,"local-italic-source.png"));
            evidence.Add(new{Case="local-italic",Spans=region.SourceStyleSpans,Runs=resolution.Runs});
        }
        File.WriteAllText(Path.Combine(output,"source-ink-style-evidence.json"),JsonSerializer.Serialize(evidence,
            new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("SOURCE_INK_STYLE_STABILITY_TESTS_PASSED");
    }
    private static IReadOnlyList<SourceVisualStyle> Words(Bitmap source,OcrTextBlock line)=>line.Words.Select(word=>
        SourceVisualStyleEstimator.Estimate(source,new(word.Text,word.Bounds,[word]),[line],Color.White)).ToArray();
    private static Bitmap Draw((string Text,float Size,FontStyle Weight,Color Color)[] parts,out OcrTextBlock line)
    {
        var bitmap=new Bitmap(1100,140);
        using var graphics=Graphics.FromImage(bitmap);
        graphics.Clear(Color.White);
        graphics.TextRenderingHint=TextRenderingHint.AntiAliasGridFit;
        var words=new List<OcrWord>();
        var x=20f;
        foreach(var part in parts)
        {
            using var font=new Font("Segoe UI",part.Size,part.Weight,GraphicsUnit.Pixel);
            using var brush=new SolidBrush(part.Color);
            var width=graphics.MeasureString(part.Text,font,int.MaxValue,StringFormat.GenericTypographic).Width;
            var ascent=font.Size*font.FontFamily.GetCellAscent(part.Weight)/font.FontFamily.GetEmHeight(part.Weight);
            var y=90-ascent;
            graphics.DrawString(part.Text,font,brush,x,y,StringFormat.GenericTypographic);
            words.Add(new(part.Text,new(x,y,width,font.GetHeight(graphics))));
            x+=width+part.Size*.3f;
        }
        var left=words.Min(word=>word.Bounds.Left)-1;
        var top=words.Min(word=>word.Bounds.Top)-1;
        var right=words.Max(word=>word.Bounds.Right)+1;
        var bottom=words.Max(word=>word.Bounds.Bottom)+1;
        line=new(string.Join(' ',parts.Select(part=>part.Text)),new(left,top,right-left,bottom-top),words);
        return bitmap;
    }
    private static bool Near(Color a,Color b,double tolerance)=>Math.Sqrt(Math.Pow(a.R-b.R,2)+Math.Pow(a.G-b.G,2)+Math.Pow(a.B-b.B,2))<=tolerance;
    private static void Check(bool value,string message) {if(!value) throw new InvalidOperationException(message);}
}
