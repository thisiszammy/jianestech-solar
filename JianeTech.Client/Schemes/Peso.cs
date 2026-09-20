using System.Globalization;

namespace JianeTech.Client.Schemes;

/// <summary>
/// Money and rates, worded the one way the console words them.
/// </summary>
/// <remarks>
/// Formatted against <see cref="CultureInfo.InvariantCulture"/> rather than the browser's
/// locale, and deliberately. Invariant gives comma thousands and a full stop for the
/// decimal — which is how the Philippines writes money — whereas the current culture would
/// hand a reader in Berlin the same peso figure punctuated as if it were euros. It is also
/// the only choice that renders identically on every machine, which matters for figures
/// that are read out and checked against a contract.
/// </remarks>
public static class Peso
{
    /// <summary>U+20B1, the peso sign. Named rather than pasted, so it survives any file encoding.</summary>
    public const string Sign = "₱";

    /// <summary>Pesos and centavos: the figure on a payment schedule.</summary>
    public static string Exact(decimal amount)
        => Sign + amount.ToString("N2", CultureInfo.InvariantCulture);

    /// <summary>Whole pesos: the figure in a register row, where centavos are noise.</summary>
    public static string Whole(decimal amount)
        => Sign + Math.Round(amount, MidpointRounding.AwayFromZero).ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>
    /// A fraction as a percentage, to two places but with trailing zeroes dropped — so a
    /// round 2% reads as "2%" and not "2.00%", while 1.25% keeps what it needs.
    /// </summary>
    public static string Rate(decimal fraction)
        => (fraction * 100).ToString("0.##", CultureInfo.InvariantCulture) + "%";

    /// <summary>
    /// A fraction as a whole percentage — the way a guideline is quoted, as against
    /// <see cref="Rate"/>, which is for a rate somebody is charged. An instalment measured
    /// against the bill a scheme is recommended up to is an advisory proportion, and
    /// printing it to the centavo would claim a precision the guideline does not have.
    /// </summary>
    public static string Share(decimal fraction)
        => Math.Round(fraction * 100, MidpointRounding.AwayFromZero)
            .ToString("0", CultureInfo.InvariantCulture) + "%";

    /// <summary>"24 months", or "1 month" when it is.</summary>
    public static string Months(int months)
        => months == 1 ? "1 month" : $"{months.ToString("N0", CultureInfo.InvariantCulture)} months";

    /// <summary>"5 years", or "1 year" when it is. An investment term is set in years.</summary>
    public static string Years(int years)
        => years == 1 ? "1 year" : $"{years.ToString("N0", CultureInfo.InvariantCulture)} years";
}
