# Implementationsstatus

## Första arbetsversionen

En körbar ASP.NET Core 10-lösning finns i Vasen.sln. Den första versionen fokuserar på kvittohantering. Mobil är primär: kompakta tabeller med anpassade kolumner, nedre navigering, tydliga tryckytor, korta formulär och decimalanpassat tangentbord för belopp. Desktop använder samma gränssnitt med mer utrymme; en separat desktoputformning kommer senare.

### Implementerat

- SQLite, EF Core och initial databasmigrering.
- Identity med lösenord och långvarig inloggning, skyddade sidor och bilagor.
- Skapande av ägarkonton via terminal, utan publik registrering eller standardlösenord.
- Registrering, visning och ändring av privata utlägg. Person låses efter registrering.
- Valfria kvittobilder/PDF och valfritt kostnadsslag, obligatorisk beskrivning.
- Filförhandsvisning vid uppladdning och markering av saknad bilaga.
- Lokal OCR av nya JPG/PNG/WebP-bilagor med Tesseract.js 7.0.0, svensk/engelsk språkdata och medföljande licenser. Knappen Läs av använder den aktuella, eventuellt beskurna bilden. Datum och tydligt märkta totalsummor visas som förslag; Använd datum/Använd belopp krävs för att ändra formuläret. Vid flera likvärdiga värden väljer användaren själv. PDF OCR-tolkas inte.
- OCR-filer hämtas från den egna servern vid behov; ingen extern OCR-tjänst eller CDN används. Avbryt, bildbyte, ny beskärning, sparande och sidbyte stoppar avläsningen. En controller-worker gör även initialiseringen avbrytbar. Maximal avläsningstid är 90 sekunder; manuell inmatning fungerar även om OCR misslyckas.
- Licenser, källor, versionslåsning och SHA-256-manifest finns under wwwroot/lib/tesseract. OCR-relaterade sökvägar tillåter WebAssembly i CSP, utan JavaScript-eval eller externa workers.
- Beskärning av nya kvittobilder före uppladdning: fri ram med pekstyrning, rotation, återställning och numerisk finjustering för tangentbord. Första valda bilden öppnas direkt; övriga bilder öppnas via Beskär. Originalet finns kvar i webbläsaren så länge formuläret är öppet. Beskurna bilder skickas som JPG via befintligt formulär; PDF påverkas inte.
- Webbläsartest utan npm-beroenden: `node tests/browser/receipt-crop.cjs`. Kontrollerar pekstyrning, beskurna bildpixlar/mått i formulärfilen, rotation, tangentbord, avbryt, återställning, flera bilagor, PDF, trasig bild och layout vid 320/390/1280 pixlar. Fysisk mobilkamera behöver fortfarande provas.
- Filter, sortering, totalsumma och summa per person, med bevarat urval efter kvittoredigering.
- Uttag för flera kvitton till en person, med separat utbetalningsbelopp och visad skillnad.
- Historik, ändring av ersatta kvitton och ångrad ersättningskoppling.
- Papperskorg, återställning och permanent radering av enskilda kvitton.
- Beständig datamapp utanför publiceringen, sparade inloggningsnycklar och filpubliceringsprofil.

### Verifiering

- OCR: 24 parserfall samt webbläsartest med riktig OCR-motor och ett genererat provkvitto. Kontrollerar datum/belopp, manuell bekräftelse, tvetydiga totalsummor, avbrott även under initialisering, sena svar, timeout, fel, PDF och layout vid 320/390/1280 pixlar. Körning under virtuell katalog och lokala språk-/motorfiler är verifierad. .NET-test kontrollerar filtyper och CSP för samtliga motorvarianter och språkdata. Fysisk mobil och träffsäkerhet på verkliga fotografier återstår att prova.

- 68 automatiska .NET-tester passerar för kvitton, eget bidrag, budget, insättningar, databasmigrering samt inloggning och formulär genom HTTP.
- Release-publicering har byggts till artifacts/publish och den genererade IIS-konfigurationen har kontrollerats. Beroendekontrollen rapporterar inga kända sårbara paket.
- Manuell webbläsarkontroll vid 390 och 320 pixels bredd, inklusive registrering utan bilaga, markering av flera kvitton och ersättning med avvikande belopp.
- Provdata används enbart i en separat förhandsvisningsdatabas under artifacts. Ingen riktig banköverföring eller driftsättning har utförts.
- Faktiska iPhone- och Android-enheter, kamera och Cloudflare/IIS behöver provas på målmiljön.

