using Avalonia.Media;
using Avalonia.Media.Fonts;

namespace OrchidUI.Host.Theming;

// SetFontFile: the shim sends the font's bytes; they're registered in an in-memory collection.
internal sealed class UserFonts : FontCollectionBase
{
    private static readonly Uri CollectionKey = new("fonts:orchid-user");
    private static UserFonts? _instance;

    public override Uri Key => CollectionKey;

    // Returns a FontFamily string for the loaded font, or null when it couldn't be read.
    public static string? Add(byte[] data, string fileName)
    {
        if (_instance is null)
        {
            _instance = new UserFonts();
            FontManager.Current.AddFontCollection(_instance);
        }
        if (!_instance.TryAddGlyphTypeface(new MemoryStream(data), out var typeface) || typeface is null)
        {
            HostLog.Warn("font file " + fileName + " couldn't be read");
            return null;
        }
        return CollectionKey + "#" + typeface.FamilyName;
    }
}
