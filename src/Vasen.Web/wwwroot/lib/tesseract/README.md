# Tesseract.js i Vasen

Filerna i denna mapp är oförändrade distributionsfiler. OCR körs i webbläsarens Web Workers. Bibliotek, motor och språkdata hämtas från samma Vasen-server; ingen extern OCR-tjänst eller CDN används vid körning.

## Låsta versioner och ursprung

- Tesseract.js 7.0.0: https://registry.npmjs.org/tesseract.js/-/tesseract.js-7.0.0.tgz (Apache-2.0)
- Tesseract.js-core 7.0.0: https://registry.npmjs.org/tesseract.js-core/-/tesseract.js-core-7.0.0.tgz (Apache-2.0)
- Svenska: https://registry.npmjs.org/@tesseract.js-data/swe/-/swe-1.0.0.tgz
- Engelska: https://registry.npmjs.org/@tesseract.js-data/eng/-/eng-1.0.0.tgz
- Språkpaketens version är 1.0.0. Data kommer från paketens 4.0.0_best_int. Paketlicensen är MIT; Tesseracts träningsdata omfattas även av Apache-2.0.
- Endast LSTM används (OEM 1). Alla tre LSTM-motorer i version 7 medföljer: standard, SIMD och relaxed SIMD. Biblioteket väljer motor efter webbläsarens stöd. Varje .wasm.js innehåller sin WebAssembly-binär; separata .wasm-filer behövs inte för denna distribution.

SHA-256 för distributionsfiler och paketens publicerade integritetsvärden finns i vendor-manifest.json. Inga npm-installationer behövs på IIS-servern. Alla filer under wwwroot följer med dotnet publish.

## Licenser och upphovsrätt

Fullständiga licenstexter ligger i licenses/. Ursprungliga .LICENSE.txt-notiser ligger kvar bredvid JavaScript-distributionerna. De anger bland annat upphovsrätten för regenerator-runtime (Facebook), buffer och ieee754 (Feross Aboukhadijeh), och zlib.js (imaya). Även licenser för inbäddade beroenden och Leptonica medföljer. Licenserna ändrar inte licensen för Vasens egen kod.

Källprojekt:
- https://github.com/naptha/tesseract.js
- https://github.com/naptha/tesseract.js-core
- https://github.com/naptha/tessdata
- https://github.com/tesseract-ocr/tessdata_best
- https://github.com/DanBloomberg/leptonica

## Uppdatering och verifiering

Hämta uttryckligen valda npm-versioner med npm pack --ignore-scripts till en separat arbetsmapp. Kopiera dist/tesseract.min.js, dist/worker.min.js och deras .LICENSE.txt-filer, motorns *-lstm.wasm.js samt språkpaketens 4.0.0_best_int/*.traineddata.gz. Behåll alla licenser och uppdatera manifestet. Om motorns urvalsregler ändras måste motsvarande filer följa med.

Uppdatera även cachePath i receipt-ocr-worker.js vid byte av språkdata. Kör parser-, webbläsar- och .NET-testerna enligt tests/browser/README.md. Webbläsartestet använder riktiga språkfiler och motorn under en virtuell katalog. Prova även på fysisk mobil.

CSP tillåter wasm-unsafe-eval endast för kvittoredigeringen och OCR-filerna; JavaScript-eval, inline-skript och externa workers tillåts inte. Controller-workern gör att även pågående initialisering kan avbrytas. Kvittobilden och OCR-texten skickas inte till en molntjänst.
