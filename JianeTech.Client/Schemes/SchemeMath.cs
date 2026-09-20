namespace JianeTech.Client.Schemes;

/// <summary>
/// The five stored terms of an installment scheme, as the configuration page holds them
/// while they are being typed.
/// </summary>
/// <remarks>
/// A record struct so that changing any field produces a new value: the page recomputes
/// from a whole set of terms rather than patching a figure at a time, which is what keeps
/// the rail beside the form from ever showing a rate derived from a payment that has since
/// been edited.
/// </remarks>
/// <param name="ReferenceBillAmount">
/// The monthly electricity bill this scheme is pitched at, as a ceiling: it is recommended
/// to households billed this much or less. It is <b>advisory</b> — the figure that decides
/// which scheme a household is offered, and nothing else. It is not money, it settles
/// nothing, and no instalment is ever netted against it. Zero means no guideline has been
/// recorded, which is a gap in the scheme's setup rather than a scheme for every household.
/// </param>
public readonly record struct SchemeTerms(
    decimal PrincipalAmount,
    decimal DownPayment,
    decimal PeriodicPayment,
    int MonthPeriods,
    decimal ReferenceBillAmount);

/// <summary>One month of the amortisation, as the schedule ledger prints it.</summary>
public readonly record struct SchedulePeriod(
    int Month,
    decimal Payment,
    decimal Charge,
    decimal Capital,
    decimal ClosingBalance);

/// <summary>
/// Everything that follows from a set of terms. Nothing here is stored; it is all read
/// back off the five figures in <see cref="SchemeTerms"/>.
/// </summary>
public sealed class SchemeFigures
{
    /// <summary>Principal less the down payment — what the scheme actually lends.</summary>
    public decimal AmountFinanced { get; init; }

    /// <summary>The monthly payment multiplied by the term.</summary>
    public decimal TotalOfPayments { get; init; }

    /// <summary>What the household hands over in total: down payment plus every instalment.</summary>
    public decimal TotalContractPrice { get; init; }

    /// <summary>
    /// The cost of the credit: what is repaid above what was financed. Zero on an
    /// interest-free scheme, and never negative — the terms are refused before that.
    /// </summary>
    public decimal FinanceCharge { get; init; }

    /// <summary>
    /// The add-on rate, per month, as a fraction. This is how instalment terms are quoted
    /// in the Philippine market: the whole charge is worked out on the opening balance and
    /// divided evenly across the term, so it is the number a customer will recognise — and
    /// it is always lower than what the money actually costs, which is why
    /// <see cref="EffectiveAnnualRate"/> is printed beside it and never instead of it.
    /// </summary>
    public decimal AddOnMonthlyRate { get; init; }

    /// <summary>
    /// The rate that actually discounts these payments back to the amount financed, per
    /// month. Solved rather than derived: see <see cref="SchemeMath"/>.
    /// </summary>
    public decimal EffectiveMonthlyRate { get; init; }

    /// <summary>The effective monthly rate compounded over a year.</summary>
    public decimal EffectiveAnnualRate { get; init; }

    /// <summary>
    /// The instalment as a fraction of the bill this scheme is recommended up to. A sizing
    /// check and nothing more: it says how heavily the scheme presses a household at the
    /// top of its band, which is what makes two schemes on different bands comparable.
    /// Zero when no guideline has been recorded.
    /// </summary>
    /// <remarks>
    /// A proportion, never a difference. The bill does not pay the instalment and is not
    /// netted off it — see <see cref="SchemeTerms.ReferenceBillAmount"/>.
    /// </remarks>
    public decimal InstalmentShareOfBill { get; init; }

    /// <summary>
    /// The month in which the running total the household has handed over — the down
    /// payment, plus every instalment since — first reaches the cash price of the
    /// installation. Everything paid after it is the cost of the credit. Null while the
    /// terms do not hold together.
    /// </summary>
    public int? CashPriceMonth { get; init; }

    /// <summary>
    /// False when the terms do not describe a scheme yet — nothing financed, no term, no
    /// payment. The page uses it to leave the charts and the rail empty rather than
    /// drawing a division by zero.
    /// </summary>
    public bool IsCoherent { get; init; }
}

