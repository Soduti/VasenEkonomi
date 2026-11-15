const { test } = require("node:test");
const assert = require("node:assert/strict");
const { parse } = require("../../src/Vasen.Web/wwwroot/js/receipt-ocr-parser.js");
const today = new Date(2026, 10, 11);
const cases = [
    ["Swedish receipt", "Datum 2026-11-10\nVirke 500,00\nMoms 100,00\nTOTALT 500,00\nKontant 1000,00\nVäxel 500,00", { dates: ["2026-11-10"], amounts: ["500,00"] }],
    ["Total includes VAT", "TOTALT inkl. moms 1 234,50", { dates: [], amounts: ["1234,50"] }],
    ["Dot thousands", "Att betala 1.234,50 kr", { dates: [], amounts: ["1234,50"] }],
    ["Dot decimals", "TOTAL SEK 1234.50", { dates: [], amounts: ["1234,50"] }],
    ["Whole kronor", "SUMMA 125:-", { dates: [], amounts: ["125,00"] }],
    ["Integer after total", "Att betala SEK 125", { dates: [], amounts: ["125,00"] }],
    ["Split total", "TOTALT\n1 234,50", { dates: [], amounts: ["1234,50"] }],
    ["VAT below total label is not total", "TOTALT\nMoms 25,00", { dates: [], amounts: [] }],
    ["Payable outranks payment", "SUMMA 100,00\nAtt betala 95,00\nBetalt 100,00", { dates: [], amounts: ["95,00"] }],
    ["Never use largest amount", "Artikel 9999,00\nDelsumma 2000,00\nMoms 500,00", { dates: [], amounts: [] }],
    ["Ambiguous totals", "TOTALT 100,00\nTOTALT 200,00", { dates: [], amounts: ["100,00", "200,00"] }],
    ["Repeated total", "TOTAL 100,00\nTOTALT 100,00", { dates: [], amounts: ["100,00"] }],
    ["Swedish date", "Datum: 10/11/2026", { dates: ["2026-11-10"], amounts: [] }],
    ["Short labeled date", "Datum 10/11/26", { dates: ["2026-11-10"], amounts: [] }],
    ["Compact labeled date", "Datum 20261110", { dates: ["2026-11-10"], amounts: [] }],
    ["Impossible date", "Datum 2026-02-30", { dates: [], amounts: [] }],
    ["Leap day", "Datum 2024-02-29", { dates: ["2024-02-29"], amounts: [] }],
    ["Due and print dates ignored", "Datum 2026-11-10\nFörfallodatum 2026-12-10\nUtskriven 2026-11-11", { dates: ["2026-11-10"], amounts: [] }],
    ["Multiple dates", "Datum 2026-11-10\nDatum 2026-11-11", { dates: ["2026-11-10", "2026-11-11"], amounts: [] }],
    ["Negative total", "TOTALT -100,00", { dates: [], amounts: [] }],
    ["Negative whole total", "TOTALT -100", { dates: [], amounts: [] }],
    ["Invalid decimals", "TOTALT 100,5", { dates: [], amounts: [] }],
    ["Uncertain thousands only", "TOTALT 1.234", { dates: [], amounts: [] }],
    ["Empty", "", { dates: [], amounts: [] }]
];
for (const [name, text, expected] of cases) test(name, () => assert.deepEqual(parse(text, today), expected));
