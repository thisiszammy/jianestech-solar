namespace JianeTech.Client.Schemes;

/// <summary>
/// Which end of the term the investor takes delivery of the installation the scheme
/// carries. Mirrors <c>JianeTech.Data.Enums.SchemeTypeEnum</c> value for value.
/// </summary>
/// <remarks>
/// Restated here rather than shared because the client does not reference the data
/// project — the same reason <c>InvestmentMath</c> restates the validator's rules. The
/// numbers are a persisted contract: they are what <c>dbo.InvestmentSchemes.SchemeType</c>
/// holds, so they must not be reordered on either side.
/// </remarks>
public enum SchemeStructure
{
    /// <summary>
    /// The installation goes up at the head of the term. The monthly payment is profit
    /// only until the tail, where the capital is returned alongside it.
    /// </summary>
    Immediate = 0,

    /// <summary>
    /// The installation is delivered at the end of the term. Every monthly payment carries
    /// capital and profit together, so the capital is paid down across the whole term
    /// rather than concentrated at its end.
    /// </summary>
    Deferred = 1,
}

/// <summary>
/// An investment scheme's terms as the configuration page holds them while they are being
/// typed, together with the placement they are being modelled at.
/// </summary>
/// <remarks>
/// <para>
/// A record struct so that changing any field produces a new value: the page recomputes
/// from a whole set of terms rather than patching a figure at a time, which is what keeps
/// the rail beside the form from ever showing a return derived from a term that has since
/// been edited.
/// </para>
/// <para>
/// <see cref="Placement"/> is the one field here that is not stored on the scheme. A
/// scheme sets a <em>minimum</em>, and the arithmetic has to be done on some actual sum —
/// so the page models it at the minimum by default and lets that be changed. It matters
/// more than it looks: the profit interest scales with the placement but the installation
/// does not, so the same scheme earns a markedly better annual return at its floor than
/// at ten times it. That is a fact about the scheme's design, and the only way to see it
/// is to be able to move this figure.
/// </para>
/// </remarks>
public readonly record struct InvestmentTerms(
    decimal Placement,
    SchemeStructure Structure,
    decimal IncomeInterest,
    int GracePeriodMonths,
    int InvestmentYears,
    decimal FreeInstallationAmt);

/// <summary>
/// One month of the payout schedule, as the ledger prints it. Month 0 is the placement
/// itself — the moment the money goes in, and where an immediate scheme's installation
/// lands.
/// </summary>
public readonly record struct InvestmentPeriod(
    int Month,
    decimal Profit,
    decimal Capital,
    decimal Installation,
    decimal Total,
    decimal CumulativeReturned,
    decimal OutstandingCapital);

/// <summary>
/// Everything that follows from a set of terms. Nothing here is stored; it is all read
/// back off the six figures in <see cref="InvestmentTerms"/>.
/// </summary>
public sealed class InvestmentFigures
{
    /// <summary>The whole term in months — the years, times twelve.</summary>
    public int TermMonths { get; init; }

    /// <summary>
    /// The months that actually pay: the term less the grace period at its head. This is
    /// the divisor in both structures' monthly payment, which is why the validator refuses
    /// a grace period that would drive it to zero.
    /// </summary>
    public int PayoutMonths { get; init; }

    /// <summary>The first month that pays anything — the month after the grace period ends.</summary>
    public int FirstPayoutMonth { get; init; }

    /// <summary>
    /// On an immediate scheme, how many months at the end of the term carry capital
    /// alongside the profit. Zero on a deferred scheme, where every paying month carries
    /// some.
    /// </summary>
    public int TailMonths { get; init; }

    /// <summary>The first month of that tail. Zero when there is no tail.</summary>
    public int FirstTailMonth { get; init; }

    /// <summary>
    /// The profit interest in pesos actually paid over the whole term. This is the floored
    /// monthly figure multiplied by the months that pay it, not the theoretical pot — see
    /// <see cref="InvestmentMath"/> on why the two differ by a few pesos.
    /// </summary>
    public decimal InterestAmount { get; init; }

    /// <summary>
    /// The capital actually handed back, which the same flooring can leave a peso or two
    /// under the placement. Printed as what it is rather than rounded up to look tidy.
    /// </summary>
    public decimal CapitalReturned { get; init; }

