using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RevitScheduleSync.App
{
    /// <summary>
    /// Draws the ribbon icon in code (a small table with a sync arrow),
    /// so no image files need to be shipped or resolved via pack URIs.
    /// </summary>
    /// <remarks>Public so tools/IconExport can render the same icon for the App Store package.</remarks>
    public static class RibbonIcons
    {
        private static readonly Color Ink = Color.FromRgb(0x3C, 0x4A, 0x5A);
        private static readonly Color Accent = Color.FromRgb(0x06, 0x96, 0xD7);

        public static BitmapSource Create(int size)
        {
            double s = size / 32.0;
            var visual = new DrawingVisual();

            using (var dc = visual.RenderOpen())
            {
                var ink = new Pen(new SolidColorBrush(Ink), 1.6 * s);
                var grid = new Pen(new SolidColorBrush(Ink), 1.0 * s);
                var accentBrush = new SolidColorBrush(Accent);

                // Table body with a highlighted header row.
                var table = new Rect(2 * s, 4 * s, 22 * s, 20 * s);
                dc.DrawRectangle(Brushes.White, null, table);
                dc.DrawRectangle(accentBrush, null, new Rect(table.X, table.Y, table.Width, 5 * s));
                dc.DrawRectangle(null, ink, table);
                dc.DrawLine(grid, new Point(table.X, 14 * s), new Point(table.Right, 14 * s));
                dc.DrawLine(grid, new Point(table.X, 19 * s), new Point(table.Right, 19 * s));
                dc.DrawLine(grid, new Point(10 * s, table.Y + 5 * s), new Point(10 * s, table.Bottom));

                // Sync arrow badge (bottom right).
                var badgeCentre = new Point(24 * s, 24 * s);
                dc.DrawEllipse(Brushes.White, new Pen(accentBrush, 1.6 * s), badgeCentre, 7 * s, 7 * s);
                var arrowPen = new Pen(accentBrush, 1.8 * s) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                dc.DrawLine(arrowPen, new Point(20.5 * s, 22 * s), new Point(27.5 * s, 22 * s));
                dc.DrawLine(arrowPen, new Point(25.5 * s, 20 * s), new Point(27.5 * s, 22 * s));
                dc.DrawLine(arrowPen, new Point(27.5 * s, 26 * s), new Point(20.5 * s, 26 * s));
                dc.DrawLine(arrowPen, new Point(22.5 * s, 28 * s), new Point(20.5 * s, 26 * s));
            }

            var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }

        /// <summary>Round "i" information icon for the About button.</summary>
        public static BitmapSource CreateAbout(int size)
        {
            double s = size / 32.0;
            var visual = new DrawingVisual();

            using (var dc = visual.RenderOpen())
            {
                var accentBrush = new SolidColorBrush(Accent);
                dc.DrawEllipse(accentBrush, null, new Point(16 * s, 16 * s), 14 * s, 14 * s);
                dc.DrawEllipse(Brushes.White, null, new Point(16 * s, 9.5 * s), 2.2 * s, 2.2 * s);
                dc.DrawRoundedRectangle(Brushes.White, null, new Rect(14 * s, 13.5 * s, 4 * s, 11 * s), 1 * s, 1 * s);
            }

            var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }
    }
}
