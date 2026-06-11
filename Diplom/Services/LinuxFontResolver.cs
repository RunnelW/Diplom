using PdfSharp.Fonts;

namespace Diplom.Services
{
    public class LinuxFontResolver : IFontResolver
    {
        private const string FontDir = "/usr/share/fonts/truetype/liberation";

        public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
        {
            string face = (isBold, isItalic) switch
            {
                (true, true)  => "LiberationSans-BoldItalic",
                (true, false) => "LiberationSans-Bold",
                (false, true) => "LiberationSans-Italic",
                _             => "LiberationSans-Regular"
            };
            return new FontResolverInfo(face);
        }

        public byte[]? GetFont(string faceName)
        {
            string path = Path.Combine(FontDir, $"{faceName}.ttf");
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }
    }
}