    /// <summary>
    /// The interest actually paid, as a share of the placement. A scheme quoted at 12% a
    /// year over five years comes to 60% of the capital, and that is worth printing beside
    /// the annual rate rather than left to be worked out.
    /// </summary>
    /// <remarks>
    /// Worked out from <see cref="InterestAmount"/> rather than from the quoted rate times
    /// the years, so it agrees to the peso with the money printed above it. The two differ
    /// only by the flooring, and only in the last decimal place the rail prints — but a
    /// percentage that does not reproduce the figure it sits under is the whole reason this
    /// group had to be rebuilt.
    /// </remarks>
    public decimal InterestShare { get; init; }

    /// <summary>
    /// The installation as a share of the placement. Stated over the term rather than per
    /// year, because it is received once: dividing it across the years would describe an
    /// annual yield the scheme does not pay.
    /// </summary>
    public decimal InstallationShare { get; init; }

    /// <summary>
    /// The whole profit as a share of the placement — <see cref="InterestShare"/> and
    /// <see cref="InstallationShare"/> together, and the percentage form of
    /// <see cref="TotalProfit"/>.
    /// </summary>
    /// <remarks>
    /// The three are computed from the same three money figures, so the rail's components
    /// add up to its total by construction rather than by coincidence.
    /// </remarks>
    public decimal ProfitShare { get; init; }

    /// <summary>
    /// The recurring monthly payment. Profit alone on an immediate scheme; capital and
    /// profit together on a deferred one.
    /// </summary>
    public decimal PeriodicReturn { get; init; }

    /// <summary>
    /// What a month in the tail pays: the recurring payment plus that month's slice of
    /// capital. Equal to <see cref="PeriodicReturn"/> on a deferred scheme, which has no
    /// tail because it has been returning capital all along.
    /// </summary>
    public decimal TailReturn { get; init; }

    /// <summary>The capital inside one paying month — of the tail on an immediate scheme, of every month on a deferred one.</summary>
    public decimal MonthlyCapital { get; init; }

    /// <summary>Everything the investor ends up with: capital, profit and the installation.</summary>
    public decimal TotalReturned { get; init; }

    /// <summary>What they end up with above what they placed.</summary>
    public decimal TotalProfit { get; init; }

    /// <summary>
    /// What the placement earns on the capital still at risk, given when each peso comes
    /// back. Solved from the real schedule rather than derived, which is why it is the one
    /// figure that tells an immediate scheme apart from a deferred one on identical
    /// headline terms: the deferred scheme starts returning capital with its first
    /// payment, so the same profit is being earned on a steadily smaller balance.
    /// </summary>
    /// <remarks>
    /// Annualised nominally — the solved monthly rate times twelve — rather than
    /// compounded into an effective annual rate. Compounding it would assume each payment
    /// is put back to work at the same rate, and this scheme pays out and stops. See the
    /// note on <see cref="InvestmentMath"/> for why no compound figure appears anywhere on
    /// this page.
    /// </remarks>
    public decimal EffectiveAnnualRate { get; init; }

    /// <summary>
    /// The month the investor is first whole again: where everything received reaches what
    /// was placed. Null when the terms never get there, which the flooring can just about
    /// manage on a scheme carrying no installation.
    /// </summary>
    public int? CapitalRecoveredMonth { get; init; }

    /// <summary>
    /// The pesos the floored payments leave undistributed over the whole term. Small by
    /// construction — at most one peso per payment — but it is the reason the schedule's
    /// columns do not quite add to the quoted interest, and the page would rather name it
    /// than let a reader find it.
    /// </summary>
    public decimal RoundingRemainder { get; init; }

    /// <summary>
    /// False when the terms do not describe a scheme yet — nothing placed, no term, or a
    /// grace period that swallows it. The page uses it to leave the charts and the rail
    /// empty rather than drawing a division by zero.
    /// </summary>
    public bool IsCoherent { get; init; }
}

