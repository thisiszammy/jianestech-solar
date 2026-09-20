// ============================================================================
// charts.js — ApexCharts, dressed in the console's own design language.
//
// The second and last JavaScript module in the console. It exists because
// ApexCharts is a DOM library and Blazor cannot reach the DOM, and because the
// parts of a chart that have to be functions — axis and tooltip formatters —
// cannot be serialised across the interop boundary at all.
//
// So the division is: C# owns the *numbers*, this file owns the *chart*. A page
// hands over a small payload of already-computed series and labels; the option
// object that turns them into a picture is built here, once per kind, where a
// formatter is just a function.
//
// Three things it is careful about, all of them house rules rather than
// ApexCharts defaults:
//
//   1. Colour comes from tokens.css and nowhere else. Every value is read off
//      the document's custom properties at render time, so dark mode, and any
//      future change to the palette, arrive here without this file being
//      touched. It reads the primitive tokens rather than the semantic aliases
//      (--trace-deep, not --data-accepted) because an alias resolves through
//      var() and not every engine hands that back from getComputedStyle.
//   2. Motion answers an action. Entrance animation is on — a chart redrawing
//      as you type is the feedback — but prefers-reduced-motion turns it off,
//      the same as base.css does for everything else.
//   3. The library is loaded on demand. It is 560KB, and six of the console's
//      eight pages never draw a chart; the import below happens the first time
//      one is asked for and is shared from then on.
// ============================================================================

const PESO = '\u20B1';

/**
 * One live chart per host element, with what it was drawn from.
 *
 * A Map rather than a WeakMap, because the theme listener below has to walk it —
 * and the entry is removed by destroy(), which ApexChart.razor calls from
 * DisposeAsync on every teardown, so nothing accumulates.
 */
const instances = new Map();

/** The shared module promise — started on first use, awaited by every caller after. */
let apexPromise = null;

function apex() {
    apexPromise ??= import('../lib/apexcharts/apexcharts.esm.js').then(m => m.default);
    return apexPromise;
}

// --- The palette, read from tokens.css ------------------------------------
function palette() {
    const style = getComputedStyle(document.documentElement);
    const token = name => style.getPropertyValue(name).trim();

    return {
        ink: token('--ink'),
        ink2: token('--ink-2'),
        ink4: token('--ink-4'),
        surface: token('--surface'),
        border: token('--border'),
        borderSoft: token('--border-soft'),

        // Instrumentation cyan for the money this scheme moves, the seal's deep
        // brass for what the credit costs on top of it, and quiet ink for the
        // levels the flows are read against — a balance still owed, a cash
        // price, a sum placed. Gold is the brand and never a data mark — see
        // the note at the top of tokens.css.
        capital: token('--trace-deep'),
        charge: token('--brass'),
        balance: token('--ink-3'),

        // The investment side reuses the two above unchanged - cyan is the
        // capital wherever it appears, brass is the money earned on top of it -
        // and adds one mark of its own for the installation, which is neither.
        // It is paid in kind rather than in cash, so it takes the neutral
        // informational blue-grey rather than either money colour.
        installation: token('--neutral'),
    };
}

function reducedMotion() {
    return window.matchMedia('(prefers-reduced-motion: reduce)').matches;
}

/** Pesos and centavos, for a tooltip, where the exact figure is the point. */
function moneyExact(value) {
    return PESO + value.toLocaleString('en-US', {
        minimumFractionDigits: 2,
        maximumFractionDigits: 2,
    });
}

/** Axis ticks are shortened, because five of them at full length will not fit. */
function moneyShort(value) {
    const n = Math.abs(value);
    if (n >= 1_000_000) return PESO + (value / 1_000_000).toFixed(1).replace(/\.0$/, '') + 'M';
    if (n >= 1_000) return PESO + Math.round(value / 1000) + 'k';
    return PESO + Math.round(value);
}

/**
 * Everything every chart in this console shares: one typeface, no toolbar, no
 * decoration, and the grid drawn quietly enough that the data outranks it.
 */
