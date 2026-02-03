using System;
using System.IO;

// Actually, Win32_Volume is cleaner but in .NET Core we might not have System.Management by default without package.
// Let's stick to a simpler approach first: `new DriveInfo("C").RootDirectory` etc doesn't give Serial.
// We can use kernel32.dll GetVolumeInformation.

using System.Runtime.InteropServices;
using System.Text;
using System.Security.Cryptography;

namespace MyFinder.Services;

public class DriveIdentificationService
{
    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool GetVolumeInformation(
        string rootPathName,
        StringBuilder volumeNameBuffer,
        int volumeNameSize,
        out uint volumeSerialNumber,
        out uint maximumComponentLength,
        out uint fileSystemFlags,
        StringBuilder fileSystemNameBuffer,
        int nFileSystemNameSize);

    public string GetDriveId(DriveInfo drive)
    {
        if (drive.DriveType == DriveType.Network)
        {
            // For network drives, use the UNC path hash
            // This is "stable" as long as the mapping points to the same place.
            // Note: We can't easily get the UNC path from DriveInfo in pure .NET without WNetGetConnection.
            // For MVP, we will fallback to Drive Root Name or try to resolve UNC.
            
            string uncParams = GetUncPath(drive.Name);
            if (!string.IsNullOrEmpty(uncParams)) return GetMd5Hash(uncParams);
            
            // Fallback: Just use the Label + DriveType as a weak ID if UNC fails
            return GetMd5Hash($"NET_{drive.VolumeLabel}");
        }
        else
        {
            // Local Drive: Use Volume Serial Number
            try
            {
                uint serialNum, maxLen, flags;
                var volBuffer = new StringBuilder(261);
                var fsBuffer = new StringBuilder(261);
                
                if (GetVolumeInformation(drive.RootDirectory.FullName, volBuffer, volBuffer.Capacity, out serialNum, out maxLen, out flags, fsBuffer, fsBuffer.Capacity))
                {
                    return serialNum.ToString("X");
                }
            }
            catch { }
            
            // Fallback: MD5 of the Root Path (weak, but works for fixed drives usually)
            return GetMd5Hash(drive.RootDirectory.FullName);
        }
    }

    [DllImport("mpr.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int WNetGetConnection(
        [MarshalAs(UnmanagedType.LPTStr)] string localName,
        [MarshalAs(UnmanagedType.LPTStr)] StringBuilder remoteName,
        ref int length);

    private string GetUncPath(string localDrive)
    {
        // "C:\" -> "C:"
        string driveLetter = localDrive.TrimEnd('\\');
        
        var sb = new StringBuilder(512);
        int size = sb.Capacity;
        
        int error = WNetGetConnection(driveLetter, sb, ref size);
        if (error == 0) return sb.ToString();
        
        return string.Empty;
    }

    private string GetMd5Hash(string input)
    {
        using var md5 = MD5.Create();
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = md5.ComputeHash(bytes);
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }
}