/// <summary>
/// The arithmetic behind an investment scheme.
/// </summary>
/// <remarks>
/// <para>
/// This lives in the client, not in <c>JianeTech.Services</c>, for the same reason
/// <see cref="SchemeMath"/> does: the configuration page has to answer "what does this
/// return?" on every keystroke, long before anything is saved, so the arithmetic has to be
/// here anyway. A second copy on the server would mean two implementations of one
/// calculation that could disagree — and the one the reader is watching while they type
/// would be the one that was not authoritative. The server stores the six terms and
/// enforces the single rule that makes them coherent (see
/// <c>InvestmentSchemeValidator</c>); everything else is read back off them here.
/// </para>
/// <para>
/// <b>Every payment is floored to the whole peso, and nothing settles the difference.</b>
/// A million placed for three years at 12% earns a pot of 360,000, which over 33 paying
/// months is 10,909.09 — and the scheme pays 10,909, thirty-three times, for 359,997. The
/// three pesos are not made up in the final month. That is a deliberate property of the
/// product rather than a rounding convenience, so the schedule here reproduces it exactly
/// and <see cref="InvestmentFigures.RoundingRemainder"/> names what it costs.
/// </para>
/// <para>
/// <b>Nothing here compounds, and no figure on this page may imply that it does.</b> The
/// whole return is fixed and scheduled before the money is placed: the interest is worked
/// out once on the opening balance and distributed, so no unpaid interest ever exists for
/// a later period's rate to act on, and nothing paid out is reinvested. An equivalent
/// compound rate was printed here once — the annual rate that would grow the placement
/// into the same total — and it had to go. It came out at 10.79% on a scheme quoting 12%
/// a year, because compounding reaches the same total from a lower rate, so the page was
/// contradicting itself three rows apart. Worse, it was blind to the one thing that
/// actually separates the two structures: it reads identically for both, since it sees
/// only the first amount and the last. <see cref="InvestmentFigures.EffectiveAnnualRate"/>
/// is the figure that does the job, and it is annualised nominally for the same reason.
/// </para>
/// <para>
/// Money is <see cref="decimal"/> throughout. The rate solve drops to
/// <see cref="double"/> because it needs <see cref="Math.Pow"/> with a fractional
/// exponent, which decimal has no equivalent for; the result comes straight back to
/// decimal and no money ever passes through binary floating point.
/// </para>
/// </remarks>
public static class InvestmentMath
{
    /// <summary>
    /// How many months past the grace period the capital return runs for, on an immediate
    /// scheme. The tail mirrors the head: a scheme that waits three months before paying
    /// anything hands the capital back over its last five.
    /// </summary>
    private const int TailMonthsBeyondGrace = 2;

    /// <summary>
    /// The widest monthly rate the solver will consider — 100% a month. Nothing a fund
    /// would offer comes close; it exists so the bisection has a bracket it cannot escape.
    /// </summary>
    private const double MaxMonthlyRate = 1.0;

    /// <summary>
    /// Enough halvings of a bracket one wide to pin the rate to roughly 1e-15, which is
    /// past the precision of every figure it is used to produce.
    /// </summary>
    private const int SolverIterations = 200;

    /// <summary>
    /// The figures the rail prints. Builds the schedule on the way, because the effective
    /// rate is solved from the real cash flows and there is no shortcut to those — but a
    /// register row wants only the summary, so <see cref="SummariseBrief"/> exists for it.
    /// </summary>
    public static InvestmentFigures Summarise(InvestmentTerms terms)
        => Compose(terms, BuildSchedule(terms));

    /// <summary>
    /// The same figures, against a schedule the caller already has. The configuration page
    /// builds one per keystroke to draw the ledger and the chart from, and a fifty-year
    /// term is six hundred rows — there is no reason to build it twice.
    /// </summary>
    public static InvestmentFigures Summarise(
        InvestmentTerms terms, IReadOnlyList<InvestmentPeriod> schedule)
        => Compose(terms, schedule);

    /// <summary>
    /// The summary without the effective rate — everything a register row prints, and
    /// nothing that needs a schedule built to produce it.
    /// </summary>
    /// <remarks>
    /// The register shows twenty rows at a time and has no use for twenty payout schedules
    /// or twenty root-finds. The effective rate comes back zero here; the column that would
    /// print it does not exist in the register, which prints the quoted rate instead.
    /// </remarks>
    public static InvestmentFigures SummariseBrief(InvestmentTerms terms)
        => Compose(terms, schedule: null);

