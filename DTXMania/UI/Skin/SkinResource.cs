using DTXMania.Core;
using DTXMania.UI.Drawable;

namespace DTXMania.UI.Skin;

public enum ResourceSource
{
    //the built-in base skin, under <exe>/System
    System,

    //the active skin's own resource folder
    Skin
}

public struct SkinResource
{
    [Themable] public ResourceSource source;

    //relative to the root the source names, e.g. "Graphics\5_bar.png" or "Sounds/decide.ogg"
    [Themable] public string path;

    public SkinResource(ResourceSource source, string path)
    {
        this.source = source;
        this.path = path;
    }

    public static SkinResource System(string path) => new(ResourceSource.System, path);

    public bool IsEmpty => string.IsNullOrWhiteSpace(path);

    public string Resolve(ResourceType type)
    {
        if (IsEmpty)
        {
            return string.Empty;
        }

        string normPath = DataPath.Normalize(path);

        if (CDTXMania.SkinManager?.currentSkin is { } skin)
        {
            // 1. Check in skin's dedicated resource folder for this type
            string typeFolder = SkinDescriptor.GetResourceFolder(type);
            string candidate = Path.Combine(skin.basePath, typeFolder, normPath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            // 2. If it's an Image, check skin.basePath/Resources/normPath
            if (type == ResourceType.Image)
            {
                candidate = Path.Combine(skin.basePath, "Resources", normPath);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            // 3. If it's a Sound and normPath starts with Sounds\, check skin.basePath/Sounds/fileName
            if (type == ResourceType.Sound && normPath.StartsWith("Sounds" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                candidate = Path.Combine(skin.basePath, "Sounds", normPath.Substring(7));
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            // 4. If it's a Video, also check skin.basePath/Videos/fileName
            if (type == ResourceType.Video)
            {
                candidate = Path.Combine(skin.basePath, "Videos", Path.GetFileName(normPath));
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            // If source is explicitly Skin and not found in skin, fallback to System if available
            if (source == ResourceSource.Skin)
            {
                string sysFallback = SkinManager.SystemPath(normPath);
                if (File.Exists(sysFallback))
                {
                    return sysFallback;
                }
            }
        }

        if (source == ResourceSource.Skin && CDTXMania.SkinManager?.currentSkin == null)
        {
            string sysFallback = SkinManager.SystemPath(normPath);
            if (File.Exists(sysFallback))
            {
                return sysFallback;
            }
            return string.Empty;
        }

        return type == ResourceType.Font ? UIFonts.GetSystemFont(path) : SkinManager.SystemPath(path);
    }

    public bool Exists(ResourceType type) => Resolve(type) is { Length: > 0 } full && File.Exists(full);

    //compared on draw paths to notice a reference changing, so it cannot go through the reflection-based
    //ValueType.Equals. Also what compact serialization uses to decide a member is untouched
    public bool Equals(SkinResource other) => source == other.source && path == other.path;

    public override bool Equals(object? obj) => obj is SkinResource other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(source, path);

    public static bool operator ==(SkinResource left, SkinResource right) => left.Equals(right);

    public static bool operator !=(SkinResource left, SkinResource right) => !left.Equals(right);

    public override string ToString() => IsEmpty ? "(none)" : $"{source}: {path}";
}