### Återstår

- Installation, återställningsprov och kontroll av Cloudflare/IIS på hemmaservern.
- Kontroll av publiceringsskriptet mot faktisk IIS-miljö på hemmaservern återstår. Current/Release-kopiering enligt AZenZeis skript är implementerad.
- Mer lättläst presentation av historiken; aktörens konto-id och uppgifter sparas redan.
- Sidindelning om kvittomängden kräver det. För närvarande visas hela urvalet.
- HEIC-stöd eller automatisk konvertering; första versionen accepterar JPG, PNG, WebP och PDF.
- Passkeys efter första etappen.

## Etapp två: budget och insättningar

- Årsbudget med uppskattade belopp per fast kostnadsslag och en gemensam budgetpott för samtliga privata kvitton.
- Gemensamma utgifter som flera delbelopp/klumpsummor per kostnadsslag och år, med rättelse och makulering.
- Automatiskt utfall från kvitton enligt kvittodatum. Uttag för kvittoersättning dubbelräknas inte.
- Ingående kassa per år och enkel beräkning av insättningsbehov för alla aktiva ägare. Månadsförslag avrundas uppåt till hela kronor.
- Årsöversikt med insättningar per person, utgifter, överskott/underskott och årets oersatta utlägg.
- Manuella engångsinsättningar och automatiska månadsregler, med rättelser och sparad historik.
- Paus, slutdatum, månadsslut, återupptagning och upphämtning efter IIS-vila. Databaskursor, transaktion och unikt index skyddar mot dubbletter.
- Nya mobilvyer för Budget och Insättningar. Papperskorgen nås nu från Mitt konto.
- Insättningar visas i en kompakt tabell med mobilanpassade kolumner. Månadsregler är utfällbara och visas också som tabell. Filter och summeringar har minskade marginaler.
- Kompakta sidor är en projektgemensam designregel, dokumenterad i AGENTS.md och implementationsplanen.
- Tilläggsmigreringen AddBudgetAndDeposits bevarar den tidigare databasens användare och kvitton; uppgraderingsfallet är testat.
- Mobilgranskning vid 320 och 390 pixels bredd med provbudget, gemensam utgift och historisk månadsregel. Ingen sidledsrullning i budget- och insättningslistorna.
- Release-publicering har byggts lokalt till artifacts/publish. Ingen driftsättning på hemmaservern har gjorts i detta arbete.

Se README.md för beräkningsregler, månadsautomatik och uppgradering.

## Förenklad budgetsida

- Kompakt tabell med fasta kostnader och en rad för Privata utlägg. Budget och utfall visas bredvid varandra.
- Alla årets aktiva privata kvitton ingår i den gemensamma potten, även utan kostnadsslag och oavsett ersättningsstatus.
- Privat budget anges som ett årsbelopp utan kategorival. En databaskontroll tillåter högst en privat budget per år.
- Insättningsförslaget ligger under tabellen. Kassa, beräkning och årets detaljerade resultat är utfällbara.
- PoolPrivateBudget tar bort tidigare privata testbudgetposter enligt användarens instruktion att testdata inte behöver bevaras. Övriga data behålls.
- Tester täcker olika kvittokategorier, okategoriserade kvitton, årsurval, papperskorg, ersättning, borttagen budget och uppdatering från föregående databasversion.
- Mobilvyn har granskats vid 320 och 390 pixels bredd med provbudget och kvitton.

## Eget bidrag vid avslut av kvitton