    /// <summary>
    /// The one body behind the three entry points above. A null schedule is the brief
    /// form: the two figures that need the real cash flows come back unset rather than
    /// wrong.
    /// </summary>
    private static InvestmentFigures Compose(
        InvestmentTerms terms, IReadOnlyList<InvestmentPeriod>? schedule)
    {
        var plan = Plan(terms);

        if (!plan.IsCoherent)
        {
            return new InvestmentFigures
            {
                TermMonths = plan.TermMonths,
                PayoutMonths = plan.PayoutMonths,
                FirstPayoutMonth = terms.GracePeriodMonths + 1,
                IsCoherent = false,
            };
        }

        var totalReturned = plan.CapitalReturned + plan.InterestPaid + terms.FreeInstallationAmt;

        // Above what was placed, not above what was promised. The flooring can leave the
        // capital a peso or two short, and a profit figure that ignored that would be the
        // one number on the page that did not agree with the ledger under it.
        var totalProfit = totalReturned - terms.Placement;

        return new InvestmentFigures
        {
            TermMonths = plan.TermMonths,
            PayoutMonths = plan.PayoutMonths,
            FirstPayoutMonth = terms.GracePeriodMonths + 1,
            TailMonths = plan.TailMonths,
            FirstTailMonth = plan.FirstTailMonth,
            InterestAmount = plan.InterestPaid,
            CapitalReturned = plan.CapitalReturned,
            PeriodicReturn = plan.MonthlyPayment,
            TailReturn = plan.MonthlyPayment + plan.TailCapital,
            MonthlyCapital = plan.TailCapital,
            TotalReturned = totalReturned,
            TotalProfit = totalProfit,

            // All three off the same three money figures and the same base, so the two
            // components add to the total on screen instead of nearly adding to it.
            InterestShare = plan.InterestPaid / terms.Placement,
            InstallationShare = terms.FreeInstallationAmt / terms.Placement,
            ProfitShare = totalProfit / terms.Placement,

            EffectiveAnnualRate = schedule is null
                ? 0m
                : AnnualiseNominally(SolveMonthlyRate(terms, schedule)),
            CapitalRecoveredMonth = schedule is null
                ? null
                : FindCapitalRecoveredMonth(terms.Placement, schedule),
            RoundingRemainder =
                (plan.InterestPot - plan.InterestPaid) + (terms.Placement - plan.CapitalReturned),
            IsCoherent = true,
        };
    }

    /// <summary>
    /// Everything about a scheme's payments that both the summary and the schedule need,
    /// worked out once so the two cannot disagree about a single peso.
    /// </summary>
    private readonly record struct PaymentPlan(
        bool IsCoherent,
        int TermMonths,
        int PayoutMonths,
        int TailMonths,
        int FirstTailMonth,
        decimal InterestPot,
        decimal MonthlyPayment,
        decimal MonthlyProfit,
        decimal MonthlyCapital,
        decimal TailCapital,
        decimal InterestPaid,
        decimal CapitalReturned);

    private static PaymentPlan Plan(InvestmentTerms terms)
    {
        var termMonths = terms.InvestmentYears * 12;
        var payoutMonths = termMonths - terms.GracePeriodMonths;

        var coherent = terms.Placement > 0
            && terms.InvestmentYears > 0
            && terms.GracePeriodMonths >= 0
            && payoutMonths > 0;

        if (!coherent)
        {
            return new PaymentPlan { IsCoherent = false, TermMonths = termMonths, PayoutMonths = payoutMonths };
        }

        // The interest the scheme sets out to pay: the placement, the annual rate, and the
        // years it runs for. Simple rather than compound — it is distributed as it is
        // earned, so no unpaid interest is ever left for a later year's rate to act on.
        var pot = terms.Placement * AnnualRate(terms) * terms.InvestmentYears;

        if (terms.Structure == SchemeStructure.Immediate)
        {
            // The tail mirrors the head, and is clamped to the paying window: a grace
            // period long enough that grace + 2 would overrun the term simply means the
            // capital comes back across every month that pays.
            var tailMonths = Math.Min(terms.GracePeriodMonths + TailMonthsBeyondGrace, payoutMonths);
            var monthlyProfit = Floor(pot / payoutMonths);
            var tailCapital = Floor(terms.Placement / tailMonths);

            return new PaymentPlan
            {
                IsCoherent = true,
                TermMonths = termMonths,
                PayoutMonths = payoutMonths,
                TailMonths = tailMonths,
                FirstTailMonth = termMonths - tailMonths + 1,
                InterestPot = pot,

                // Every paying month pays the profit; only the tail adds capital to it.
                MonthlyPayment = monthlyProfit,
                MonthlyProfit = monthlyProfit,
                MonthlyCapital = 0m,
                TailCapital = tailCapital,

                InterestPaid = monthlyProfit * payoutMonths,
                CapitalReturned = tailCapital * tailMonths,
            };
        }

        // Deferred: capital and profit travel together in one floored payment. The capital
        // share is floored on its own so the two parts always add back to the payment
        // exactly, which is what lets the schedule's columns be summed and checked.
        var payment = Floor((terms.Placement + pot) / payoutMonths);
        var capitalShare = Floor(terms.Placement / payoutMonths);
        var profitShare = payment - capitalShare;

        return new PaymentPlan
        {
            IsCoherent = true,
            TermMonths = termMonths,
            PayoutMonths = payoutMonths,
            TailMonths = 0,
            FirstTailMonth = 0,
            InterestPot = pot,

            MonthlyPayment = payment,
            MonthlyProfit = profitShare,
            MonthlyCapital = capitalShare,
            TailCapital = 0m,

            InterestPaid = profitShare * payoutMonths,
            CapitalReturned = capitalShare * payoutMonths,
        };
    }

