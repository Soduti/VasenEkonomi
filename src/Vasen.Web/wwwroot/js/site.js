"use strict";
const ownContribution = document.querySelector("#CountDifferenceAsOwnContribution");
if (ownContribution) {
    const amount = document.querySelector("#Amount");
    const preview = document.querySelector("#own-contribution-preview");
    const updateOwnContribution = () => {
        preview.hidden = !ownContribution.checked;
        const paid = Number(amount.value.replace(/\s/g, "").replace(",", "."));
        const difference = Number(preview.dataset.totalOre) - Math.round(paid * 100);
        preview.textContent = !Number.isFinite(paid) || paid < 0 ? "Ange ett giltigt uttagsbelopp." : difference < 0
            ? "Uttaget överstiger kvittosumman. Avmarkera eget bidrag eller ändra beloppet."
            : "Eget bidrag: " + new Intl.NumberFormat("sv-SE", {style:"currency",currency:"SEK"}).format(difference / 100);
    };
    ownContribution.addEventListener("change", updateOwnContribution);
    amount.addEventListener("input", updateOwnContribution);
    updateOwnContribution();
}
document.querySelectorAll("form[data-confirm]").forEach(form => form.addEventListener("submit", event => {
    if (!window.confirm(form.dataset.confirm)) event.preventDefault();
}));
const selection = document.querySelector("#selection-form");
if (selection) {
    const boxes = [...selection.querySelectorAll('input[name="ids"]')];
    const update = () => {
        const selected = boxes.filter(x => x.checked);
        const mixed = new Set(selected.map(x => x.dataset.person)).size > 1;
        document.querySelector("#selection-bar").hidden = !selected.length;
        document.querySelector("#selection-total").textContent = new Intl.NumberFormat("sv-SE", {style:"currency",currency:"SEK"}).format(selected.reduce((n,x) => n + Number(x.dataset.ore),0)/100);
        document.querySelector("#selection-count").textContent = `${selected.length} kvitton markerade`;
        document.querySelector("#selection-error").textContent = mixed ? "Välj kvitton för en person åt gången." : "";
        document.querySelector("#reimburse-button").disabled = mixed;
    };
    boxes.forEach(box => box.addEventListener("change", update));
    window.addEventListener("pageshow", update);
    update();
}
document.querySelectorAll("[data-period]").forEach(button => button.addEventListener("click", () => {
    const now = new Date();
    const year = now.getFullYear();
    const month = now.getMonth();
    const dateText = date => `${date.getFullYear()}-${String(date.getMonth()+1).padStart(2,"0")}-${String(date.getDate()).padStart(2,"0")}`;
    const form = button.closest("form");
    form.querySelector('[name="From"]').value = dateText(new Date(year,button.dataset.period === "year" ? 0 : month,1));
    form.querySelector('[name="To"]').value = dateText(new Date(year,button.dataset.period === "year" ? 12 : month+1,0));
}));
const receiptQuickFilters = document.querySelector("#receipt-quick-filters");
if (receiptQuickFilters) {
    receiptQuickFilters.hidden = false;
    document.querySelector("#quick-show-paid").addEventListener("change", event => {
        receiptQuickFilters.elements.namedItem("Status").value = event.target.checked ? "all" : "unpaid";
        receiptQuickFilters.requestSubmit();
    });
    document.querySelector("#quick-only-mine").addEventListener("change", event => {
        receiptQuickFilters.elements.namedItem("Person").value = event.target.checked ? receiptQuickFilters.dataset.currentMember : "";
        receiptQuickFilters.requestSubmit();
    });
}