function base(colors, height) {
    return {
        chart: {
            // A number, never '100%'. Resolved against the parent, ApexCharts sizes the
            // plot to it and then draws the axis titles outside — the cost chart came out
            // 65px taller than the box it was given and sat on the caption underneath it.
            // An explicit height is the whole SVG, and the host reserves the same figure
            // as a minimum so nothing shifts while the module loads.
            height,
            fontFamily: getComputedStyle(document.body).fontFamily,
            foreColor: colors.ink4,
            toolbar: { show: false },
            zoom: { enabled: false },
            animations: {
                enabled: !reducedMotion(),
                speed: 220,
                animateGradually: { enabled: false },
                dynamicAnimation: { enabled: !reducedMotion(), speed: 220 },
            },
            parentHeightOffset: 0,
        },
        grid: {
            borderColor: colors.borderSoft,
            strokeDashArray: 0,
            padding: { left: 4, right: 8, top: 0, bottom: 0 },
        },
        dataLabels: { enabled: false },
        tooltip: {
            theme: 'light',
            style: { fontSize: '13px' },
        },
        // Drawn in markup instead, beside the panel head — see .chart-legend. Two
        // reasons, and the second is the real one. ApexCharts lays its legend out with
        // absolute offsets inside a flex row, which put the schedule chart's three keys
        // on two lines in the wrong order; and a legend written in Razor is set in the
        // console's own type and reads the same tokens as everything else on the page.
        // Nothing is lost but click-to-toggle, and on charts of two and three series
        // where every series is half of the comparison, there is nothing worth hiding.
        legend: { show: false },
        states: {
            hover: { filter: { type: 'lighten', value: 0.06 } },
            active: { filter: { type: 'none' } },
        },
        noData: {
            text: 'Fill in the terms to see the cost.',
            style: { color: colors.ink4, fontSize: '13px' },
        },
    };
}

// --- What the household pays ----------------------------------------------
// Everything handed over so far - the down payment, then one instalment a
// month - against the cash price of the installation. Where the rising line
// passes the flat one is the month the household has paid what the system costs
// outright; everything above it is the cost of the credit, drawn to scale.
//
// It replaced a chart that ran this same line against the household's
// electricity bill, climbing forever, and marked where the two crossed as the
// month the installation had "paid for itself". That was a picture of a product
// this fund does not sell. The bill is a guideline for which scheme to offer a
// household - it pays for nothing - so the second line was an outlay nobody
// avoids by signing, and the crossing read as a saving the fund never promised.
// The cash price is the one comparison the five stored terms actually support.
function payments(data, colors) {
    const b = base(colors, data.height);

    return {
        ...b,
        series: [
            { name: 'Paid so far', data: data.paidSoFar },
            { name: 'Cash price', data: data.cashPrice },
        ],
        chart: { ...b.chart, type: 'area', stacked: false },
        colors: [colors.capital, colors.balance],

        // The cash price is a level, not a flow, so it takes the dashed rule the
        // console gives every other level it draws - the same treatment the
        // investment side's placement line gets.
        stroke: { curve: 'straight', width: [2.5, 2], dashArray: [0, 4] },
        fill: { type: 'solid', opacity: [0.12, 0] },
        xaxis: {
            type: 'numeric',
            min: 0,
            max: data.horizon,
            tickAmount: Math.min(12, Math.max(1, data.horizon)),
            labels: { formatter: v => `${Math.round(v)}` },
            title: { text: 'Months from installation', style: { fontSize: '12px', fontWeight: 500 } },
            axisBorder: { color: colors.border },
            axisTicks: { color: colors.border },
        },
        yaxis: {
            labels: { formatter: moneyShort },
            forceNiceScale: true,
        },
        tooltip: {
            ...b.tooltip,
            shared: true,
            intersect: false,
            x: {
                formatter: v => {
                    const months = Math.round(v);
                    if (months === 0) return 'Before the first instalment';
                    return months === 1 ? 'After 1 month' : `After ${months} months`;
                },
            },
            y: { formatter: moneyExact },
        },
        markers: { size: 0, hover: { size: 4 } },
        annotations: data.cashPriceMonth === null || data.cashPriceMonth === undefined
            ? {}
            : annotationsAt(
                [{ x: data.cashPriceMonth, text: data.cashPriceLabel, anchor: data.cashPriceAnchor }],
                colors),
    };
}

// --- The schedule ---------------------------------------------------------
// Where each instalment actually goes, month by month, with what is still owed
// drawn over the top. Stacked because the two parts sum to the payment by
// construction, and a line for the balance because it is a level, not a flow —
// the only honest way to put both on one frame.
function schedule(data, colors) {
    const b = base(colors, data.height);

    return {
        ...b,
        series: [
            { name: 'Capital', type: 'column', data: data.capital },
            { name: 'Charge', type: 'column', data: data.charge },
            { name: 'Still owed', type: 'line', data: data.balance },
        ],
        chart: { ...b.chart, type: 'line', stacked: true },
        colors: [colors.capital, colors.charge, colors.balance],
        stroke: { width: [0, 0, 2], curve: 'straight', dashArray: [0, 0, 3] },
        plotOptions: {
            bar: {
                columnWidth: data.months.length > 36 ? '92%' : '68%',
                borderRadius: 0,
            },
        },
        xaxis: {
            categories: data.months,
            tickAmount: Math.min(12, data.months.length),
            tickPlacement: 'on',
            labels: {
                rotate: 0,
                hideOverlappingLabels: true,
                formatter: v => `${v}`,
            },
            title: { text: 'Month of the term', style: { fontSize: '12px', fontWeight: 500 } },
            axisBorder: { color: colors.border },
            axisTicks: { color: colors.border },
        },
        yaxis: [
            {
                seriesName: 'Capital',
                labels: { formatter: moneyShort },
                title: { text: 'Per payment', style: { fontSize: '12px', fontWeight: 500 } },
            },
            { seriesName: 'Capital', show: false },
            {
                seriesName: 'Still owed',
                opposite: true,
                labels: { formatter: moneyShort },
                title: { text: 'Still owed', style: { fontSize: '12px', fontWeight: 500 } },
            },
        ],
        tooltip: {
            ...b.tooltip,
            shared: true,
            intersect: false,
            x: { formatter: v => `Month ${v}` },
            y: { formatter: moneyExact },
        },
        markers: { size: 0 },
    };
}