    /// <summary>
    /// The payout month by month, from the placement itself to the end of the term.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Month 0 is the placement. It is a row rather than a preamble because on an
    /// immediate scheme something really does happen there — the installation goes up
    /// before a peso of income has been earned — and on a deferred one the fact that
    /// nothing happens is exactly what distinguishes the two.
    /// </para>
    /// <para>
    /// Every figure is floored to the whole peso and no month settles the remainder. That
    /// is the product, not a convenience: a scheme paying 10,909.09 pays 10,909 every time
    /// it pays, and the schedule would be lying if its last row quietly made the
    /// difference up.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<InvestmentPeriod> BuildSchedule(InvestmentTerms terms)
    {
        var plan = Plan(terms);

        if (!plan.IsCoherent)
        {
            return [];
        }

        var isImmediate = terms.Structure == SchemeStructure.Immediate;
        var rows = new List<InvestmentPeriod>(plan.TermMonths + 1);

        var cumulative = 0m;
        var outstanding = terms.Placement;

        // Month 0: the money goes in. An immediate scheme's installation lands here, which
        // is the whole of what "immediate" means.
        var openingInstallation = isImmediate ? terms.FreeInstallationAmt : 0m;
        cumulative += openingInstallation;

        rows.Add(new InvestmentPeriod(
            Month: 0,
            Profit: 0m,
            Capital: 0m,
            Installation: openingInstallation,
            Total: openingInstallation,
            CumulativeReturned: cumulative,
            OutstandingCapital: outstanding));

        for (var month = 1; month <= plan.TermMonths; month++)
        {
            var isPaying = month > terms.GracePeriodMonths;
            var isLastMonth = month == plan.TermMonths;

            var profit = isPaying ? plan.MonthlyProfit : 0m;

            var capital = !isPaying ? 0m
                : isImmediate ? (month >= plan.FirstTailMonth ? plan.TailCapital : 0m)
                : plan.MonthlyCapital;

            // The installation is the other half of what the structure decides: it either
            // landed at month 0 above, or it lands here.
            var installation = isLastMonth && !isImmediate ? terms.FreeInstallationAmt : 0m;

            outstanding -= capital;

            var total = profit + capital + installation;
            cumulative += total;

            rows.Add(new InvestmentPeriod(
                Month: month,
                Profit: profit,
                Capital: capital,
                Installation: installation,
                Total: total,
                CumulativeReturned: cumulative,
                OutstandingCapital: Math.Max(0m, outstanding)));
        }

        return rows;
    }

