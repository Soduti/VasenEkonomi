/* One disposable controller worker per scan, including initialization.
   Terminating it also stops its nested Tesseract worker. */
self.onmessage = async ({ data: { file, base } }) => {
    let worker;
    try {
        importScripts(new URL("tesseract.min.js", base).href);
        worker = await Tesseract.createWorker(["swe", "eng"], 1, {
            workerPath: new URL("worker.min.js", base).href,
            corePath: new URL("core", base).href,
            langPath: new URL("lang", base).href,
            workerBlobURL: false,
            cachePath: "vasen-ocr-7.0.0",
            logger: message => {
                if (message.status === "recognizing text")
                    self.postMessage({ type: "progress", percent: Math.floor(message.progress * 10) * 10 });
            },
            errorHandler: () => self.postMessage({ type: "error" })
        });
        await worker.setParameters({ tessedit_pageseg_mode: "6", preserve_interword_spaces: "1", user_defined_dpi: "300" });
        const { data } = await worker.recognize(file);
        self.postMessage({ type: "result", text: data.text });
    } catch {
        self.postMessage({ type: "error" });
    } finally {
        if (worker) await worker.terminate().catch(() => {});
    }
};