// --- What the investor is paid, month by month ----------------------------
// The counterpart of schedule() on the other side of the fund. Stacked, again,
// because the two parts sum to the month's payment by construction; with the
// running total over the top as a line, because that is a level rather than a
// flow.
//
// Neither structure returns the capital in one piece, which is what makes this
// chart drawable: a deferred scheme pays capital down from its first payment,
// and an immediate one spreads it across the last few months. The columns can
// therefore be the whole payment. The only thing marked with a rule instead of
// drawn is the installation, which is not a payment - on an immediate scheme it
// arrives at month zero, before any income exists to pay.
function investmentPayout(data, colors) {
    const b = base(colors, data.height);

    return {
        ...b,
        series: [
            { name: 'Capital', type: 'column', data: data.capital },
            { name: 'Profit', type: 'column', data: data.profit },
            { name: 'Returned so far', type: 'line', data: data.cumulative },
        ],
        chart: { ...b.chart, type: 'line', stacked: true },
        colors: [colors.capital, colors.charge, colors.balance],
        stroke: { width: [0, 0, 2], curve: 'straight', dashArray: [0, 0, 3] },
        plotOptions: {
            bar: {
                columnWidth: data.months.length > 36 ? '92%' : '68%',
                borderRadius: 0,
            },
        },
        xaxis: {
            categories: data.months,
            tickAmount: Math.min(12, data.months.length),
            tickPlacement: 'on',
            labels: {
                rotate: 0,
                hideOverlappingLabels: true,
                formatter: v => `${v}`,
            },
            title: { text: 'Months from placement', style: { fontSize: '12px', fontWeight: 500 } },
            axisBorder: { color: colors.border },
            axisTicks: { color: colors.border },
        },
        yaxis: [
            {
                seriesName: 'Capital',
                labels: { formatter: moneyShort },
                title: { text: 'Paid each month', style: { fontSize: '12px', fontWeight: 500 } },
            },
            { seriesName: 'Capital', show: false },
            {
                seriesName: 'Returned so far',
                opposite: true,
                labels: { formatter: moneyShort },
                title: { text: 'Returned so far', style: { fontSize: '12px', fontWeight: 500 } },
            },
        ],
        tooltip: {
            ...b.tooltip,
            shared: true,
            intersect: false,
            x: { formatter: v => (v === '0' || v === 0 ? 'At placement' : `Month ${v}`) },
            y: { formatter: moneyExact },
        },
        markers: { size: 0 },
        annotations: annotateMonths(data.marks, colors),
    };
}

// --- When the capital is whole again --------------------------------------
// Everything handed back so far, against the sum that was placed. Where the
// rising line crosses the flat one is the month the investor is square, and on
// this product that is the only question a chart can usefully answer.
//
// It replaced a growth chart, and the reason is worth keeping. That one drew
// the placement compounding at the annual rate which would produce the same
// final total - a perfectly ordinary curve, and a picture of a product this
// fund does not sell. Nothing here compounds: the whole return is worked out
// and scheduled before the money moves, and every peso paid out leaves the
// arrangement. The curve also could not tell the two structures apart, because
// it saw only the first amount and the last, and they are identical. This
// crossing is where they differ, and it differs by months.
function investmentRecovery(data, colors) {
    const b = base(colors, data.height);

    return {
        ...b,
        series: [
            { name: 'Returned so far', data: data.returned },
            { name: 'Capital placed', data: data.placed },
        ],
        chart: { ...b.chart, type: 'area', stacked: false },
        colors: [colors.capital, colors.balance],

        // The placed line is a level, not a flow, so it takes the dashed rule
        // the console gives every other level it draws.
        stroke: { curve: 'straight', width: [2.5, 2], dashArray: [0, 4] },
        fill: { type: 'solid', opacity: [0.12, 0] },
        xaxis: {
            type: 'numeric',
            min: 0,
            max: data.horizon,
            tickAmount: Math.min(12, Math.max(1, data.horizon)),
            labels: { formatter: v => `${Math.round(v)}` },
            title: { text: 'Months from placement', style: { fontSize: '12px', fontWeight: 500 } },
            axisBorder: { color: colors.border },
            axisTicks: { color: colors.border },
        },
        yaxis: {
            labels: { formatter: moneyShort },
            forceNiceScale: true,
        },
        tooltip: {
            ...b.tooltip,
            shared: true,
            intersect: false,
            x: {
                formatter: v => {
                    const months = Math.round(v);
                    if (months === 0) return 'At placement';
                    return months === 1 ? 'After 1 month' : `After ${months} months`;
                },
            },
            y: { formatter: moneyExact },
        },
        markers: { size: 0, hover: { size: 4 } },
        annotations: data.recoveredMonth === null || data.recoveredMonth === undefined
            ? {}
            : annotationsAt(
                [{ x: data.recoveredMonth, text: data.recoveredLabel, anchor: data.recoveredAnchor }],
                colors),
    };
}

