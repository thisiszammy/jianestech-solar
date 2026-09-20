namespace JianeTech.Data.Entities
{
    public partial class InstallmentScheme
    {
        /// <summary>
        /// The scheme's primary key, widened into the Guid that
        /// <c>dbo.ActivityLogs.Reference</c> holds.
        /// </summary>
        /// <remarks>
        /// Every other audited entity in this platform is keyed by a Guid, so the audit
        /// column is a <c>uniqueidentifier</c> — and <c>dbo.InstallmentSchemes.SchemeId</c>
        /// is an <c>int</c>. Rather than leave the reference null (which would make a
        /// scheme the one domain object whose audit rows cannot be targeted) the id is
        /// written into the first four bytes and the rest left zero, so scheme 3 reads as
        /// <c>00000003-0000-0000-0000-000000000000</c> in the log. That is legible at a
        /// glance, reversible by <see cref="FromAuditReference"/>, and cannot collide with
        /// a real Guid at any rate worth worrying about.
        /// </remarks>
        public static Guid ToAuditReference(int schemeId) => new(schemeId, 0, 0, new byte[8]);

        /// <summary>
        /// The inverse of <see cref="ToAuditReference"/>: reads a scheme id back out of an
        /// audit row's reference, or null when the value was not written by it.
        /// </summary>
        public static int? FromAuditReference(Guid reference)
        {
            var bytes = reference.ToByteArray();

            // Everything after the first four bytes must be zero, or this Guid came from
            // somewhere else and the int inside it would be meaningless.
            for (var i = 4; i < bytes.Length; i++)
            {
                if (bytes[i] != 0) return null;
            }

            return BitConverter.ToInt32(bytes, 0);
        }
    }
}
