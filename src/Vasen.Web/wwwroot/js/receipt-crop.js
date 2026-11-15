(() => {
    const upload = document.querySelector("#Uploads");
    const dialog = document.querySelector("#receipt-crop");
    if (!upload || !dialog) return;
    const previews = document.querySelector("#file-previews");
    const canvas = document.querySelector("#crop-canvas");
    const ctx = canvas.getContext("2d");
    const apply = document.querySelector("#crop-apply");
    const error = document.querySelector("#crop-error");
    const fields = ["left", "top", "right", "bottom"].map(edge => document.querySelector("#crop-" + edge));
    const supported = file => /^(image\/(jpeg|png|webp))$/.test(file.type);
    const canCrop = !!(ctx && window.DataTransfer && dialog.showModal && canvas.toBlob);
    let files = [], urls = [], active = null, image = null, rotation = 0;
    let rect = { left: 0, top: 0, right: 1, bottom: 1 };
    let drag = null, generation = 0, busy = false, opener = null;
    let imageWidth = 0, imageHeight = 0;

    function syncFiles() {
        const transfer = new DataTransfer();
        files.forEach(entry => transfer.items.add(entry.current));
        upload.files = transfer.files;
    }

    function renderPreviews() {
        urls.forEach(url => URL.revokeObjectURL(url));
        urls = [];
        previews.replaceChildren();
        files.forEach((entry, index) => {
            const item = document.createElement("div");
            item.className = "receipt-file-preview";
            if (supported(entry.current)) {
                const thumbnail = document.createElement("img");
                thumbnail.src = URL.createObjectURL(entry.current);
                urls.push(thumbnail.src);
                thumbnail.alt = entry.current.name;
                item.append(thumbnail);
                if (canCrop) {
                    const edit = document.createElement("button");
                    edit.type = "button";
                    edit.className = "button secondary";
                    edit.textContent = "Beskär";
                    edit.setAttribute("aria-label", "Beskär " + entry.original.name);
                    edit.addEventListener("click", () => openCrop(index, edit));
                    item.append(edit);
                }
            }
            if (supported(entry.current) && document.querySelector("#receipt-ocr") && window.Worker && window.WebAssembly) {
                const read = document.createElement("button");
                read.type = "button";
                read.className = "button secondary";
                read.textContent = "Läs av";
                read.dataset.receiptRead = String(index);
                read.setAttribute("aria-label", "Läs av kvitto " + entry.original.name);
                item.append(read);
            }
            const name = document.createElement("span");
            name.textContent = entry.current.name;
            item.append(name);
            previews.append(item);
        });
        document.dispatchEvent(new Event("receipt:attachmentschanged"));
    }

    function draw() {
        if (!image) return;
        ctx.clearRect(0, 0, canvas.width, canvas.height);
        ctx.save();
        ctx.translate(canvas.width / 2, canvas.height / 2);
        ctx.rotate(rotation * Math.PI / 2);
        const odd = rotation % 2;
        ctx.drawImage(image, -(odd ? canvas.height : canvas.width) / 2,
            -(odd ? canvas.width : canvas.height) / 2,
            odd ? canvas.height : canvas.width, odd ? canvas.width : canvas.height);
        ctx.restore();
        const left = rect.left * canvas.width, top = rect.top * canvas.height;
        const right = rect.right * canvas.width, bottom = rect.bottom * canvas.height;
        ctx.fillStyle = "#0009";
        ctx.fillRect(0, 0, canvas.width, top);
        ctx.fillRect(0, bottom, canvas.width, canvas.height - bottom);
        ctx.fillRect(0, top, left, bottom - top);
        ctx.fillRect(right, top, canvas.width - right, bottom - top);
        const scale = canvas.width / (canvas.getBoundingClientRect().width || canvas.width);
        ctx.strokeStyle = "#fff";
        ctx.lineWidth = 2 * scale;
        ctx.strokeRect(left, top, right - left, bottom - top);
        ctx.fillStyle = "#244c3d";
        const size = 10 * scale;
        for (const [x, y] of [[left, top], [right, top], [left, bottom], [right, bottom],
            [(left + right) / 2, top], [(left + right) / 2, bottom],
            [left, (top + bottom) / 2], [right, (top + bottom) / 2]]) {
            ctx.fillRect(x - size / 2, y - size / 2, size, size);
            ctx.strokeRect(x - size / 2, y - size / 2, size, size);
        }
    }

    function update() {
        fields.forEach((field, index) => {
            field.value = Math.round(rect[["left", "top", "right", "bottom"][index]] * 100);
        });
        draw();
    }

    function resetFrame() {
        imageWidth = rotation % 2 ? image.naturalHeight : image.naturalWidth;
        imageHeight = rotation % 2 ? image.naturalWidth : image.naturalHeight;
        const scale = Math.min(1, 900 / imageWidth, 900 / imageHeight);
        canvas.width = Math.max(1, Math.round(imageWidth * scale));
        canvas.height = Math.max(1, Math.round(imageHeight * scale));
        rect = { left: 0, top: 0, right: 1, bottom: 1 };
        drag = null;
        update();
    }

    function setBusy(value) {
        busy = value;
        dialog.querySelectorAll("button, input").forEach(control => control.disabled = value);
        apply.textContent = value ? "Bearbetar…" : "Använd beskärning";
        canvas.setAttribute("aria-busy", String(value));
    }

    async function openCrop(index, trigger) {
        document.dispatchEvent(new Event("receipt:cropopened"));
        active = files[index];
        opener = trigger;
        const entry = active, token = ++generation;
        rotation = 0;
        image = null;
        error.textContent = "";
        document.querySelector("#crop-filename").textContent = entry.original.name;
        canvas.width = 1; canvas.height = 1;
        setBusy(true);
        dialog.showModal();
        const url = URL.createObjectURL(entry.original);
        try {
            const decoded = new Image();
            decoded.src = url;
            await decoded.decode();
            if (token !== generation || !dialog.open) return;
            image = decoded;
            resetFrame();
            setBusy(false);
            apply.focus();
        } catch {
            if (token !== generation || !dialog.open) return;
            error.textContent = "Bilden kunde inte öppnas. Du kan behålla hela bilden eller välja en annan fil.";
            setBusy(false);
            apply.disabled = true;
            document.querySelector("#crop-rotate").disabled = true;
            document.querySelector("#crop-reset").disabled = true;
            fields.forEach(field => field.disabled = true);
        } finally {
            URL.revokeObjectURL(url);
        }
    }

    function closeCrop() {
        generation++;
        setBusy(false);
        dialog.close();
        image = null;
        drag = null;
        active = null;
        // Rendering replaces the original preview button; use its replacement if necessary.
        if (opener?.isConnected) opener.focus();
        else upload.focus();
    }

    function point(event) {
        const bounds = canvas.getBoundingClientRect();
        return { x: Math.max(0, Math.min(1, (event.clientX - bounds.left) / bounds.width)),
            y: Math.max(0, Math.min(1, (event.clientY - bounds.top) / bounds.height)) };
    }

    canvas.addEventListener("pointerdown", event => {
        if (!image || busy || !event.isPrimary || event.button !== 0) return;
        const p = point(event), bounds = canvas.getBoundingClientRect();
        const near = (a, b, length) => Math.abs(a - b) * length <= 22;
        const horizontal = p.y >= rect.top - 22 / bounds.height && p.y <= rect.bottom + 22 / bounds.height;
        const vertical = p.x >= rect.left - 22 / bounds.width && p.x <= rect.right + 22 / bounds.width;
        let edges = [];
        if (horizontal) {
            if (near(p.x, rect.left, bounds.width)) edges.push("left");
            else if (near(p.x, rect.right, bounds.width)) edges.push("right");
        }
        if (vertical) {
            if (near(p.y, rect.top, bounds.height)) edges.push("top");
            else if (near(p.y, rect.bottom, bounds.height)) edges.push("bottom");
        }
        if (!edges.length && !(p.x >= rect.left && p.x <= rect.right && p.y >= rect.top && p.y <= rect.bottom)) return;
        drag = { pointerId: event.pointerId, point: p, start: { ...rect }, edges };
        canvas.setPointerCapture(event.pointerId);
        event.preventDefault();
    });

    canvas.addEventListener("pointermove", event => {
        if (!drag || drag.pointerId !== event.pointerId) return;
        const p = point(event), dx = p.x - drag.point.x, dy = p.y - drag.point.y;
        const clamp = (value, min, max) => Math.max(min, Math.min(max, value));
        const start = drag.start;
        if (!drag.edges.length) {
            const x = clamp(dx, -start.left, 1 - start.right);
            const y = clamp(dy, -start.top, 1 - start.bottom);
            rect = { left: start.left + x, right: start.right + x, top: start.top + y, bottom: start.bottom + y };
        } else {
            const minX = Math.min(0.05, start.right - start.left, 44 / canvas.getBoundingClientRect().width);
            const minY = Math.min(0.05, start.bottom - start.top, 44 / canvas.getBoundingClientRect().height);
            for (const edge of drag.edges) {
                if (edge === "left") rect.left = clamp(start.left + dx, 0, start.right - minX);
                if (edge === "right") rect.right = clamp(start.right + dx, start.left + minX, 1);
                if (edge === "top") rect.top = clamp(start.top + dy, 0, start.bottom - minY);
                if (edge === "bottom") rect.bottom = clamp(start.bottom + dy, start.top + minY, 1);
            }
        }
        update();
    });
    for (const eventName of ["pointerup", "pointercancel", "lostpointercapture"]) {
        canvas.addEventListener(eventName, () => { drag = null; });
    }

    fields.forEach((field, index) => field.addEventListener("change", () => {
        if (!image || !field.checkValidity() || field.value === "") { update(); return; }
        const edge = ["left", "top", "right", "bottom"][index], value = Number(field.value) / 100;
        if ((edge === "left" && value <= rect.right - 0.01) ||
            (edge === "right" && value >= rect.left + 0.01) ||
            (edge === "top" && value <= rect.bottom - 0.01) ||
            (edge === "bottom" && value >= rect.top + 0.01)) rect[edge] = value;
        update();
    }));
    fields.forEach(field => field.addEventListener("keydown", event => {
        if (event.key === "Enter") { event.preventDefault(); field.dispatchEvent(new Event("change")); }
    }));

    document.querySelector("#crop-rotate").addEventListener("click", () => {
        rotation = (rotation + 1) % 4;
        resetFrame();
    });
    document.querySelector("#crop-reset").addEventListener("click", () => {
        rotation = 0;
        resetFrame();
    });
    document.querySelector("#crop-original").addEventListener("click", () => {
        active.current = active.original;
        syncFiles();
        renderPreviews();
        closeCrop();
    });
    document.querySelector("#crop-cancel").addEventListener("click", closeCrop);
    dialog.addEventListener("cancel", event => {
        event.preventDefault();
        closeCrop();
    });

    apply.addEventListener("click", async () => {
        if (!image || busy) return;
        const token = generation, entry = active;
        setBusy(true);
        error.textContent = "";
        const output = document.createElement("canvas");
        try {
            const left = Math.round(rect.left * imageWidth), top = Math.round(rect.top * imageHeight);
            output.width = Math.max(1, Math.round(rect.right * imageWidth) - left);
            output.height = Math.max(1, Math.round(rect.bottom * imageHeight) - top);
            const context = output.getContext("2d");
            context.fillStyle = "#fff";
            context.fillRect(0, 0, output.width, output.height);
            context.translate(imageWidth / 2 - left, imageHeight / 2 - top);
            context.rotate(rotation * Math.PI / 2);
            context.drawImage(image, -image.naturalWidth / 2, -image.naturalHeight / 2);
            const blob = await new Promise(resolve => output.toBlob(resolve, "image/jpeg", 0.95));
            if (token !== generation || !dialog.open) return;
            if (!blob) throw new Error("Bilden kunde inte bearbetas. Försök igen eller behåll hela bilden.");
            if (blob.size > 10 * 1024 * 1024) throw new Error("Den beskurna bilden är större än 10 MB. Beskär mer eller välj en mindre bild.");
            const name = entry.original.name.replace(/\.[^.]+$/, "") + "-beskuren.jpg";
            const cropped = new File([blob], name, { type: "image/jpeg", lastModified: Date.now() });
            // Update the native file input before showing the new preview.
            const previous = entry.current;
            entry.current = cropped;
            try { syncFiles(); } catch (failure) { entry.current = previous; throw failure; }
            renderPreviews();
            closeCrop();
        } catch (failure) {
            if (token === generation && dialog.open)
                error.textContent = failure.message || "Bilden kunde inte bearbetas. Försök igen eller behåll hela bilden.";
        } finally {
            output.width = 0; output.height = 0;
            if (token === generation) setBusy(false);
        }
    });

    upload.addEventListener("change", () => {
        generation++;
        files = Array.from(upload.files, original => ({ original, current: original }));
        renderPreviews();
        const firstImage = files.findIndex(entry => supported(entry.original));
        if (canCrop && firstImage >= 0) openCrop(firstImage, upload);
    });
    upload.form.addEventListener("submit", event => {
        if (dialog.open || busy) event.preventDefault();
    });
    window.addEventListener("resize", draw);
})();
