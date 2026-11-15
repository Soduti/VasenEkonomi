/* Receipt-specific interpretation of OCR text. Values are suggestions only. */
(() => {
    function parse(text, today = new Date()) {
        const lines = String(text).normalize("NFKC").replace(/\u00a0/g, " ").split(/\r?\n/).map(line => line.trim()).filter(Boolean);
        const dates = new Map(), amounts = new Map();
        function addDate(year, month, day, score) {
            year = Number(year); month = Number(month); day = Number(day);
            const date = new Date(Date.UTC(year, month - 1, day));
            if (year < 2000 || year > today.getFullYear() + 1 ||
                date.getUTCFullYear() !== year || date.getUTCMonth() + 1 !== month || date.getUTCDate() !== day) return;
            const value = year + "-" + String(month).padStart(2, "0") + "-" + String(day).padStart(2, "0");
            dates.set(value, Math.max(dates.get(value) || 0, score));
        }
        function money(line, allowWhole) {
            const found = [];
            // Require decimals unless a total label explicitly accompanies a whole-krona amount.
            const pattern = /(?<![\d.,])((?:\d{1,3}(?:[ .]\d{3})+|\d+)[,.]\d{2}|\d+\s*:[-–])(?=$|[^\d])/g;
            for (const match of line.matchAll(pattern)) {
                if (/-\s*$/.test(line.slice(0, match.index))) continue;
                const normalized = match[1].replace(/\s/g, "").replace(/:[-–]/, ",00");
                const split = Math.max(normalized.lastIndexOf(","), normalized.lastIndexOf("."));
                const whole = normalized.slice(0, split).replace(/[.,]/g, "");
                const cents = Number(whole) * 100 + Number(normalized.slice(split + 1));
                if (Number.isSafeInteger(cents) && cents > 0 && cents <= 999999999) found.push(cents);
            }
            if (!found.length && allowWhole) {
                const match = line.match(/^\s*(?:sek|kr)?\s*([1-9]\d{0,6})\s*(?:kr|sek)?\s*$/i);
                if (match && !/-\s*$/.test(line.slice(0, match.index))) found.push(Number(match[1]) * 100);
            }
            return found;
        }
        for (let index = 0; index < lines.length; index++) {
            const line = lines[index], lower = line.toLocaleLowerCase("sv-SE");
            if (!/förfall|giltig|utgång|utskrift|utskriven|due date/i.test(lower)) {
                const score = /köpdatum|kvittodatum|transaktionsdatum|datum|date/i.test(lower) ? 2 : 1;
                for (const m of line.matchAll(/(?<!\d)(20\d{2})[-/.](\d{1,2})[-/.](\d{1,2})(?!\d)/g)) addDate(m[1], m[2], m[3], score);
                for (const m of line.matchAll(/(?<!\d)(\d{1,2})[-/.](\d{1,2})[-/.](20\d{2})(?!\d)/g)) addDate(m[3], m[2], m[1], score);
                if (score === 2) {
                    for (const m of line.matchAll(/(?<!\d)(20\d{2})(\d{2})(\d{2})(?!\d)/g)) addDate(m[1], m[2], m[3], score);
                    for (const m of line.matchAll(/(?<!\d)(\d{2})[/.](\d{2})[/.](\d{2})(?!\d)/g)) addDate(2000 + Number(m[3]), m[2], m[1], score);
                }
            }
            const cleaned = lower.replace(/inkl\.?\s*(?:moms|vat)/g, "");
            if (/delsumma|subtotal|sub total|moms|vat|exkl|rabatt|växel|tillbaka|åter|kontant|cash|dricks|tip\b/.test(cleaned)) continue;
            let score = 0, label;
            const labels = [
                [/att\s+betala|to\s+pay|amount\s+due/i, 100],
                [/total\s*summa|total\s*belopp|total\s*amount|grand\s+total/i, 90],
                [/\btotal(?:t)?\b/i, 80],
                [/\bsumma\b/i, 70],
                [/\bbelopp\b/i, 60],
                [/\bbetalt\b|kortköp/i, 50]
            ];
            for (const [pattern, rank] of labels) {
                const match = pattern.exec(line);
                if (match) { label = match; score = rank; break; }
            }
            if (!score) continue;
            // Only inspect the text after the label, avoiding dates and item quantities.
            const suffix = line.slice(label.index + label[0].length).replace(/^\s*[:=]\s*/, "");
            let values = money(suffix, true);
            if (!values.length && !suffix.trim() && index + 1 < lines.length &&
                /^(?:sek\s*)?[\d .,:–-]+\s*(?:kr|sek)?$/i.test(lines[index + 1])) values = money(lines[index + 1], true);
            for (const cents of values) amounts.set(cents, Math.max(amounts.get(cents) || 0, score));
        }
        function best(map) {
            if (!map.size) return [];
            const highest = Math.max(...map.values());
            return [...map.entries()].filter(([, score]) => score === highest).map(([value]) => value);
        }
        return { dates: best(dates), amounts: best(amounts).map(cents => (cents / 100).toFixed(2).replace(".", ",")) };
    }
    if (typeof module !== "undefined" && module.exports) module.exports = { parse };
    else window.VasenReceiptParser = { parse };
})();