- Avslutsformuläret har ett uttryckligt val för att räkna kvittosumma minus faktiskt uttag som eget bidrag.
- Exemplet 15 000 kr i kvitto, 10 000 kr i uttag och 5 000 kr i eget bidrag är verifierat i tester och mobilflödet.
- Hela kvittobeloppet kvarstår i utgifterna. Inga extra bankinsättningar skapas.
- Budgeten visar bankinsättningar, eget bidrag och totalt bidrag per person i en kompakt tabell.
- Ångring utesluter bidraget. Kvittorättelser ändrar inte automatiskt det registrerade bidraget. Årsindelningen följer avslutsdatumet för bidraget och kvittodatumet för kostnaden.
- AddOwnContribution lägger till ett belopp på uttaget med standardvärde noll för befintliga avslut.
- Release-publicering är byggd lokalt till artifacts/publish; ingen driftsättning har gjorts på hemmaservern.

Se README.md för lokala startkommandon och IIS-inställningar. IMPLEMENTATIONSPLAN.md är den fullständiga kravplanen; denna fil beskriver vad som faktiskt är genomfört.

## Användarhantering

- Kompakt användartabell under Mitt konto → Användare, med namn, e-post och status. Skapande och ändring kräver namn och giltig e-post. Inloggning fungerar med namn eller e-post; ingen mailserver eller e-postutskick finns.
- Endast ägare kan skapa och ändra konton, pausa/återaktivera samt återställa andras lösenord. Ägarmarkeringen kan ändras under Ändra användare, utan maxgräns och med skydd för den sista aktiva ägaren.
- Tillfälliga lösenord genereras kryptografiskt, visas enbart i direkt svaret och lagras inte i klartext eller TempData. Obligatoriskt val av eget lösenord skyddas på serversidan för samtliga övriga sidor och åtgärder.
- Paus och lösenordsåterställning spärrar tidigare inloggningar vid nästa förfrågan. Återställning häver även tillfällig inloggningslåsning. Eget konto och den sista aktiva ägaren kan inte pausas.
- Ägare kan anonymisera pausade konton efter en separat bekräftelse. Kontots identitets- och autentiseringsuppgifter rensas; interna kopplingar och ekonomisk historik behålls. Fritext och bilagor ändras inte automatiskt. Ingen återaktivering efter anonymisering.
- Tidigare insättningar kan rättas för pausade personer. Återaktivering hoppar över pausade månadsperioder, även om appen inte kördes under pausen.
- Tilläggsmigreringen AddMemberAdministration bevarar tidigare konton, lösenord och ekonomiposter. Äldre konton kan fortsatt logga in med sitt tidigare inloggningsnamn.
- Integrationstester täcker ägarbegränsad administration, ägarbegränsad rensning, bevarade ekonomiska kopplingar, lösenordsval och återställning, återkallade sessioner, paus/återaktivering, samtidighetskontroll, CSRF och uppgradering.
- Visuell webbläsarkontroll med separat provdatabas vid 320 och 390 pixels bredd: användarlista, formulär och anonymiseringens bekräftelse. Ingen sidledsrullning i de kontrollerade vyerna.

### Förtydligande: namn och e-post

- Namn och e-post är obligatoriska vid skapande och ändring. Båda kan användas vid inloggning. Svenska tecken, mellanslag i namn och skillnader i stora/små bokstäver hanteras.
- Dubbletter och kollisioner mellan olika personers namn, e-post och äldre inloggningsnamn avvisas. Om äldre konton har samma namn väljer inloggningen aldrig ett godtyckligt konto; unik e-post kan fortsatt användas.
- Befintliga konton utan e-post behålls och kan kompletteras under Användare, där saknad e-post markeras. Ingen e-postadress fabriceras och ingen databasändring behövs för förtydligandet.
- Tillfälliga lösenord, obligatoriskt lösenordsbyte, paus och anonymisering behålls. Ingen mailserver eller e-postsändning har införts.
- Åtta ytterligare testfall verifierar kontaktändringar, inloggning med svenska namn/e-post, validering och äldre tvetydiga namn. Samtliga 55 tester passerar.

## Kompakt kvittosida enligt designreglerna

