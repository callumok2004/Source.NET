using Source.Common.Filesystem;
using Source.Common.GUI;
using Source.Common.MaterialSystem;

using FreeTypeSharp;
using static FreeTypeSharp.FT;
using System.Runtime.InteropServices;
using Source.Common.Launcher;
using System.Text;
using CommunityToolkit.HighPerformance;

namespace Source.MaterialSystem.Surface;

public abstract class BaseFont
{
	public abstract SurfaceFontFlags GetFlags();
	public abstract int GetHeight();
	public abstract int GetMaxCharWidth();
	public abstract bool IsEqualTo(ReadOnlySpan<char> fontName, int tall, int weight, int blur, int scanlines, SurfaceFontFlags flags);

	public abstract bool GetUnderlined();

	public abstract int GetCharYOffset(char ch);
	public abstract void GetCharABCwidths(char ch, out int a, out int b, out int c);
	internal abstract void GetCharRGBA(char @char, int fontWide, int fontTall, Span<byte> pRGBA);

	internal abstract void GetKernedCharWidth(char ch, char chBefore, char chAfter, out float flWide, out float flabcA, out float flabcC);
}

public record struct FontRange
{
	public int LowRange;
	public int HighRange;
	public BaseFont Font;
}

public class FontAmalgam : IFont
{
	List<FontRange> Fonts = new(4);
	string Name = "";
	int MaxWidth;
	int MaxHeight;

	public void SetName(ReadOnlySpan<char> name) {
		Name = new(name);
	}

	public SurfaceFontFlags GetFlags(int i) {
		Span<FontRange> fonts = Fonts.AsSpan();
		if (fonts.Length > i) {
			ref FontRange range = ref fonts[i];
			BaseFont? font = range.Font;
			if (font == null) return 0;

			return font.GetFlags();
		}
		else
			return 0;
	}

	public void SetFontScale(float sx, float sy) {
		if (Fonts.Count == 0)
			return;

		if (0 != (GetFlags(0) & SurfaceFontFlags.Bitmap))
			DevWarning("Bitmap isn't supported yet\n");
		else
			Warning("Bitmap isn't supported yet but that's an illegal operation anyway...\n");
	}


	public ReadOnlySpan<char> GetFontName(char c) {
		throw new NotImplementedException();
	}

	public ReadOnlySpan<char> GetFontFamilyName(char c) {
		throw new NotImplementedException();
	}

	public nint GetCount() {
		return Fonts.Count;
	}

	public void RemoveAll() {
		Fonts.Clear();
		MaxHeight = 0;
		MaxWidth = 0;
	}

	public BaseFont? GetFontForChar(char ch) {
		foreach (var font in Fonts) {
			if (ch >= font.LowRange && ch <= font.HighRange) {
				return font.Font;
			}
		}

		return null;
	}

	internal void AddFont(BaseFont font, int low, int high) {
		Fonts.Add(new() {
			Font = font,
			LowRange = low,
			HighRange = high
		});

		MaxHeight = Math.Max(font.GetHeight(), MaxHeight);
		MaxWidth = Math.Max(font.GetMaxCharWidth(), MaxWidth);
	}


	public int GetFontHeight() {
		if (Fonts.Count == 0)
			return MaxHeight;

		return Fonts[0].Font.GetHeight();
	}

	internal bool GetUnderlined() {
		if (Fonts.Count == 0)
			return false;

		return Fonts[0].Font.GetUnderlined();
	}

	internal int GetFontMaxWidth() {
		return MaxWidth;
	}
}

// NOTE ABOUT ARG #3: Not a circular reference because it's instantiated by the ISurface implementation itself.
public unsafe class FontManager(IMaterialSystem materialSystem, IFileSystem fileSystem, ISystem system, ISurface surface)
{
	List<FontAmalgam> FontAmalgams = [];
	List<FreeTypeFont> FreeTypeFonts = [];

	Dictionary<ulong, nint> FontBinaries = [];
	Dictionary<ulong, nint> FontBinaryLengths = [];
	Dictionary<ulong, string> CustomFontFiles = [];