/**
 * The console's one annotated-point idiom, kept in one place so every chart
 * that uses it draws the same dashed rule with the same brass label.
 *
 * Takes {x, text, anchor, row} in whatever unit the axis is in. The caller does
 * the wording and the placement, because the caller is the one that knows where
 * in the term each mark falls: a label centred on a rule at month zero hangs
 * half of itself off the left of the plot, and one centred at the last month
 * does the same on the right. An empty list is an annotations option of
 * nothing.
 */
function annotationsAt(marks, colors) {
    if (!marks || marks.length === 0) {
        return {};
    }

    return {
        xaxis: marks.map(mark => ({
            x: mark.x,
            borderColor: colors.charge,
            strokeDashArray: 4,
            label: {
                text: mark.text,
                position: 'top',
                orientation: 'horizontal',
                textAnchor: mark.anchor ?? 'middle',
                // Marks close enough together to collide are given separate
                // rows by the caller, which is what keeps two labels a few
                // months apart from being set on top of one another.
                offsetY: mark.row ? 16 : -4,
                borderColor: colors.charge,
                style: {
                    background: colors.charge,
                    color: colors.surface,
                    fontSize: '11px',
                    fontWeight: 600,
                    padding: { left: 6, right: 6, top: 3, bottom: 3 },
                },
            },
        })),
    };
}

/**
 * On a category axis the annotation is matched against the category's own
 * label, not its index - so the month has to go over as the same string the
 * categories array holds.
 */
function annotateMonths(marks, colors) {
    return annotationsAt(
        (marks ?? []).map(m => ({
            x: String(m.month), text: m.text, anchor: m.anchor, row: m.row,
        })),
        colors);
}


const builders = { payments, schedule, investmentPayout, investmentRecovery };

// --- Interop surface ------------------------------------------------------

export async function render(host, kind, data, height) {
    if (!host || !builders[kind]) {
        return;
    }

    // A second render onto a live host would leave the first chart orphaned in
    // the DOM. Callers should not do it; if one does, the old chart goes first.
    destroy(host);

    const ApexCharts = await apex();

    // The page may have navigated away while the 560KB module was loading. The
    // host is out of the document by then, and drawing into it would leak an
    // instance nothing will ever dispose.
    if (!host.isConnected) {
        return;
    }

    const chart = new ApexCharts(host, builders[kind]({ ...data, height }, palette()));
    instances.set(host, { chart, kind, data, height });
    await chart.render();
}

/**
 * Re-options an existing chart rather than rebuilding it, so a keystroke in the
 * form animates the columns to their new heights instead of flashing the panel.
 */
export async function update(host, kind, data, height) {
    const live = instances.get(host);

    if (!live) {
        await render(host, kind, data, height);
        return;
    }

    instances.set(host, { ...live, kind, data, height });
    await live.chart.updateOptions(
        builders[kind]({ ...data, height }, palette()), false, !reducedMotion());
}

export function destroy(host) {
    const live = instances.get(host);

    if (live) {
        live.chart.destroy();
        instances.delete(host);
    }
}

// --- The theme, after the fact --------------------------------------------
// A chart reads tokens.css once, when it is drawn. Someone who switches their
// system to dark with the page already open would otherwise watch the console
// invert around a chart still wearing the light palette — the one place in the
// app where the theme did not carry through. This redraws every live chart
// against the tokens as they now resolve.
//
// Registered once, at module scope, and never removed: the module is imported
// once per document and outlives every chart in it.
window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', () => {
    for (const [host, live] of instances) {
        if (!host.isConnected) {
            continue;
        }

        // No animation on a re-theme. Nobody asked for a chart to move; they
        // asked for it to change colour.
        live.chart.updateOptions(
            builders[live.kind]({ ...live.data, height: live.height }, palette()), false, false);
    }
});