- Kvittolistan visas som en gemensam tabell i stället för separata kort. Mobil visar beskrivning med datum, person och kostnadsslag under, samt belopp och status. Desktop får separata kolumner. Långa beskrivningar begränsas till två rader i listan; hela texten finns i kvittot.
- Mindre rubriker, kompakt totalsummering och mindre dekorativt utrymme. Filter är hopfällda även efter ett urval, med antal aktiva filter/sorteringsval i rubriken. Personsummering är utfällbar.
- Kvittoformuläret prioriterar belopp, beskrivning, datum och person. Filuppladdning är kompakt; kostnadsslag, hjälp, historik och övriga åtgärder är utfällbara. Sparat kostnadsslag framgår i den hopfällda rubriken.
- Markeringsytor och kontroller är minst 44 pixlar i de berörda vyerna, med bibehållen tangentbordsfokus. Val av olika personer fortsätter att spärras före avslut.
- Tillbaka och avbryt bevarar listans urval, även för nytt kvitto via listans knapp. Menyns Nytt utlägg öppnar ett tomt formulär från befintliga kvitton.
- Samtliga 55 tester passerar. Visuell kontroll med separat provdatabas vid 320 och 390 pixels bredd samt desktop: tabell, filter, markeringar, formulär och bibehållet urval. Ingen sidledsrullning i de kontrollerade vyerna.

## Snabbfilter på kvittosidan

- Två checkboxar är alltid synliga ovanför de utfällbara filtren: Visa ersatta och Bara mina kvitton. Listan uppdateras direkt när ett snabbval ändras.
- Visa ersatta är förvalt och inkluderar klara/ersatta kvitton. Avmarkering visar enbart oersatta. Bara mina kvitton väljer den inloggade utläggaren; avmarkering återgår till alla personer.
- Snabbvalen använder befintliga person- och statusfilter. Datumintervall, kostnadsslag och sortering behålls och valen följer med tillbaka från ett kvitto.
- Mobilkontroll vid 320 och 390 pixels bredd visar båda valen på en rad utan sidledsrullning. Funktion och återgång från kvittodetalj är verifierade med separat provdata. Samtliga 55 tester passerar.

## Publiceringsskript för hemmaservern

- Publish-ToNetworkShare.ps1 bygger och kopierar till Y:\ med samma Release/Current-princip som AZenZei. OfflineOnly, OnlineOnly, KeepOffline och SkipPublish stöds.
- Byggfel stoppar publiceringen; kopieringsfel lämnar appen offline. Datamappar och nycklar hålls utanför publiceringen. Ingen automatisk databackup eller radering av gamla filer görs.
- Verifierat med riktig lokal Release-byggning och kopiering till isolerad testmapp: separata releaser, OfflineOnly/OnlineOnly, KeepOffline, simulerat fillås, avvisad databasfil och avbruten publicering vid byggfel. Ingen serverkontakt i verifieringen.
- Se README.md för kommandon, serverinställningar och backup. Ingen publicering har körts mot servern.



## Visual Studio Publish

- Profilen Server bygger lokalt och anropar Publish-ToNetworkShare.ps1 med SkipPublish mot Y:\. Folder fortsätter att publicera enbart lokalt.
- Verifierat mot isolerad lokal testmapp med både dotnet publish och Visual Studios DeployOnBuild-flöde. En release per körning och återgång online kontrollerades. Skriptfel propageras till MSBuild/Publish.
- Ingen publicering mot servern har gjorts. Automatisk databackup och kontroll av migreringsresultat ingår ännu inte.

## Ägarmarkering i användarhanteringen

- Ändra användare har kryssrutan Ägare med en kort förklaring om fördelning av insättningsbehov och tillgång till rensningsrutinen. Endast ägare får ändra markeringen. Nya konton skapas som läsare eller kvittoregistrerare.
- Ingen maxgräns för antalet ägare finns. Den sista aktiva ägaren kan inte avmarkeras. Budgetfördelningen följer antalet aktiva ägare.
- Ändringen är transaktionell och använder befintlig samtidighetskontroll. Före/efter-värden och aktör loggas utan att ändra tidigare kvitton eller insättningar. Rätt till rensning följer aktuell markering direkt, även för redan inloggade.
- Historiska insättningar och månadsregler kan fortsatt rättas för tidigare ägare, men nya insättningar kräver en aktiv ägare. Tidigare ägare finns kvar i insättningarnas personfilter.
- Förfallna månader behandlas före ägarbyte, så att perioder utan ägarmarkering inte skapar retroaktiva insättningar vid återgång till ägare.
- Fem ytterligare integrationstester täcker markering/avmarkering, uppdaterad åtkomst, budgetpersoner, ekonomisk historik, skydd av sista aktiva ägaren, fler än tre ägare och månadsregler. Samtliga 60 tester passerar.

