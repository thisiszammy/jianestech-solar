namespace JianeTech.Services.DTOs
{
    /// <summary>
    /// One investment scheme as the console reads it: the six stored terms, who set them,
    /// and whether the scheme is still on offer.
    /// </summary>
    /// <remarks>
    /// Deliberately carries no derived figure — no periodic return, no per-annum profit,
    /// no payout schedule. Every one of those is a pure function of the terms below, and
    /// the configuration page has to recompute them on each keystroke anyway, long before
    /// anything is saved. Sending them would mean two implementations of the same
    /// arithmetic that could disagree, and the one the reader is looking at while they
    /// type would be the one that was not authoritative. The client owns the arithmetic
    /// (<c>InvestmentMath</c>); this carries the terms it is computed from.
    /// <para>
    /// The one exception is <see cref="SchemeTypeLabel"/>. That is not arithmetic — it is
    /// wording, and it comes from <c>SchemeTypeEnumExtensions</c> so the console and the
    /// audit trail call the two structures by the same name.
    /// </para>
    /// </remarks>
    public class InvestmentSchemeDTO
    {
        public Guid SchemeId { get; set; }

        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// <c>SchemeTypeEnum</c> as its stored int. Immediate (0) hands the investor the
        /// installation at the head of the term and returns their capital at the tail;
        /// deferred (1) amortises the capital across the payouts and hands over the
        /// installation at the end.
        /// </summary>
        public int SchemeType { get; set; }

        /// <summary>"Immediate" / "Deferred" — the word, not the enum name.</summary>
        public string SchemeTypeLabel { get; set; } = string.Empty;

        /// <summary>Where in the term the installation lands, said in words.</summary>
        public string SchemeTypeTiming { get; set; } = string.Empty;

        /// <summary>The floor under a placement. Nothing smaller may be taken on these terms.</summary>
        public decimal MinimumInvestment { get; set; }

        /// <summary>
        /// The profit interest <em>per annum</em>, as a percentage — 12.50 means 12.5% a
        /// year. Over a five-year term that accumulates to 60% of the capital, simple
        /// rather than compounded, because it is paid out as it is earned. It is a
        /// percentage and not a fraction because the column is decimal(5, 2), which is far
        /// too coarse to hold one (0.12 would be the finest step available).
        /// </summary>
        public decimal IncomeInterest { get; set; }

        /// <summary>
        /// Months at the head of the term that pay nothing. The fund is building and
        /// commissioning during them; there is no income yet to distribute.
        /// </summary>
        public int GracePeriodMonths { get; set; }

        /// <summary>The whole term, in years. Times twelve is the term in months.</summary>
        public int InvestmentYears { get; set; }

        /// <summary>
        /// What the installation the investor receives at no cost is worth. Paid in kind
        /// rather than in cash, which is why it sits outside the periodic return and lands
        /// whole at one end of the term.
        /// </summary>
        public decimal FreeInstallationAmt { get; set; }

        /// <summary>Whether the scheme may still be offered to an investor.</summary>
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
