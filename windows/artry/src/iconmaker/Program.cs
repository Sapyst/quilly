using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

if (args.Length < 2) throw new ArgumentException("Uso: IconMaker <ico> <preview-png>");

var icoPath = args[0];
var previewPath = args[1];

var dir = Path.GetDirectoryName(icoPath) ?? "";
var masterPath = Path.Combine(dir, "master-icon.png");
if (!File.Exists(masterPath))
{
    masterPath = Path.Combine(dir, "icon.png");
}

if (!File.Exists(masterPath))
{
    throw new FileNotFoundException("Arquivo mestre de ícone não encontrado", masterPath);
}

// 1. Carrega imagem mestre fornecida pelo usuário (media_1789102097121.png)
using var masterImage = new Bitmap(masterPath);
int w = masterImage.Width;
int h = masterImage.Height;

// 2. Cria imagem mestre garantindo canal alfa 32bpp ARGB impecável
using var transparentMaster = new Bitmap(w, h, PixelFormat.Format32bppArgb);
using (var g = Graphics.FromImage(transparentMaster))
{
    g.SmoothingMode = SmoothingMode.AntiAlias;
    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
    g.CompositingQuality = CompositingQuality.HighQuality;
    g.Clear(Color.FromArgb(0, 0, 0, 0));
    g.DrawImage(masterImage, 0, 0, w, h);
}

// 3. Salva icon-preview.png e src/icon.png com transparência absoluta
transparentMaster.Save(previewPath, ImageFormat.Png);
var targetPng = Path.Combine(dir, "icon.png");
transparentMaster.Save(targetPng, ImageFormat.Png);

// 4. Gera todas as resoluções do arquivo .ico em formato PNG com canal alfa nativo
int[] sizes = [256, 128, 64, 48, 32, 24, 16];
var imageBuffers = new List<(int Size, byte[] Data)>();

foreach (var sz in sizes)
{
    using var resized = new Bitmap(sz, sz, PixelFormat.Format32bppArgb);
    using (var g = Graphics.FromImage(resized))
    {
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.CompositingQuality = CompositingQuality.HighQuality;
        g.Clear(Color.FromArgb(0, 0, 0, 0));

        g.DrawImage(transparentMaster, new Rectangle(0, 0, sz, sz));
    }

    using var ms = new MemoryStream();
    resized.Save(ms, ImageFormat.Png);
    imageBuffers.Add((sz, ms.ToArray()));
}

using (var fs = File.Create(icoPath))
using (var bw = new BinaryWriter(fs))
{
    // ICONDIR
    bw.Write((ushort)0); // Reserved
    bw.Write((ushort)1); // Type: 1 = ICO
    bw.Write((ushort)imageBuffers.Count); // ImageCount

    var offset = 6 + (imageBuffers.Count * 16);

    for (int i = 0; i < imageBuffers.Count; i++)
    {
        var (sz, data) = imageBuffers[i];

        bw.Write((byte)(sz >= 256 ? 0 : sz)); // Width
        bw.Write((byte)(sz >= 256 ? 0 : sz)); // Height
        bw.Write((byte)0); // Colors
        bw.Write((byte)0); // Reserved
        bw.Write((ushort)1); // Color planes
        bw.Write((ushort)32); // Bits per pixel
        bw.Write((uint)data.Length); // Image size
        bw.Write((uint)offset); // Offset

        offset += data.Length;
    }

    foreach (var (_, data) in imageBuffers)
    {
        bw.Write(data);
    }
}

if (args.Length > 2 && !string.IsNullOrWhiteSpace(args[2]))
{
    var mobileResDir = args[2];
    var mipmaps = new (string Folder, int Size)[]
    {
        ("mipmap-mdpi", 48),
        ("mipmap-hdpi", 72),
        ("mipmap-xhdpi", 96),
        ("mipmap-xxhdpi", 144),
        ("mipmap-xxxhdpi", 192),
    };

    foreach (var (folder, sz) in mipmaps)
    {
        var targetFolder = Path.Combine(mobileResDir, folder);
        Directory.CreateDirectory(targetFolder);
        using var resized = new Bitmap(sz, sz, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(resized))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.Clear(Color.FromArgb(0, 0, 0, 0));
            g.DrawImage(transparentMaster, new Rectangle(0, 0, sz, sz));
        }
        resized.Save(Path.Combine(targetFolder, "ic_launcher.png"), ImageFormat.Png);
        resized.Save(Path.Combine(targetFolder, "ic_launcher_round.png"), ImageFormat.Png);
    }
    Console.WriteLine($"Mipmaps Android gerados com sucesso em {mobileResDir}.");
}

Console.WriteLine($"Ícone gerado a partir do mestre com sucesso em {icoPath} ({imageBuffers.Count} resoluções).");