## Behörighetsnivåer och inbjudan (2026-11-12)

- Ägare har full åtkomst. Endast ägare hanterar konton, behörigheter, uttag/avslut, budget, gemensamma utgifter, bankinsättningar och papperskorg. Ingen maxgräns för antalet ägare finns. Skyddet för sista aktiva ägaren behålls.
- Läsare kan se ekonomin, kvittodetaljer, bilagor och historik samt sköta sitt eget lösenord.
- Kvittoregistrerare har samma läsrättigheter och kan dessutom skapa utlägg för valfri aktiv användare och rätta egna registreringar medan de är oavslutade. Ansvarig utläggare och registrerande konto är separata; ansvarig kan inte ändras i efterhand. Vid ångrat avslut gäller åter rättelsenivån för ett öppet utlägg. Papperskorg och permanent radering är ägaråtgärder.
- Under Användare väljer ägaren Läsare eller Kvittoregistrerare. Ägarmarkeringen ger full behörighet oavsett det valet. Nya konton får läsbehörighet som standard. Ett tillfälligt lösenord visas en gång och byts vid första inloggning; ägaren delar uppgifterna själv.
- Razor Pages använder en central kontroll med explicita tillåtna åtgärder för icke-ägare. Databasens aktuella behörighet används på varje förfrågan; behörighetsändringar gäller även redan inloggade konton. Skrivande tjänster gör egna kontroller före ändring och filhantering. Knappar och länkar anpassas efter behörigheten.
- Migreringen AddAccessLevels bevarar ägare, lösenord, bilagor och ekonomi. Befintliga icke-anonymiserade konton utan ägarmarkering får kvittoregistreringsbehörighet. Nya konton får standardvärdet Läsare. CreatedById för äldre kvitton hämtas från den första Skapat-posten i historiken; om uppgiften saknas lämnas den tom och bara ägare får rätta kvittot. Ansvarig person används aldrig som gissning om registreraren.
- Sju nya .NET-testfall täcker läsning och bilagor, direktanrop mot ändringsåtgärder, serverkontroller i tjänster, ansvarig kontra registrerare, avslut/ångrat avslut, nivåbyten i befintlig session, kontoskapande med tillfälligt lösenord, ogiltiga nivåer och uppgradering från föregående databasversion. Äldre administrations- och ekonomitester använder nu ägare för åtgärder som kräver ägarrätt.
- Verifierat: 68 .NET-tester, 24 OCR-parserfall samt beskärnings- och OCR-webbläsartester passerar. Layouttestet visar faktiska Razor-svar för läsare, registrerare och ägare vid 320/390/1280 pixlar utan sidledsrullning eller JavaScript-fel. Release-publicering till artifacts/access-check/publish lyckas, och EF rapporterar inga modelländringar utan migrering. Ingen driftsättning är utförd.

## Obegränsat antal ägare (2026-11-12)

- Maxgränsen för ägare är borttagen från både webbens kontohantering och terminalens ägarskapande. Befintlig ägarbehörighet krävs fortfarande för administration, och sista aktiva ägaren skyddas fortsatt.
- Års- och månadsförslaget för insättningar delas lika mellan det aktuella antalet aktiva ägare. Pausade ägare och vanliga medlemmar räknas inte med. Månadsbelopp avrundas fortsatt uppåt till hela kronor; årsbelopp avrundas uppåt till ören.
- Budgetvyn visar antalet aktiva ägare. Om ingen aktiv ägare finns visas att förslaget inte kan beräknas, utan division med noll.
- Befintliga insättningar, ekonomisk historik och månadsregler skrivs inte om när förslaget räknas om. Månadsregler justeras separat som tidigare.
- Testet för treägargränsen ersattes med ett test som lägger till fjärde och femte ägaren. Sju tillkommande budgetfall täcker 0/1/2/3/4/7 ägare, avrundning samt paus och avmarkering. Samtliga 75 .NET-tester passerar.