	internal IFont CreateFont() {
		FontAmalgam font = new();
		FontAmalgams.Add(font);
		return font;
	}

	internal bool SetFontGlyphSet(IFont font, ReadOnlySpan<char> fontName, int tall, int weight, int blur, int scanlines, SurfaceFontFlags flags, int rangeMin, int rangeMax) {
		if (font is not FontAmalgam fontAmalgam) {
			Warning($"Invalid IFont input into SetFontGlyphSet!\n");
			return false;
		}

		if (fontAmalgam.GetCount() > 0)
			fontAmalgam.RemoveAll();

		FreeTypeFont? baseFont = CreateOrFindFreeTypeFont(fontName, tall, weight, blur, scanlines, flags);

		do {
			if (IsFontForeignLanguageCapable(fontName)) {
				if (baseFont != null) {
					fontAmalgam.AddFont(baseFont, 0x0000, 0xFFFF);
					return true;
				}
			}
			else {
				ReadOnlySpan<char> localizedFontName = GetForeignFallbackFontName();
				if (baseFont != null && localizedFontName.Equals(fontName, StringComparison.OrdinalIgnoreCase)) {
					fontAmalgam.AddFont(baseFont, 0x0000, 0xFFFF);
					return true;
				}

				FreeTypeFont? extendedFont = CreateOrFindFreeTypeFont(localizedFontName, tall, weight, blur, scanlines, flags);
				if (baseFont != null && extendedFont != null) {
					int min = 0x0000, max = 0x00FF;

					if (rangeMin > 0 || rangeMax > 0) {
						min = rangeMin;
						max = rangeMax;

						if (min > max)
							(max, min) = (min, max);
					}

					if (min > 0)
						fontAmalgam.AddFont(extendedFont, 0x0000, min - 1);

					fontAmalgam.AddFont(baseFont, min, max);

					if (max < 0xFFFF)
						fontAmalgam.AddFont(extendedFont, max + 1, 0xFFFF);

					return true;
				}
				else if (extendedFont != null) {
					fontAmalgam.AddFont(extendedFont, 0x0000, 0xFFFF);
					return true;
				}
			}
		}
		while ((fontName = GetFallbackFontName(fontName)) != null);

		return false;
	}

	private ReadOnlySpan<char> GetFallbackFontName(ReadOnlySpan<char> fontName) {
		int i;
		for (i = 0; FallbackFonts[i].Font != null; i++) {
			if (fontName.Equals(FallbackFonts[i].Font, StringComparison.OrdinalIgnoreCase))
				return FallbackFonts[i].Fallback;
		}

		return FallbackFonts[i].Fallback;
	}

	private ReadOnlySpan<char> GetForeignFallbackFontName() {
#if WIN32
		return "Tahoma";
#elif OSX
		return "Helvetica";
#elif LINUX
		return "WenQuanYi Zen Hei";
#else
#error Please define GetForeignFallbackFontName for this platform.
#endif
	}

	public struct FallbackFont
	{
		public string? Font;
		public string? Fallback;
		public FallbackFont(string? font, string? fallback) {
			Font = font;
			Fallback = fallback;
		}
	}

#if WIN32
	static readonly string?[] ValidAsianFonts = ["Marlett", null];
	static readonly FallbackFont[] FallbackFonts = [
		new("Times New Roman", "Courier New"),
		new("Courier New", "Courier"),
		new("Verdana", "Arial"),
		new("Trebuchet MS", "Arial"),
		new("Tahoma", null),
		new(null, "Tahoma")
	];
#elif OSX
	static readonly string?[] ValidAsianFonts = ["Apple Symbols", null];
	static readonly FallbackFont[] FallbackFonts = [
		new("Marlett", "Apple Symbols"),
		new("Lucida Console", "Lucida Grande"),
		new("Tahoma", "Helvetica"),
		new("Helvetica", "Monaco"),
		new("Monaco", null),
		new("null", "Monaco"),
	];
#elif LINUX
	static readonly string?[] ValidAsianFonts = ["Marlett", "WenQuanYi Zen Hei", "unifont", null];
	static readonly FallbackFont[] FallbackFonts = [
		new("FreeSans", null),
		new(null, "FreeSans")
	];
#else
#error Please define fallback fonts for this platform
#endif

