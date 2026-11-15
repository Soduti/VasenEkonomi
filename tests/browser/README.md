# Beskärning i webbläsare

Kör från projektroten:

    node tests/browser/receipt-crop.cjs

Testet använder Node.js 22+ och en installerad Chromium/Chrome. På Windows hittas en lokal Playwright-installation automatiskt. Annars anges sökvägen till webbläsaren i miljövariabeln CROP_TEST_CHROME. Inga npm-paket krävs.

En separat lokal HTTP-server visar beskärningsdialogen från Razor-filen med projektets CSS, JavaScript och CSP. Inga användarkonton eller projektdata används. Testet kontrollerar pekstyrning, bildinnehåll och mått i formulärets fil, rotation, tangentbord, återställning, avbryt, flera bilagor, PDF samt trasig bild. Skärmbilder vid 320, 390 och 1280 pixlar sparas under artifacts/crop-check. Server och testwebbläsare stängs efter körning.

Detta kompletterar dotnet test Vasen.sln. Fysisk iPhone/Android och kamerans bildorientering behöver fortfarande provas på enhet.

# OCR av kvitto

    node --test tests/browser/receipt-ocr-parser.test.cjs
    node tests/browser/receipt-ocr.cjs

Parsern testas mot 24 fall: svenska och internationella datumformat, skottår, ogiltiga datum, tusentalsavgränsare, decimaler, hela kronor, moms/delsummor/betalning/växel, tvetydiga totalsummor och saknade träffar.

Webbläsartestet kräver samma Node.js och Chromium som beskärningstestet. Det kör den riktiga OCR-motorn och lokala svenska/engelska språkdata mot ett genererat provkvitto. Därefter simuleras sena svar, fel och timeout för att kontrollera att befintliga formulärvärden bevaras och att avbrott städar upp workers. En verklig avbrytning under initialisering kontrolleras också. Inga OCR-filer hämtas från internet under testet. Appen testas under /vasen/ för att även kontrollera virtuell katalog.

Skärmbilder vid 320, 390 och 1280 pixlar finns i artifacts/ocr-check. OCR-testerna använder inga användarkonton eller riktiga kvitton. Prov på fysisk mobil och träffsäkerhet på verkliga kvittofotografier återstår.

.NET-testet OcrAssetsAndWebAssemblyPolicyAreAvailableLocally kontrollerar att språkdata och alla tre motorkodsversioner går att hämta via ASP.NET samt att Content-Security-Policy tillåter WebAssembly för OCR utan att tillåta JavaScript-eval.

# Behörigheter och mobilvyer

    dotnet test Vasen.sln
    node tests/browser/access-layout.cjs

AccessTests och de anpassade MemberTests kontrollerar nivåerna ägare, läsare och kvittoregistrerare både genom HTTP och direkt mot skrivande tjänster. Matrisen omfattar registrering, egna/andras utlägg, avslut, bilagor, ekonomi, kontohantering, nivåbyte i befintlig session och en verklig SQLite-uppgradering från föregående schema.

.NET-testerna sparar exempel på serverns renderade Razor-vyer i artifacts/access-check. Layouttestet visar dessa med projektets CSS och JavaScript i Chromium vid 320, 390 och 1280 pixlar. Det kontrollerar att läsare saknar ändringskontroller, att navigationen anpassas, och att sidorna fungerar utan sidledsrullning eller JavaScript-fel. Skärmbilder sparas i samma mapp. Endast separata testkonton och provdata används.
