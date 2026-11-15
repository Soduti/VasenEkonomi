# Vasen

Mobilanpassad kvittohantering för det gemensamma landstället. ASP.NET Core 10, Razor Pages, Identity och SQLite. Inget separat API och ingen molnlagring.

## Kom igång

Öppna `Vasen.sln` i Visual Studio eller projektmappen i VS Code. .NET 10 SDK krävs; `global.json` väljer .NET 10 även när ett nyare preview-SDK är installerat.

```powershell
dotnet restore
dotnet run --project src/Vasen.Web -- --create-owner
```

Skapa ägarkontona genom att köra det andra kommandot en gång per person. Namn, e-post och tillfälligt lösenord efterfrågas i terminalen; lösenord skrivs inte ut. Ägaren väljer eget lösenord vid första inloggningen. Ingen publik registrering eller förvald inloggning finns. Lösenord måste ha minst 10 tecken, stor och liten bokstav samt siffra. Övriga konton skapas och hanteras under Mitt konto → Användare. Namn och e-post krävs; ingen mailserver eller e-postutskick behövs.

```powershell
dotnet run --project src/Vasen.Web --launch-profile http
```

Öppna http://localhost:5242. Databasen skapas genom migrering vid första starten. I utvecklingsläge sparas data i `Vasen-data` i projektroten; mappen är ignorerad av Git. Använd separata datamappar för test och verkliga kvitton.

## Första implementationen

- Svenskt gränssnitt byggt för mobil: kvittokort, formulär och nedre navigering.
- Inloggning med lösenord och valbar 90 dagars förnybar inloggning.
- Kvitton med datum, person, belopp och obligatorisk beskrivning. Kostnadsslag och bilagor är valfria.
- Filförhandsvisning; högst åtta nya bilagor per sparning, max 10 MB per fil. JPG, PNG, WebP och PDF stöds. HEIC behöver exporteras som JPG/PDF i denna version.
- Filter och sortering på datum, person, belopp, kostnadsslag och ersättningsstatus. Summor per urval och person. Hela urvalet visas, utan sidindelning i första versionen.
- Ersättning av flera kvitton för samma person i ett uttag. Utbetalt belopp får skilja sig från kvittosumman.
- Ändringshistorik, rättning av ersatta kvitton, ångrad ersättningskoppling och papperskorg med återställning/permanent radering per kvitto.
- Tre nivåer: ägare har full åtkomst, läsare kan se ekonomin, kvittoregistrerare kan dessutom skapa och rätta egna registreringar fram till avslut. Alla kan byta sitt eget lösenord under Mitt konto.

Budget, gemensamma kontoutgifter och insättningar är implementerade i etapp två nedan. Passkeys kommer senare. Webbhantering av konton finns under Mitt konto. Mer lättläst historik förbättras i kommande arbete; aktörens konto-id sparas redan.

## Budget och insättningar

### Klara kvitton och eget bidrag

Markera kvitton i listan och välj **Avsluta valda**. Ange det faktiskt utbetalda beloppet. Kvittot blir klart även när uttaget inte är lika stort som kostnaden.

Om mellanskillnaden är personens eget bidrag, välj **Räkna skillnaden som eget bidrag**. Exempel: kvitto 15 000 kr och uttag 10 000 kr ger eget bidrag 5 000 kr. Kvittot räknas fortfarande som 15 000 kr i budgeten. Ett uttag på noll tillåts med detta val när hela kvittot bekostas som eget bidrag.

Bidraget får kvittots person och avslutets datum. I årets personsammanställning visas insättningar till kontot, eget bidrag och totalt bidrag separat. Årets överskott/underskott är insättningar plus eget bidrag minus hela de registrerade utgifterna. Ingen extra bankinsättning skapas.

Valet är frivilligt; vanliga avrundningsskillnader blir inte automatiskt eget bidrag. Bidraget sparas som ett fast belopp vid avslutet och ändras inte automatiskt när kvittot rättas eller läggs i papperskorgen. Ångra avslutet och registrera det på nytt för att ändra bidraget. Ett ångrat avsluts bidrag räknas inte längre med. Äldre avslut får noll i eget bidrag och räknas inte om automatiskt.

Öppna **Budget** i mobilens nedre meny och välj år. En kompakt tabell visar Budget (planerat årsbelopp), Utfall (registrerat hittills) och Kvar. På mobilen visas Kvar under utfallet i samma cell.

1. Välj **＋ Fast kostnad** för att planera exempelvis el, lån eller försäkring.
2. Välj **Registrera utgift** för belopp som faktiskt betalats från gemensamt konto.
3. Tryck på raden **Privata utlägg** för att ange en enda årspott för privata inköp. Utfallet hämtas automatiskt från alla årets kvitton.
4. Under **Insättning per ägare** visas ett förslag per månad för hela året. Kassa och beräkning samt årets detaljerade resultat kan fällas ut.