/// <summary>
/// The arithmetic behind an installment scheme.
/// </summary>
/// <remarks>
/// <para>
/// This lives in the client, not in <c>JianeTech.Services</c>, and it is deliberate. The
/// configuration page has to answer "what does this cost?" on every keystroke, long before
/// anything is saved, so the arithmetic has to be here anyway. Putting a second copy on
/// the server would mean two implementations of one calculation that could disagree — and
/// the one the reader is watching while they type would be the one that was not
/// authoritative. The server stores the five terms and enforces the single rule that makes
/// them coherent (see <c>InstallmentSchemeValidator</c>); everything else is read back off
/// them here.
/// </para>
/// <para>
/// Money is <see cref="decimal"/> throughout. The rate solve drops to <see cref="double"/>
/// because it needs <see cref="Math.Pow"/> with a fractional exponent, which decimal has
/// no equivalent for; the result comes straight back to decimal and no money ever passes
/// through binary floating point.
/// </para>
/// </remarks>
public static class SchemeMath
{
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
    /// The figures the rail prints. Does not build the schedule — the register shows
    /// twenty rows at a time and has no use for twenty amortisation tables.
    /// </summary>
    public static SchemeFigures Summarise(SchemeTerms terms)
    {
        var financed = terms.PrincipalAmount - terms.DownPayment;
        var total = terms.PeriodicPayment * terms.MonthPeriods;
        var coherent = financed > 0 && terms.MonthPeriods > 0 && terms.PeriodicPayment > 0;

        if (!coherent)
        {
            return new SchemeFigures
            {
                AmountFinanced = financed,
                TotalOfPayments = total,
                TotalContractPrice = terms.DownPayment + total,
                InstalmentShareOfBill = ShareOfBill(terms),
                IsCoherent = false,
            };
        }

        // Clamped at zero rather than reported negative. The server refuses terms whose
        // payments do not cover what was financed, so a negative charge here only ever
        // means the form is halfway through being filled in — and "the credit costs minus
        // four thousand pesos" is not a useful thing to print at somebody mid-keystroke.
        var charge = Math.Max(0m, total - financed);

        var effectiveMonthly = SolveMonthlyRate(financed, terms.PeriodicPayment, terms.MonthPeriods);

        return new SchemeFigures
        {
            AmountFinanced = financed,
            TotalOfPayments = total,
            TotalContractPrice = terms.DownPayment + total,
            FinanceCharge = charge,
            AddOnMonthlyRate = charge / financed / terms.MonthPeriods,
            EffectiveMonthlyRate = effectiveMonthly,
            EffectiveAnnualRate = AnnualiseMonthly(effectiveMonthly),
            InstalmentShareOfBill = ShareOfBill(terms),
            CashPriceMonth = FindCashPriceMonth(terms),
            IsCoherent = true,
        };
    }

    /// <summary>
    /// The month-by-month amortisation at the effective rate, which is the only rate under
    /// which the payments actually retire the balance.
    /// </summary>
    /// <remarks>
    /// Each row's charge is rounded to the centavo, because that is the figure a household
    /// is billed — carrying the unrounded value forward would produce a table whose
    /// arithmetic does not add up on paper. The rounding leaves a few centavos of drift by
    /// the end of a long term, so the final row is settled against the balance rather than
    /// taking the standard payment: the last instalment on a real agreement is the one that
    /// clears the account, and it is the one that absorbs the difference.
    /// </remarks>
    public static IReadOnlyList<SchedulePeriod> BuildSchedule(SchemeTerms terms, SchemeFigures figures)
    {
        if (!figures.IsCoherent)
        {
            return [];
        }

        var rows = new List<SchedulePeriod>(terms.MonthPeriods);
        var balance = figures.AmountFinanced;
        var rate = figures.EffectiveMonthlyRate;

        for (var month = 1; month <= terms.MonthPeriods; month++)
        {
            var charge = Math.Round(balance * rate, 2, MidpointRounding.AwayFromZero);

            var isLast = month == terms.MonthPeriods;
            var capital = isLast ? balance : terms.PeriodicPayment - charge;
            var payment = isLast ? balance + charge : terms.PeriodicPayment;

            balance -= capital;

            rows.Add(new SchedulePeriod(month, payment, charge, capital, Math.Max(0m, balance)));
        }

        return rows;
    }

