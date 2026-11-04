using System.Drawing;
using System.Drawing.Text;

internal static class DailyTranslationPageFixture
{
    internal static Bitmap Create(out IReadOnlyList<string> sourceLines)
    {
        var truth=new List<string>();
        var bitmap=new Bitmap(1280,620);
        using var graphics=Graphics.FromImage(bitmap);
        graphics.Clear(Color.White);
        graphics.TextRenderingHint=TextRenderingHint.AntiAliasGridFit;
        Draw(26,24,28,Color.FromArgb(30,110,190),"ACCOUNT SETTINGS");
        Draw(26,90,22,Color.FromArgb(35,35,35),"Choose a language for this application.",
            "Your changes will apply after saving.","You can cancel and keep the old settings.");
        Draw(26,230,26,Color.FromArgb(35,35,35),"SAVE CHANGES");
        Draw(26,302,22,Color.FromArgb(35,35,35),"Press Ctrl+S to save your work.",
            "The original file remains available.");
        Draw(670,24,28,Color.FromArgb(30,110,190),"DOWNLOAD READY");
        Draw(670,90,22,Color.FromArgb(35,35,35),"The latest update is ready to install.",
            "You can continue working while it downloads.","Keep your documents open until you finish.");
        Draw(670,230,26,Color.FromArgb(35,35,35),"CONTINUE");
        Draw(670,302,22,Color.FromArgb(35,35,35),"OpenAI provides an optional online service.",
            "Local translation keeps the image on this device.");
        Draw(26,450,24,Color.FromArgb(195,45,50),"WARNING");
        Draw(26,500,22,Color.FromArgb(35,35,35),"Do not close the application while saving.",
            "Wait for the completion message before exiting.");
        Draw(670,450,24,Color.FromArgb(35,35,35),"SEARCH RESULTS");
        Draw(670,500,22,Color.FromArgb(35,35,35),"Select a result to view the full description.",
            "No account is required for offline features.");
        sourceLines=truth;
        return bitmap;

        void Draw(float x,float y,float size,Color color,params string[] lines)
        {
            using var font=new Font("Segoe UI",size,FontStyle.Regular,GraphicsUnit.Pixel);
            using var brush=new SolidBrush(color);
            for(var index=0;index<lines.Length;index++)
            {
                truth.Add(lines[index]);
                graphics.DrawString(lines[index],font,brush,x,y+index*34,StringFormat.GenericTypographic);
            }
        }
    }
}
