using System;
using System.Runtime.InteropServices;

namespace Claudium.Services;

/// <summary>
/// Native Win32 folder picker (IFileOpenDialog with FOS_PICKFOLDERS).
/// Windows.Storage.Pickers.FolderPicker (the WinRT picker) throws
/// COMException 0x80004005 (E_FAIL) in unpackaged, non-MSIX apps like this one —
/// it expects package identity that a plain .exe doesn't have. This is the standard
/// workaround: talk to the same folder-picker dialog through its Win32 COM interface.
/// </summary>
internal static class NativeFolderPicker
{
    private const uint FOS_PICKFOLDERS = 0x00000020;
    private const uint FOS_FORCEFILESYSTEM = 0x00000040;
    private const uint SIGDN_FILESYSPATH = 0x80058000;
    private const int ERROR_CANCELLED_HRESULT = unchecked((int)0x800704C7);

    /// <summary>Shows the picker modally over <paramref name="ownerHwnd"/>; returns null if cancelled.</summary>
    public static string? PickFolder(IntPtr ownerHwnd)
    {
        var dialog = (IFileOpenDialog)new FileOpenDialogRCW();
        try
        {
            dialog.GetOptions(out uint options);
            dialog.SetOptions(options | FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM);

            return ShowAndGetPath(dialog, ownerHwnd);
        }
        finally
        {
            Marshal.ReleaseComObject(dialog);
        }
    }

    /// <summary>Shows a plain file picker (any file type) modally; returns null if cancelled.</summary>
    public static string? PickFile(IntPtr ownerHwnd)
    {
        var dialog = (IFileOpenDialog)new FileOpenDialogRCW();
        try
        {
            dialog.GetOptions(out uint options);
            dialog.SetOptions(options | FOS_FORCEFILESYSTEM);

            return ShowAndGetPath(dialog, ownerHwnd);
        }
        finally
        {
            Marshal.ReleaseComObject(dialog);
        }
    }

    private static string? ShowAndGetPath(IFileOpenDialog dialog, IntPtr ownerHwnd)
    {
        int hr = dialog.Show(ownerHwnd);
        if (hr == ERROR_CANCELLED_HRESULT)
        {
            return null;
        }
        if (hr != 0)
        {
            Marshal.ThrowExceptionForHR(hr);
        }

        dialog.GetResult(out IShellItem item);
        try
        {
            item.GetDisplayName(SIGDN_FILESYSPATH, out IntPtr pathPtr);
            try
            {
                return Marshal.PtrToStringUni(pathPtr);
            }
            finally
            {
                Marshal.FreeCoTaskMem(pathPtr);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(item);
        }
    }

    [ComImport]
    [Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
    private class FileOpenDialogRCW
    {
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("d57c7288-d4ad-4768-be02-9d969532d960")]
    private interface IFileOpenDialog
    {
        [PreserveSig]
        int Show(IntPtr parent);
        void SetFileTypes(uint cFileTypes, IntPtr rgFilterSpec);
        void SetFileTypeIndex(uint iFileType);
        void GetFileTypeIndex(out uint piFileType);
        void Advise(IntPtr pfde, out uint pdwCookie);
        void Unadvise(uint dwCookie);
        void SetOptions(uint fos);
        void GetOptions(out uint pfos);
        void SetDefaultFolder(IShellItem psi);
        void SetFolder(IShellItem psi);
        void GetFolder(out IShellItem ppsi);
        void GetCurrentSelection(out IShellItem ppsi);
        void SetFileName(string pszName);
        void GetFileName(out IntPtr pszName);
        void SetTitle(string pszTitle);
        void SetOkButtonLabel(string pszText);
        void SetFileNameLabel(string pszLabel);
        void GetResult(out IShellItem ppsi);
        void AddPlace(IShellItem psi, uint alignment);
        void SetDefaultExtension(string pszDefaultExtension);
        void Close(int hr);
        void SetClientGuid(ref Guid guid);
        void ClearClientData();
        void SetFilter(IntPtr pFilter);
        void GetResults(out IntPtr ppenum);
        void GetSelectedItems(out IntPtr ppsai);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
    private interface IShellItem
    {
        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        void GetParent(out IShellItem ppsi);
        void GetDisplayName(uint sigdnName, out IntPtr ppszName);
        void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        void Compare(IShellItem psi, uint hint, out int piOrder);
    }
}