    /// <summary>
    /// The monthly payment an add-on rate produces: the whole charge worked out on the
    /// opening balance, then the balance and the charge together split evenly across the
    /// term. Used by the configuration page when the rate is the figure being set.
    /// </summary>
    public static decimal PaymentFromAddOnRate(decimal financed, decimal monthlyAddOnRate, int monthPeriods)
    {
        if (financed <= 0 || monthPeriods <= 0)
        {
            return 0m;
        }

        var total = financed + (financed * monthlyAddOnRate * monthPeriods);
        return Math.Round(total / monthPeriods, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// The running total the household has handed over, month by month — the down payment
    /// at month zero, then one instalment a month to the end of the term — carried beside
    /// the cash price of the installation. Where the total passes the cash price is the
    /// point from which everything further paid is the cost of the credit.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This replaced a chart that ran the household's instalments against the electricity
    /// bill they were already paying, and settled where the two crossed. That picture was
    /// wrong about the product: the bill is a guideline for which scheme to offer a
    /// household and is never a source of payment, so there was no second outlay to run
    /// against — the comparison invented one, and read as a saving the fund does not
    /// promise. What the household pays, against what the thing costs in cash, is the
    /// comparison the terms actually support.
    /// </para>
    /// <para>
    /// The cash price is carried as a value per month rather than as two end points, so the
    /// chart's shared tooltip has something to report for it wherever the reader hovers —
    /// the same reason the investment side carries its placement that way.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<(int Month, decimal PaidSoFar, decimal CashPrice)> BuildPaymentProgress(
        SchemeTerms terms)
    {
        var rows = new List<(int, decimal, decimal)>(terms.MonthPeriods + 1);

        for (var month = 0; month <= terms.MonthPeriods; month++)
        {
            rows.Add((
                month,
                terms.DownPayment + (terms.PeriodicPayment * month),
                terms.PrincipalAmount));
        }

        return rows;
    }

    /// <summary>
    /// The rate at which these payments discount back to exactly the amount financed —
    /// solved by bisection rather than by a formula, because there is no closed form for
    /// it.
    /// </summary>
    /// <remarks>
    /// The present value of an annuity falls monotonically as the rate rises, which is what
    /// makes bisection both safe and sufficient here: at a rate of zero the payments are
    /// worth their full face value (at or above the balance, since the terms are refused
    /// otherwise), and at 100% a month they are worth far less. Newton would converge in
    /// fewer steps and can walk off a near-zero rate; two hundred halvings of a bracket one
    /// wide cost nothing on a page that recomputes per keystroke.
    /// </remarks>
    private static decimal SolveMonthlyRate(decimal financed, decimal payment, int monthPeriods)
    {
        var principal = (double)financed;
        var instalment = (double)payment;

        // An interest-free scheme has no rate to find, and the bracket below would be
        // degenerate: present value at rate zero is already exactly the balance.
        if (instalment * monthPeriods <= principal)
        {
            return 0m;
        }

        var low = 0.0;
        var high = MaxMonthlyRate;

        for (var i = 0; i < SolverIterations; i++)
        {
            var mid = (low + high) / 2;
            var presentValue = PresentValueOfAnnuity(instalment, mid, monthPeriods);

            if (presentValue > principal)
            {
                low = mid;   // payments still worth more than the balance: charge more.
            }
            else
            {
                high = mid;
            }
        }

        return (decimal)((low + high) / 2);
    }

    private static double PresentValueOfAnnuity(double payment, double rate, int periods)
        => rate <= 0
            ? payment * periods
            : payment * (1 - Math.Pow(1 + rate, -periods)) / rate;

    /// <summary>
    /// The effective monthly rate compounded over twelve months. Capped rather than left to
    /// overflow: a half-filled form can briefly describe a rate no decimal can raise to the
    /// twelfth power.
    /// </summary>
    private static decimal AnnualiseMonthly(decimal monthlyRate)
    {
        if (monthlyRate <= 0)
        {
            return 0m;
        }

        var annual = Math.Pow(1 + (double)monthlyRate, 12) - 1;
        return double.IsFinite(annual) ? (decimal)Math.Min(annual, 999.0) : 999m;
    }

    /// <summary>
    /// The instalment measured against the guideline bill, as a proportion. Zero where no
    /// guideline is recorded: a scheme with no band is not a scheme whose instalment is
    /// infinitely large, and the page says "no guideline" rather than printing a rate.
    /// </summary>
    private static decimal ShareOfBill(SchemeTerms terms)
        => terms.ReferenceBillAmount > 0
            ? terms.PeriodicPayment / terms.ReferenceBillAmount
            : 0m;

    /// <summary>
    /// The first month whose running total reaches the cash price of the installation.
    /// There is always one inside the term on terms the server would accept: the payments
    /// have to come to at least what was financed, or they are refused.
    /// </summary>
    private static int? FindCashPriceMonth(SchemeTerms terms)
    {
        for (var month = 0; month <= terms.MonthPeriods; month++)
        {
            if (terms.DownPayment + (terms.PeriodicPayment * month) >= terms.PrincipalAmount)
            {
                return month;
            }
        }

        return null;
    }
}