	private bool IsFontForeignLanguageCapable(ReadOnlySpan<char> fontName) {
		for (int i = 0; ValidAsianFonts[i] != null; i++) {
			if (fontName.Equals(ValidAsianFonts[i], StringComparison.OrdinalIgnoreCase))
				return true;
		}

		return false;
	}

	internal byte* GetFontBinary(ReadOnlySpan<char> fontName, out nint length) {
		if (!FontBinaries.TryGetValue(fontName.Hash(), out nint binary)) {
			length = 0;
			return null;
		}
		length = FontBinaryLengths[fontName.Hash()];
		return (byte*)binary;
	}

	private FreeTypeFont? CreateOrFindFreeTypeFont(ReadOnlySpan<char> fontName, int tall, int weight, int blur, int scanlines, SurfaceFontFlags flags) {
		FreeTypeFont? foundFont = null;
		foreach (var font in FreeTypeFonts) {
			if (font.IsEqualTo(fontName, tall, weight, blur, scanlines, flags)) {
				foundFont = font;
				break;
			}
		}
		if (foundFont == null) {
			if (!FontBinaries.TryGetValue(fontName.Hash(), out nint binary)) {
				// Load the binary
				if (!CustomFontFiles.TryGetValue(fontName.Hash(), out string? filePath))
					filePath = new(system.GetSystemFontPath(fontName)); // If not custom font file, load from the OS

				FileInfo info = new(filePath);
				if (!info.Exists)
					return null; // cannot load...

				FileStream file = File.OpenRead(filePath);
				binary = (nint)NativeMemory.AllocZeroed((nuint)file.Length);
				using UnmanagedMemoryStream stream = new UnmanagedMemoryStream((byte*)binary, 0, file.Length, FileAccess.Write);
				file.CopyTo(stream);
				FontBinaries[fontName.Hash()] = binary;
				FontBinaryLengths[fontName.Hash()] = (nint)info.Length;
			}

			FreeTypeFont font = new FreeTypeFont(this, system);
			if (font.Create(fontName, tall, weight, blur, scanlines, flags)) {
				foundFont = font;
				FreeTypeFonts.Add(font);
			}
			else
				return null;
		}
		return foundFont;
	}

	public struct FT_SfntName
	{
		public ushort PlatformID;
		public ushort EncodingID;
		public ushort LanguageID;
		public ushort NameID;
		public byte* String;
		public uint StringLen;
	}

	internal bool AddCustomFontFile(ReadOnlySpan<char> fontName, ReadOnlySpan<char> fontFileName) {
		ReadOnlySpan<char> fontFilepath = fileSystem.RelativePathToFullPath(fontFileName, null, null);

		if (fontFilepath == null) {
			Warning($"Couldn't find custom font file '{fontFileName}' for font '{(fontName == null ? "" : fontName)}'\n");
			return false;
		}

		if (CustomFontFiles.TryGetValue(fontName.Hash(), out string? path))
			return true; // Already loaded

		FileInfo info = new(new(fontFilepath));
		if (!info.Exists) {
			Msg($"Failed to load custom font file '{fontFilepath}'\n");
			return false;
		}

		// We now need to resolve fontName if it wasn't provided.
		if (fontName == null) {
			// TODO: This means we load the font twice... not ideal..
			Span<byte> filePathAlloc = stackalloc byte[Encoding.UTF8.GetByteCount(fontFilepath) + 1];
			int written = Encoding.UTF8.GetBytes(fontFilepath, filePathAlloc);
			filePathAlloc[written] = 0;
			FT_Error err;
			FT_FaceRec_* face;
			fixed (byte* pathPtr = filePathAlloc)
				err = FT_New_Face(FreeTypeFont.Library, pathPtr, 0, &face);

			if (err != FT_Error.FT_Err_Ok) {
				Assert(false);
				return false;
			}

			byte* name = face->family_name;
			nint len = 0;
			byte* nameReadForLength = name;
			while (*nameReadForLength != 0) {
				len++;
				nameReadForLength++;
			}
			string nameManaged = Encoding.ASCII.GetString(name, (int)len);
			fontName = nameManaged;
			// don't leave a dead copy lying around
			FT_Done_Face(face);
		}

		// Register the custom font file
		CustomFontFiles[fontName.Hash()] = new(fontFilepath);

		return true;
	}

