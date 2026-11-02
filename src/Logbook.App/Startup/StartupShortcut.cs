using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace Logbook.App.Startup;

/// <summary>
/// The "start with Windows" shortcut in the current user's Startup folder (docs/settings.md). A login change
/// wipes it, and the launch-time offer brings it back. Written through the shell's IShellLink, no registry.
/// </summary>
internal static class StartupShortcut
{
    private const string ShortcutName = "Logbook.lnk";

    public static string Path => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), ShortcutName);

    public static bool IsRegistered() => File.Exists(Path);

    public static void Register()
    {
        string target = Environment.ProcessPath ?? throw new InvalidOperationException("The executable path is unknown.");
        IShellLinkW link = (IShellLinkW)new ShellLink();
        link.SetPath(target);
        link.SetWorkingDirectory(System.IO.Path.GetDirectoryName(target) ?? "");
        link.SetDescription("Logbook");
        ((IPersistFile)link).Save(Path, fRemember: true);
    }

    public static void Unregister()
    {
        if (File.Exists(Path))
        {
            File.Delete(Path);
        }
    }

    // The coclass stays unsealed so the cast to IShellLinkW compiles; the runtime supplies the real object.
#pragma warning disable CA1812, CA1852
    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink;
#pragma warning restore CA1812, CA1852

    // Only the members used are declared in full; the rest keep their vtable slots.
    [ComImport]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);

        void GetIDList(out IntPtr ppidl);

        void SetIDList(IntPtr pidl);

        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszName, int cch);

        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);

        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszDir, int cch);

        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);

        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszArgs, int cch);

        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);

        void GetHotkey(out ushort pwHotkey);

        void SetHotkey(ushort wHotkey);

        void GetShowCmd(out int piShowCmd);

        void SetShowCmd(int iShowCmd);

        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszIconPath, int cch, out int piIcon);

        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);

        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);

        void Resolve(IntPtr hwnd, uint fFlags);

        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }
}
