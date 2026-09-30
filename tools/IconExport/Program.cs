using System;
using System.IO;
using System.Windows.Media.Imaging;
using RevitScheduleSync.App;

// Writes icon-32.png ... icon-512.png. Uses only WPF drawing; never loads Revit.
var output = args.Length > 0 ? args[0] : ".";
Directory.CreateDirectory(output);
foreach (var size in new[] { 16, 32, 64, 128, 256, 512 })
{
    var encoder = new PngBitmapEncoder();
    encoder.Frames.Add(BitmapFrame.Create(RibbonIcons.Create(size)));
    var path = Path.Combine(output, $"icon-{size}.png");
    using var file = File.Create(path);
    encoder.Save(file);
    Console.WriteLine(path);
}