	internal int GetFontTall(IFont? font) => ((FontAmalgam?)font)?.GetFontHeight() ?? 0;
	internal bool GetFontUnderlined(IFont font) => ((FontAmalgam?)font)!.GetUnderlined();

	internal bool IsBitmapFont(IFont font) {
		return false; // Todo
	}

	internal BaseFont? GetFontForChar(IFont font, char @char) {
		return ((FontAmalgam?)font)!.GetFontForChar(@char);
	}

	internal int GetCharYOffset(IFont? font, char ch) {
		if (font is not FontAmalgam amalgam) {
			return 0;
		}

		BaseFont? baseFont = amalgam.GetFontForChar(ch);
		return baseFont?.GetCharYOffset(ch) ?? 0;
	}
	internal void GetCharABCwide(IFont? font, char ch, out int a, out int b, out int c) {
		if (font is not FontAmalgam amalgam) {
			a = b = c = 0;
			return;
		}

		BaseFont? baseFont = amalgam.GetFontForChar(ch);
		if (baseFont != null)
			baseFont.GetCharABCwidths(ch, out a, out b, out c);
		else {
			a = c = 0;
			b = amalgam.GetFontMaxWidth();
		}
	}

	internal int IsFontAdditive(IFont? font) {
		if (font is not FontAmalgam amalgam)
			return 0;

		return (amalgam.GetFlags(0) & SurfaceFontFlags.Additive) != 0 ? 1 : 0;
	}

	internal int GetCharacterWidth(IFont? font, char ch) {
		if (!char.IsControl(ch)) {
			GetCharABCwide(font, ch, out int a, out int b, out int c);
			return a + b + c;
		}
		return 0;
	}

	internal void GetTextSize(IFont? font, ReadOnlySpan<char> text, out int wide, out int tall) {
		wide = 0;
		tall = 0;

		if (text == null)
			return;

		tall = GetFontTall(font);

		float xx = 0;
		char chBefore = '\0';
		char chAfter = '\0';
		for (int i = 0; ; i++) {
			char ch = text[i];
			if (ch == 0)
				break;

			chAfter = text[i + 1];

			if (ch == '\n') {
				tall += GetFontTall(font);
				xx = 0;
			}
			else if (ch == '&') { }
			else {
				GetKernedCharWidth(font, ch, chBefore, chAfter, out float flWide, out float flabcA, out float flabcC);
				xx += flWide;
				if (xx > wide) {
					wide = (int)MathF.Ceiling(xx);
				}
			}

			chBefore = ch;
		}
	}

	public void GetKernedCharWidth(IFont? font, char ch, char chBefore, char chAfter, out float flWide, out float flabcA, out float flabcC) {
		flWide = 0.0f;
		flabcA = 0.0f;
		flabcC = 0.0f;

		Assert(font != null);
		if (font == null)
			return;

		if (font is not FontAmalgam fontAmalgam)
			return;

		BaseFont? baseFont = fontAmalgam.GetFontForChar(ch);
		if (baseFont == null) {
			// no font for this range, just use the default width
			flabcA = 0.0f;
			flWide = fontAmalgam.GetFontMaxWidth();
			return;
		}

		if (fontAmalgam.GetFontForChar(chBefore) != font)
			chBefore = '\0';

		if (fontAmalgam.GetFontForChar(chAfter) != font)
			chAfter = '\0';

		baseFont.GetKernedCharWidth(ch, chBefore, chAfter, out flWide, out flabcA, out flabcC);
	}
}
