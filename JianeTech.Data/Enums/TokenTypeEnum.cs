namespace JianeTech.Data.Enums
{
    /// <summary>
    /// Refresh token lifetime class. ShortLived rotates on every use;
    /// LongLived ("remember me") is reused until it expires.
    /// </summary>
    public enum TokenTypeEnum
    {
        ShortLived = 0,
        LongLived = 1,
    }
}