- **Gemensamma utgifter:** registrera ett eller flera delbelopp per kostnadsslag och år, utan kvittobilagor. Exempel: el januari–juni och el juli–december. Posterna kan rättas eller makuleras med sparad historik.
- **Privata utlägg:** alla aktiva kvitton summeras i samma budgetpott enligt kvittodatum, inklusive oersatta kvitton och kvitton utan kostnadsslag. Ingen budgetkoppling behöver väljas på kvittot. Kostnadsslag används fortsatt i kvittolistans filter och statistik. Ersättningsuttagen räknas inte som en ny utgift.
- **Utfall:** saknade gemensamma utgifter visas som Ej angivet. Ett registrerat nollbelopp är ett känt nollutfall. Totalsumman visar endast det registrerade utfallet hittills.
- **Ingående kassa:** ett valfritt värde per år som anges under Insättning per ägare. Insättningsbehovet är max(0, årets budget minus ingående kassa). Negativ ingående kassa ökar behovet. Beloppet delas lika mellan alla aktiva ägare, och månadsförslaget avrundas uppåt till hela kronor. Ingen buffertmodell finns.
- **Årsöversikt:** budget och utfall per kostnadsslag, bidrag per person med bankinsättningar och eget bidrag var för sig, årets överskott/underskott samt årets oersatta utlägg. Resultatet inkluderar privata utlägg även om de ännu inte ersatts och är därför inte ett banksaldo.

Under **Insättningar** kan engångsbelopp registreras och rättas per ägare. Års- och personfilter styr listan och totalsumman. Om samma betalning redan har skapats av en månadsregel ska den posten rättas, inte registreras manuellt på nytt.

### Automatiska månadsinsättningar

Skapa en månadsregel per ägare med belopp, dag i månaden, startdatum och valfritt slutdatum. Regelns person och startdatum låses efter registrering. Om vald dag saknas används månadens sista dag. Ett historiskt startdatum skapar redan förfallna insättningar; framtida insättningar väntar till rätt dag.

Regler kan pausas och avslutas. Pausade och avslutade månader hoppas över och fylls inte i när regeln återupptas eller förlängs. Ändringar i belopp och betalningsdag gäller efter att tidigare förfallna tillfällen har hanterats med de gamla inställningarna. Befintliga insättningar ändras separat. Makulerade automatiska insättningar återskapas inte för samma regel och månad.

En bakgrundstjänst kontrollerar reglerna vid appstart och en gång i timmen medan IIS kör appen. Kontrollen körs även när budgeten eller insättningarna öppnas. Efter IIS-vila hanteras förfallna månader vid nästa start/besök. En databaskursor och ett unikt index per regel/månad skyddar mot dubbletter. Kalendern följer svensk tid.

Detta registrerar uppgifter i Vasen. Banköverföringarna behöver fortfarande vara upplagda i banken; ingen bankkoppling eller bankbekräftelse finns.

### Uppgradering från första versionen

Migreringen `AddBudgetAndDeposits` lägger till nya tabeller vid nästa appstart och behåller befintliga användare, kvitton och bilagor. Ta en SQLite-säker backup av databas, kvitton och nycklar innan Current uppdateras. Publicera till Release och växla Current enligt samma rutin som för den första versionen. Publicering från utvecklingsprojektet går att bygga lokalt; ingen serverinstallation har utförts av denna implementation.

Migreringen `PoolPrivateBudget` förenklar privata budgetar till en pott per år. Enligt beslut under testfasen tas äldre privata budgetposter bort; ange den privata årsbudgeten på nytt. Fasta budgetposter, kvitton, bilagor, insättningar och användare påverkas inte av denna rensning.

## Tester

```powershell
dotnet test Vasen.sln
```

41 tester använder egna SQLite-databaser i temporära mappar. De provar kvittohantering, eget bidrag utan bankinsättning, gemensam privat budgetpott och utfall utan dubbelräkning, uppgradering av gammal databas, insättningsregler, månadsslut, skottår, pauser, samtidiga körningar samt inloggning och formulär genom Razor Pages.

## IIS och publicering

Avsedd struktur på Windows Server:

```text
Vasen/
  cloudflare/
  Current/
  Release/
    yyyy-MM-dd_HH-mm/
  Vasen-data/
    database/
    receipts/
    identity-keys/
```

Installera ASP.NET Core Hosting Bundle för .NET 10 på servern. Skapa en separat IIS-webbplats och applikationspool, utan hanterad CLR, med `Current` som fysisk sökväg. Ange miljövariabeln `Vasen__DataPath` till den absoluta sökvägen för `Vasen-data` i IIS-processens miljö. Produktion vägrar starta utan explicit datamapp. Applikationspoolens identitet behöver ändringsrättighet till datamappen och läsrättighet till programfilerna. Skydda datamapp och nycklar med Windows-behörigheter.

