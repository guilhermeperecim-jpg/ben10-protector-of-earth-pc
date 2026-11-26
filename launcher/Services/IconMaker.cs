using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Ben10Launcher.Services;

/// <summary>A picture the player chose (PNG, JPEG, WebP, BMP…) made into a Windows icon for the game's shortcut, on their
/// own PC: the whole picture fitted into a square (transparent around it), at 256, 48, 32 and 16 pixels, each stored
/// as PNG inside the .ico (as Windows Vista and later read them). Nothing of it comes with the launcher.</summary>
public static class IconMaker
{
    private static readonly int[] Sizes = { 256, 48, 32, 16 };

    public static void Make(string picture, string ico)
    {
        using var source = new Bitmap(picture);
        var pngs = Sizes.Select(n => Render(source, n)).ToList();
        using var f = new BinaryWriter(File.Create(ico + ".new"));
        f.Write((ushort)0);
        f.Write((ushort)1);
        f.Write((ushort)Sizes.Length);
        var offset = 6 + 16 * Sizes.Length;
        for (int i = 0; i < Sizes.Length; i++)
        {
            f.Write((byte)(Sizes[i] % 256));
            f.Write((byte)(Sizes[i] % 256));
            f.Write((byte)0);
            f.Write((byte)0);
            f.Write((ushort)1);
            f.Write((ushort)32);
            f.Write(pngs[i].Length);
            f.Write(offset);
            offset += pngs[i].Length;
        }
        foreach (var png in pngs)
            f.Write(png);
        f.Close();
        File.Move(ico + ".new", ico, overwrite: true);
    }

    private static byte[] Render(Bitmap source, int n)
    {
        using var target = new RenderTargetBitmap(new PixelSize(n, n), new Vector(96, 96));
        using (var ctx = target.CreateDrawingContext())
        {
            var scale = Math.Min((double)n / source.PixelSize.Width, (double)n / source.PixelSize.Height);
            var w = source.PixelSize.Width * scale;
            var h = source.PixelSize.Height * scale;
            ctx.DrawImage(source, new Rect(0, 0, source.PixelSize.Width, source.PixelSize.Height),
                          new Rect((n - w) / 2, (n - h) / 2, w, h));
        }
        using var stream = new MemoryStream();
        target.Save(stream);
        return stream.ToArray();
    }
}
