using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using Forms = System.Windows.Forms;

namespace AudioMixer.Desktop;

/// <summary>Owns the tray's packaged icon and process-private menu font for their complete lifetime.</summary>
internal sealed class TrayBranding : IDisposable
{
    private Icon? _icon;
    private Font? _menuFont;
    private FontFamily? _fontFamily;
    private PrivateFontCollection? _fonts;
    private IntPtr _fontMemory;
    private IntPtr _fontRegistration;
    private bool _disposed;

    internal Icon Icon => _icon ?? throw new ObjectDisposedException(nameof(TrayBranding));
    internal Font MenuFont => _menuFont ?? throw new ObjectDisposedException(nameof(TrayBranding));

    internal TrayBranding()
    {
        try
        {
            using Stream stream = OpenResource("Assets/Branding/tray.ico");
            using var icon = new Icon(stream, Forms.SystemInformation.SmallIconSize);
            _icon = (Icon)icon.Clone();
            LoadMenuFont();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private void LoadMenuFont()
    {
        try
        {
            using Stream stream = OpenResource("Assets/Fonts/IBM-Plex-Sans-Regular.ttf");
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            byte[] bytes = buffer.ToArray();
            _fontMemory = Marshal.AllocHGlobal(bytes.Length);
            Marshal.Copy(bytes, 0, _fontMemory, bytes.Length);
            _fonts = new PrivateFontCollection();
            _fonts.AddMemoryFont(_fontMemory, bytes.Length);

            // ToolStrip's TextRenderer uses GDI; AddMemoryFont alone supplies only
            // GDI+. This registration remains private to this process and is removed
            // on disposal. It does not install or enumerate a Windows system font.
            _fontRegistration = AddFontMemResourceEx(_fontMemory, (uint)bytes.Length, IntPtr.Zero, out uint count);
            if (_fontRegistration == IntPtr.Zero || count == 0)
                throw new InvalidOperationException("Could not register the packaged tray font for this process.");

            _fontFamily = _fonts.Families[0];
            if (!string.Equals(_fontFamily.Name, "IBM Plex Sans", StringComparison.Ordinal))
                throw new InvalidOperationException("The packaged tray font has an unexpected family name.");
            _menuFont = new Font(_fontFamily, System.Drawing.SystemFonts.MenuFont?.SizeInPoints ?? 9f, System.Drawing.FontStyle.Regular, GraphicsUnit.Point);
        }
        catch (Exception error) when (error is ArgumentException or ExternalException or InvalidOperationException or IOException or NotSupportedException)
        {
            // A machine-specific font failure must not prevent tray commands or
            // audio startup. The packaged assets are checked before distribution.
            Debug.WriteLine(error);
            ReleaseFontResources();
            _menuFont = (Font)(System.Drawing.SystemFonts.MenuFont ?? System.Drawing.SystemFonts.DefaultFont).Clone();
        }
    }

    private static Stream OpenResource(string path)
    {
        var uri = new Uri("/AudioMixer;component/" + path, UriKind.Relative);
        return Application.GetResourceStream(uri)?.Stream
            ?? throw new FileNotFoundException("A packaged Vine Mix tray asset is missing.", path);
    }

    private void ReleaseFontResources()
    {
        _menuFont?.Dispose();
        _menuFont = null;
        _fontFamily?.Dispose();
        _fontFamily = null;
        _fonts?.Dispose();
        _fonts = null;
        if (_fontRegistration != IntPtr.Zero)
        {
            RemoveFontMemResourceEx(_fontRegistration);
            _fontRegistration = IntPtr.Zero;
        }
        if (_fontMemory != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_fontMemory);
            _fontMemory = IntPtr.Zero;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _icon?.Dispose();
        _icon = null;
        ReleaseFontResources();
    }

    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern IntPtr AddFontMemResourceEx(IntPtr memory, uint size, IntPtr reserved, out uint count);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveFontMemResourceEx(IntPtr handle);
}
