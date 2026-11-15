(() => {
    const upload = document.querySelector("#Uploads");
    const panel = document.querySelector("#receipt-ocr");
    if (!upload || !panel) return;
    const status = panel.querySelector("[data-ocr-status]");
    const results = panel.querySelector("[data-ocr-results]");
    const cancel = panel.querySelector("[data-ocr-cancel]");
    const dateSelect = panel.querySelector("[data-ocr-date]");
    const amountSelect = panel.querySelector("[data-ocr-amount]");
    const applyDate = panel.querySelector("[data-ocr-use-date]");
    const applyAmount = panel.querySelector("[data-ocr-use-amount]");
    const base = new URL(panel.dataset.ocrBase, location.href).href;
    let job = null, source = null;

    function validSource() {
        return source && Array.from(upload.files).includes(source);
    }

    function stop(message = "") {
        if (job) {
            clearTimeout(job.timeout);
            job.worker.terminate();
            job = null;
        }
        cancel.hidden = true;
        panel.removeAttribute("aria-busy");
        document.querySelectorAll("[data-receipt-read]").forEach(button => button.disabled = false);
        status.textContent = message;
    }

    function invalidate() {
        stop();
        source = null;
        results.hidden = true;
        panel.hidden = true;
    }

    function populate(select, values, placeholder) {
        select.replaceChildren(new Option(placeholder, ""));
        values.forEach(value => select.add(new Option(value, value)));
        select.value = values.length === 1 ? values[0] : "";
        select.disabled = values.length === 0;
    }

    function read(file) {
        invalidate();
        source = file;
        panel.hidden = false;
        panel.setAttribute("aria-busy", "true");
        status.textContent = "Förbereder avläsning… Första gången hämtas språkfiler.";
        results.hidden = true;
        cancel.hidden = false;
        document.querySelectorAll("[data-receipt-read]").forEach(button => button.disabled = true);
        panel.scrollIntoView({ block: "nearest", behavior: "smooth" });
        const failed = () => stop("Kvittot kunde inte läsas. Försök igen eller fyll i uppgifterna själv.");
        try {
            const current = { worker: new Worker(panel.dataset.ocrWorker), timeout: null };
            job = current;
            current.timeout = setTimeout(() => {
                if (job === current) stop("Avläsningen tog för lång tid. Försök med en mindre, tydligare bild eller fyll i uppgifterna själv.");
            }, 90000);
            current.worker.onerror = event => {
                event.preventDefault();
                if (job === current) failed();
            };
            current.worker.onmessage = ({ data }) => {
                if (job !== current || !validSource()) return;
                if (data.type === "progress") status.textContent = "Läser kvittot… " + data.percent + " %";
                else if (data.type === "error") failed();
                else if (data.type === "result") {
                    const suggestions = window.VasenReceiptParser.parse(data.text);
                    populate(dateSelect, suggestions.dates, suggestions.dates.length ? "Välj datum" : "Inget datum hittades");
                    populate(amountSelect, suggestions.amounts, suggestions.amounts.length ? "Välj belopp" : "Inget totalbelopp hittades");
                    applyDate.disabled = !dateSelect.value;
                    applyAmount.disabled = !amountSelect.value;
                    results.hidden = !suggestions.dates.length && !suggestions.amounts.length;
                    stop(results.hidden
                        ? "Inga tydliga datum eller totalbelopp hittades. Prova att beskära eller fyll i uppgifterna själv."
                        : "Kontrollera förslagen mot kvittot. Välj Använd för att ändra formuläret.");
                }
            };
            current.worker.postMessage({ file, base });
        } catch {
            failed();
        }
    }

    document.addEventListener("click", event => {
        const button = event.target.closest("[data-receipt-read]");
        if (!button || button.disabled) return;
        const file = upload.files[Number(button.dataset.receiptRead)];
        if (file && /^(image\/(jpeg|png|webp))$/.test(file.type)) read(file);
    });
    cancel.addEventListener("click", () => stop("Avläsningen avbröts. Du kan fylla i uppgifterna själv."));
    document.addEventListener("receipt:attachmentschanged", invalidate);
    document.addEventListener("receipt:cropopened", invalidate);
    dateSelect.addEventListener("change", () => applyDate.disabled = !dateSelect.value);
    amountSelect.addEventListener("change", () => applyAmount.disabled = !amountSelect.value);
    for (const [button, select, id, message] of [
        [applyDate, dateSelect, "Date", "Datumet har förts in i formuläret."],
        [applyAmount, amountSelect, "Amount", "Beloppet har förts in i formuläret."]
    ]) {
        button.addEventListener("click", () => {
            if (!validSource() || !select.value) { invalidate(); return; }
            const target = document.getElementById(id);
            target.value = select.value;
            target.dispatchEvent(new Event("input", { bubbles: true }));
            target.dispatchEvent(new Event("change", { bubbles: true }));
            status.textContent = message;
            target.focus();
        });
    }
    upload.form.addEventListener("submit", () => stop());
    window.addEventListener("pagehide", invalidate);
})();
