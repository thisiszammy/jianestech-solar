namespace JianeTech.Services.DTOs
{
    /// <summary>
    /// One installment scheme as the console reads it: the five stored terms, who set
    /// them, and whether the scheme is still on offer.
    /// </summary>
    /// <remarks>
    /// Deliberately carries no derived figure — no finance charge, no add-on rate, no
    /// instalment measured against the guideline bill. Every one of those is a pure
    /// function of the terms below, and the configuration page recomputes them on each
    /// keystroke anyway,
    /// long before anything is saved. Sending them would mean two implementations of the
    /// same arithmetic that could disagree, and the one the reader is looking at while
    /// they type would be the one that was not authoritative. The client owns the
    /// arithmetic (<c>SchemeMath</c>); this carries the terms it is computed from.
    /// </remarks>
    public class InstallmentSchemeDTO
    {
        public int SchemeId { get; set; }

        public string Description { get; set; } = string.Empty;

        /// <summary>The cash price of the installation, before anything is paid.</summary>
        public decimal PrincipalAmount { get; set; }

        /// <summary>Paid up front. The rest is what the scheme finances.</summary>
        public decimal DownPayment { get; set; }

        /// <summary>The amount due each month for <see cref="MonthPeriods"/> months.</summary>
        public decimal PeriodicPayment { get; set; }

        public int MonthPeriods { get; set; }

        /// <summary>
        /// The monthly electricity bill this scheme is recommended up to: a household
        /// billed this much or less is one the scheme suits. It is advisory — the figure
        /// that decides who is offered the scheme, and nothing more. It is not part of the
        /// debt, it pays no part of the instalment, and nothing is ever netted against it.
        /// </summary>
        public decimal ReferenceBillAmount { get; set; }

        /// <summary>Whether the scheme may still be offered to a customer.</summary>
        public bool IsActive { get; set; }

        /// <summary>On offer / Withdrawn — one word the table both prints and paints with.</summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>Same vocabulary the rest of the console's dots use: success / warn.</summary>
        public string StatusTone { get; set; } = string.Empty;

        public DateTime? CreatedOn { get; set; }

        /// <summary>Null when no identifiable actor is recorded against the row.</summary>
        public string? CreatedByName { get; set; }

        public DateTime? UpdatedOn { get; set; }

        public string? UpdatedByName { get; set; }
    }
}