### Publicera från Visual Studio

Högerklicka på **Vasen.Web** i Solution Explorer → **Publish** → välj profilen **Server** → **Publish**. Profilen bygger i Release och kör därefter `Publish-ToNetworkShare.ps1 -SkipPublish` med de nybyggda filerna. Målet är `Y:\`: en ny Release sparas och Current uppdateras med ett kort driftstopp. Skriptfel gör att Publish rapporterar fel. Y: måste vara ansluten och skrivbar för samma Windows-användare som kör Visual Studio. IIS och datamappen måste vara konfigurerade enligt ovan. Databasmigreringarna körs vid appstart; automatisk databackup och kontroll av migreringens resultat ingår ännu inte.

Profilen **Folder** publicerar enbart lokalt till `artifacts/publish`. **Server** sparar en separat lokal byggmapp under `artifacts/server-publish` för varje körning. Filerna kan granskas där. Byggmapparna rensas inte automatiskt.

Motsvarande terminalkommando är `dotnet publish src/Vasen.Web -c Release -p:PublishProfile=Server`. För enbart lokal kontroll av serverprofilen, lägg till `-p:DeployToNetworkShare=false`. För annan serverrot kan `-p:NetworkDeployRoot=...` anges; i Visual Studio ändras motsvarande värde i `Properties/PublishProfiles/Server.pubxml`.

### Publicera från terminalen
Publicera med `Publish-ToNetworkShare.ps1` från projektroten (samma kopieringsprincip som AZenZei):

```powershell
.\Publish-ToNetworkShare.ps1
```

Konfigurera IIS request filtering `maxAllowedContentLength` till minst 89128960 byte om uppladdningar över IIS standardgräns behövs. Appen tillåter högst 85 MB per formulär. Kontrollera också eventuell uppladdningsgräns i Cloudflare.

Skriptet bygger lokalt i en ny mapp under `artifacts/network-publish`, kontrollerar resultatet och kopierar programfilerna till `Y:\Release\yyyy-MM-dd_HH-mm` och `Y:\Current`. Vid flera körningar samma minut får releasen ett unikt suffix. Serverroten måste redan finnas. `Current` ska vara en vanlig mapp; kataloglänkar avvisas. Inga gamla filer eller releaser raderas automatiskt, enligt AZenZeis rutin.

`app_offline.htm` stoppar appen under kopieringen, med tio sekunders väntan för att släppa fillås. Vid lyckad kopiering tas filen bort, om inte `-KeepOffline` valts. Vid kopieringsfel lämnas appen offline; rätta felet och publicera på nytt innan den startas. En misslyckad byggning påverkar inte Current.

```powershell
# Stoppa appen inför exempelvis en SQLite-säker backup:
.\Publish-ToNetworkShare.ps1 -OfflineOnly
# Publicera men låt appen vara stoppad för kontroll:
.\Publish-ToNetworkShare.ps1 -KeepOffline
# Starta igen efter kontroll:
.\Publish-ToNetworkShare.ps1 -OnlineOnly
# Kopiera en redan byggd publicering (standard: artifacts/publish):
.\Publish-ToNetworkShare.ps1 -SkipPublish
# Annan serverrot eller egen lokal publicering:
.\Publish-ToNetworkShare.ps1 -ShareRoot '\\server\share\Vasen' -SkipPublish -LocalPublishDir 'D:\publish\Vasen'
```

`-ShareRoot` är hela Vasen-mappen, inte dess förälder. `-Configuration` väljer byggkonfiguration. Databas, kvitton och inloggningsnycklar ingår inte i publiceringen; misstänkta datafiler avvisas. Behåll serverinställningar i IIS-miljön, inklusive `Vasen__DataPath` med serverns lokala absoluta sökväg till `Vasen-data` (inte klientens `Y:`). Publicerade konfigurationsfiler skrivs över vid uppdatering.

Den nya appen tillämpar databasmigreringar vid start. Ta därför SQLite-säker backup av databas, kvitton och nycklar före uppdateringen, exempelvis efter `-OfflineOnly`. Säkerställ att appen verkligen har stoppats innan backup tas. Återgång till äldre programfiler kräver kompatibel databas eller återställning av backup. Skriptet tar ingen automatisk databackup. Ingen publicering har körts mot servern i detta arbete.

Cloudflare ska leverera HTTPS till webbläsaren. IIS behöver se korrekt HTTPS-schema för att undvika omdirigeringsloopar, exempelvis via HTTPS till IIS. Om tunneln använder HTTP till IIS måste betrodda proxyadresser och forwarded headers konfigureras uttryckligen; appen litar inte generellt på inkommande proxyheaders. Verifiera upplägget mot AZenZei innan driftsättning.

Ta återkommande backup med appen stoppad eller genom SQLite backup-API; kopiera inte enbart en aktiv `.db`-fil utan dess WAL-data. Provåterställ backup inklusive bilagor och nycklar innan verkliga kvitton läggs in. Permanent raderade kvitton kan finnas kvar i äldre säkerhetskopior tills dessa löper ut.

Se [IMPLEMENTATIONSPLAN.md](IMPLEMENTATIONSPLAN.md) för hela planen och avstämda regler.

## Användare

Som ägare öppnar du **Mitt konto → Användare**. Endast ägare kan:

- Skapa ett konto med **namn, e-post och behörighet** (Läsare eller Kvittoregistrerare). Personen kan logga in med valfritt av dem, oberoende av stora och små bokstäver. Ett tillfälligt lösenord visas direkt och enbart då. Ge uppgifterna till personen på valfritt sätt; appen skickar inga mejl.
- Ändra namn och e-post. Båda ska vara unika så att inloggningen pekar på rätt person. Namn får innehålla mellanslag och svenska tecken. Befintliga konton utan e-post kompletteras här; ingen adress fylls i automatiskt. Tidigare inloggningsnamn fungerar tills kontouppgifterna sparas på nytt. Om äldre konton har samma namn måste de logga in med sina unika e-postadresser tills namnen ändras.
- Skapa ett nytt tillfälligt lösenord för någon som glömt sitt. Gamla lösenord och öppna inloggningar slutar fungera. Personen måste välja ett annat, eget lösenord innan appen kan användas.
- Pausa och återaktivera andra konton. Pausade personer kan inte logga in eller väljas för nya registreringar, men deras ekonomiska historik och rättelsemöjligheter behålls. Månadsregler skapar inga insättningar för pausade månader, även efter återaktivering.

Du kan inte pausa ditt eget konto eller den sista aktiva ägaren. Under Ändra användare kan en ägare markera eller avmarkera Ägare och spara uppgifterna. Markeringen ger full åtkomst och gör personen valbar för nya insättningar. Det finns ingen maxgräns för antalet ägare. Minst en aktiv ägare måste finnas kvar. Budgetens fördelning följer antalet aktiva ägare.

Endast ägare ser **Rensa pausade konton**. Välj en pausad person och bekräfta anonymiseringen. Kontots namn, användarnamn, lösenord, eventuell e-post/telefon och autentiseringsuppgifter rensas permanent. Kvitton, uttag, bidrag, insättningar och historik behåller sina kopplingar och visas som **Anonymiserad person**. Ägarmarkeringen behålls och månadsregler pausas. Fritext och bilagor kan fortfarande innehålla personuppgifter och ändras inte av denna rutin. Anonymisering kan inte ångras.

Databasuppgraderingen `AddMemberAdministration` bevarar befintliga konton och ekonomiposter. Befintliga konton tvingas inte byta lösenord förrän någon återställer lösenordet.



### Läsare och kvittoregistrerare

**Läsare** kan se kvitton, bilagor, uttag, budget, gemensamma utgifter, insättningar och ekonomisk historik. De kan inte ändra dessa uppgifter eller hantera andra konton.

**Kvittoregistrerare** kan dessutom lägga till utlägg och välja vilken aktiv användare som helst, även sig själva, som ansvarig. Ansvarig är den person vars utlägg ska ersättas. Den som registrerade kvittot sparas separat och får rätta den egna registreringen tills den avslutas. Registreraren får inte rätta någon annans registrering, även om utlägget tillhör registreraren. Om ägaren ångrar avslutet kan registreraren åter rätta sitt öppna utlägg. Ansvarig kan inte ändras i efterhand.

Behörigheten väljs under **Ändra användare → Behörighet → Spara uppgifter**. Ägare har alltid full åtkomst oavsett valet Läsare/Kvittoregistrerare. Nivåbyten gäller vid nästa förfrågan även för användare som redan är inloggade.

Uppgraderingen **AddAccessLevels** behåller befintliga ägare och ger befintliga övriga, icke-anonymiserade konton registreringsbehörighet. Nya konton är läsare som standard. För äldre kvitton hämtas registreraren från skapandehistoriken; om uppgiften saknas kan bara ägare rätta kvittot.

### Gör en medlem till ägare

Öppna **Mitt konto → Användare**, välj personen, kryssa i **Ägare** och tryck **Spara uppgifter**. Avmarkera samma ruta för att göra en ägare till vanlig medlem. Ingen maxgräns finns för antalet ägare. Nya konton får läsbehörighet som standard; ägaren kan välja Kvittoregistrerare.

Ändringen sparas i historiken. Tidigare kvitton och insättningar behålls och kan rättas även efter avmarkering. Månadsregler skapar inte insättningar under perioder då personen saknar ägarmarkering, och sådana perioder fylls inte i retroaktivt vid ny markering.