    /// <summary>
    /// Two running lines over the months of the term: everything the investor has been
    /// handed by that point, against the sum they placed to begin with.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Where they cross is the month the capital is whole again, and on this product that
    /// is the question worth drawing. Two schemes on identical terms return identical
    /// totals — the only thing that separates them is when, and a chart of totals could
    /// not show it. This one does: a deferred scheme crosses the line around two thirds of
    /// the way through, an immediate one only in its tail.
    /// </para>
    /// <para>
    /// There is no compounding curve here, and that is deliberate. This scheme's whole
    /// return is fixed and scheduled before a peso moves; nothing accrues on anything, so
    /// a curve drawn at an equivalent compound rate would be a picture of a product the
    /// fund does not sell.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<(int Month, decimal Returned, decimal Placed)> BuildRecovery(
        InvestmentTerms terms, IReadOnlyList<InvestmentPeriod> schedule)
    {
        if (schedule.Count == 0)
        {
            return [];
        }

        // The placement is carried as a value per month rather than as two end points, so
        // the chart's shared tooltip has something to report for it at every month the
        // reader hovers.
        return schedule
            .Select(p => (p.Month, p.CumulativeReturned, terms.Placement))
            .ToList();
    }

    /// <summary>
    /// The monthly rate at which this schedule's receipts discount back to exactly what was
    /// placed — solved by bisection rather than by a formula, because there is no closed
    /// form for it.
    /// </summary>
    /// <remarks>
    /// The present value of a set of receipts falls monotonically as the rate rises, which
    /// is what makes bisection both safe and sufficient: at a rate of zero the receipts are
    /// worth their full face value (above the placement, since the validator requires a
    /// profit interest above zero), and at 100% a month they are worth almost nothing.
    /// Month 0's receipt is not discounted — an immediate scheme's installation is in hand
    /// on the day the money goes in, and that is exactly why it earns more than a deferred
    /// scheme on the same headline terms.
    /// </remarks>
    private static decimal SolveMonthlyRate(
        InvestmentTerms terms, IReadOnlyList<InvestmentPeriod> schedule)
    {
        var placed = (double)terms.Placement;

        if (placed <= 0)
        {
            return 0m;
        }

        var flows = schedule.Select(p => ((double)p.Total, p.Month)).ToArray();

        // Nothing to find when the receipts never exceed the placement. Coherent terms
        // always do, so this only fires mid-keystroke.
        if (flows.Sum(f => f.Item1) <= placed)
        {
            return 0m;
        }

        var low = 0.0;
        var high = MaxMonthlyRate;

        for (var i = 0; i < SolverIterations; i++)
        {
            var mid = (low + high) / 2;
            var presentValue = 0.0;

            foreach (var (amount, month) in flows)
            {
                presentValue += month == 0 ? amount : amount / Math.Pow(1 + mid, month);
            }

            if (presentValue > placed)
            {
                low = mid;   // receipts still worth more than the placement: discount harder.
            }
            else
            {
                high = mid;
            }
        }

        return (decimal)((low + high) / 2);
    }

    /// <summary>
    /// A monthly rate stated as an annual one: twelve times it, and nothing more.
    /// </summary>
    /// <remarks>
    /// Nominal rather than effective, unlike <see cref="SchemeMath"/>'s counterpart on the
    /// instalment side. Raising it to the twelfth power would be asserting that each
    /// payment is put straight back to work at the same rate for the rest of the term,
    /// which is precisely what this product does not do: the return is fixed and scheduled
    /// before the money is placed, and a peso paid out in month four is simply gone from
    /// the arrangement. Capped for the same reason the other one is — a half-filled form
    /// can briefly describe a rate no figure on the page should carry.
    /// </remarks>
    private static decimal AnnualiseNominally(decimal monthlyRate)
        => monthlyRate <= 0 ? 0m : Math.Min(monthlyRate * 12m, 999m);

    private static int? FindCapitalRecoveredMonth(
        decimal placement, IReadOnlyList<InvestmentPeriod> schedule)
    {
        foreach (var period in schedule)
        {
            if (period.CumulativeReturned >= placement)
            {
                return period.Month;
            }
        }

        return null;
    }

    /// <summary>
    /// The stored profit interest as a fraction. It is held as a percentage — 12.50 means
    /// 12.5% — because the column is decimal(5, 2), which is far too coarse to carry a
    /// fraction to any useful precision.
    /// </summary>
    private static decimal AnnualRate(InvestmentTerms terms) => terms.IncomeInterest / 100m;

    /// <summary>
    /// Down to the whole peso. Every payment this scheme makes is floored, and nothing
    /// anywhere settles the difference — see the note on <see cref="InvestmentMath"/>.
    /// </summary>
    private static decimal Floor(decimal value) => Math.Floor(value);
}
