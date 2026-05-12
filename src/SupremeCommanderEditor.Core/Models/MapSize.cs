namespace SupremeCommanderEditor.Core.Models;

public static class MapSize
{
    public const int FiveKm = 256;
    public const int TenKm = 512;
    public const int TwentyKm = 1024;
    public const int FortyKm = 2048;
    public const int EightyOneKm = 4096;

    public static readonly int[] ValidSizes = [FiveKm, TenKm, TwentyKm, FortyKm, EightyOneKm];

    public static bool IsValid(int size) => size > 0 && (size & (size - 1)) == 0;

    public static string GetDisplayName(int size) => size switch
    {
        FiveKm => "5x5 km",
        TenKm => "10x10 km",
        TwentyKm => "20x20 km",
        FortyKm => "40x40 km",
        EightyOneKm => "81x81 km",
        _ => $"{size}x{size}"
    };
}
