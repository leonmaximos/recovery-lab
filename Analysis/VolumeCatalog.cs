using System.IO;

namespace RecoveryLab;

internal sealed record VolumeOption(char Letter, string DisplayName)
{
    internal byte[] ReadBytes(long offset, int count) => RawDiskReader.ReadBytes(Letter, offset, count);
}

internal static class VolumeCatalog
{
    internal static IReadOnlyList<VolumeOption> GetNtfsVolumes()
    {
        List<VolumeOption> volumes = new();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.DriveType is not (DriveType.Fixed or DriveType.Removable) || !drive.IsReady
                    || !string.Equals(drive.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase))
                    continue;

                char letter = char.ToUpperInvariant(drive.Name[0]);
                string label = ReadLabel(drive);
                volumes.Add(new VolumeOption(letter, string.IsNullOrWhiteSpace(label)
                    ? $"{letter}: · Volume NTFS" : $"{letter}: · {label}"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // This drive may have been disconnected while the list was queried.
            }
        }

        return volumes.OrderBy(volume => volume.Letter).ToArray();
    }

    private static string ReadLabel(DriveInfo drive)
    {
        try
        {
            return drive.VolumeLabel;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }
}
